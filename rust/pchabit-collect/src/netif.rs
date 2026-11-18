//! 系统级网络吞吐：用 PDH 计数器读每块网卡的收发速率并求和。
//!
//! 为什么不用 netd 的 `TOTALS`：netd 非提权时拿不到系统级统计（`estats=false`），
//! 系统上下行要靠宿主测出来再推给它（`SYS up down`）。WPF 版测的是
//! LHM 的吞吐传感器，Rust 侧没有 LHM，就用性能计数器直接读，口径一致。
//!
//! 口径：排除 loopback 与虚拟网卡（C# `IsVirtualNic` 同一张关键词表），
//! 否则代理类工具（sing-box / TUN）会让一份流量被数两次。

const PDH_FMT_DOUBLE: u32 = 0x0000_0200;
const PDH_MORE_DATA: u32 = 0x8000_07D2;

#[repr(C)]
struct FmtCounterValue {
    status: u32,
    _pad: u32,
    value_bits: u64,
}

#[repr(C)]
struct FmtCounterValueItemW {
    name: *mut u16,
    value: FmtCounterValue,
}

#[link(name = "pdh")]
extern "system" {
    fn PdhOpenQueryW(data_source: *const u16, user_data: usize, query: *mut isize) -> u32;
    fn PdhAddEnglishCounterW(
        query: isize,
        path: *const u16,
        user_data: usize,
        counter: *mut isize,
    ) -> u32;
    fn PdhCollectQueryData(query: isize) -> u32;
    fn PdhGetFormattedCounterArrayW(
        counter: isize,
        format: u32,
        size: *mut u32,
        count: *mut u32,
        items: *mut FmtCounterValueItemW,
    ) -> u32;
    fn PdhCloseQuery(query: isize) -> u32;
}

const SKIP_KEYS: [&str; 13] = [
    "virtual", "vmware", "hyper-v", "hyper v", "vbox", "loopback", "tunnel", "tap", "tun",
    "bluetooth", "zerotier", "tailscale", "wan miniport",
];

fn skipped(name: &str) -> bool {
    let lower = name.to_ascii_lowercase();
    SKIP_KEYS.iter().any(|k| lower.contains(k))
}

fn wide(s: &str) -> Vec<u16> {
    let mut v: Vec<u16> = s.encode_utf16().collect();
    v.push(0);
    v
}

pub struct NetMeter {
    query: isize,
    rx: isize,
    tx: isize,
    last: Option<(u64, u64)>,
    primed: bool,
}

impl NetMeter {
    pub fn new() -> Option<NetMeter> {
        unsafe {
            let mut query: isize = 0;
            if PdhOpenQueryW(std::ptr::null(), 0, &mut query) != 0 {
                return None;
            }
            let rx_path = wide(r"\Network Interface(*)\Bytes Received/sec");
            let tx_path = wide(r"\Network Interface(*)\Bytes Sent/sec");
            let mut rx: isize = 0;
            let mut tx: isize = 0;
            if PdhAddEnglishCounterW(query, rx_path.as_ptr(), 0, &mut rx) != 0
                || PdhAddEnglishCounterW(query, tx_path.as_ptr(), 0, &mut tx) != 0
            {
                let _ = PdhCloseQuery(query);
                return None;
            }
            // 先收一次，让速率有基准（否则第一秒是 0）
            let _ = PdhCollectQueryData(query);
            Some(NetMeter {
                query,
                rx,
                tx,
                last: None,
                primed: false,
            })
        }
    }

    fn sum(&self, counter: isize, format: u32) -> f64 {
        unsafe {
            let mut size: u32 = 0;
            let mut count: u32 = 0;
            let st = PdhGetFormattedCounterArrayW(
                counter,
                format,
                &mut size,
                &mut count,
                std::ptr::null_mut(),
            );
            if st != PDH_MORE_DATA && st != 0 {
                return 0.0;
            }
            if size == 0 {
                return 0.0;
            }
            let mut buf = vec![0u8; size as usize];
            let items = buf.as_mut_ptr() as *mut FmtCounterValueItemW;
            let st2 =
                PdhGetFormattedCounterArrayW(counter, format, &mut size, &mut count, items);
            if st2 != 0 {
                return 0.0;
            }
            let mut total = 0.0f64;
            for i in 0..count as usize {
                let item = &*items.add(i);
                let name = if item.name.is_null() {
                    String::new()
                } else {
                    let mut len = 0usize;
                    while *item.name.add(len) != 0 {
                        len += 1;
                    }
                    String::from_utf16_lossy(std::slice::from_raw_parts(item.name, len))
                };
                if skipped(&name) {
                    continue;
                }
                let v = f64::from_bits(item.value.value_bits);
                if v.is_finite() && v > 0.0 {
                    total += v;
                }
            }
            total
        }
    }

    /// 返回「自上一拍以来」的 (上行字节, 下行字节)。第一拍返回 None。
    pub fn sample(&mut self, elapsed_sec: f64) -> Option<(i64, i64)> {
        unsafe {
            let _ = PdhCollectQueryData(self.query);
        }
        let rate_rx = self.sum(self.rx, PDH_FMT_DOUBLE);
        let rate_tx = self.sum(self.tx, PDH_FMT_DOUBLE);
        let sec = elapsed_sec.clamp(0.05, 10.0);
        let up = (rate_tx * sec).max(0.0) as i64;
        let down = (rate_rx * sec).max(0.0) as i64;

        if !self.primed {
            self.primed = true;
            self.last = Some((up as u64, down as u64));
            return None;
        }
        // 速率型计数器本身已经是「每秒字节」，这里按真实间隔折算成字节
        let _ = self.last;
        self.last = Some((up as u64, down as u64));
        Some((up, down))
    }
}

impl Drop for NetMeter {
    fn drop(&mut self) {
        unsafe {
            let _ = PdhCloseQuery(self.query);
        }
    }
}

/// 取当前网卡累计字节（只用于校验/日志，不做记账）。
pub fn snapshot_bytes() -> Option<(u64, u64)> {
    // 用性能计数器的原始值不方便，这里直接靠 PDH 速率换算不到累计，故省略。
    None
}
