//! PID → 进程名解析（对应 C# 侧 `ResolveName` / `QueryFullProcessImageName`）。

use std::ffi::c_void;
use std::os::raw::c_int;

const PROCESS_QUERY_LIMITED_INFORMATION: u32 = 0x1000;

extern "system" {
    fn OpenProcess(access: u32, inherit: c_int, pid: u32) -> *mut c_void;
    fn CloseHandle(h: *mut c_void) -> c_int;
    fn QueryFullProcessImageNameW(
        process: *mut c_void,
        flags: u32,
        exe_name: *mut u16,
        size: *mut u32,
    ) -> c_int;
    fn K32EnumProcesses(ids: *mut u32, bytes: u32, needed: *mut u32) -> c_int;
}

/// 解析进程映像基名（不含扩展名）。失败返回 None（进程已退出/权限受限）。
pub fn name_of(pid: u32) -> Option<String> {
    if pid == 0 || pid == u32::MAX {
        return None;
    }
    unsafe {
        let handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, 0, pid);
        if handle.is_null() {
            return None;
        }
        let mut buf = [0u16; 1024];
        let mut size = buf.len() as u32;
        let ok = QueryFullProcessImageNameW(handle, 0, buf.as_mut_ptr(), &mut size);
        CloseHandle(handle);
        if ok == 0 || size == 0 {
            return None;
        }

        let full: String = String::from_utf16_lossy(&buf[..size as usize]);
        let file = full.rsplit('\\').next().unwrap_or(&full);
        let stem = file
            .strip_suffix(".exe")
            .or_else(|| file.strip_suffix(".EXE"))
            .unwrap_or(file);
        if stem.is_empty() {
            None
        } else {
            Some(stem.to_string())
        }
    }
}

/// 枚举全部进程 PID（对应 `Process.GetProcesses()`）。
pub fn enum_pids() -> Vec<u32> {
    let mut capacity = 1024usize;
    loop {
        let mut ids = vec![0u32; capacity];
        let mut needed: u32 = 0;
        let ok = unsafe {
            K32EnumProcesses(
                ids.as_mut_ptr(),
                (capacity * std::mem::size_of::<u32>()) as u32,
                &mut needed,
            )
        };
        if ok == 0 {
            return Vec::new();
        }
        let count = needed as usize / std::mem::size_of::<u32>();
        if count < capacity {
            ids.truncate(count);
            ids.retain(|&p| p != 0);
            return ids;
        }
        // 缓冲区被填满：扩容重试
        capacity *= 2;
        if capacity > 65536 {
            return Vec::new();
        }
    }
}
