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

/// 一个「(日期, 小时, 进程)」桶里的键盘累计（落库后整桶被取走，不再需要水位）。
#[derive(Default, Clone)]
struct KbBucket {
    total: i64,
    repeat: i64,
    backspace: i64,
    delete: i64,
    undo: i64,
    freq: BTreeMap<i32, i64>,
    cats: BTreeMap<String, i64>,
    combos: BTreeMap<String, i64>,
    // 连续输入段
    burst_start_ms: u64,
    burst_count: i64,
    last_key_ms: u64,
    bursts: Vec<(u64, u64, i64)>,
    peak_speed: f64,
    seg_sum: f64,
    segs: i64,
}

impl KbBucket {
    fn avg_speed(&self) -> f64 {
        if self.segs > 0 {
            self.seg_sum / self.segs as f64
        } else {
            0.0
        }
    }
}

/// 一个「(日期, 小时, 进程)」桶里的鼠标累计。
#[derive(Default, Clone)]
struct MsBucket {
    left: i64,
    right: i64,
    middle: i64,
    scroll: i64,
    dist: f64,
    move_events: i64,
    last_pt: Option<(i32, i32)>,
}

#[derive(Default)]
struct KbState {
    buckets: HashMap<(String, u32, String), KbBucket>,
    pressed: HashSet<i32>,
    last_down: HashMap<i32, u64>,
    mods: Mods,
}

#[derive(Default)]
struct MsState {
    buckets: HashMap<(String, u32, String), MsBucket>,
}

#[derive(Default)]
struct FgCache {
    pid: u32,
    name: String,
    checked: Option<Instant>,
}

pub struct InputShared {
    kb: Mutex<KbState>,
    ms: Mutex<MsState>,
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
            kb: Mutex::new(KbState::default()),
            ms: Mutex::new(MsState::default()),
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

        // 按 (日期, 小时, 前台进程) 分桶累加：不再有「每小时一行、进程被后者覆盖」的问题，
        // 也不会在整点把未落库的增量清掉。
        let mods = kb.mods;
        let proc = self.foreground();
        let b = kb.buckets.entry((date, hour, proc)).or_default();

        if is_repeat {
            b.repeat += 1;
            return;
        }

        b.total += 1;
        *b.freq.entry(vk).or_insert(0) += 1;
        let cat = key_category(vk);
        *b.cats.entry(cat.to_string()).or_insert(0) += 1;
        // 组合键：只有 Ctrl / Alt / Win 参与才算快捷键（Shift+字母是正常大写输入）
        if mods.ctrl || mods.alt || mods.win {
            let mut parts: Vec<&str> = Vec::new();
            if mods.ctrl {
                parts.push("Ctrl");
            }
            if mods.alt {
                parts.push("Alt");
            }
            if mods.shift {
                parts.push("Shift");
            }
            if mods.win {
                parts.push("Win");
            }
            let name = format!("{}+{}", parts.join("+"), key_name(vk));
            *b.combos.entry(name).or_insert(0) += 1;
        }
        match vk {
            0x08 => b.backspace += 1,
            0x2E => b.delete += 1,
            0x5A if mods.ctrl => b.undo += 1,
            _ => {}
        }

        // 连续输入段（按桶统计）
        if b.last_key_ms == 0 {
            b.burst_start_ms = now_ms;
        } else {
            let gap = now_ms.saturating_sub(b.last_key_ms);
            if gap > BURST_GAP_MS {
                let dur = b.last_key_ms.saturating_sub(b.burst_start_ms);
                if b.burst_count > 0 {
                    let start = b.burst_start_ms;
                    let count = b.burst_count;
                    b.bursts.push((start, dur, count));
                    let kpm = count as f64 / (dur as f64 / 60_000.0).max(0.001);
                    if kpm > b.peak_speed {
                        b.peak_speed = kpm;
                    }
                    b.seg_sum += kpm;
                    b.segs += 1;
                }
                b.burst_start_ms = now_ms;
                b.burst_count = 0;
            }
        }
        b.burst_count += 1;
        b.last_key_ms = now_ms;
    }

    fn on_mouse(&self, msg: usize, point: (i32, i32), wheel: bool) {
        let t = timefmt::now();
        let date = timefmt::date(&t);
        let hour = timefmt::hour(&t);
        // 每次事件都取当前前台进程（旧版整小时只取一次，后续切到别的应用也记在前一个头上）
        let proc = self.foreground();
        let mut ms = self.ms.lock().unwrap();
        let b = ms.buckets.entry((date, hour, proc)).or_default();

        match msg {
            WM_LBUTTONDOWN => b.left += 1,
            WM_RBUTTONDOWN => b.right += 1,
            WM_MBUTTONDOWN => b.middle += 1,
            WM_MOUSEWHEEL if wheel => b.scroll += 1,
            WM_MOUSEMOVE => {
                if let Some((px, py)) = b.last_pt {
                    let dx = (point.0 - px) as f64;
                    let dy = (point.1 - py) as f64;
                    let d = (dx * dx + dy * dy).sqrt();
                    if d >= 1.0 {
                        b.dist += d;
                        b.move_events += 1;
                    }
                }
                b.last_pt = Some(point);
            }
            _ => {}
        }
    }

    /// 取走所有桶（落库后清空，下一次从零开始累加）。
    fn drain(&self) -> (Vec<KbDelta>, Vec<MsDelta>) {
        let mut kb_rows: Vec<KbDelta> = Vec::new();
        {
            let mut kb = self.kb.lock().unwrap();
            for ((date, hour, proc), b) in kb.buckets.drain() {
                if b.total == 0 && b.repeat == 0 && b.freq.is_empty() && b.combos.is_empty() {
                    continue;
                }
                let mut bursts = b.bursts.clone();
                if b.burst_count >= 2 && b.last_key_ms > b.burst_start_ms {
                    bursts.push((
                        b.burst_start_ms,
                        b.last_key_ms - b.burst_start_ms,
                        b.burst_count,
                    ));
                }
                let avg = b.avg_speed();
                kb_rows.push(KbDelta {
                    date,
                    hour,
                    process: proc,
                    total: b.total,
                    repeat: b.repeat,
                    backspace: b.backspace,
                    delete: b.delete,
                    undo: b.undo,
                    freq: b.freq,
                    cats: b.cats,
                    combos: b.combos,
                    avg_speed: avg,
                    peak_speed: b.peak_speed,
                    bursts,
                });
            }
        }

        let mut ms_rows: Vec<MsDelta> = Vec::new();
        {
            let mut ms = self.ms.lock().unwrap();
            for ((date, hour, proc), b) in ms.buckets.drain() {
                if b.left == 0 && b.right == 0 && b.middle == 0 && b.scroll == 0 && b.dist < 0.5 {
                    continue;
                }
                ms_rows.push(MsDelta {
                    date,
                    hour,
                    process: proc,
                    left: b.left,
                    right: b.right,
                    middle: b.middle,
                    scroll: b.scroll,
                    dist: b.dist,
                    move_events: b.move_events,
                });
            }
        }
        (kb_rows, ms_rows)
    }
}

/// 落库：把这一轮的增量写进 KeyboardSessions / MouseSessions。
/// 统一口径：进程名一律去扩展名（WPF 的鼠标会话写的是 `xxx.exe`，键盘会话是 `xxx`，
/// 混在一张图里会把同一个应用拆成两行）。
pub fn flush(shared: &InputShared, store: &Store) -> Result<(i64, i64), String> {
    let (kb_rows, ms_rows) = shared.drain();
    let mut keys = 0i64;
    let mut clicks = 0i64;
    // 设置页的「记录键盘 / 记录鼠标」在这里生效：关掉就不再落库（增量照常丢弃）
    if crate::settings::bool_of("TrackKeyboard", true) {
        for mut d in kb_rows {
            d.process = norm_proc(&d.process);
            keys += d.total;
            store.kb_add(&d)?;
        }
    }
    if crate::settings::bool_of("TrackMouse", true) {
        for mut d in ms_rows {
            d.process = norm_proc(&d.process);
            clicks += d.left + d.right + d.middle;
            store.mouse_add(&d)?;
        }
    }
    Ok((keys, clicks))
}

/// 去扩展名（不区分大小写），与 `win::process_stem` 的口径对齐。
fn norm_proc(name: &str) -> String {
    let t = name.trim();
    if t.len() > 4 && t[t.len() - 4..].eq_ignore_ascii_case(".exe") {
        t[..t.len() - 4].to_string()
    } else {
        t.to_string()
    }
}

fn key_category(vk: i32) -> &'static str {    match vk {
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

/// VK 码 → 给人看的键名（组合键显示、页面排行都用这套口径）。
pub fn key_name(vk: i32) -> String {
    match vk {
        0x41..=0x5A => ((b'A' + (vk as u8 - 0x41)) as char).to_string(),
        0x30..=0x39 => ((b'0' + (vk as u8 - 0x30)) as char).to_string(),
        0x70..=0x87 => format!("F{}", vk - 0x6F),
        0x60..=0x69 => format!("小键盘{}", vk - 0x60),
        0x08 => "Backspace".into(),
        0x09 => "Tab".into(),
        0x0D => "Enter".into(),
        0x1B => "Esc".into(),
        0x20 => "Space".into(),
        0x2E => "Delete".into(),
        0x25 => "←".into(),
        0x26 => "↑".into(),
        0x27 => "→".into(),
        0x28 => "↓".into(),
        0x21 => "PageUp".into(),
        0x22 => "PageDown".into(),
        0x23 => "End".into(),
        0x24 => "Home".into(),
        0x2D => "-".into(),
        0x2B => "+".into(),
        0xBD => "-".into(),
        0xBB => "+".into(),
        0xBC => ",".into(),
        0xBE => ".".into(),
        0xBF => "/".into(),
        0xBA => ";".into(),
        0xDE => "'".into(),
        0xDB => "[".into(),
        0xDD => "]".into(),
        0xDC => "\\".into(),
        0xC0 => "`".into(),
        0xFC => "输入法(未定义)".into(),
        other => format!("VK_{other:02X}"),
    }
}

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

#[cfg(test)]
mod tests {
    use super::*;

    /// 计数口径回归：普通键按下计 1；修饰键不计；按住不放只进 repeat；左右中键各计 1。
    #[test]
    fn key_and_click_counting() {
        let s = InputShared::new();
        // 10 个字母，每个都成对按下/抬起
        for _ in 0..10 {
            s.on_key(0x41, true, false);
            s.on_key(0x41, false, true);
        }
        // Ctrl 按下不计入总数，但随后 C 要算进组合键
        s.on_key(0x11, true, false);
        s.on_key(0x43, true, false);
        s.on_key(0x43, false, true);
        s.on_key(0x11, false, true);
        // 按住不放（没有抬起）→ 只进 repeat
        s.on_key(0x42, true, false);
        s.on_key(0x42, true, false);

        let (kb_rows, _) = s.drain();
        let total: i64 = kb_rows.iter().map(|d| d.total).sum();
        let repeat: i64 = kb_rows.iter().map(|d| d.repeat).sum();
        let combos: i64 = kb_rows.iter().map(|d| d.combos.values().sum::<i64>()).sum();
        assert_eq!(total, 12, "10 个字母 + C 一次 + B 首次按下");
        assert_eq!(repeat, 1, "B 的第二次按下算长按重复");
        assert_eq!(combos, 1, "Ctrl+C 应进组合键");

        // 左键 3 次、右键 1 次、中键 1 次
        for _ in 0..3 {
            s.on_mouse(WM_LBUTTONDOWN, (0, 0), false);
        }
        s.on_mouse(WM_RBUTTONDOWN, (0, 0), false);
        s.on_mouse(WM_MBUTTONDOWN, (0, 0), false);
        let (_, ms_rows) = s.drain();
        let l: i64 = ms_rows.iter().map(|d| d.left).sum();
        let r: i64 = ms_rows.iter().map(|d| d.right).sum();
        let m: i64 = ms_rows.iter().map(|d| d.middle).sum();
        assert_eq!(l, 3);
        assert_eq!(r, 1);
        assert_eq!(m, 1);
    }
}


