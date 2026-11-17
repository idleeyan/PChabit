//! PChabit 进程网络采样内核 —— 常驻守护进程。
//!
//! 用法：
//!   pchabit-netd probe                                     # 一次性耗时度量
//!   pchabit-netd daemon [--exclude-name PChabit] [--idle-exit 90] [--verbose]
//!
//! 职责边界（与 C# 侧的分工）：
//!   Rust：连接表枚举、ESTATS 逐连接增量、进程名解析、速率/会话/今日聚合、跨天清零。
//!   C#  ：提供系统级速率（LHM 权威口径）、把库内今日累计回填、消费快照落库与展示。
//!
//! 之所以这么切：裸指针表解析与逐连接 syscall 是 .NET 最吃亏的地方（实测 5.2ms/拍 →
//! 0.8ms/拍），而系统速率的权威来源仍在上游硬件监控里，交给宿主推送最不容易漂口径。

#![windows_subsystem = "console"]

mod iphelper;
mod json;
mod netstate;
mod pipe;
mod probe;
mod procname;
mod timelocal;

use std::sync::{Arc, Mutex};
use std::time::Duration;

fn main() {
    let mut args = std::env::args().skip(1);
    let mode = args.next().unwrap_or_else(|| "probe".to_string());

    match mode.as_str() {
        "probe" => probe::run(),
        "daemon" => run_daemon(args.collect()),
        other => {
            eprintln!("未知模式：{other}（可用：probe / daemon）");
            std::process::exit(2);
        }
    }
}

fn run_daemon(argv: Vec<String>) {
    let mut exclude_names: Vec<String> = vec!["PChabit".to_string()];
    let mut idle_exit_secs: f64 = 90.0;
    let mut verbose = false;

    let mut i = 0;
    while i < argv.len() {
        match argv[i].as_str() {
            "--exclude-name" => {
                if let Some(v) = argv.get(i + 1) {
                    exclude_names.push(v.clone());
                    i += 2;
                } else {
                    i += 1;
                }
            }
            "--idle-exit" => {
                if let Some(v) = argv.get(i + 1).and_then(|s| s.parse::<f64>().ok()) {
                    idle_exit_secs = v;
                }
                i += 2;
            }
            "--verbose" => {
                verbose = true;
                i += 1;
            }
            _ => i += 1,
        }
    }

    if !acquire_singleton() {
        // 已有实例在跑：静默退出，由宿主的连接逻辑复用既有实例
        std::process::exit(0);
    }

    let state = Arc::new(Mutex::new(netstate::NetState::new(exclude_names)));

    // 采样线程：1 Hz，独立于 IPC 线程
    let sampler_state = Arc::clone(&state);
    std::thread::spawn(move || loop {
        std::thread::sleep(Duration::from_secs(1));
        let mut guard = sampler_state.lock().unwrap();
        let info = guard.tick();
        if verbose {
            eprintln!(
                "[netd] {} tick#{} {:.3}ms 连接行={} ESTATS={} 字节={}",
                timelocal::local_time_text(),
                guard.tick_count,
                info.duration_ms,
                info.conn_rows,
                info.estats_ok,
                info.estats_bytes
            );
        }
        if guard.idle_secs() > idle_exit_secs {
            drop(guard);
            eprintln!("[netd] 宿主 {idle_exit_secs:.0} 秒无请求，退出");
            std::process::exit(0);
        }
    });

    // IPC 主线程（阻塞等待宿主连接）
    pipe::serve(state);
    std::process::exit(0);
}

/// 单实例守卫：同名 Mutex 已存在说明另一个 daemon 在跑。
fn acquire_singleton() -> bool {
    use std::ffi::c_void;
    use std::os::raw::c_int;
    use std::ptr;

    #[link(name = "kernel32")]
    extern "system" {
        fn CreateMutexW(attrs: *mut c_void, initial_owner: c_int, name: *const u16) -> *mut c_void;
        fn GetLastError() -> u32;
    }

    const ERROR_ALREADY_EXISTS: u32 = 183;

    let name = to_wide(r"Global\pchabit-netd-v1");
    let handle = unsafe { CreateMutexW(ptr::null_mut(), 0, name.as_ptr()) };
    if handle.is_null() {
        return true; // 拿不到就放行，不阻塞功能
    }
    (unsafe { GetLastError() }) != ERROR_ALREADY_EXISTS
}

fn to_wide(s: &str) -> Vec<u16> {
    let mut v: Vec<u16> = s.encode_utf16().collect();
    v.push(0);
    v
}
