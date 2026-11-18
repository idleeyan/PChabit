//! netd 托管：进程拉起、命名管道客户端、今日累计回填。
//!
//! 分工不变：netd 负责「谁在用网、用了多少」的原始增量；宿主负责把读数变成库里的行。
//! 与 WPF 版的唯一区别是宿主换成了壳，所以 netd 的 agent 生命周期归这里管：
//! 连得上就复用，连不上就拉起，退出前发 STOP。

use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};
use std::time::Duration;

use crate::win::{to_wide, CloseHandle, CreateFileW, GetLastError, ReadFile, WriteFile};

const PIPE_NAME: &str = r"\\.\pipe\pchabit-netd-v1";
const GENERIC_READ: u32 = 0x8000_0000;
const GENERIC_WRITE: u32 = 0x4000_0000;
const OPEN_EXISTING: u32 = 3;
const ERROR_FILE_NOT_FOUND: u32 = 2;
const ERROR_PIPE_BUSY: u32 = 231;

pub struct Pipe {
    handle: *mut std::ffi::c_void,
}

// 句柄本身是个内核对象指针；同一时刻只有一个线程使用（netd_loop 独占）。
unsafe impl Send for Pipe {}

impl Pipe {
    /// 连接 netd；最多等 `tries * 100ms`。
    pub fn connect(tries: usize) -> Option<Pipe> {
        let name = to_wide(PIPE_NAME);
        for _ in 0..tries {
            let handle = unsafe {
                CreateFileW(
                    name.as_ptr(),
                    GENERIC_READ | GENERIC_WRITE,
                    0,
                    std::ptr::null_mut(),
                    OPEN_EXISTING,
                    0,
                    std::ptr::null_mut(),
                )
            };
            if !handle.is_null() && handle as isize != -1 {
                return Some(Pipe { handle });
            }
            let err = unsafe { GetLastError() };
            // 忙 / 还没建好都值得重试；其它错误也重试（netd 可能正在起）
            let _ = (err, ERROR_FILE_NOT_FOUND, ERROR_PIPE_BUSY);
            std::thread::sleep(Duration::from_millis(100));
        }
        None
    }

    /// 发一条命令并读回一行 JSON。
    pub fn cmd(&mut self, text: &str) -> Option<String> {
        let mut payload = text.as_bytes().to_vec();
        payload.push(b'\n');
        let mut written: u32 = 0;
        let ok = unsafe {
            WriteFile(
                self.handle,
                payload.as_ptr(),
                payload.len() as u32,
                &mut written,
                std::ptr::null_mut(),
            )
        };
        if ok == 0 {
            return None;
        }

        let mut acc: Vec<u8> = Vec::with_capacity(4096);
        let mut chunk = [0u8; 4096];
        loop {
            let mut read: u32 = 0;
            let ok = unsafe {
                ReadFile(
                    self.handle,
                    chunk.as_mut_ptr(),
                    chunk.len() as u32,
                    &mut read,
                    std::ptr::null_mut(),
                )
            };
            if ok == 0 || read == 0 {
                return None;
            }
            acc.extend_from_slice(&chunk[..read as usize]);
            if let Some(pos) = acc.iter().position(|&b| b == b'\n') {
                let line = String::from_utf8_lossy(&acc[..pos]).to_string();
                return Some(line);
            }
            if acc.len() > 1024 * 1024 {
                return None;
            }
        }
    }
}

impl Drop for Pipe {
    fn drop(&mut self) {
        unsafe { CloseHandle(self.handle) };
    }
}

/// 找 netd 可执行文件：壳同目录、壳的上一级（发布包布局）、以及部署目录。
pub fn find_netd() -> Option<PathBuf> {
    let mut dirs: Vec<PathBuf> = Vec::new();
    if let Ok(exe) = std::env::current_exe() {
        if let Some(d) = exe.parent() {
            dirs.push(d.to_path_buf());
            if let Some(p) = d.parent() {
                dirs.push(p.to_path_buf());
            }
        }
    }
    dirs.push(PathBuf::from(r"D:\Tool\PChabit"));

    for d in dirs {
        let candidate = d.join("pchabit-netd.exe");
        if candidate.exists() {
            return Some(candidate);
        }
    }
    None
}

/// 确保 netd 在跑：先试连，再拉起（拉起后最多等 3 秒）。
pub fn ensure_daemon() -> Option<(Pipe, bool)> {
    if let Some(p) = Pipe::connect(3) {
        return Some((p, false));
    }
    let exe = find_netd()?;
    let _ = Command::new(&exe)
        .args([
            "daemon",
            "--exclude-name",
            "PChabit",
            "--exclude-name",
            "PChabitShell",
            "--exclude-name",
            "pchabit-collect",
        ])
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .spawn()
        .ok()?;
    let p = Pipe::connect(30)?;
    Some((p, true))
}

/// 把库里的今日累计回填给 netd，避免「netd 从 0 起算」与「库里已有今天数据」重复累加。
pub fn seed(pipe: &mut Pipe, date: &str, per_proc: &[(String, i64, i64)]) -> bool {
    let mut up = 0i64;
    let mut down = 0i64;
    for (name, u, d) in per_proc {
        let ok = pipe
            .cmd(&format!("SEEDP {u} {d} {name}"))
            .map(|r| r.contains("true"))
            .unwrap_or(false);
        if !ok {
            return false;
        }
        up += *u;
        down += *d;
    }
    let _ = date;
    pipe.cmd(&format!("SEEDSYS {up} {down}"))
        .map(|r| r.contains("true"))
        .unwrap_or(false)
}

/// 请求 netd 退出（宿主关闭时调用）。
pub fn stop(pipe: &mut Pipe) {
    let _ = pipe.cmd("STOP");
}

pub fn db_path_for_log(p: &Path) -> String {
    p.display().to_string()
}
