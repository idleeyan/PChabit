//! 前台窗口监控（P2 第一块迁移）。
//!
//! 为什么搬这块：宿主的 `AppMonitor` 用 `SetWinEventHook` 监听前台切换与标题变化，
//! 钩子回调跑在 C# 的 STA 消息循环里——UI 线程一卡，前台切换就漏记。
//! 这里改成独立进程里 1 Hz 轮询 `GetForegroundWindow`：平时只有一次系统调用（微秒级），
//! 只在窗口/标题变化时才做完整解析，宿主按秒拉快照，事件处理零 IPC。
//!
//! 口径约定：只报告「当前前台窗口是谁」，不判断这算不算一次切换——宿主用既有逻辑
//! 比对进程名与标题，保持与 C# 实现完全一致的事件语义。

use std::ffi::c_void;
use std::os::raw::c_int;
use std::ptr;

use crate::json::Obj;

const PROCESS_QUERY_LIMITED_INFORMATION: u32 = 0x1000;

#[link(name = "user32")]
extern "system" {
    fn GetForegroundWindow() -> *mut c_void;
    fn GetWindowThreadProcessId(hwnd: *mut c_void, pid: *mut u32) -> u32;
    fn GetWindowTextLengthW(hwnd: *mut c_void) -> c_int;
    fn GetWindowTextW(hwnd: *mut c_void, buf: *mut u16, max: c_int) -> c_int;
    fn GetClassNameW(hwnd: *mut c_void, buf: *mut u16, max: c_int) -> c_int;
    fn IsZoomed(hwnd: *mut c_void) -> c_int;
    fn GetWindowRect(hwnd: *mut c_void, rect: *mut Rect) -> c_int;
}

#[link(name = "kernel32")]
extern "system" {
    fn OpenProcess(access: u32, inherit: c_int, pid: u32) -> *mut c_void;
    fn QueryFullProcessImageNameW(handle: *mut c_void, flags: u32, buf: *mut u16, size: *mut u32) -> c_int;
    fn CloseHandle(handle: *mut c_void) -> c_int;
}

#[repr(C)]
#[derive(Default, Clone, Copy)]
struct Rect {
    left: i32,
    top: i32,
    right: i32,
    bottom: i32,
}

/// 一次前台窗口快照。`hwnd=0` 表示当前取不到前台窗口（锁屏、切换瞬间）。
#[derive(Clone, Default)]
pub struct AppSnapshot {
    pub hwnd: i64,
    pub pid: u32,
    pub process: String,
    pub title: String,
    pub class: String,
    pub x: i32,
    pub y: i32,
    pub width: i32,
    pub height: i32,
    pub maximized: bool,
    /// 采样轮次，宿主可用它判断内核是否还在跑。
    pub ticks: u64,
}

impl AppSnapshot {
    pub fn to_json(&self) -> String {
        let mut o = Obj::new();
        o.boolean("ok", self.hwnd != 0);
        o.num("hwnd", self.hwnd);
        o.num("pid", self.pid as i64);
        o.string("proc", &self.process);
        o.string("title", &self.title);
        o.string("class", &self.class);
        o.num("x", self.x as i64);
        o.num("y", self.y as i64);
        o.num("w", self.width as i64);
        o.num("h", self.height as i64);
        o.boolean("max", self.maximized);
        o.num("ticks", self.ticks as i64);
        o.finish()
    }
}

/// 采一次前台窗口。任何一步失败都退化成「上一份快照 + ticks 递增」，不产生空窗。
pub fn sample(prev: &AppSnapshot) -> AppSnapshot {
    let hwnd = unsafe { GetForegroundWindow() };
    if hwnd.is_null() {
        let mut s = prev.clone();
        s.ticks += 1;
        return s;
    }

    let mut pid: u32 = 0;
    unsafe { GetWindowThreadProcessId(hwnd, &mut pid) };
    if pid == 0 {
        let mut s = prev.clone();
        s.ticks += 1;
        return s;
    }

    // 同一窗口且 pid 未变：只重读标题（浏览器切标签、文档改名都会走到这里）
    let same_window = prev.hwnd == hwnd as i64 && prev.pid == pid;
    if same_window {
        let title = window_title(hwnd);
        if title == prev.title {
            let mut s = prev.clone();
            s.ticks += 1;
            return s;
        }
        let mut s = prev.clone();
        s.ticks += 1;
        s.title = title;
        return s;
    }

    let mut rect = Rect::default();
    unsafe { GetWindowRect(hwnd, &mut rect) };

    AppSnapshot {
        hwnd: hwnd as i64,
        pid,
        process: process_name(pid),
        title: window_title(hwnd),
        class: window_class(hwnd),
        x: rect.left,
        y: rect.top,
        width: rect.right - rect.left,
        height: rect.bottom - rect.top,
        maximized: unsafe { IsZoomed(hwnd) } != 0,
        ticks: prev.ticks + 1,
    }
}

fn window_title(hwnd: *mut c_void) -> String {
    let len = unsafe { GetWindowTextLengthW(hwnd) };
    if len <= 0 {
        return String::new();
    }
    let mut buf = vec![0u16; (len + 1) as usize];
    let n = unsafe { GetWindowTextW(hwnd, buf.as_mut_ptr(), buf.len() as c_int) };
    if n <= 0 {
        return String::new();
    }
    String::from_utf16_lossy(&buf[..n as usize])
}

fn window_class(hwnd: *mut c_void) -> String {
    let mut buf = vec![0u16; 256];
    let n = unsafe { GetClassNameW(hwnd, buf.as_mut_ptr(), buf.len() as c_int) };
    if n <= 0 {
        return String::new();
    }
    String::from_utf16_lossy(&buf[..n as usize])
}

/// 进程映像名（不含扩展名），与宿主 `Path.GetFileNameWithoutExtension` 口径一致。
fn process_name(pid: u32) -> String {
    let handle = unsafe { OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, 0, pid) };
    if handle.is_null() {
        return String::new();
    }

    let mut buf = vec![0u16; 1024];
    let mut size = buf.len() as u32;
    let ok = unsafe { QueryFullProcessImageNameW(handle, 0, buf.as_mut_ptr(), &mut size) };
    unsafe { CloseHandle(handle) };

    if ok == 0 || size == 0 {
        return String::new();
    }

    let path = String::from_utf16_lossy(&buf[..size as usize]);
    match path.rsplit(['\\', '/']).next() {
        Some(file) => match file.rsplit_once('.') {
            Some((stem, _)) => stem.to_string(),
            None => file.to_string(),
        },
        None => String::new(),
    }
}

/// `app-probe` 模式：打印每次变化，用来和宿主日志对照。
pub fn run_probe() {
    let mut prev = AppSnapshot::default();
    for _ in 0..30 {
        let snap = sample(&prev);
        if snap.hwnd != prev.hwnd || snap.title != prev.title {
            println!(
                "[{}] pid={} proc={} class={} {}x{} @({},{}) max={} title={}",
                snap.hwnd, snap.pid, snap.proc_name(), snap.class,
                snap.width, snap.height, snap.x, snap.y, snap.maximized, snap.title
            );
        }
        prev = snap;
        std::thread::sleep(std::time::Duration::from_secs(1));
    }
    println!("完成：共 {} 拍", prev.ticks);
}

impl AppSnapshot {
    fn proc_name(&self) -> &str {
        if self.process.is_empty() { "(空)" } else { &self.process }
    }
}

// 让未使用告警不干扰构建：ptr 在部分平台分支下备用
#[allow(dead_code)]
fn _unused(_: *mut c_void) {
    let _ = ptr::null_mut::<c_void>();
}
