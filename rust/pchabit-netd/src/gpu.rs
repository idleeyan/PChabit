//! GPU 传感器探针（NVML 直读）。
//!
//! 为什么要有这个模块：宿主走 LibreHardwareMonitor 的 `GpuNvidia.Update()`，
//! 实测 85.7 ms/拍（占整个硬件监控 86.5 ms 的 99%）。而 PChabit 实际只展示
//! 6 个指标：占用 / 温度 / 显存 / 风扇 / 频率 / 功耗，NVML 全部能直读。
//!
//! 口径约定：
//!   热指标（占用/温度/显存）每拍读，冷指标（风扇/频率/功耗）每 5 拍读一次，
//!   中间沿用上次值——这三项单次成本 0.5~3.7 ms，全速读会把优势吃掉一半。
//!
//! 边界：只覆盖 NVIDIA。无 nvml.dll / 无 N 卡 / 任何调用失败，`available` 为 false，
//! 宿主必须原样退回 LHM，绝不使用半截数据。

use std::ffi::c_void;
use std::os::raw::c_int;
use std::ptr;

const NVML_SUCCESS: c_int = 0;
const NVML_TEMPERATURE_GPU: u32 = 0;
const NVML_CLOCK_SM: u32 = 1;
/// 冷指标降频周期（拍）。
const COLD_EVERY: u64 = 5;

#[repr(C)]
#[derive(Default, Clone, Copy)]
struct Util {
    gpu: u32,
    memory: u32,
}

#[repr(C)]
#[derive(Default, Clone, Copy)]
struct MemInfo {
    total: u64,
    free: u64,
    used: u64,
}

extern "system" {
    fn LoadLibraryW(name: *const u16) -> *mut c_void;
    fn GetProcAddress(lib: *mut c_void, name: *const u8) -> *mut c_void;
    fn FreeLibrary(lib: *mut c_void) -> c_int;
}

type FnInit = unsafe extern "system" fn() -> c_int;
type FnCount = unsafe extern "system" fn(*mut u32) -> c_int;
type FnHandle = unsafe extern "system" fn(u32, *mut *mut c_void) -> c_int;
type FnName = unsafe extern "system" fn(*mut c_void, *mut i8, u32) -> c_int;
type FnUtil = unsafe extern "system" fn(*mut c_void, *mut Util) -> c_int;
type FnTemp = unsafe extern "system" fn(*mut c_void, u32, *mut u32) -> c_int;
type FnMem = unsafe extern "system" fn(*mut c_void, *mut MemInfo) -> c_int;
type FnU32 = unsafe extern "system" fn(*mut c_void, *mut u32) -> c_int;
type FnClock = unsafe extern "system" fn(*mut c_void, u32, *mut u32) -> c_int;

/// 一次采样结果。`available=false` 表示本机不可用，宿主应退回 LHM。
#[derive(Clone, Default)]
pub struct GpuSnapshot {
    pub available: bool,
    pub name: String,
    pub load: f32,
    pub temp: f32,
    /// 显存单位为 MB，与 LHM 展示口径一致。
    pub vram_used_mb: f32,
    pub vram_total_mb: f32,
    pub fan_percent: f32,
    pub clock_mhz: f32,
    pub power_w: f32,
    pub sample_ms: f64,
    pub ticks: u64,
}

impl GpuSnapshot {
    pub fn to_json(&self) -> String {
        let mut o = crate::json::Obj::new();
        o.boolean("avail", self.available);
        o.string("n", &self.name);
        o.fnum("load", self.load as f64);
        o.fnum("temp", self.temp as f64);
        o.fnum("vramUsed", self.vram_used_mb as f64);
        o.fnum("vramTotal", self.vram_total_mb as f64);
        o.fnum("fan", self.fan_percent as f64);
        o.fnum("clock", self.clock_mhz as f64);
        o.fnum("power", self.power_w as f64);
        o.fnum("sampleMs", self.sample_ms);
        o.num("ticks", self.ticks as i64);
        o.finish()
    }
}

struct Nvml {
    lib: *mut c_void,
    dev: *mut c_void,
    name: String,
    get_util: FnUtil,
    get_temp: FnTemp,
    get_mem: FnMem,
    get_fan: FnU32,
    get_power: FnU32,
    get_clock: FnClock,
}

impl Nvml {
    /// 加载 nvml.dll 并取 0 号设备。任何一步失败返回 None（宿主退回 LHM）。
    fn open() -> Option<Nvml> {
        unsafe {
            let mut lib = ptr::null_mut();
            for dll in ["nvml.dll", r"C:\Windows\System32\nvml.dll"] {
                lib = LoadLibraryW(to_wide(dll).as_ptr());
                if !lib.is_null() {
                    break;
                }
            }
            if lib.is_null() {
                return None;
            }

            let f_init: FnInit = resolve(lib, b"nvmlInit_v2\0")?;
            let f_count: FnCount = resolve(lib, b"nvmlDeviceGetCount_v2\0")?;
            let f_handle: FnHandle = resolve(lib, b"nvmlDeviceGetHandleByIndex_v2\0")?;
            let f_name: FnName = resolve(lib, b"nvmlDeviceGetName\0")?;
            let get_util: FnUtil = resolve(lib, b"nvmlDeviceGetUtilizationRates\0")?;
            let get_temp: FnTemp = resolve(lib, b"nvmlDeviceGetTemperature\0")?;
            let get_mem: FnMem = resolve(lib, b"nvmlDeviceGetMemoryInfo\0")?;
            let get_fan: FnU32 = resolve(lib, b"nvmlDeviceGetFanSpeed\0")?;
            let get_power: FnU32 = resolve(lib, b"nvmlDeviceGetPowerUsage\0")?;
            let get_clock: FnClock = resolve(lib, b"nvmlDeviceGetClockInfo\0")?;

            if f_init() != NVML_SUCCESS {
                FreeLibrary(lib);
                return None;
            }

            let mut count: u32 = 0;
            if f_count(&mut count) != NVML_SUCCESS || count == 0 {
                FreeLibrary(lib);
                return None;
            }

            let mut dev: *mut c_void = ptr::null_mut();
            if f_handle(0, &mut dev) != NVML_SUCCESS || dev.is_null() {
                FreeLibrary(lib);
                return None;
            }

            let mut name_buf = [0i8; 96];
            let name = if f_name(dev, name_buf.as_mut_ptr(), name_buf.len() as u32) == NVML_SUCCESS {
                let bytes: Vec<u8> = name_buf
                    .iter()
                    .take_while(|&&c| c != 0)
                    .map(|&c| c as u8)
                    .collect();
                String::from_utf8_lossy(&bytes).to_string()
            } else {
                String::new()
            };

            Some(Nvml {
                lib,
                dev,
                name,
                get_util,
                get_temp,
                get_mem,
                get_fan,
                get_power,
                get_clock,
            })
        }
    }

    /// 采一拍。`read_cold=false` 时跳过风扇/频率/功耗（沿用调用方缓存）。
    fn sample(&self, read_cold: bool) -> Option<(f32, f32, f32, f32, Option<(f32, f32, f32)>)> {
        unsafe {
            let mut util = Util::default();
            if (self.get_util)(self.dev, &mut util) != NVML_SUCCESS {
                return None;
            }
            let mut temp: u32 = 0;
            if (self.get_temp)(self.dev, NVML_TEMPERATURE_GPU, &mut temp) != NVML_SUCCESS {
                return None;
            }
            let mut mem = MemInfo::default();
            if (self.get_mem)(self.dev, &mut mem) != NVML_SUCCESS {
                return None;
            }

            let cold = if read_cold {
                let mut fan: u32 = 0;
                let mut clock: u32 = 0;
                let mut power: u32 = 0;
                let fan_ok = (self.get_fan)(self.dev, &mut fan) == NVML_SUCCESS;
                let clock_ok = (self.get_clock)(self.dev, NVML_CLOCK_SM, &mut clock) == NVML_SUCCESS;
                let power_ok = (self.get_power)(self.dev, &mut power) == NVML_SUCCESS;
                if fan_ok && clock_ok && power_ok {
                    Some((fan as f32, clock as f32, power as f32 / 1000.0))
                } else {
                    None
                }
            } else {
                None
            };

            Some((
                util.gpu as f32,
                temp as f32,
                mem.used as f32 / 1024.0 / 1024.0,
                mem.total as f32 / 1024.0 / 1024.0,
                cold,
            ))
        }
    }
}

impl Drop for Nvml {
    fn drop(&mut self) {
        unsafe {
            FreeLibrary(self.lib);
        }
    }
}

/// 常驻采样器：持有 NVML 会话与冷指标缓存，按拍产出快照。
pub struct GpuSampler {
    nvml: Option<Nvml>,
    name: String,
    snapshot: GpuSnapshot,
    cold: (f32, f32, f32),
}

impl GpuSampler {
    pub fn new() -> GpuSampler {
        match Nvml::open() {
            Some(n) => {
                let available = n.sample(true).is_some();
                let name = n.name.clone();
                let name_clone = name.clone();
                GpuSampler {
                    nvml: Some(n),
                    name,
                    snapshot: GpuSnapshot {
                        available,
                        name: name_clone,
                        ..Default::default()
                    },
                    cold: (0.0, 0.0, 0.0),
                }
            }
            None => GpuSampler {
                name: String::new(),
                nvml: None,
                snapshot: GpuSnapshot::default(),
                cold: (0.0, 0.0, 0.0),
            },
        }
    }

    /// 采一拍并更新内部快照，返回本次耗时（毫秒）。
    pub fn tick(&mut self) -> f64 {
        let started = std::time::Instant::now();
        let Some(nvml) = self.nvml.as_ref() else {
            self.snapshot.available = false;
            return 0.0;
        };

        self.snapshot.ticks += 1;
        let read_cold = self.snapshot.ticks % COLD_EVERY == 1;

        match nvml.sample(read_cold) {
            Some((load, temp, used, total, cold)) => {
                if let Some(c) = cold {
                    self.cold = c;
                }
                self.snapshot.available = true;
                self.snapshot.load = load;
                self.snapshot.temp = temp;
                self.snapshot.vram_used_mb = used;
                self.snapshot.vram_total_mb = total;
                self.snapshot.fan_percent = self.cold.0;
                self.snapshot.clock_mhz = self.cold.1;
                self.snapshot.power_w = self.cold.2;
            }
            None => {
                // 单拍失败不改 available，避免偶发抖动导致宿主来回切实现
            }
        }

        let ms = started.elapsed().as_secs_f64() * 1000.0;
        self.snapshot.sample_ms = ms;
        ms
    }

    pub fn snapshot(&self) -> GpuSnapshot {
        self.snapshot.clone()
    }
}

fn resolve<T: Copy>(lib: *mut c_void, name: &[u8]) -> Option<T> {
    unsafe {
        let p = GetProcAddress(lib, name.as_ptr());
        if p.is_null() {
            None
        } else {
            Some(std::mem::transmute_copy::<*mut c_void, T>(&p))
        }
    }
}

fn to_wide(s: &str) -> Vec<u16> {
    let mut v: Vec<u16> = s.encode_utf16().collect();
    v.push(0);
    v
}

/// `nvml-probe` 模式：一次性度量，输出与 C# 侧基准同样的指标。
pub fn run_probe() {
    let mut sampler = GpuSampler::new();
    match sampler.nvml {
        None => {
            println!("nvml 不可用（无 nvml.dll 或无 N 卡），宿主会退回 LHM");
            return;
        }
        Some(_) => {}
    }

    println!("设备名：{}", if sampler.name.is_empty() { "(未取到)" } else { &sampler.name });

    // 预热
    for _ in 0..5 {
        sampler.tick();
        std::thread::sleep(std::time::Duration::from_millis(100));
    }

    let mut samples: Vec<f64> = Vec::with_capacity(60);
    for _ in 0..60 {
        samples.push(sampler.tick());
        std::thread::sleep(std::time::Duration::from_millis(100));
    }

    samples.sort_by(|a, b| a.partial_cmp(b).unwrap());
    let avg: f64 = samples.iter().sum::<f64>() / samples.len() as f64;
    let p50 = samples[samples.len() / 2];
    let p95 = samples[(samples.len() as f64 * 0.95) as usize];
    let max = samples[samples.len() - 1];

    let snap = sampler.snapshot();
    println!(
        "读数：占用 {:.0}% | 温度 {:.0}C | 显存 {:.0}/{:.0}MB | 风扇 {:.0}% | 频率 {:.0}MHz | 功耗 {:.0}W",
        snap.load, snap.temp, snap.vram_used_mb, snap.vram_total_mb, snap.fan_percent, snap.clock_mhz, snap.power_w
    );
    println!(
        "样本 {} 拍 | 平均 {:.3} ms | p50 {:.3} | p95 {:.3} | 最大 {:.3}",
        samples.len(),
        avg,
        p50,
        p95,
        max
    );
    println!("按 1 Hz：占单核 {:.2}% | 占 12 核整机 {:.3}%", avg / 10.0, avg / 10.0 / 12.0);
}
