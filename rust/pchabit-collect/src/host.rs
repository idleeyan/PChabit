//! 采集宿主：把 netd 读数、本地硬件采样、前台窗口、键鼠钩子合成库里的行。
//!
//! 线程分工（每个 1 Hz，互不阻塞）：
//! - `netd`：连 netd 拉 TOTALS / ALLTODAY / GPU，算系统与每进程增量；
//! - `sample`：CPU/内存本地采样，与 GPU、网络增量合成「硬件分钟样本」并落库；
//! - `app`：前台窗口快照 → AppSessions 会话切段；
//! - `flush`：每 15 s 把键鼠内存累计与进程小时增量刷进库。
//!
//! 单写者约束：WPF 版（`PChabit.exe`）在跑时拒绝启动——两个宿主同时写库会把
//! 同一分钟写成两行/两份增量，数据不可对账。

use std::collections::HashMap;
use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

use serde_json::Value;

use crate::input::{self, InputShared};
use crate::netd::{self, Pipe};
use crate::sampler::{self, CpuSampler};
use crate::store::{AppRow, HwBucket, Store};
use crate::timefmt;
use crate::win;

pub type LogFn = Arc<dyn Fn(String) + Send + Sync>;

#[derive(Clone)]
pub struct Config {
    pub db: PathBuf,
    pub hooks: bool,
    pub log: LogFn,
    /// 自测用：跑满 N 秒后自动停（None = 常驻）
    pub run_for: Option<u64>,
    /// 是否起浏览器扩展通道（`127.0.0.1:ws_port`），网页会话靠它产数据
    pub websync: bool,
    pub ws_port: u16,
}

#[derive(Default, Clone)]
struct GpuSnap {
    available: bool,
    load: f64,
    temp: f64,
    vram_used: f64,
    vram_total: f64,
}

#[derive(Default, Clone, Copy)]
struct NetDelta {
    up: i64,
    down: i64,
    peak_up: f64,
    peak_down: f64,
}

struct Ctx {
    store: Arc<Mutex<Store>>,
    gpu: Mutex<GpuSnap>,
    net: Mutex<NetDelta>,
    proc: Mutex<HashMap<String, (i64, i64, i64)>>,
    /// 最近一拍的系统级网络增量（字节），由 `sample_loop` 用网卡计数器测出
    last_sys: Mutex<(i64, i64)>,
    /// netd 只有一条连接：netd 的命名管道同一时刻只服务一个实例，多线程共用一条即可
    netd: Mutex<Option<Pipe>>,
    running: AtomicBool,
    log: LogFn,
}

impl Ctx {
    fn say(&self, msg: impl Into<String>) {
        (self.log)(msg.into());
    }

    /// 走 netd 的命令入口：失败时重连一次再重试（netd 重启/超时自杀都能自愈）。
    fn netd_cmd(&self, text: &str) -> Option<String> {
        let mut guard = self.netd.lock().unwrap();
        if let Some(p) = guard.as_mut() {
            if let Some(r) = p.cmd(text) {
                return Some(r);
            }
        }
        let mut fresh = Pipe::connect(3)?;
        let r = fresh.cmd(text);
        *guard = Some(fresh);
        r
    }
}

pub struct Host {
    ctx: Arc<Ctx>,
    shared_input: Option<Arc<InputShared>>,
    web: Arc<Mutex<crate::websync::Engine>>,
    ws: Option<crate::websync::Server>,
}

/// WPF 是否真的在采集：进程在跑 **且** 心跳文件新鲜。
///
/// WPF 只读模式（`PCHABIT_COLLECT=shell`）不写心跳，所以此时宿主可以正常启动；
/// WPF 崩溃留下的旧心跳超过 5 分钟就作废，宿主人不会再被尸体挡住。
fn wpf_is_collecting() -> bool {
    if !win::process_running("PChabit.exe") {
        return false;
    }
    let Some(base) = std::env::var("LOCALAPPDATA").ok().map(PathBuf::from) else {
        return true; // 拿不到路径就退回保守判断
    };
    let beat = base.join("PChabit").join("Data").join(".wpf-collecting");
    let Ok(text) = std::fs::read_to_string(&beat) else {
        return false; // 只读模式：根本没写心跳
    };
    let Ok(ms) = text.trim().parse::<u64>() else {
        return false;
    };
    let now = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_millis() as u64)
        .unwrap_or(0);
    now.saturating_sub(ms) < 300_000
}

impl Host {
    /// 启动采集。返回句柄，`stop()` 后线程退出、netd 收到 STOP。
    pub fn start(cfg: Config) -> Result<Host, String> {
        if wpf_is_collecting() {
            return Err(
                "检测到 PChabit.exe 正在采集：采集宿主要求单写者，请先关闭 WPF 版（或让它以只读模式运行）"
                    .to_string(),
            );
        }

        let store = Arc::new(Mutex::new(Store::open(&cfg.db)?));
        let (today, seeds) = {
            let s = store.lock().unwrap();
            let today = timefmt::date(&timefmt::now());
            let seeds = s.today_proc_totals(&today);
            (today, seeds)
        };
        (cfg.log)(format!(
            "采集宿主启动：库 {} / 今日已有 {} 个进程累计可以回填 netd",
            cfg.db.display(),
            seeds.len()
        ));

        let ctx = Arc::new(Ctx {
            store: Arc::clone(&store),
            gpu: Mutex::new(GpuSnap::default()),
            net: Mutex::new(NetDelta::default()),
            proc: Mutex::new(HashMap::new()),
            last_sys: Mutex::new((0, 0)),
            netd: Mutex::new(None),
            running: AtomicBool::new(true),
            log: Arc::clone(&cfg.log),
        });

        // netd：先连，连不上就拉起；随后回填今日累计
        let mut pipe = match netd::ensure_daemon() {
            Some((p, spawned)) => {
                (cfg.log)(format!(
                    "netd：{}",
                    if spawned { "已拉起新实例" } else { "复用既有实例" }
                ));
                p
            }
            None => return Err("netd 不可用：找不到 pchabit-netd.exe 或管道连不上".to_string()),
        };
        if !netd::seed(&mut pipe, &today, &seeds) {
            (cfg.log)("netd：今日累计回填失败（将从 0 起算，今日数据可能有偏差）".to_string());
        }
        *ctx.netd.lock().unwrap() = Some(pipe);

        // 键鼠钩子
        let shared_input = if cfg.hooks {
            let sh = Arc::new(InputShared::new());
            if input::start(Arc::clone(&sh)) {
                (cfg.log)("键鼠钩子：键盘 + 鼠标均安装成功".to_string());
                Some(sh)
            } else {
                (cfg.log)("键鼠钩子安装失败（权限或其它钩子冲突），本轮不记录键鼠".to_string());
                None
            }
        } else {
            None
        };

        // 线程 1：netd 拉数
        {
            let ctx = Arc::clone(&ctx);
            std::thread::spawn(move || netd_loop(ctx));
        }

        // 线程 2：硬件分钟样本
        {
            let ctx = Arc::clone(&ctx);
            std::thread::spawn(move || sample_loop(ctx));
        }

        // 线程 3：前台应用会话
        {
            let ctx = Arc::clone(&ctx);
            std::thread::spawn(move || app_loop(ctx));
        }

        // 线程 4：键鼠与进程小时增量落库
        {
            let ctx = Arc::clone(&ctx);
            let sh = shared_input.clone();
            std::thread::spawn(move || flush_loop(ctx, sh));
        }

        // 浏览器扩展通道（网页会话的数据源）
        let web = Arc::new(Mutex::new(crate::websync::Engine::new()));
        let ws = if cfg.websync {
            match crate::websync::start(
                cfg.ws_port,
                Arc::clone(&store),
                Arc::clone(&web),
                Arc::clone(&cfg.log),
            ) {
                Ok(s) => Some(s),
                Err(e) => {
                    (cfg.log)(format!(
                        "浏览器通道未启动：{e}（网页会话本轮不产新数据，端口可能被 WPF 版占用）"
                    ));
                    None
                }
            }
        } else {
            None
        };
        // 设置页的 `WebSocketPort`：除了扩展固定连的 8765，再额外监听一个自定义端口，
        // 供自建客户端/调试工具接入（两者共用同一个 Engine，数据不重复）。
        let _extra_ws = if cfg.websync {
            // 设置里这个键是字符串（"8765"），也要接受纯数字
            let raw = crate::settings::str_of("WebSocketPort", "8765");
            let custom = raw
                .trim()
                .parse::<i64>()
                .unwrap_or_else(|_| crate::settings::i64_of("WebSocketPort", 8765));
            if custom > 0 && custom <= 65535 && custom as u16 != cfg.ws_port {
                match crate::websync::start(
                    custom as u16,
                    Arc::clone(&store),
                    Arc::clone(&web),
                    Arc::clone(&cfg.log),
                ) {
                    Ok(s) => {
                        (cfg.log)(format!("浏览器通道额外监听 {}（设置页 WebSocketPort）", custom));
                        Some(s)
                    }
                    Err(e) => {
                        (cfg.log)(format!("额外端口 {custom} 未启动：{e}"));
                        None
                    }
                }
            } else {
                None
            }
        } else {
            None
        };

        // 线程 5：就业中网页会话每 30 s 兜底写一次（进程被杀也不丢整段）
        {
            let ctx = Arc::clone(&ctx);
            let web = Arc::clone(&web);
            std::thread::spawn(move || web_save_loop(ctx, web));
        }

        // 线程 6：日汇总（每小时检查一次昨天；启动 10 s 后先跑一轮）
        {
            let ctx = Arc::clone(&ctx);
            std::thread::spawn(move || daily_loop(ctx));
        }

        let host = Host {
            ctx: Arc::clone(&ctx),
            shared_input,
            web,
            ws,
        };

        if let Some(secs) = cfg.run_for {
            let ctx2 = Arc::clone(&ctx);
            std::thread::spawn(move || {
                std::thread::sleep(Duration::from_secs(secs));
                ctx2.running.store(false, Ordering::SeqCst);
            });
        }

        Ok(host)
    }

    pub fn stop(&self) {
        self.ctx.running.store(false, Ordering::SeqCst);
        // 给采集线程一个退出窗口：sample_loop 要先收口「当前分钟」（否则最后一分钟
        // 的网络行/硬件样本只剩 20 秒那份兜底），app_loop 要把当前会话写掉。
        std::thread::sleep(Duration::from_millis(2200));
        // 最后一次把内存里的增量落库（钩子累计不落就走丢）
        if let Some(sh) = &self.shared_input {
            let store = self.ctx.store.lock().unwrap();
            match input::flush(sh, &store) {
                Ok((k, c)) => self
                    .ctx
                    .say(format!("退出前落库键鼠：{k} 键 / {c} 击")),
                Err(e) => self.ctx.say(format!("退出前落库键鼠失败：{e}")),
            }
        }
        // 浏览器会话先收尾（带 URL 的会话不落就是整段丢）
        if let Some(ws) = &self.ws {
            ws.stop();
        }
        let web_n = {
            let store = self.ctx.store.lock().unwrap();
            let mut eng = self.web.lock().unwrap();
            eng.finalize_all(&store, &self.ctx.log)
        };
        if web_n > 0 {
            self.ctx.say(format!("退出前收尾网页会话：{web_n} 个"));
        }
        // 让 netd 也退（它自己 90 秒无请求也会退，这里显式更快）
        if let Some(p) = self.ctx.netd.lock().unwrap().as_mut() {
            netd::stop(p);
        }
        self.ctx.say("采集宿主已停止");
    }

    pub fn is_running(&self) -> bool {
        self.ctx.running.load(Ordering::SeqCst)
    }
}

fn netd_loop(ctx: Arc<Ctx>) {
    // netd 非提权运行时 TOTALS 的 su/sd 恒为 0（拿不到系统级统计），所以系统增量
    // 统一由「每进程今日累计之和」推出来——这样与 ProcessNetworkUsages 也能对上账。
    let mut prev_proc: HashMap<String, (i64, i64)> = HashMap::new();
    let mut first = true;

    while ctx.running.load(Ordering::SeqCst) {
        let started = Instant::now();

        // 一次 SNAP 同时拿到 today（累计）与 rate（B/s），省一个往返
        let snap = ctx
            .netd_cmd("SNAP 12")
            .and_then(|t| serde_json::from_str::<Value>(&t).ok());

        if let Some(v) = snap.as_ref() {
            let today = v["today"].as_array().cloned().unwrap_or_default();
            let rate = v["rate"].as_array().cloned().unwrap_or_default();

            let mut total_up = 0i64;
            let mut total_down = 0i64;
            let mut deltas: Vec<(String, i64, i64)> = Vec::new();

            for item in &today {
                let name = item["n"].as_str().unwrap_or("").to_string();
                if name.is_empty() {
                    continue;
                }
                let up = item["u"].as_i64().unwrap_or(0);
                let down = item["d"].as_i64().unwrap_or(0);
                total_up += up;
                total_down += down;

                if let Some((pu, pd)) = prev_proc.insert(name.clone(), (up, down)) {
                    let d_up = (up - pu).max(0);
                    let d_down = (down - pd).max(0);
                    if d_up > 0 || d_down > 0 {
                        deltas.push((name, d_up, d_down));
                    }
                }
            }

            // netd 的 ESTATS 逐连接计数在本机实测严重虚高（Doubao 20 秒报 2.6 GB，
            // 网卡真实只有 2 MB）。这里按「系统级增量」等比缩放每进程增量：
            // 分布保留，总量与网卡读数对齐，写进库的每进程行才是可信的。
            let (sys_up, sys_down) = *ctx.last_sys.lock().unwrap();
            let raw_up: i64 = deltas.iter().map(|d| d.1).sum();
            let raw_down: i64 = deltas.iter().map(|d| d.2).sum();
            let scale_up = if raw_up > 0 && raw_up as f64 > sys_up as f64 * 1.05 {
                sys_up as f64 / raw_up as f64
            } else {
                1.0
            };
            let scale_down = if raw_down > 0 && raw_down as f64 > sys_down as f64 * 1.05 {
                sys_down as f64 / raw_down as f64
            } else {
                1.0
            };

            let mut proc_accum = ctx.proc.lock().unwrap();
            for (name, d_up, d_down) in deltas {
                let up = (d_up as f64 * scale_up) as i64;
                let down = (d_down as f64 * scale_down) as i64;
                let entry = proc_accum.entry(name).or_insert((0, 0, 0));
                entry.0 += up;
                entry.1 += down;
            }

            for r in &rate {
                let name = r["n"].as_str().unwrap_or("");
                if name.is_empty() {
                    continue;
                }
                let bps = (r["u"].as_f64().unwrap_or(0.0) + r["d"].as_f64().unwrap_or(0.0)).max(0.0)
                    * scale_down.max(scale_up);
                if bps > 0.0 {
                    let entry = proc_accum.entry(name.to_string()).or_insert((0, 0, 0));
                    if (bps as i64) > entry.2 {
                        entry.2 = bps as i64;
                    }
                }
            }
            drop(proc_accum);

            if first {
                ctx.say(format!(
                    "netd 首拍基准：{} 进程 / 今日累计 {:.1} MB 上行 {:.1} MB 下行",
                    prev_proc.len(),
                    total_up as f64 / 1e6,
                    total_down as f64 / 1e6
                ));
                first = false;
            }
        }

        // GPU 快照
        if let Some(text) = ctx.netd_cmd("GPU") {
            if let Ok(v) = serde_json::from_str::<Value>(&text) {
                let avail = v["avail"].as_bool().unwrap_or(false);
                let load = v["load"].as_f64().unwrap_or(0.0);
                let temp = v["temp"].as_f64().unwrap_or(0.0);
                let used = v["vramUsed"].as_f64().unwrap_or(0.0);
                let total = v["vramTotal"].as_f64().unwrap_or(0.0);
                *ctx.gpu.lock().unwrap() = GpuSnap {
                    available: avail,
                    load,
                    temp,
                    vram_used: used,
                    vram_total: total,
                };
            }
        }

        let spent = started.elapsed().as_millis() as u64;
        let sleep = 1000u64.saturating_sub(spent);
        std::thread::sleep(Duration::from_millis(sleep.max(1)));
    }

    // 退出由 Host::stop() 统一发 STOP，这里只报告规模
    ctx.say(format!("netd 轮询退出：跟踪过 {} 个进程", prev_proc.len()));
}

fn sample_loop(ctx: Arc<Ctx>) {
    let mut cpu = CpuSampler::new();
    let mut bucket = HwBucket::default();
    let mut last_key = String::new();
    let mut last_flush = Instant::now();
    let mut meter = crate::netif::NetMeter::new();
    if meter.is_none() {
        ctx.say("网卡计数器不可用：系统上下行将记 0（硬件样本网络列为空）".to_string());
    }
    let mut disk = crate::netif::DiskMeter::new();
    if disk.is_none() {
        ctx.say("磁盘计数器不可用：DiskActivity/DiskRead/DiskWrite 三列将为空".to_string());
    }
    let mut last_net_tick = Instant::now();

    while ctx.running.load(Ordering::SeqCst) {
        let t = timefmt::now();
        let key = timefmt::minute_key(&t);

        if bucket.key.is_empty() {
            bucket.key = key.clone();
            last_key = key.clone();
        }

        // 分钟翻转：先把上一分钟写掉，再开新桶
        if key != last_key {
            flush_minute(&ctx, &mut bucket, &last_key);
            bucket = HwBucket {
                key: key.clone(),
                ..Default::default()
            };
            last_key = key.clone();
            last_flush = Instant::now();
        }

        bucket.samples += 1;

        // 系统级网络：网卡计数器测增量，推给 netd（它的 SYS 累计靠宿主喂），
        // 同时进本分钟的硬件样本与网络分钟行。
        if let Some(m) = meter.as_mut() {
            let elapsed = last_net_tick.elapsed().as_secs_f64();
            if let Some((up, down)) = m.sample(elapsed) {
                // 网卡计数器回绕/驱动重载会让单拍增量变成天文数字（实测出现过 51 GB/分钟的行）。
                // 以 200 MB/s（约 1.6 Gbps）为上界，超界视为脏增量、本拍丢弃，也不喂 netd 的 SYS。
                let sec = elapsed.clamp(0.05, 10.0);
                let cap = (200_000_000.0 * sec + 1024.0) as i64;
                let plausible = up.abs() <= cap && down.abs() <= cap;
                if plausible && (up > 0 || down > 0) {
                    let _ = ctx.netd_cmd(&format!("SYS {up} {down}"));
                    let mut net = ctx.net.lock().unwrap();
                    net.up += up;
                    net.down += down;
                    net.peak_up = net.peak_up.max(up as f64 / sec);
                    net.peak_down = net.peak_down.max(down as f64 / sec);
                } else if !plausible {
                    ctx.say(format!(
                        "网卡增量异常（上 {up} / 下 {down}，上界 {cap} 字节），本拍丢弃"
                    ));
                }
                *ctx.last_sys.lock().unwrap() = (up, down);
            }
            last_net_tick = Instant::now();
        }

        // 磁盘：活动率进均值、读写进峰值（与 WPF 侧 DISK.* 三键同口径）
        if let Some(d) = disk.as_mut() {
            if let Some((act, read, write)) = d.sample() {
                bucket.disk_act_sum += act;
                bucket.disk_act_n += 1;
                if read > bucket.disk_read_max {
                    bucket.disk_read_max = read;
                }
                if write > bucket.disk_write_max {
                    bucket.disk_write_max = write;
                }
                bucket.flags |= crate::store::F_DISK_ACT
                    | crate::store::F_DISK_READ
                    | crate::store::F_DISK_WRITE;
            }
        }

        if let Some(load) = cpu.tick() {
            bucket.cpu_sum += load;
            bucket.cpu_n += 1;
            if load > bucket.cpu_max {
                bucket.cpu_max = load;
            }
            bucket.flags |= crate::store::F_CPU_LOAD;
        }

        if let Some(m) = sampler::memory() {
            bucket.mem_sum += m.load;
            bucket.mem_n += 1;
            if m.load > bucket.mem_max {
                bucket.mem_max = m.load;
            }
            if m.used_mb > bucket.mem_used_max {
                bucket.mem_used_max = m.used_mb;
            }
            bucket.flags |= crate::store::F_MEM_LOAD | crate::store::F_MEM_USED;
        }

        {
            let g = ctx.gpu.lock().unwrap();
            if g.available {
                bucket.gpu_sum += g.load;
                bucket.gpu_n += 1;
                if g.load > bucket.gpu_max {
                    bucket.gpu_max = g.load;
                }
                if g.temp > bucket.gpu_temp {
                    bucket.gpu_temp = g.temp;
                }
                if g.vram_used > bucket.vram_used {
                    bucket.vram_used = g.vram_used;
                }
                if g.vram_total > bucket.vram_total {
                    bucket.vram_total = g.vram_total;
                }
                bucket.flags |= crate::store::F_GPU_LOAD | crate::store::F_GPU_TEMP;
                if g.vram_used > 0.0 {
                    bucket.flags |= crate::store::F_VRAM_USED;
                }
                if g.vram_total > 0.0 {
                    bucket.flags |= crate::store::F_VRAM_TOTAL;
                }
            }
        }

        // 每 20 秒兜底写一次当前分钟（进程被杀也不至于丢整分钟）
        if last_flush.elapsed().as_secs() >= 20 {
            let _ = write_bucket(&ctx, &mut bucket);
            last_flush = Instant::now();
        }
        sleep_until_next_second();
    }

    // 退出前把最后这一分钟也写掉（否则「当前分钟」的网络行会丢）
    flush_minute(&ctx, &mut bucket, &last_key);
}

/// 收口一分钟：硬件样本 upsert + 网络分钟行累加，然后清空网络增量。
fn flush_minute(ctx: &Ctx, b: &mut HwBucket, key: &str) {
    let _ = write_bucket(ctx, b);
    let (up, down, peak_up, peak_down) = {
        let mut net = ctx.net.lock().unwrap();
        let v = *net;
        *net = NetDelta::default();
        (v.up, v.down, v.peak_up, v.peak_down)
    };
    if (up > 0 || down > 0) && !key.is_empty() {
        let store = ctx.store.lock().unwrap();
        let _ = store.net_minute_add(key, up, down, peak_up, peak_down);
    }
}

/// 把当前桶写进库：网络列在此刻从 `ctx.net` 取（折算成「本分钟平均 B/s」）。
fn write_bucket(ctx: &Ctx, b: &mut HwBucket) -> Result<(), String> {
    if b.key.is_empty() || b.samples <= 0 {
        return Ok(());
    }
    {
        let net = ctx.net.lock().unwrap();
        if net.up > 0 || net.down > 0 {
            b.net_up_sum = net.up as f64;
            b.net_down_sum = net.down as f64;
            b.net_n = b.samples.max(1);
            b.flags |= crate::store::F_NET_UP | crate::store::F_NET_DOWN;
        }
    }
    let store = ctx.store.lock().unwrap();
    store.hw_upsert(b)
}

fn sleep_until_next_second() {
    let t = timefmt::now();
    let ms = (t.wMilliseconds as u64).min(999);
    std::thread::sleep(Duration::from_millis(1000 - ms));
}

/// 前台应用会话：进程或标题变化即切段，落一行 AppSessions。
fn app_loop(ctx: Arc<Ctx>) {
    struct Current {
        start_stamp: String,
        start_instant: Instant,
        process: String,
        app_name: String,
        path: String,
        title: String,
        class: String,
        x: f64,
        y: f64,
        w: f64,
        h: f64,
        max: bool,
    }
    let mut current: Option<Current> = None;
    let mut seen: u32 = 0;
    let mut misses: u32 = 0;

    while ctx.running.load(Ordering::SeqCst) {
        let snap = ctx.netd_cmd("APP");
        if snap.is_none() {
            misses += 1;
            if misses == 3 {
                ctx.say("APP 快照连续失败：netd 不可用，继续重试");
            }
        } else {
            misses = 0;
        }
        let snap = snap.and_then(|t| serde_json::from_str::<Value>(&t).ok());
        if let Some(v) = snap {
            let ok = v["ok"].as_bool().unwrap_or(false);
            let proc_stem = v["proc"].as_str().unwrap_or("").to_string();
            let title = v["title"].as_str().unwrap_or("").to_string();
            let class = v["class"].as_str().unwrap_or("").to_string();
            let pid = v["pid"].as_u64().unwrap_or(0) as u32;
            let x = v["x"].as_f64().unwrap_or(0.0);
            let y = v["y"].as_f64().unwrap_or(0.0);
            let w = v["w"].as_f64().unwrap_or(0.0);
            let h = v["h"].as_f64().unwrap_or(0.0);
            let maximized = v["max"].as_bool().unwrap_or(false);

            if ok && !proc_stem.is_empty() {
                // 自家进程与任务栏小窗这类「工具窗」不能算前台应用：
                // 只读模式下 WPF 常驻，任务栏小窗（无标题、极扁）会抢到前台，
                // 之前会把会话一直粘在 PChabit.exe 上，导致整段时间没有切段。
                let own = matches!(
                    proc_stem.to_ascii_lowercase().as_str(),
                    "pchabit" | "pchabit-shell" | "pchabit-collect" | "pchabit-netd"
                );
                let tool_window = title.trim().is_empty() && h < 120.0;
                if own || tool_window {
                    continue;
                }
                seen += 1;
                if seen == 1 {
                    ctx.say(format!(
                        "前台首个快照：{proc_stem}.exe / {} / {}x{}",
                        title, w, h
                    ));
                }
                let process = format!("{proc_stem}.exe");
                let changed = match &current {
                    None => true,
                    Some(c) => c.process != process || c.title != title,
                };
                if changed {
                    if let Some(c) = current.take() {
                        let end = timefmt::stamp7(&timefmt::now());
                        let elapsed = c.start_instant.elapsed();
                        let ticks = (elapsed.as_millis() as i64) * 10_000;
                        let row = AppRow {
                            start: c.start_stamp,
                            end,
                            duration_ticks: ticks,
                            process: c.process,
                            path: c.path,
                            app_name: c.app_name,
                            title: c.title,
                            class: c.class,
                            x: c.x,
                            y: c.y,
                            width: c.w,
                            height: c.h,
                            maximized: c.max,
                            category: None,
                        };
                        let store = ctx.store.lock().unwrap();
                        let category = store.category_of(&row.process);
                        let mut row2 = row;
                        row2.category = category;
                        match store.app_insert(&row2) {
                            Ok(_) => ctx.say(format!(
                                "会话入库 {} / {:.1}s",
                                row2.process,
                                row2.duration_ticks as f64 / 10_000_000.0
                            )),
                            Err(e) => ctx.say(format!("会话入库失败: {e}")),
                        }
                    }

                    let path = win::process_path(pid);
                    let app_name = {
                        let store = ctx.store.lock().unwrap();
                        store.app_name_of(&path, proc_stem.trim_end_matches(".exe"))
                    };
                    current = Some(Current {
                        start_stamp: timefmt::stamp7(&timefmt::now()),
                        start_instant: Instant::now(),
                        process,
                        app_name,
                        path,
                        title,
                        class,
                        x,
                        y,
                        w,
                        h,
                        max: maximized,
                    });
                }
            }
        }

        std::thread::sleep(Duration::from_millis(900));
    }

    // 退出：把当前会话收尾
    if let Some(c) = current.take() {
        let end = timefmt::stamp7(&timefmt::now());
        let ticks = (c.start_instant.elapsed().as_millis() as i64) * 10_000;
        let row = AppRow {
            start: c.start_stamp,
            end,
            duration_ticks: ticks,
            process: c.process.clone(),
            path: c.path,
            app_name: c.app_name,
            title: c.title,
            class: c.class,
            x: c.x,
            y: c.y,
            width: c.w,
            height: c.h,
            maximized: c.max,
            category: None,
        };
        let store = ctx.store.lock().unwrap();
        let category = store.category_of(&row.process);
        let mut row2 = row;
        row2.category = category;
        match store.app_insert(&row2) {
            Ok(_) => ctx.say(format!(
                "退出前收尾会话：{} / {:.1}s",
                row2.process,
                row2.duration_ticks as f64 / 10_000_000.0
            )),
            Err(e) => ctx.say(format!("退出前收尾会话失败：{e}")),
        }
    } else {
        ctx.say(format!("应用会话线程退出：共计 {seen} 个有效快照"));
    }
}

fn flush_loop(ctx: Arc<Ctx>, shared: Option<Arc<InputShared>>) {
    let mut last_hour: Option<u32> = None;
    while ctx.running.load(Ordering::SeqCst) {
        std::thread::sleep(Duration::from_secs(15));
        if !ctx.running.load(Ordering::SeqCst) {
            break;
        }

        let t = timefmt::now();
        let date = timefmt::date(&t);
        let hour = timefmt::hour(&t);

        // 进程网络小时行
        let delta: Vec<(String, (i64, i64, i64))> = {
            let mut accum = ctx.proc.lock().unwrap();
            accum.drain().collect()
        };
        if !delta.is_empty() {
            let store = ctx.store.lock().unwrap();
            let mut rows = 0;
            for (name, (up, down, peak)) in delta {
                if store.proc_hour_add(&date, hour, &name, up, down, peak).is_ok() {
                    rows += 1;
                }
            }
            ctx.say(format!(
                "网络小时行：{} 进程（{} 时）",
                rows, hour
            ));
        }

        // 键鼠增量
        if let Some(sh) = &shared {
            let store = ctx.store.lock().unwrap();
            match input::flush(sh, &store) {
                Ok((keys, clicks)) => {
                    if keys > 0 || clicks > 0 {
                        ctx.say(format!("键鼠落库：{keys} 键 / {clicks} 击"));
                    }
                }
                Err(e) => ctx.say(format!("键鼠落库失败：{e}")),
            }
        }

        if last_hour != Some(hour) {
            last_hour = Some(hour);
            ctx.say(format!("进入 {date} {}:00 时段", hour));
        }
    }
}

/// 日汇总：每小时检查一次「昨天」要不要重算（口径照抄 WPF 的 incomplete 判定）。
/// 今日行不写（今日 KPI 一直是现算，与 4.2.0 起的做法一致）。
///
/// 另外接管三件原本只在 WPF 采集服务里跑的事（WPF 只读后没人做）：
/// 启动时补一次 AppDailyStats / 硬件日指标回填；每天过零点后按 `DataRetentionDays` 清一次旧数据。
fn daily_loop(ctx: Arc<Ctx>) {
    // 先等 10 s，让分钟桶与前台会话先落一点数据再判
    std::thread::sleep(Duration::from_secs(10));

    // 启动时的一次性回填（AppDailyStats 全空才补；硬件列缺的补最近 30 天）
    {
        let store = ctx.store.lock().unwrap();
        let n = crate::daily::backfill_app_daily(&store, 30, &ctx.log);
        if n > 0 {
            ctx.say(format!("AppDailyStats 回填完成：{n} 天"));
        }
        let h = crate::daily::backfill_hardware(&store, 30, &ctx.log);
        if h > 0 {
            ctx.say(format!("硬件日指标回填完成：{h} 天"));
        }
    }

    let mut last_cleanup_day = String::new();

    loop {
        if !ctx.running.load(Ordering::SeqCst) {
            break;
        }
        {
            let store = ctx.store.lock().unwrap();
            let yesterday = crate::dt::date_ms(crate::dt::local_ms_now() - 86_400_000);
            if crate::daily::needs_aggregate(&store, &yesterday) {
                let now = crate::timefmt::stamp7(&crate::timefmt::now());
                match crate::daily::aggregate_day(&store, &yesterday, &now, &ctx.log) {
                    Ok((k, c, n)) => ctx.say(format!(
                        "日汇总完成：{yesterday} 键 {k} / 点击 {c} / 应用 {n}"
                    )),
                    Err(e) => ctx.say(format!("日汇总失败（{yesterday}）：{e}")),
                }
            }

            // 每天清一次旧数据（今天第一次进循环时执行）
            let today = crate::dt::date_ms(crate::dt::local_ms_now());
            if last_cleanup_day != today {
                last_cleanup_day = today.clone();
                if !crate::settings::bool_of("AutoCleanupEnabled", true) {
                    ctx.say("自动清理已关闭（AutoCleanupEnabled=false），跳过".to_string());
                } else {
                    // 保留天数每次清理前重读：设置页改完不用重启采集宿主
                    let retention = read_retention_days();
                    // WPF 的 cutoff：Today.AddDays(-retention) 的 yyyy-MM-dd；硬件分钟样本再拼 " 00:00"
                    let cutoff = crate::dt::date_ms(crate::dt::local_ms_now() - retention * 86_400_000);
                    let sample_cutoff = format!("{cutoff} 00:00");
                    // 清理前归档：先 VACUUM INTO 一份整库快照，再删
                    if crate::settings::bool_of("ArchiveBeforeCleanup", false) {
                        match archive_before_cleanup(&store) {
                            Ok(Some((path, size))) => ctx.say(format!(
                                "清理前归档：{}（{:.0} MB）",
                                path,
                                size as f64 / 1024.0 / 1024.0
                            )),
                            Ok(None) => ctx.say("清理前归档：今天已归档过，跳过".to_string()),
                            Err(e) => ctx.say(format!("清理前归档失败（继续清理）：{e}")),
                        }
                    }
                    let n = store.cleanup_before(&cutoff, &sample_cutoff);
                    if n > 0 {
                        ctx.say(format!("数据清理完成：删除 {n} 条 {retention} 天前的记录（截止 {cutoff}）"));
                    } else {
                        ctx.say(format!("无过期数据需要清理（保留 {retention} 天）"));
                    }

                    // 便签保留期：`StickyNotesRetentionDays` > 0 才清理（0/负数=永久保留）
                    let note_days = crate::settings::i64_of("StickyNotesRetentionDays", 0);
                    if note_days > 0 {
                        let note_cutoff = crate::dt::date_ms(
                            crate::dt::local_ms_now() - note_days * 86_400_000,
                        );
                        let cutoff_stamp = format!("{note_cutoff} 00:00:00");
                        let now_stamp = crate::timefmt::stamp7(&crate::timefmt::now());
                        let now_stamp = now_stamp.split('.').next().unwrap_or("").to_string();
                        let gone = store.notes_purge_old(&cutoff_stamp, &now_stamp);
                        if gone > 0 {
                            ctx.say(format!(
                                "便签清理：软删除 {gone} 张 {note_days} 天前的空白/已归档便签（有正文的不动）"
                            ));
                        }
                    }
                }
            }
        }
        // 睡一小时，期间每 5 秒看一次停止标志
        for _ in 0..720 {
            if !ctx.running.load(Ordering::SeqCst) {
                return;
            }
            std::thread::sleep(Duration::from_secs(5));
        }
    }
}

/// 保留天数：读 `%LOCALAPPDATA%\PChabit\settings.json` 的 `DataRetentionDays`（默认 90，钳到 1..=3650）。
/// 清理前归档：每天最多一份 `archive_before_cleanup_YYYYMMDD.db`，已存在就跳过。
fn archive_before_cleanup(store: &crate::store::Store) -> Result<Option<(String, u64)>, String> {
    let base = std::env::var("LOCALAPPDATA")
        .map(PathBuf::from)
        .unwrap_or_else(|_| PathBuf::from("."))
        .join("PChabit")
        .join("Backups");
    std::fs::create_dir_all(&base).map_err(|e| format!("创建归档目录失败: {e}"))?;
    let day = crate::dt::date_ms(crate::dt::local_ms_now()).replace('-', "");
    let path = base.join(format!("archive_before_cleanup_{day}.db"));
    if path.exists() {
        return Ok(None);
    }
    let text = path.to_string_lossy().to_string();
    let size = store.vacuum_into(&text)?;
    Ok(Some((text, size)))
}

fn read_retention_days() -> i64 {    // 自测用覆盖（不写进设置，也不影响生产）
    if let Ok(v) = std::env::var("PCHABIT_RETENTION_DAYS") {
        if let Ok(n) = v.trim().parse::<i64>() {
            return n.clamp(1, 3650);
        }
    }
    let Some(base) = std::env::var("LOCALAPPDATA").ok().map(PathBuf::from) else {
        return 90;
    };
    let path = base.join("PChabit").join("settings.json");
    let raw = std::fs::read_to_string(&path).unwrap_or_default();
    let v: serde_json::Value = serde_json::from_str(&raw).unwrap_or(serde_json::Value::Null);
    let n = v["DataRetentionDays"].as_i64().unwrap_or(90);
    n.clamp(1, 3650)
}

/// 网页会话兜底：每 30 s 把在场会话按「未结束」写一行，进程被强杀也不至于整段丢。
fn web_save_loop(ctx: Arc<Ctx>, web: Arc<Mutex<crate::websync::Engine>>) {
    let mut last_report = Instant::now();
    while ctx.running.load(Ordering::SeqCst) {
        std::thread::sleep(Duration::from_secs(30));
        if !ctx.running.load(Ordering::SeqCst) {
            break;
        }
        let store = ctx.store.lock().unwrap();
        let mut eng = web.lock().unwrap();
        eng.save_active(&store, &ctx.log);
        // 当日搜索计数落到 DailySummaries.SearchCount（WPF 只读后这条没人写了，改为这里兜）
        let today = crate::dt::date_ms(crate::dt::local_ms_now());
        let n = eng.search_count(&today);
        if n > 0 {
            let now = crate::timefmt::stamp7(&crate::timefmt::now());
            if let Err(e) = store.search_count_set(&today, n, &now) {
                ctx.say(format!("SearchCount 写入失败：{e}"));
            }
        }
        if last_report.elapsed().as_secs() >= 600 {
            last_report = Instant::now();
            let n = eng.active_count();
            if n > 0 {
                ctx.say(format!("网页会话进行中：{n} 个"));
            }
        }
    }
}



