//! 键鼠采集：Rust 侧低层钩子（WH_KEYBOARD_LL / WH_MOUSE_LL）。
//!
//! 为什么搬这块：WPF 版把钩子回调跑在 UI 的 STA 消息循环里，UI 一卡就说漏键；
//! 这里每个钩子一个独立线程 + 自己的消息循环，回调里只做内存累加，落库交给
//! 宿主线程每 15 秒刷一次增量（口径同 C# 的 `FlushLiveInputSessions`）。
//!
//! 口径：
//! - 长按自动重复只计 `RepeatKeyPresses`，不进按键总数/分布/速度（3.87.0 定下的口径）；
//! - 修饰键不计入按键数；
//! - 每个键归属「当前前台进程」的映像名；
//! - 类型速度按「连续输入段」算：间隔 > 1 秒即封一段。

use std::collections::{BTreeMap, HashMap, HashSet};
use std::sync::{Arc, Mutex, OnceLock};
use std::time::Instant;

use crate::store::{KbDelta, MsDelta, Store};
use crate::timefmt;
use crate::win::*;

const REPEAT_STALE_MS: u64 = 1500;
const BURST_GAP_MS: u64 = 1000;

#[derive(Default, Clone, Copy)]
struct Mods {
    shift: bool,
    ctrl: bool,
    alt: bool,
    win: bool,
}

#[derive(Default)]
struct KbAcc {
    date: String,
    hour: u32,
    process: String,
    total: i64,
    repeat: i64,
    backspace: i64,
    delete: i64,
    undo: i64,
    freq: BTreeMap<i32, i64>,
    cats: BTreeMap<String, i64>,
    // 已落库水位
    c_total: i64,
    c_repeat: i64,
    c_backspace: i64,
    c_delete: i64,
    c_undo: i64,
    c_freq: BTreeMap<i32, i64>,
    c_cats: BTreeMap<String, i64>,
    // 状态
    pressed: HashSet<i32>,
    last_down: HashMap<i32, u64>,
    mods: Mods,
    // 连续输入段
    burst_start_ms: u64,
    burst_count: i64,
    last_key_ms: u64,
    bursts: Vec<(u64, u64, i64)>,
    peak_speed: f64,
    avg_speed: f64,
}

#[derive(Default)]
struct MsAcc {
    date: String,
    hour: u32,
    process: String,
    left: i64,
    right: i64,
    middle: i64,
    scroll: i64,
    dist: f64,
    move_events: i64,
    c_left: i64,
    c_right: i64,
    c_middle: i64,
    c_scroll: i64,
    c_dist: f64,
    c_move_events: i64,
    last_pt: Option<(i32, i32)>,
}

#[derive(Default)]
struct FgCache {
    pid: u32,
    name: String,
    checked: Option<Instant>,
}

pub struct InputShared {
    kb: Mutex<KbAcc>,
    ms: Mutex<MsAcc>,
    fg: Mutex<FgCache>,
}

impl Default for InputShared {
    fn default() -> Self {
        Self::new()
    }
}

impl InputShared {
    pub fn new() -> Self {
        Self {
            kb: Mutex::new(KbAcc::default()),
            ms: Mutex::new(MsAcc::default()),
            fg: Mutex::new(FgCache::default()),
        }
    }

    /// 前台进程名（映像名，不含扩展名）。250 ms 缓存，避免在钩子路径上反复打开进程。
    fn foreground(&self) -> String {
        let mut fg = self.fg.lock().unwrap();
        let now = Instant::now();
        if let Some(t) = fg.checked {
            if now.duration_since(t).as_millis() < 250 {
                return fg.name.clone();
            }
        }
        let hwnd = unsafe { GetForegroundWindow() };
        let mut pid: u32 = 0;
        if !hwnd.is_null() {
            unsafe { GetWindowThreadProcessId(hwnd, &mut pid) };
        }
        if pid > 0 && pid != fg.pid {
            let name = process_stem(pid);
            if !name.is_empty() {
                fg.name = name;
            }
        }
        fg.pid = pid;
        fg.checked = Some(now);
        fg.name.clone()
    }

    fn day_ms() -> u64 {
        let t = timefmt::now();
        (t.wHour as u64) * 3_600_000
            + (t.wMinute as u64) * 60_000
            + (t.wSecond as u64) * 1000
            + (t.wMilliseconds as u64)
    }

    fn on_key(&self, vk: i32, is_down: bool, is_up: bool) {
        let mut kb = self.kb.lock().unwrap();
        let t = timefmt::now();
        let date = timefmt::date(&t);
        let hour = timefmt::hour(&t);
        if kb.date != date || kb.hour != hour {
            kb.date = date;
            kb.hour = hour;
            // 新的小时：水量归零，但仍在同一对象里累加
            kb.total = 0;
            kb.repeat = 0;
            kb.backspace = 0;
            kb.delete = 0;
            kb.undo = 0;
            kb.freq.clear();
            kb.cats.clear();
            kb.c_total = 0;
            kb.c_repeat = 0;
            kb.c_backspace = 0;
            kb.c_delete = 0;
            kb.c_undo = 0;
            kb.c_freq.clear();
            kb.c_cats.clear();
            kb.burst_start_ms = 0;
            kb.burst_count = 0;
            kb.last_key_ms = 0;
            kb.bursts.clear();
        }

        // 修饰键
        let modifier = matches!(
            vk,
            0x10 | 0xA0 | 0xA1 | 0x11 | 0xA2 | 0xA3 | 0x12 | 0xA4 | 0xA5 | 0x5B | 0x5C
        );
        match vk {
            0x10 | 0xA0 | 0xA1 => kb.mods.shift = is_down,
            0x11 | 0xA2 | 0xA3 => kb.mods.ctrl = is_down,
            0x12 | 0xA4 | 0xA5 => kb.mods.alt = is_down,
            0x5B | 0x5C => kb.mods.win = is_down,
            _ => {}
        }
        if is_up {
            kb.pressed.remove(&vk);
            kb.last_down.remove(&vk);
            return;
        }
        if !is_down || modifier || vk <= 0 {
            return;
        }

        let now_ms = Self::day_ms();
        let is_repeat = if kb.pressed.insert(vk) {
            false
        } else {
            match kb.last_down.get(&vk) {
                Some(prev) if now_ms.saturating_sub(*prev) <= REPEAT_STALE_MS => true,
                _ => false,
            }
        };
        kb.last_down.insert(vk, now_ms);

        if is_repeat {
            kb.repeat += 1;
            return;
        }

        let proc = self.foreground();
        if !proc.is_empty() {
            kb.process = proc.clone();
        }

        kb.total += 1;
        *kb.freq.entry(vk).or_insert(0) += 1;
        let cat = key_category(vk);
        *kb.cats.entry(cat.to_string()).or_insert(0) += 1;
        match vk {
            0x08 => kb.backspace += 1,
            0x2E => kb.delete += 1,
            0x5A if kb.mods.ctrl => kb.undo += 1,
            _ => {}
        }

        // 连续输入段
        if kb.last_key_ms == 0 {
            kb.burst_start_ms = now_ms;
        } else {
            let gap = now_ms.saturating_sub(kb.last_key_ms);
            if gap > BURST_GAP_MS {
                let dur = kb.last_key_ms.saturating_sub(kb.burst_start_ms);
                if kb.burst_count > 0 {
                    let start = kb.burst_start_ms;
                    let count = kb.burst_count;
                    kb.bursts.push((start, dur, count));
                    let kpm = count as f64 / (dur as f64 / 60_000.0).max(0.001);
                    if kpm > kb.peak_speed {
                        kb.peak_speed = kpm;
                    }
                    let n = kb.bursts.len() as f64;
                    let avg = kb
                        .bursts
                        .iter()
                        .map(|(_, d, c)| *c as f64 / ((*d as f64 / 60_000.0).max(0.001)))
                        .sum::<f64>()
                        / n;
                    kb.avg_speed = avg;
                }
                kb.burst_start_ms = now_ms;
                kb.burst_count = 0;
            }
        }
        kb.burst_count += 1;
        kb.last_key_ms = now_ms;
    }

    fn on_mouse(&self, msg: usize, point: (i32, i32), wheel: bool) {
        let mut ms = self.ms.lock().unwrap();
        let t = timefmt::now();
        let date = timefmt::date(&t);
        let hour = timefmt::hour(&t);
        if ms.date != date || ms.hour != hour {
            ms.date = date;
            ms.hour = hour;
            ms.left = 0;
            ms.right = 0;
            ms.middle = 0;
            ms.scroll = 0;
            ms.dist = 0.0;
            ms.move_events = 0;
            ms.c_left = 0;
            ms.c_right = 0;
            ms.c_middle = 0;
            ms.c_scroll = 0;
            ms.c_dist = 0.0;
            ms.c_move_events = 0;
            ms.last_pt = None;
        }
        if ms.process.is_empty() {
            let p = self.foreground();
            if !p.is_empty() {
                ms.process = p;
            }
        }

        match msg {
            WM_LBUTTONDOWN => ms.left += 1,
            WM_RBUTTONDOWN => ms.right += 1,
            WM_MBUTTONDOWN => ms.middle += 1,
            WM_MOUSEWHEEL if wheel => ms.scroll += 1,
            WM_MOUSEMOVE => {
                if let Some((px, py)) = ms.last_pt {
                    let dx = (point.0 - px) as f64;
                    let dy = (point.1 - py) as f64;
                    let d = (dx * dx + dy * dy).sqrt();
                    if d >= 1.0 {
                        ms.dist += d;
                        ms.move_events += 1;
                    }
                }
                ms.last_pt = Some(point);
            }
            _ => {}
        }
    }

    /// 取「相对上次落库」的增量（并推进水位）。
    fn take_deltas(&self) -> (String, u32, String, KbDelta, Option<(String, u32, String, MsDelta)>) {
        let mut kb = self.kb.lock().unwrap();
        let kd = KbDelta {
            total: kb.total - kb.c_total,
            repeat: kb.repeat - kb.c_repeat,
            backspace: kb.backspace - kb.c_backspace,
            delete: kb.delete - kb.c_delete,
            undo: kb.undo - kb.c_undo,
            freq: kb
                .freq
                .iter()
                .filter_map(|(k, v)| {
                    let c = kb.c_freq.get(k).copied().unwrap_or(0);
                    if *v - c > 0 {
                        Some((*k, *v - c))
                    } else {
                        None
                    }
                })
                .collect(),
            cats: kb
                .cats
                .iter()
                .filter_map(|(k, v)| {
                    let c = kb.c_cats.get(k).copied().unwrap_or(0);
                    if *v - c > 0 {
                        Some((k.clone(), *v - c))
                    } else {
                        None
                    }
                })
                .collect(),
            avg_speed: kb.avg_speed,
            peak_speed: kb.peak_speed,
            bursts: std::mem::take(&mut kb.bursts),
        };
        kb.c_total = kb.total;
        kb.c_repeat = kb.repeat;
        kb.c_backspace = kb.backspace;
        kb.c_delete = kb.delete;
        kb.c_undo = kb.undo;
        kb.c_freq = kb.freq.clone();
        kb.c_cats = kb.cats.clone();
        let kb_key = (kb.date.clone(), kb.hour, kb.process.clone());

        let mut ms = self.ms.lock().unwrap();
        let md = MsDelta {
            left: ms.left - ms.c_left,
            right: ms.right - ms.c_right,
            middle: ms.middle - ms.c_middle,
            scroll: ms.scroll - ms.c_scroll,
            dist: ms.dist - ms.c_dist,
            move_events: ms.move_events - ms.c_move_events,
        };
        let has_mouse = md.left != 0
            || md.right != 0
            || md.middle != 0
            || md.scroll != 0
            || md.dist >= 0.5;
        ms.c_left = ms.left;
        ms.c_right = ms.right;
        ms.c_middle = ms.middle;
        ms.c_scroll = ms.scroll;
        ms.c_dist = ms.dist;
        ms.c_move_events = ms.move_events;
        let ms_key = (
            ms.date.clone(),
            ms.hour,
            ms.process.clone(),
            ms_len(ms.move_events, &md),
        );
        let mouse = if has_mouse {
            Some((ms_key.0, ms_key.1, ms_key.2, md))
        } else {
            None
        };

        (kb_key.0, kb_key.1, kb_key.2, kd, mouse)
    }
}

fn ms_len(_ev: i64, _d: &MsDelta) -> i64 {
    0
}

/// 落库：把这一轮的增量写进 KeyboardSessions / MouseSessions。
pub fn flush(shared: &InputShared, store: &Store) -> Result<(i64, i64), String> {
    let (date, hour, proc, kd, mouse) = shared.take_deltas();
    let mut keys = 0i64;
    let mut clicks = 0i64;
    if !date.is_empty() {
        if kd.total != 0 || kd.repeat != 0 {
            keys = kd.total;
            store.kb_add(&date, hour, &proc, &kd)?;
        }
        if let Some((md, mh, mp, delta)) = mouse {
            clicks = delta.left + delta.right + delta.middle;
            store.mouse_add(&md, mh, &mp, &delta)?;
        }
    }
    Ok((keys, clicks))
}

fn key_category(vk: i32) -> &'static str {
    match vk {
        0x41..=0x5A => "字母",
        0x30..=0x39 => "数字",
        0x70..=0x87 => "功能键",
        0x20 => "空格",
        0x0D => "回车",
        0x08 => "退格",
        0x2E => "删除",
        0x09 => "Tab",
        0x1B => "Esc",
        0x25..=0x28 => "方向键",
        _ => "其他",
    }
}

static SHARED: OnceLock<Arc<InputShared>> = OnceLock::new();

unsafe extern "system" fn kb_hook(code: i32, w: usize, l: isize) -> isize {
    if code >= 0 {
        if let Some(shared) = SHARED.get() {
            let info = &*(l as *const KbdLlHookStruct);
            let down = w == WM_KEYDOWN || w == WM_SYSKEYDOWN;
            let up = w == WM_KEYUP || w == WM_SYSKEYUP;
            // 钩子回调里绝不能 panic（会静默卸载钩子，键数统计全停）
            let guard = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
                shared.on_key(info.vkCode as i32, down, up);
            }));
            let _ = guard;
        }
    }
    unsafe { CallNextHookEx(std::ptr::null_mut(), code, w, l) }
}

unsafe extern "system" fn mouse_hook(code: i32, w: usize, l: isize) -> isize {
    if code >= 0 {
        if let Some(shared) = SHARED.get() {
            let info = &*(l as *const MsLlHookStruct);
            let wheel = info.mouseData & 0xffff != 0 || info.mouseData != 0;
            let guard = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
                shared.on_mouse(w, (info.pt.x, info.pt.y), wheel);
            }));
            let _ = guard;
        }
    }
    unsafe { CallNextHookEx(std::ptr::null_mut(), code, w, l) }
}

/// 装钩子：两条独立线程，各自跑消息循环。返回 false 表示安装失败。
pub fn start(shared: Arc<InputShared>) -> bool {
    let _ = SHARED.set(Arc::clone(&shared));

    let kb_ok = std::sync::Arc::new(std::sync::atomic::AtomicBool::new(false));
    let kb_flag = Arc::clone(&kb_ok);
    std::thread::spawn(move || unsafe {
        let module = GetModuleHandleW(std::ptr::null());
        let hook = SetWindowsHookExW(WH_KEYBOARD_LL, kb_hook, module, 0);
        if hook.is_null() {
            return;
        }
        kb_flag.store(true, std::sync::atomic::Ordering::Relaxed);
        let mut msg = Msg::default();
        while GetMessageW(&mut msg, 0, 0, 0) > 0 {}
        UnhookWindowsHookEx(hook);
    });

    let ms_ok = std::sync::Arc::new(std::sync::atomic::AtomicBool::new(false));
    let ms_flag = Arc::clone(&ms_ok);
    std::thread::spawn(move || unsafe {
        let module = GetModuleHandleW(std::ptr::null());
        let hook = SetWindowsHookExW(WH_MOUSE_LL, mouse_hook, module, 0);
        if hook.is_null() {
            return;
        }
        ms_flag.store(true, std::sync::atomic::Ordering::Relaxed);
        let mut msg = Msg::default();
        while GetMessageW(&mut msg, 0, 0, 0) > 0 {}
        UnhookWindowsHookEx(hook);
    });

    std::thread::sleep(std::time::Duration::from_millis(150));
    kb_ok.load(std::sync::atomic::Ordering::Relaxed)
        && ms_ok.load(std::sync::atomic::Ordering::Relaxed)
}


