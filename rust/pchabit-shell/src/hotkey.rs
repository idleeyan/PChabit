//! 全局热键（`StickyNotesHotkeyNew` / `HotkeyBoard`）。
//!
//! 用线程级 `RegisterHotKey(NULL, ...)`：不需要自建窗口类，热键消息直接进
//! 调用线程的消息队列，`GetMessageW` 里捞到 `WM_HOTKEY` 就执行动作。
//! 两条默认热键：`Ctrl+Alt+N` 新建便签、`Ctrl+Alt+B` 打开主窗口。

use std::sync::Mutex;

#[cfg(windows)]
mod imp {
    use std::sync::Mutex;

    use std::ffi::c_void;

    type Hwnd = *mut c_void;
    type Hinstance = *mut c_void;

    #[repr(C)]
    #[derive(Default)]
    struct Msg {
        hwnd: Hwnd,
        message: u32,
        w_param: usize,
        l_param: isize,
        time: u32,
        pt_x: i32,
        pt_y: i32,
        l_private: u32,
    }

    #[link(name = "user32")]
    extern "system" {
        fn RegisterHotKey(hwnd: Hwnd, id: i32, fs_modifiers: u32, vk: u32) -> i32;
        fn UnregisterHotKey(hwnd: Hwnd, id: i32) -> i32;
        fn GetMessageW(msg: *mut Msg, hwnd: Hwnd, min: u32, max: u32) -> i32;
    }

    pub const MOD_ALT: u32 = 0x0001;
    pub const MOD_CONTROL: u32 = 0x0002;
    pub const MOD_SHIFT: u32 = 0x0004;
    pub const MOD_WIN: u32 = 0x0008;
    pub const WM_HOTKEY: u32 = 0x0312;

    /// 把 `"Ctrl+Alt+N"` 解析成 (修饰键掩码, 虚拟键码)。
    pub fn parse(combo: &str) -> Option<(u32, u32)> {
        let mut mods = 0u32;
        let mut vk: Option<u32> = None;
        for part in combo.split('+').map(|p| p.trim()) {
            if part.is_empty() {
                continue;
            }
            match part.to_ascii_lowercase().as_str() {
                "ctrl" | "control" => mods |= MOD_CONTROL,
                "alt" => mods |= MOD_ALT,
                "shift" => mods |= MOD_SHIFT,
                "win" | "meta" => mods |= MOD_WIN,
                other => {
                    if other.len() == 1 {
                        let c = other.chars().next().unwrap().to_ascii_uppercase();
                        vk = Some(c as u32);
                    } else if let Some(rest) = other.strip_prefix('f') {
                        if let Ok(n) = rest.parse::<u32>() {
                            if (1..=24).contains(&n) {
                                vk = Some(0x6F + n); // F1 = 0x70
                            }
                        }
                    }
                }
            }
        }
        vk.map(|v| (mods, v))
    }

    /// 注册并跑消息循环；`on_hot` 收到 hotkey id。
    pub fn run(pairs: Vec<(i32, u32, u32)>, on_hot: impl Fn(i32) + Send + 'static) {
        unsafe {
            let mut ok = Vec::new();
            for (id, mods, vk) in &pairs {
                if RegisterHotKey(std::ptr::null_mut(), *id, *mods, *vk) != 0 {
                    ok.push(*id);
                }
            }
            if ok.is_empty() {
                return;
            }
            let mut msg = Msg::default();
            loop {
                let r = GetMessageW(&mut msg, std::ptr::null_mut(), 0, 0);
                if r <= 0 {
                    break;
                }
                if msg.message == WM_HOTKEY {
                    on_hot(msg.w_param as i32);
                }
            }
            for id in ok {
                UnregisterHotKey(std::ptr::null_mut(), id);
            }
        }
    }
}

#[cfg(windows)]
use imp::{parse, run, MOD_ALT, MOD_CONTROL};

static STARTED: Mutex<bool> = Mutex::new(false);

/// 起热键线程（幂等）。`new_note` / `show_main` 是热键命中后的动作。
pub fn start(new_note: impl Fn() + Send + 'static, show_main: impl Fn() + Send + 'static) {
    #[cfg(not(windows))]
    {
        let _ = (new_note, show_main);
        return;
    }
    #[cfg(windows)]
    {
        {
            let mut s = STARTED.lock().unwrap();
            if *s {
                return;
            }
            *s = true;
        }
        std::thread::spawn(move || {
            let note_combo = crate::setting_str("StickyNotesHotkeyNew", "Ctrl+Alt+N");
            let board_combo = {
                let b = crate::setting_str("StickyNotesHotkeyBoard", "");
                if b.trim().is_empty() {
                    let h = crate::setting_str("HotkeyBoard", "");
                    if h.trim().is_empty() {
                        "Ctrl+Alt+B".to_string()
                    } else {
                        h
                    }
                } else {
                    b
                }
            };
            let mut pairs: Vec<(i32, u32, u32)> = Vec::new();
            if crate::setting_bool_default("StickyNotesEnabled", true) {
                if let Some((m, vk)) = parse(&note_combo) {
                    pairs.push((1, m, vk));
                } else {
                    pairs.push((1, MOD_CONTROL | MOD_ALT, 'N' as u32));
                }
            }
            if let Some((m, vk)) = parse(&board_combo) {
                pairs.push((2, m, vk));
            } else {
                pairs.push((2, MOD_CONTROL | MOD_ALT, 'B' as u32));
            }
            let desc = pairs
                .iter()
                .map(|(id, _, _)| {
                    if *id == 1 {
                        format!("新建便签 {note_combo}")
                    } else {
                        format!("打开主窗口 {board_combo}")
                    }
                })
                .collect::<Vec<_>>()
                .join(" / ");
            crate::log_line(&format!("[热键] 注册全局热键：{desc}"));
            run(pairs, move |id| match id {
                1 => new_note(),
                2 => show_main(),
                _ => {}
            });
        });
    }
}
