//! Win32 FFI 声明集中处：只声明用得到的函数，不引入 windows crate。
#![allow(non_snake_case, clippy::upper_case_acronyms)]

use std::os::raw::{c_int, c_void};

#[repr(C)]
#[derive(Default, Clone, Copy)]
pub struct SystemTime {
    pub wYear: u16,
    pub wMonth: u16,
    pub wDayOfWeek: u16,
    pub wDay: u16,
    pub wHour: u16,
    pub wMinute: u16,
    pub wSecond: u16,
    pub wMilliseconds: u16,
}

#[repr(C)]
#[derive(Default, Clone, Copy)]
pub struct FileTime {
    pub low: u32,
    pub high: u32,
}

impl FileTime {
    pub fn q(self) -> u64 {
        (self.high as u64) << 32 | self.low as u64
    }
}

#[repr(C)]
#[derive(Default, Clone, Copy)]
pub struct MemStatus {
    pub dwLength: u32,
    pub dwMemoryLoad: u32,
    pub ullTotalPhys: u64,
    pub ullAvailPhys: u64,
    pub ullTotalPageFile: u64,
    pub ullAvailPageFile: u64,
    pub ullTotalVirtual: u64,
    pub ullAvailVirtual: u64,
    pub ullAvailExtendedVirtual: u64,
}

#[repr(C)]
#[derive(Default, Clone, Copy)]
pub struct Point {
    pub x: i32,
    pub y: i32,
}

#[repr(C)]
#[derive(Default, Clone, Copy)]
pub struct Msg {
    pub hwnd: isize,
    pub message: u32,
    pub wParam: usize,
    pub lParam: isize,
    pub time: u32,
    pub pt: Point,
    pub lPrivate: u32,
}

/// WH_KEYBOARD_LL 的 lParam
#[repr(C)]
#[derive(Default, Clone, Copy)]
pub struct KbdLlHookStruct {
    pub vkCode: u32,
    pub scanCode: u32,
    pub flags: u32,
    pub time: u32,
    pub dwExtraInfo: usize,
}

/// WH_MOUSE_LL 的 lParam
#[repr(C)]
#[derive(Default, Clone, Copy)]
pub struct MsLlHookStruct {
    pub pt: Point,
    pub mouseData: u32,
    pub flags: u32,
    pub time: u32,
    pub dwExtraInfo: usize,
}

pub const WH_KEYBOARD_LL: c_int = 13;
pub const WH_MOUSE_LL: c_int = 14;
pub const WM_KEYDOWN: usize = 0x0100;
pub const WM_KEYUP: usize = 0x0101;
pub const WM_SYSKEYDOWN: usize = 0x0104;
pub const WM_SYSKEYUP: usize = 0x0105;
pub const WM_MOUSEMOVE: usize = 0x0200;
pub const WM_LBUTTONDOWN: usize = 0x0201;
pub const WM_RBUTTONDOWN: usize = 0x0204;
pub const WM_MBUTTONDOWN: usize = 0x0207;
pub const WM_MOUSEWHEEL: usize = 0x020A;

pub const PROCESS_QUERY_LIMITED_INFORMATION: u32 = 0x1000;
pub const TH32CS_SNAPPROCESS: u32 = 0x0000_0002;
pub const INVALID_HANDLE_VALUE: isize = -1;

pub type HookProc = unsafe extern "system" fn(c_int, usize, isize) -> isize;

#[link(name = "kernel32")]
extern "system" {
    pub fn GetLocalTime(t: *mut SystemTime);
    pub fn GetSystemTimes(idle: *mut FileTime, kernel: *mut FileTime, user: *mut FileTime) -> c_int;
    pub fn GlobalMemoryStatusEx(b: *mut MemStatus) -> c_int;
    pub fn GetModuleHandleW(name: *const u16) -> *mut c_void;
    pub fn GetLastError() -> u32;
    pub fn CloseHandle(h: *mut c_void) -> c_int;
    pub fn OpenProcess(access: u32, inherit: c_int, pid: u32) -> *mut c_void;
    pub fn QueryFullProcessImageNameW(
        h: *mut c_void,
        flags: u32,
        buf: *mut u16,
        size: *mut u32,
    ) -> c_int;
    pub fn CreateToolhelp32Snapshot(flags: u32, pid: u32) -> *mut c_void;
    pub fn Process32FirstW(snap: *mut c_void, entry: *mut ProcessEntry32W) -> c_int;
    pub fn Process32NextW(snap: *mut c_void, entry: *mut ProcessEntry32W) -> c_int;
    // 管道
    pub fn CreateFileW(
        name: *const u16,
        access: u32,
        share: u32,
        sec: *mut c_void,
        disposition: u32,
        flags: u32,
        template: *mut c_void,
    ) -> *mut c_void;
    pub fn ReadFile(
        h: *mut c_void,
        buf: *mut u8,
        len: u32,
        read: *mut u32,
        overlapped: *mut c_void,
    ) -> c_int;
    pub fn WriteFile(
        h: *mut c_void,
        buf: *const u8,
        len: u32,
        written: *mut u32,
        overlapped: *mut c_void,
    ) -> c_int;
    pub fn CreateMutexW(sec: *mut c_void, owner: c_int, name: *const u16) -> *mut c_void;
}

#[repr(C)]
#[derive(Clone, Copy)]
pub struct ProcessEntry32W {
    pub dwSize: u32,
    pub cntUsage: u32,
    pub th32ProcessID: u32,
    pub th32DefaultHeapID: usize,
    pub th32ModuleID: u32,
    pub cntThreads: u32,
    pub th32ParentProcessID: u32,
    pub pcPriClassBase: i32,
    pub dwFlags: u32,
    pub szExeFile: [u16; 260],
}

impl Default for ProcessEntry32W {
    fn default() -> Self {
        Self {
            dwSize: 0,
            cntUsage: 0,
            th32ProcessID: 0,
            th32DefaultHeapID: 0,
            th32ModuleID: 0,
            cntThreads: 0,
            th32ParentProcessID: 0,
            pcPriClassBase: 0,
            dwFlags: 0,
            szExeFile: [0u16; 260],
        }
    }
}

#[link(name = "user32")]
extern "system" {
    pub fn SetWindowsHookExW(
        id: c_int,
        proc: HookProc,
        module: *mut c_void,
        thread: u32,
    ) -> *mut c_void;
    pub fn UnhookWindowsHookEx(hook: *mut c_void) -> c_int;
    pub fn CallNextHookEx(hook: *mut c_void, code: c_int, w: usize, l: isize) -> isize;
    pub fn GetMessageW(msg: *mut Msg, hwnd: isize, min: u32, max: u32) -> c_int;
    pub fn GetForegroundWindow() -> *mut c_void;
    pub fn GetWindowThreadProcessId(hwnd: *mut c_void, pid: *mut u32) -> u32;
}

#[link(name = "version")]
extern "system" {
    pub fn GetFileVersionInfoSizeW(file: *const u16, handle: *mut u32) -> u32;
    pub fn GetFileVersionInfoW(file: *const u16, handle: u32, len: u32, data: *mut c_void) -> c_int;
    pub fn VerQueryValueW(
        block: *const c_void,
        sub: *const u16,
        out: *mut *mut c_void,
        len: *mut u32,
    ) -> c_int;
}

pub fn to_wide(s: &str) -> Vec<u16> {
    let mut v: Vec<u16> = s.encode_utf16().collect();
    v.push(0);
    v
}

/// 按进程 ID 取映像名（不含扩展名），与 C# `Path.GetFileNameWithoutExtension` 口径一致。
pub fn process_stem(pid: u32) -> String {
    let h = unsafe { OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, 0, pid) };
    if h.is_null() {
        return String::new();
    }
    let mut buf = vec![0u16; 1024];
    let mut size = buf.len() as u32;
    let ok = unsafe { QueryFullProcessImageNameW(h, 0, buf.as_mut_ptr(), &mut size) };
    unsafe { CloseHandle(h) };
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

/// 按进程 ID 取完整映像路径，取不到返回空串。
pub fn process_path(pid: u32) -> String {
    let h = unsafe { OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, 0, pid) };
    if h.is_null() {
        return String::new();
    }
    let mut buf = vec![0u16; 1024];
    let mut size = buf.len() as u32;
    let ok = unsafe { QueryFullProcessImageNameW(h, 0, buf.as_mut_ptr(), &mut size) };
    unsafe { CloseHandle(h) };
    if ok == 0 || size == 0 {
        return String::new();
    }
    String::from_utf16_lossy(&buf[..size as usize])
}

/// 文件描述（版本信息里的 FileDescription），取不到返回空串。
pub fn file_description(path: &str) -> String {
    if path.is_empty() {
        return String::new();
    }
    let wide = to_wide(path);
    let mut handle: u32 = 0;
    let size = unsafe { GetFileVersionInfoSizeW(wide.as_ptr(), &mut handle) };
    if size == 0 {
        return String::new();
    }
    let mut data = vec![0u8; size as usize];
    let ok = unsafe {
        GetFileVersionInfoW(
            wide.as_ptr(),
            handle,
            size,
            data.as_mut_ptr() as *mut c_void,
        )
    };
    if ok == 0 {
        return String::new();
    }
    // 040904B0 = 美国英语 + Unicode；中文资源一般是 080404B0，两个都试
    for lang in ["040904B0", "080404B0"] {
        let sub = to_wide(&format!("\\StringFileInfo\\{lang}\\FileDescription"));
        let mut out: *mut c_void = std::ptr::null_mut();
        let mut len: u32 = 0;
        let got = unsafe {
            VerQueryValueW(
                data.as_ptr() as *const c_void,
                sub.as_ptr(),
                &mut out,
                &mut len,
            )
        };
        if got != 0 && len > 0 && !out.is_null() {
            let s = unsafe { std::slice::from_raw_parts(out as *const u16, len as usize) };
            let text = String::from_utf16_lossy(s).trim_end_matches('\0').to_string();
            if !text.trim().is_empty() {
                return text;
            }
        }
    }
    String::new()
}

/// 进程是否在跑（按 exe 名，忽略大小写）。
pub fn process_running(exe_name: &str) -> bool {
    let snap = unsafe { CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0) };
    if snap.is_null() || snap as isize == INVALID_HANDLE_VALUE {
        return false;
    }
    let target = exe_name.to_ascii_lowercase();
    let mut entry = ProcessEntry32W {
        dwSize: std::mem::size_of::<ProcessEntry32W>() as u32,
        ..Default::default()
    };
    let mut found = false;
    let mut ok = unsafe { Process32FirstW(snap, &mut entry) };
    while ok != 0 {
        let name = String::from_utf16_lossy(&entry.szExeFile)
            .trim_end_matches('\0')
            .to_ascii_lowercase();
        if name == target {
            found = true;
            break;
        }
        ok = unsafe { Process32NextW(snap, &mut entry) };
    }
    unsafe { CloseHandle(snap) };
    found
}

/// 单实例锁：同一时间只允许一个采集宿主。返回句柄保持存活即可。
pub fn acquire_singleton(name: &str) -> Option<*mut c_void> {
    let wide = to_wide(name);
    let h = unsafe { CreateMutexW(std::ptr::null_mut(), 0, wide.as_ptr()) };
    if h.is_null() {
        return None;
    }
    // 已存在则 GetLastError = ERROR_ALREADY_EXISTS(183)
    if unsafe { GetLastError() } == 183 {
        unsafe { CloseHandle(h) };
        return None;
    }
    Some(h)
}
