//! 命名管道服务端：单客户端、行式文本协议、单行 JSON 应答。
//!
//! 协议（客户端每行一条命令，服务端每行一条 JSON 应答）：
//!   PING                       → {"ok":true}
//!   HEALTH                     → {"elev":bool,"estats":bool,"ticks":n,"tickMs":f,"conns":n,"estatsBytes":n}
//!   SYS <upBytes> <downBytes>   → 宿主推送本拍系统上下行增量，{"ok":true}
//!   SEEDP <up> <down> <name>   → 回填进程今日累计，{"ok":true}
//!   SEEDSYS <up> <down>        → 回填系统今日累计，{"ok":true}
//!   TOTALS                     → {"su":n,"sd":n,"tu":n,"td":n}
//!   ALLTODAY                   → [{"n":"..","u":n,"d":n},...]
//!   SNAP <top>                 → {"sys":{...},"rate":[...],"today":[...]}
//!   STOP                       → {"ok":true} 并退出进程

use std::ffi::c_void;
use std::os::raw::c_int;
use std::ptr;
use std::sync::{Arc, Mutex};

use crate::json::{self, Obj};
use crate::netstate::NetState;

pub const PIPE_NAME: &str = r"\\.\pipe\pchabit-netd-v1";

const PIPE_ACCESS_DUPLEX: u32 = 0x0000_0003;
const PIPE_TYPE_BYTE: u32 = 0x0000_0000;
const PIPE_READMODE_BYTE: u32 = 0x0000_0000;
const PIPE_WAIT: u32 = 0x0000_0000;
const PIPE_UNLIMITED_INSTANCES: u32 = 255;
const ERROR_PIPE_CONNECTED: u32 = 535;

extern "system" {
    fn CreateNamedPipeW(
        name: *const u16,
        open_mode: u32,
        pipe_mode: u32,
        max_instances: u32,
        out_buffer_size: u32,
        in_buffer_size: u32,
        default_timeout: u32,
        security_attributes: *mut c_void,
    ) -> *mut c_void;

    fn ConnectNamedPipe(handle: *mut c_void, overlapped: *mut c_void) -> c_int;
    fn DisconnectNamedPipe(handle: *mut c_void) -> c_int;
    fn ReadFile(
        handle: *mut c_void,
        buffer: *mut u8,
        len: u32,
        read: *mut u32,
        overlapped: *mut c_void,
    ) -> c_int;
    fn WriteFile(
        handle: *mut c_void,
        buffer: *const u8,
        len: u32,
        written: *mut u32,
        overlapped: *mut c_void,
    ) -> c_int;
    fn FlushFileBuffers(handle: *mut c_void) -> c_int;
    fn CloseHandle(handle: *mut c_void) -> c_int;
    fn GetLastError() -> u32;
}

pub enum ServeOutcome {
    Continue,
    Stop,
}

/// 常驻服务：反复接受连接、逐行处理，直到收到 STOP。
pub fn serve(state: Arc<Mutex<NetState>>) {
    let name_utf16 = to_wide(PIPE_NAME);
    loop {
        let handle = unsafe {
            CreateNamedPipeW(
                name_utf16.as_ptr(),
                PIPE_ACCESS_DUPLEX,
                PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
                PIPE_UNLIMITED_INSTANCES,
                4096,
                4096,
                0,
                ptr::null_mut(),
            )
        };
        if handle.is_null() {
            eprintln!("[netd] CreateNamedPipeW 失败，1 秒后重试");
            std::thread::sleep(std::time::Duration::from_secs(1));
            continue;
        }

        let connected = unsafe { ConnectNamedPipe(handle, ptr::null_mut()) };
        if connected == 0 && unsafe { GetLastError() } != ERROR_PIPE_CONNECTED {
            unsafe { CloseHandle(handle) };
            continue;
        }

        let outcome = pump(handle, &state);
        unsafe {
            FlushFileBuffers(handle);
            DisconnectNamedPipe(handle);
            CloseHandle(handle);
        }

        if let ServeOutcome::Stop = outcome {
            return;
        }
    }
}

fn pump(handle: *mut c_void, state: &Arc<Mutex<NetState>>) -> ServeOutcome {
    let mut acc: Vec<u8> = Vec::with_capacity(256);
    let mut chunk = [0u8; 4096];

    loop {
        let mut read: u32 = 0;
        let ok = unsafe {
            ReadFile(
                handle,
                chunk.as_mut_ptr(),
                chunk.len() as u32,
                &mut read,
                ptr::null_mut(),
            )
        };
        if ok == 0 || read == 0 {
            return ServeOutcome::Continue; // 客户端断开
        }
        acc.extend_from_slice(&chunk[..read as usize]);

        loop {
            let Some(pos) = acc.iter().position(|&b| b == b'\n') else {
                // 单行过长保护
                if acc.len() > 8 * 1024 {
                    acc.clear();
                }
                break;
            };
            let line: Vec<u8> = acc.drain(..=pos).collect();
            let text = String::from_utf8_lossy(&line);
            let cmd = text.trim();
            if cmd.is_empty() {
                continue;
            }

            let (reply, stop) = dispatch(cmd, state);
            if !write_line(handle, &reply) {
                return ServeOutcome::Continue;
            }
            if stop {
                return ServeOutcome::Stop;
            }
        }
    }
}

fn write_line(handle: *mut c_void, text: &str) -> bool {
    let mut payload = Vec::with_capacity(text.len() + 1);
    payload.extend_from_slice(text.as_bytes());
    payload.push(b'\n');

    let mut offset = 0usize;
    while offset < payload.len() {
        let mut written: u32 = 0;
        let ok = unsafe {
            WriteFile(
                handle,
                payload[offset..].as_ptr(),
                (payload.len() - offset) as u32,
                &mut written,
                ptr::null_mut(),
            )
        };
        if ok == 0 || written == 0 {
            return false;
        }
        offset += written as usize;
    }
    true
}

fn dispatch(cmd: &str, state: &Arc<Mutex<NetState>>) -> (String, bool) {
    state.lock().unwrap().touch_client();
    let mut parts = cmd.splitn(4, ' ');
    let verb = parts.next().unwrap_or("").to_ascii_uppercase();

    match verb.as_str() {
        "PING" => {
            let mut o = Obj::new();
            o.boolean("ok", true);
            (o.finish(), false)
        }
        "STOP" => {
            let mut o = Obj::new();
            o.boolean("ok", true);
            (o.finish(), true)
        }
        "HEALTH" => {
            let st = state.lock().unwrap();
            let mut o = Obj::new();
            o.boolean("elev", st.is_elevated());
            o.boolean("estats", st.estats_granted());
            o.num("ticks", st.tick_count as i64);
            o.fnum("tickMs", st.last_tick_ms);
            o.num("conns", st.last_conn_rows as i64);
            o.num("estatsBytes", st.last_estats_bytes);
            o.num("estatsOk", st.last_estats_ok as i64);
            (o.finish(), false)
        }
        "SYS" => {
            let up = parts.next().and_then(|v| v.trim().parse::<i64>().ok()).unwrap_or(0);
            let down = parts.next().and_then(|v| v.trim().parse::<i64>().ok()).unwrap_or(0);
            state.lock().unwrap().add_system_delta(up, down);
            let mut o = Obj::new();
            o.boolean("ok", true);
            (o.finish(), false)
        }
        "SEEDP" => {
            let up = parts.next().and_then(|v| v.trim().parse::<i64>().ok()).unwrap_or(0);
            let down = parts.next().and_then(|v| v.trim().parse::<i64>().ok()).unwrap_or(0);
            let name = parts.next().unwrap_or("").trim();
            state.lock().unwrap().seed_today(name, up, down);
            let mut o = Obj::new();
            o.boolean("ok", true);
            (o.finish(), false)
        }
        "SEEDSYS" => {
            let up = parts.next().and_then(|v| v.trim().parse::<i64>().ok()).unwrap_or(0);
            let down = parts.next().and_then(|v| v.trim().parse::<i64>().ok()).unwrap_or(0);
            state.lock().unwrap().seed_system(up, down);
            let mut o = Obj::new();
            o.boolean("ok", true);
            (o.finish(), false)
        }
        "TOTALS" => {
            let st = state.lock().unwrap();
            let (su, sd, tu, td) = st.totals();
            let mut o = Obj::new();
            o.num("su", su);
            o.num("sd", sd);
            o.num("tu", tu);
            o.num("td", td);
            (o.finish(), false)
        }
        "ALLTODAY" => {
            let st = state.lock().unwrap();
            let rows = st.all_today();
            let items: Vec<String> = rows
                .iter()
                .map(|r| {
                    let mut o = Obj::new();
                    o.string("n", &r.name);
                    o.num("u", r.up);
                    o.num("d", r.down);
                    o.finish()
                })
                .collect();
            (json::array(&items), false)
        }
        "SNAP" => {
            let top = parts.next().and_then(|v| v.trim().parse::<usize>().ok()).unwrap_or(5);
            let st = state.lock().unwrap();
            let (su, sd, tu, td) = st.totals();

            let mut sys = Obj::new();
            sys.num("su", su);
            sys.num("sd", sd);
            sys.num("tu", tu);
            sys.num("td", td);
            let sys_json = sys.finish();

            let rate_items: Vec<String> = st
                .by_rate(top)
                .iter()
                .map(|r| {
                    let mut o = Obj::new();
                    o.string("n", &r.name);
                    o.num("p", r.pid as i64);
                    o.fnum("u", r.up_bps);
                    o.fnum("d", r.down_bps);
                    o.num("c", r.conns as i64);
                    o.num("su", r.session_up);
                    o.num("sd", r.session_down);
                    o.finish()
                })
                .collect();

            let today_items: Vec<String> = st
                .by_today(top)
                .iter()
                .map(|r| {
                    let mut o = Obj::new();
                    o.string("n", &r.name);
                    o.num("u", r.up);
                    o.num("d", r.down);
                    o.finish()
                })
                .collect();

            let mut o = Obj::new();
            o.boolean("elev", st.is_elevated());
            o.boolean("estats", st.estats_granted());
            o.raw("sys", &sys_json);
            o.raw("rate", &json::array(&rate_items));
            o.raw("today", &json::array(&today_items));
            (o.finish(), false)
        }
        _ => {
            let mut o = Obj::new();
            o.boolean("ok", false);
            o.string("err", "unknown command");
            (o.finish(), false)
        }
    }
}

fn to_wide(s: &str) -> Vec<u16> {
    let mut v: Vec<u16> = s.encode_utf16().collect();
    v.push(0);
    v
}
