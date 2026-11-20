//! 任务栏小窗（Rust 版）：把子窗口挂到 `Shell_TrayWnd` 左侧，用 GDI 画 CPU / 内存 / 网速，
//! 点击打开仪表盘。口径对齐 C# `Services/TaskbarWidget.cs`（色键透明、左侧 8px、灰度抗锯齿）。
//!
//! 只在显式开启时启动（`PChabitShell.exe --taskbar`），避免与 WPF 的小窗同时存在。

use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::{Mutex, OnceLock};

// ---------- Win32 ----------

type HWND = isize;
type HBRUSH = isize;
type HFONT = isize;
type HGDIOBJ = isize;
type HDC = isize;
type HINSTANCE = isize;
type LRESULT = isize;
type WPARAM = usize;
type LPARAM = isize;

#[repr(C)]
#[derive(Copy, Clone, Default)]
struct Rect {
    left: i32,
    top: i32,
    right: i32,
    bottom: i32,
}
impl Rect {
    fn w(&self) -> i32 {
        self.right - self.left
    }
    fn h(&self) -> i32 {
        self.bottom - self.top
    }
}

#[repr(C)]
#[derive(Copy, Clone, Default)]
struct Size {
    cx: i32,
    cy: i32,
}

#[repr(C)]
struct WndClassW {
    style: u32,
    lpfn_wnd_proc: Option<unsafe extern "system" fn(HWND, u32, WPARAM, LPARAM) -> LRESULT>,
    cb_cls_extra: i32,
    cb_wnd_extra: i32,
    h_instance: HINSTANCE,
    h_icon: isize,
    h_cursor: isize,
    hbr_background: HBRUSH,
    lpsz_menu_name: *const u16,
    lpsz_class_name: *const u16,
}

#[repr(C)]
#[derive(Copy, Clone, Default)]
struct PaintStruct {
    hdc: HDC,
    f_erase: i32,
    rc_paint: Rect,
    f_restore: i32,
    f_inc_update: i32,
    reserved: [u8; 32],
}

#[repr(C)]
#[derive(Copy, Clone, Default)]
struct Msg {
    hwnd: HWND,
    message: u32,
    wparam: WPARAM,
    lparam: LPARAM,
    time: u32,
    pt_x: i32,
    pt_y: i32,
}

#[repr(C)]
#[derive(Copy, Clone, Default)]
struct MemoryStatusEx {
    length: u32,
    memory_load: u32,
    total_phys: u64,
    avail_phys: u64,
    total_page_file: u64,
    avail_page_file: u64,
    total_virtual: u64,
    avail_virtual: u64,
    avail_extended_virtual: u64,
}

#[link(name = "user32")]
extern "system" {
    fn FindWindowW(class: *const u16, name: *const u16) -> HWND;
    fn RegisterClassW(wc: *const WndClassW) -> u16;
    fn CreateWindowExW(
        ex_style: u32,
        class_name: *const u16,
        window_name: *const u16,
        style: u32,
        x: i32,
        y: i32,
        w: i32,
        h: i32,
        parent: HWND,
        menu: isize,
        instance: HINSTANCE,
        param: isize,
    ) -> HWND;
    fn DefWindowProcW(hwnd: HWND, msg: u32, wp: WPARAM, lp: LPARAM) -> LRESULT;
    fn SetParent(child: HWND, parent: HWND) -> HWND;
    fn GetParent(hwnd: HWND) -> HWND;
    fn GetWindowLongPtrW(hwnd: HWND, index: i32) -> isize;
    fn SetWindowLongPtrW(hwnd: HWND, index: i32, value: isize) -> isize;
    fn SetWindowPos(hwnd: HWND, insert_after: HWND, x: i32, y: i32, cx: i32, cy: i32, flags: u32) -> i32;
    fn GetWindowRect(hwnd: HWND, rect: *mut Rect) -> i32;
    fn GetClientRect(hwnd: HWND, rect: *mut Rect) -> i32;
    fn IsWindow(hwnd: HWND) -> i32;
    fn DestroyWindow(hwnd: HWND) -> i32;
    fn InvalidateRect(hwnd: HWND, rect: *const Rect, erase: i32) -> i32;
    fn SetLayeredWindowAttributes(hwnd: HWND, key: u32, alpha: u8, flags: u32) -> i32;
    fn GetDC(hwnd: HWND) -> HDC;
    fn ReleaseDC(hwnd: HWND, hdc: HDC) -> i32;
    fn GetDpiForWindow(hwnd: HWND) -> u32;
    fn BeginPaint(hwnd: HWND, ps: *mut PaintStruct) -> HDC;
    fn EndPaint(hwnd: HWND, ps: *const PaintStruct) -> i32;
    fn GetMessageW(msg: *mut Msg, hwnd: HWND, min: u32, max: u32) -> i32;
    fn TranslateMessage(msg: *const Msg) -> i32;
    fn DispatchMessageW(msg: *const Msg) -> isize;
    fn PostQuitMessage(code: i32);
    fn GetSystemTimes(idle: *mut u64, kernel: *mut u64, user: *mut u64) -> i32;
    fn LoadCursorW(instance: HINSTANCE, name: *const u16) -> isize;
    fn SetCursor(cursor: isize) -> isize;
    fn PostMessageW(hwnd: HWND, msg: u32, wp: WPARAM, lp: LPARAM) -> i32;
}

#[link(name = "gdi32")]
extern "system" {
    fn CreateFontW(
        height: i32,
        width: i32,
        escapement: i32,
        orientation: i32,
        weight: i32,
        italic: u32,
        underline: u32,
        strike_out: u32,
        charset: u32,
        out_precision: u32,
        clip_precision: u32,
        quality: u32,
        pitch_and_family: u32,
        face: *const u16,
    ) -> HFONT;
    fn DeleteObject(obj: HGDIOBJ) -> i32;
    fn CreateSolidBrush(color: u32) -> HBRUSH;
    fn CreateCompatibleDC(hdc: HDC) -> HDC;
    fn CreateCompatibleBitmap(hdc: HDC, w: i32, h: i32) -> isize;
    fn SelectObject(hdc: HDC, obj: HGDIOBJ) -> HGDIOBJ;
    fn DeleteDC(hdc: HDC) -> i32;
    fn BitBlt(dst: HDC, x: i32, y: i32, w: i32, h: i32, src: HDC, sx: i32, sy: i32, rop: u32) -> i32;
    fn TextOutW(hdc: HDC, x: i32, y: i32, text: *const u16, len: i32) -> i32;
    fn SetTextColor(hdc: HDC, color: u32) -> u32;
    fn SetBkMode(hdc: HDC, mode: i32) -> i32;
    fn GetTextExtentPoint32W(hdc: HDC, text: *const u16, len: i32, size: *mut Size) -> i32;
}

#[link(name = "kernel32")]
extern "system" {
    fn GetModuleHandleW(name: *const u16) -> HINSTANCE;
    fn GlobalMemoryStatusEx(status: *mut MemoryStatusEx) -> i32;
}

// ---------- 常量 ----------

const WS_POPUP: u32 = 0x8000_0000;
const WS_CHILD: u32 = 0x4000_0000;
const WS_VISIBLE: u32 = 0x1000_0000;
const WS_CLIPSIBLINGS: u32 = 0x0400_0000;
const WS_EX_LAYERED: u32 = 0x0008_0000;
const WS_EX_TOOLWINDOW: u32 = 0x0000_0080;
const WS_EX_NOACTIVATE: u32 = 0x0800_0000;
const GWL_STYLE: i32 = -16;
const SWP_NOACTIVATE: u32 = 0x0010;
const SWP_SHOWWINDOW: u32 = 0x0040;
const LWA_COLORKEY: u32 = 0x1;
const TRANSPARENT: i32 = 1;
const SRCCOPY: u32 = 0x00CC_0020;
const WM_PAINT: u32 = 0x000F;
const WM_SETCURSOR: u32 = 0x0020;
const WM_ERASEBKGND: u32 = 0x0014;
const WM_LBUTTONDOWN: u32 = 0x0201;
const WM_LBUTTONUP: u32 = 0x0202;
const WM_CLOSE: u32 = 0x0010;
const WM_DESTROY: u32 = 0x0002;

// 色键：浅 210,210,211 / 深 40,40,41（COLORREF 是 BGR）
const BG_KEY_LIGHT: u32 = 0x00D3_D2D2;
const BG_KEY_DARK: u32 = 0x0029_2828;
// 文字色
const LABEL_LIGHT: u32 = 0x003A_3A3A;
const LABEL_DARK: u32 = 0x00C8_C8C8;
const VALUE_LIGHT: u32 = 0x0011_1111;
const VALUE_DARK: u32 = 0x00F8_F8F8;

const CLASS_NAME: &str = "PChabitTaskbarWidgetRust";
/// IDC_ARROW 是资源 ID 32512，`LoadCursorW(NULL, ...)` 直接取系统箭头
const ARROW_ID: *const u16 = 32512usize as *const u16;
const PADDING: i32 = 10;
const ITEM_GAP: i32 = 12;
const LABEL_GAP: i32 = 3;
const MIN_WIDTH: i32 = 72;
/// 字号（pt）：11/15 贴到任务栏上偏小，抬到 12/16
const LABEL_PT: i32 = 12;
const VALUE_PT: i32 = 16;
/// 下行网速单独放大加粗，作为「主指标」
const NET_PT: i32 = 21;
/// 网速区与 CPU/内存区之间的分隔线宽度、两侧留白
const DIVIDER_W: i32 = 1;
const DIVIDER_GAP: i32 = 9;

/// 任务栏是深是浅，只能问 Windows 的个性化设置（`SystemUsesLightTheme`），
/// 不能看应用内主题：应用深色 + 任务栏浅色时会用近白色字画在浅灰底上，糊成一片。
pub fn system_uses_light_theme() -> bool {
    const HKEY_CURRENT_USER: usize = 0x8000_0001;
    const KEY_READ: u32 = 0x2_0019;
    const REG_DWORD: u32 = 4;
    extern "system" {
        fn RegOpenKeyExW(hkey: usize, sub: *const u16, opt: u32, rights: u32, out: *mut usize) -> i32;
        fn RegQueryValueExW(
            key: usize,
            name: *const u16,
            reserved: *mut u32,
            ty: *mut u32,
            data: *mut u8,
            cb: *mut u32,
        ) -> i32;
        fn RegCloseKey(key: usize) -> i32;
    }
    unsafe {
        let sub = w("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
        let mut key: usize = 0;
        if RegOpenKeyExW(HKEY_CURRENT_USER, sub.as_ptr(), 0, KEY_READ, &mut key) != 0 {
            return false;
        }
        let mut val: u32 = 0;
        let mut ty: u32 = 0;
        let mut cb: u32 = 4;
        let name = w("SystemUsesLightTheme");
        let rc = RegQueryValueExW(
            key,
            name.as_ptr(),
            std::ptr::null_mut(),
            &mut ty,
            &mut val as *mut u32 as *mut u8,
            &mut cb,
        );
        RegCloseKey(key);
        rc == 0 && ty == REG_DWORD && val != 0
    }
}

fn w(text: &str) -> Vec<u16> {
    let mut v: Vec<u16> = text.encode_utf16().collect();
    v.push(0);
    v
}
const fn rgb(r: u8, g: u8, b: u8) -> u32 {
    ((b as u32) << 16) | ((g as u32) << 8) | (r as u32)
}

// ---------- 状态 ----------

#[derive(Clone, Copy, PartialEq)]
enum Tone {
    Label,
    Value,
    Warn,
    Crit,
    Net,
}

#[derive(Clone, Copy, PartialEq)]
enum Slot {
    /// 按实测文本宽度排版
    Auto,
    /// 固定占位（网速/磁盘速率）：按最宽速率串预留，数字位数变化不推挤后面的项
    Rate,
    /// 固定占位（下行网速）：字号比常规大，探针略长一档
    RateNet,
    /// 固定占位（百分比）：按「100%」预留，10% → 8% 这类变化不挪动后面的项
    Percent,
}

/// 速率占位用的最宽参考串（下行大字号：21pt 下 8 字符，覆盖到 999.9M/s）
/// 原来两列共用 "1023.9M/s"（9 字符），常态只显示 "12.3M/s" 这类 7 字符，
/// 每列右边都空出一截；下行收 1 字符、上行收 2 字符，小窗整体窄约 33px。
const PROBE_RATE_NET: &str = "999.9M/s";
/// 速率占位用的最宽参考串（常规字号：上行 / 磁盘读写，7 字符）
/// 超出探针时 `slot_width` 会退化成按实测宽度，只会临时撑开，不会截断。
const PROBE_RATE: &str = "99.9M/s";
/// 百分比占位用的最宽参考串
const PROBE_PCT: &str = "100%";

impl Segment {
    /// 该段数值区的固定宽度（0 = 按实测走）
    fn slot_width(&self, hdc: HDC, measured: i32) -> i32 {
        match self.slot {
            Slot::Auto => 0,
            Slot::RateNet => unsafe { text_w(hdc, PROBE_RATE_NET) }.max(measured),
            Slot::Rate => unsafe { text_w(hdc, PROBE_RATE) }.max(measured),
            Slot::Percent => unsafe { text_w(hdc, PROBE_PCT) }.max(measured),
        }
    }
}

#[derive(Clone)]
struct Segment {
    label: String,
    value: String,
    tone: Tone,
    /// 数值字号（pt）：下行网速用更大的字，其余用常规字号
    value_pt: i32,
    /// 这一项前面要不要画一条竖分隔线（把「网速区」和「CPU/内存区」分开）
    divider_before: bool,
    /// 数值区是否固定宽度（右对齐），避免 6K/s → 21.3M/s 这种位数变化把后面推走
    slot: Slot,
}

struct State {
    hwnd: HWND,
    segments: Vec<Segment>,
    width: i32,
    light: bool,
    /// 从任务栏实际背景取的色键（0 表示没取到，退回按主题写死值）。
    /// Win11 任务栏是亚克力/随壁纸变色的，写死的 #D2D2D3 很容易对不上，
    /// 对不上就会在小窗周围留一块灰底。
    bg_key: u32,
    mouse_down: bool,
}

static STATE: OnceLock<Mutex<State>> = OnceLock::new();
/// 上一次点击的时刻（毫秒），用于在日志里量「点击 → 浮窗显示」的真实延迟
pub(crate) static CLICK_AT: AtomicU64 = AtomicU64::new(0);
/// 小窗是否在跑（设置页实时开关用）
static RUNNING: AtomicBool = AtomicBool::new(false);
static APP: OnceLock<tauri::AppHandle> = OnceLock::new();

fn now_ms() -> u64 {
    std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_millis() as u64)
        .unwrap_or(0)
}

/// 点小窗 → 立刻在主线程弹浮窗（浮窗起不来就退回打开主窗口，保证点击有反应）。
/// 必须在鼠标消息里直接调用：走 tick 标志消费会引入最多一个刷新周期的延迟。
fn spawn_open_netprocs() {
    let Some(app) = APP.get() else { return };
    let app2 = app.clone();
    let app3 = app.clone();
    let _ = app2.run_on_main_thread(move || {
        if let Err(e) = crate::open_netprocs(&app3) {
            crate::log_line(&format!("[浮窗] 打开失败，退回主窗口：{e}"));
            crate::open_window_at(&app3, "app.html", "任务栏小窗点击");
        }
    });
}

fn with_state<T>(f: impl FnOnce(&mut State) -> T) -> Option<T> {
    STATE.get().map(|m| f(&mut m.lock().unwrap()))
}

/// 启动小窗：建窗口线程 + 每秒刷新线程。`--taskbar` 才会走到这里。
pub fn start(app: tauri::AppHandle, light: bool) {
    // 已经在跑就别再插一个（设置页里反复开关会用到）
    if RUNNING.swap(true, Ordering::SeqCst) {
        return;
    }
    let _ = APP.set(app);
    STATE.get_or_init(|| {
        Mutex::new(State {
            hwnd: 0,
            segments: Vec::new(),
            width: MIN_WIDTH,
            light,
            bg_key: 0,
            mouse_down: false,
        })
    });

    std::thread::Builder::new()
        .name("taskbar-widget".into())
        .spawn(move || unsafe {
            create_window();
            // 消息循环（窗口在本线程创建，绘制也在这里）
            let mut msg = Msg::default();
            while GetMessageW(&mut msg, 0, 0, 0) > 0 {
                TranslateMessage(&msg);
                DispatchMessageW(&msg);
            }
        })
        .ok();

    std::thread::Builder::new()
        .name("taskbar-metrics".into())
        .spawn(move || tick_loop())
        .ok();
}

/// 摘掉任务栏小窗（设置页把「任务栏小窗」关掉时立刻生效，不用重启壳）。
pub fn stop() {
    if !RUNNING.swap(false, Ordering::SeqCst) {
        return;
    }
    if let Some(h) = with_state(|s| s.hwnd) {
        if h != 0 {
            unsafe {
                // 走 WM_CLOSE → DefWindowProc 销毁窗口 → 消息循环退出；线程自己收尾
                PostMessageW(h, WM_CLOSE, 0, 0);
            }
        }
    }
    crate::log_line("[小窗] 已按设置停止任务栏小窗");
}

pub fn is_running() -> bool {
    RUNNING.load(Ordering::SeqCst)
}

/// 读 `%LOCALAPPDATA%\PChabit\settings.json` 里的布尔项（缺省取 `default`）。
/// 每拍重读一次，设置页改完立刻体现，不必重启。
pub fn setting_bool(key: &str, default: bool) -> bool {
    let Some(base) = std::env::var("LOCALAPPDATA").ok().map(std::path::PathBuf::from) else {
        return default;
    };
    let path = base.join("PChabit").join("settings.json");
    let raw = match std::fs::read_to_string(&path) {
        Ok(s) => s,
        Err(_) => return default,
    };
    let v: serde_json::Value = match serde_json::from_str(&raw) {
        Ok(v) => v,
        Err(_) => return default,
    };
    match v.get(key) {
        Some(serde_json::Value::Bool(b)) => *b,
        Some(serde_json::Value::Number(n)) => n.as_i64().unwrap_or(0) != 0,
        _ => default,
    }
}

unsafe fn create_window() {
    let instance = GetModuleHandleW(std::ptr::null());
    let class = w(CLASS_NAME);
    let wc = WndClassW {
        style: 0,
        lpfn_wnd_proc: Some(wnd_proc),
        cb_cls_extra: 0,
        cb_wnd_extra: 0,
        h_instance: instance,
        h_icon: 0,
        h_cursor: unsafe { LoadCursorW(0, ARROW_ID) },
        hbr_background: 0,
        lpsz_menu_name: std::ptr::null(),
        lpsz_class_name: class.as_ptr(),
    };
    RegisterClassW(&wc);

    let hwnd = CreateWindowExW(
        WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
        class.as_ptr(),
        std::ptr::null(),
        WS_POPUP,
        0,
        0,
        MIN_WIDTH,
        40,
        0,
        0,
        instance,
        0,
    );
    if hwnd == 0 {
        crate::log_line("[小窗] 窗口创建失败");
        return;
    }
    with_state(|s| s.hwnd = hwnd);
    // 先取任务栏真实底色再挂上去（挂上去以后这个位置就是小窗自己了，取不到原色）
    let sampled = taskbar_bg_color();
    with_state(|s| s.bg_key = sampled);
    attach();

    let light = with_state(|s| s.light).unwrap_or(false);
    let bg = with_state(|s| s.bg_key).unwrap_or(0);
    SetLayeredWindowAttributes(hwnd, pick_key(light, bg), 0, LWA_COLORKEY);
    crate::log_line("[小窗] 已创建并挂载任务栏（Rust）");
}

/// 色键：优先用从任务栏采样到的真实底色，取不到才退回按主题写死的值。
fn pick_key(light: bool, bg: u32) -> u32 {
    if bg != 0 {
        bg
    } else if light {
        BG_KEY_LIGHT
    } else {
        BG_KEY_DARK
    }
}

/// 在任务栏左端取一个像素当透明色键。
unsafe fn taskbar_bg_color() -> u32 {
    extern "system" {
        fn GetPixel(hdc: HDC, x: i32, y: i32) -> u32;
    }
    let tb = FindWindowW(w("Shell_TrayWnd").as_ptr(), std::ptr::null());
    if tb == 0 {
        return 0;
    }
    let hdc = GetDC(tb);
    if hdc == 0 {
        return 0;
    }
    let mut rc = Rect::default();
    if GetWindowRect(tb, &mut rc) == 0 || rc.h() <= 0 {
        ReleaseDC(tb, hdc);
        return 0;
    }
    // 采样点选在左端、垂直居中：Win11 图标居中排列，这里通常是纯任务栏底色；
    // 万一落到图标上（很深的颜色）就只取一次，交给下面按亮度兜底。
    let px = GetPixel(hdc, rc.left + 6, rc.top + rc.h() / 2);
    ReleaseDC(tb, hdc);
    if px == 0xFFFF_FFFF {
        return 0;
    }
    px
}

unsafe fn attach() {    let hwnd = match with_state(|s| s.hwnd) {
        Some(h) if h != 0 => h,
        _ => return,
    };
    let taskbar = FindWindowW(w("Shell_TrayWnd").as_ptr(), std::ptr::null());
    if taskbar == 0 {
        return;
    }
    SetParent(hwnd, taskbar);
    let style = GetWindowLongPtrW(hwnd, GWL_STYLE);
    let new_style = (style & !(WS_POPUP as isize)) | (WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS) as isize;
    SetWindowLongPtrW(hwnd, GWL_STYLE, new_style);
    position();
}

unsafe fn position() {
    let hwnd = match with_state(|s| s.hwnd) {
        Some(h) if h != 0 => h,
        _ => return,
    };
    let mut tb = Rect::default();
    if GetWindowRect(FindWindowW(w("Shell_TrayWnd").as_ptr(), std::ptr::null()), &mut tb) == 0 {
        return;
    }
    let height = tb.h();
    if height <= 0 {
        return;
    }
    let width = with_state(|s| s.width).unwrap_or(MIN_WIDTH);
    SetWindowPos(hwnd, 0, 8, 0, width, height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
}

unsafe extern "system" fn wnd_proc(hwnd: HWND, msg: u32, wp: WPARAM, lp: LPARAM) -> LRESULT {
    match msg {
        WM_PAINT => {
            paint(hwnd);
            0
        }
        WM_ERASEBKGND => 1,
        WM_SETCURSOR => {
            // 鼠标移上来必须明确给箭头：hCursor 为空且不处理 WM_SETCURSOR 时，
            // 光标会沿用系统/父窗口的「忙碌」状态，看起来就是沙漏。
            let arrow = LoadCursorW(0, ARROW_ID);
            SetCursor(arrow);
            1
        }
        WM_LBUTTONDOWN => {
            with_state(|s| s.mouse_down = true);
            0
        }
        WM_LBUTTONUP => {
            let was_down = with_state(|s| std::mem::replace(&mut s.mouse_down, false)).unwrap_or(false);
            if was_down {
                // 直接投递到主线程。原来只置 CLICKED 标志、等主循环每秒一拍去消费，
                // 点击到弹出平均要 500 ms、最坏 1 s —— 主观就是「点了卡一阵才出来」。
                CLICK_AT.store(now_ms(), Ordering::Relaxed);
                spawn_open_netprocs();
            }
            0
        }
        WM_DESTROY => {
            PostQuitMessage(0);
            0
        }
        _ => DefWindowProcW(hwnd, msg, wp, lp),
    }
}

/// 画一帧：背景填色键 → 逐段画「小标签 + 大数值」→ 贴到窗口。
unsafe fn paint(hwnd: HWND) {
    let mut ps = PaintStruct::default();
    let hdc = BeginPaint(hwnd, &mut ps);
    if hdc == 0 {
        return;
    }
    let mut rc = Rect::default();
    if GetClientRect(hwnd, &mut rc) == 0 || rc.w() <= 0 || rc.h() <= 0 {
        EndPaint(hwnd, &ps);
        return;
    }
    let (w_, h_) = (rc.w(), rc.h());
    // 每帧都按 Windows 当前个性化设置定深浅：任务栏切主题后不必重启小窗
    let mut theme_light = system_uses_light_theme();
    let (segments, prev_light) = match with_state(|s| (s.segments.clone(), s.light)) {
        Some(v) => v,
        None => {
            EndPaint(hwnd, &ps);
            return;
        }
    };
    if prev_light != theme_light {
        with_state(|s| s.light = theme_light);
        let bg = with_state(|s| s.bg_key).unwrap_or(0);
        SetLayeredWindowAttributes(hwnd, pick_key(theme_light, bg), 0, LWA_COLORKEY);
        log_theme_switch(theme_light);
    } else {
        theme_light = prev_light;
    }
    let light = theme_light;

    let mem_dc = CreateCompatibleDC(hdc);
    let bmp = CreateCompatibleBitmap(hdc, w_, h_);
    let old = SelectObject(mem_dc, bmp);

    let bg = with_state(|s| s.bg_key).unwrap_or(0);
    let key = pick_key(light, bg);
    let brush = CreateSolidBrush(key);
    let full = Rect { left: 0, top: 0, right: w_, bottom: h_ };
    let _ = fill_rect(mem_dc, &full, brush);
    DeleteObject(brush as HGDIOBJ);

    SetBkMode(mem_dc, TRANSPARENT);

    let dpi = {
        let d = GetDpiForWindow(hwnd);
        if d == 0 { 96 } else { d }
    };
    let font_label = make_font(LABEL_PT, 400, dpi);
    let font_value = make_font(VALUE_PT, 700, dpi);
    let font_net = make_font(NET_PT, 700, dpi);

    let mut x = PADDING;
    for seg in &segments {
        // 分隔线：把网速区和 CPU/内存区分开
        if seg.divider_before && x > PADDING {
            let line = Rect {
                left: x,
                top: (h_ as f64 * 0.18) as i32,
                right: x + DIVIDER_W,
                bottom: (h_ as f64 * 0.82) as i32,
            };
            let divider = CreateSolidBrush(divider_color(light));
            let _ = fill_rect(mem_dc, &line, divider);
            DeleteObject(divider as HGDIOBJ);
            x += DIVIDER_W + DIVIDER_GAP;
        }

        let label_font = if seg.value_pt >= NET_PT { font_net } else { font_label };

        // 标签
        let l = w(&seg.label);
        SetTextColor(mem_dc, label_color(light));
        SelectObject(mem_dc, label_font);
        let mut sz = Size::default();
        GetTextExtentPoint32W(mem_dc, l.as_ptr(), (l.len() - 1) as i32, &mut sz);
        let ly = (h_ - sz.cy) / 2;
        TextOutW(mem_dc, x, ly, l.as_ptr(), (l.len() - 1) as i32);
        x += sz.cx + LABEL_GAP;

        // 数值
        let v = w(&seg.value);
        SetTextColor(mem_dc, tone_color(seg.tone, light));
        let value_font = if seg.value_pt >= NET_PT { font_net } else { font_value };
        SelectObject(mem_dc, value_font);
        let mut sz2 = Size::default();
        GetTextExtentPoint32W(mem_dc, v.as_ptr(), (v.len() - 1) as i32, &mut sz2);
        let vy = (h_ - sz2.cy) / 2;
        // 速率类固定占位：宽度按最宽参考串预留，文本右对齐 → 位数变化时数字不跳、后面不位移
        let slot_w = seg.slot_width(mem_dc, sz2.cx);
        let adv = if slot_w > 0 { slot_w } else { sz2.cx };
        let tx = if slot_w > 0 { x + slot_w - sz2.cx } else { x };
        TextOutW(mem_dc, tx, vy, v.as_ptr(), (v.len() - 1) as i32);
        x += adv + ITEM_GAP;
    }

    BitBlt(hdc, 0, 0, w_, h_, mem_dc, 0, 0, SRCCOPY);

    SelectObject(mem_dc, old);
    DeleteObject(bmp as HGDIOBJ);
    DeleteDC(mem_dc);
    DeleteObject(font_label as HGDIOBJ);
    DeleteObject(font_value as HGDIOBJ);
    DeleteObject(font_net as HGDIOBJ);
    EndPaint(hwnd, &ps);
}

unsafe fn fill_rect(hdc: HDC, rc: &Rect, brush: HBRUSH) -> i32 {
    // FillRect 在 user32 里；直接用 GDI 的 PatBlt 会漏边，这里自己按矩形填
    extern "system" {
        fn FillRect(hdc: HDC, rc: *const Rect, brush: HBRUSH) -> i32;
    }
    FillRect(hdc, rc, brush)
}

/// 用当前已选中的字体量一段文本的宽度（固定占位用）。
unsafe fn text_w(hdc: HDC, s: &str) -> i32 {
    let ws = w(s);
    let mut sz = Size::default();
    GetTextExtentPoint32W(hdc, ws.as_ptr(), (ws.len() - 1) as i32, &mut sz);
    sz.cx
}

unsafe fn make_font(pt: i32, weight: i32, dpi: u32) -> HFONT {    let px = -((pt as f64 * dpi as f64 / 96.0).round() as i32);
    CreateFontW(
        px,
        0,
        0,
        0,
        weight,
        0,
        0,
        0,
        1,
        0,
        0,
        4,
        0,
        w("Microsoft YaHei UI").as_ptr(),
    )
}

fn label_color(light: bool) -> u32 {
    if light { LABEL_LIGHT } else { LABEL_DARK }
}

/// 网速区与其它指标之间的竖分隔线
fn divider_color(light: bool) -> u32 {
    if light { rgb(0xB0, 0xB4, 0xB8) } else { rgb(0x5A, 0x5E, 0x62) }
}

fn log_theme_switch(light: bool) {
    crate::log_line(&format!(
        "[小窗] 任务栏主题切换：{}",
        if light { "浅色" } else { "深色" }
    ));
}

fn tone_color(tone: Tone, light: bool) -> u32 {
    match tone {
        Tone::Label => label_color(light),
        Tone::Value => {
            if light { VALUE_LIGHT } else { VALUE_DARK }
        }
        Tone::Warn => {
            if light { rgb(0xB5, 0x75, 0x00) } else { rgb(0xFF, 0xD6, 0x66) }
        }
        Tone::Crit => {
            if light { rgb(0xC0, 0x30, 0x30) } else { C_RED }
        }
        Tone::Net => {
            if light { rgb(0x18, 0x5F, 0xA5) } else { rgb(0x85, 0xB7, 0xEB) }
        }
    }
}
const C_RED: u32 = 0x00_66_66_FF; // BGR #FF6666

// ---------- 采样 ----------

fn tick_loop() {
    let mut last_idle = 0u64;
    let mut last_kernel = 0u64;
    let mut last_user = 0u64;
    let mut first = true;
    let mut meter = pchabit_collect::netif::NetMeter::new();
    // 磁盘：活动率 / 读 / 写（PDH，口径与 HardwareSamples 的 DiskActivity/DiskRead/DiskWrite 一致）
    let mut disk_meter = pchabit_collect::netif::DiskMeter::new();
    let mut tick: u32 = 0;
    // netd 管道：GPU 每秒问一次，断了就下一拍重连（不阻塞采集）
    let mut gpu_pipe: Option<pchabit_collect::netd::Pipe> = pchabit_collect::netd::Pipe::connect(1);
    let mut gpu_logged = false;

    loop {
        std::thread::sleep(std::time::Duration::from_millis(1000));
        if !RUNNING.load(Ordering::SeqCst) {
            break;
        }
        tick = tick.wrapping_add(1);

        // CPU：GetSystemTimes 的增量口径
        let mut idle = 0u64;
        let mut kernel = 0u64;
        let mut user = 0u64;
        unsafe {
            GetSystemTimes(&mut idle, &mut kernel, &mut user);
        }
        let cpu = if first {
            first = false;
            0.0
        } else {
            let di = idle.saturating_sub(last_idle) as f64;
            let dk = kernel.saturating_sub(last_kernel) as f64;
            let du = user.saturating_sub(last_user) as f64;
            let total = dk + du;
            if total > 0.0 { ((total - di) / total * 100.0).clamp(0.0, 100.0) } else { 0.0 }
        };
        last_idle = idle;
        last_kernel = kernel;
        last_user = user;

        // 内存
        let mut ms = MemoryStatusEx { length: std::mem::size_of::<MemoryStatusEx>() as u32, ..Default::default() };
        let mem = unsafe {
            if GlobalMemoryStatusEx(&mut ms) != 0 { ms.memory_load as f64 } else { 0.0 }
        };

        // 网速：PDH 计数器（每拍调用一次，首拍只做预热）
        let (down, up) = match meter.as_mut() {
            Some(m) => match m.sample(1.0) {
                Some((u, d)) => (d as f64, u as f64),
                None => (0.0, 0.0),
            },
            None => (0.0, 0.0),
        };

        // 磁盘：活动率（%）、读/写字节每秒；首拍没有基准时给 (0,0,0)
        let (disk_act, disk_read, disk_write) = match disk_meter.as_mut() {
            Some(m) => m.sample().unwrap_or((0.0, 0.0, 0.0)),
            None => (0.0, 0.0, 0.0),
        };

        // GPU：向 netd 问一次快照（NVML 直读；无 N 卡时 avail=false）
        let (gpu_load, vram_used, vram_total) = {
            let reply = match gpu_pipe.as_mut() {
                Some(p) => p.cmd("GPU"),
                None => None,
            };
            let mut out = None;
            if let Some(text) = reply {
                if let Ok(v) = serde_json::from_str::<serde_json::Value>(&text) {
                    if v["avail"].as_bool().unwrap_or(false) {
                        out = Some((
                            v["load"].as_f64().unwrap_or(0.0),
                            v["vramUsed"].as_f64().unwrap_or(0.0),
                            v["vramTotal"].as_f64().unwrap_or(0.0),
                        ));
                    }
                }
            } else {
                // 管道断了：这拍先空着，下一拍重新连
                gpu_pipe = pchabit_collect::netd::Pipe::connect(1);
            }
            match out {
                Some(t) => t,
                None => (-1.0, 0.0, 0.0),
            }
        };
        let gpu_avail = gpu_load >= 0.0;
        if gpu_avail && !gpu_logged {
            gpu_logged = true;
            crate::log_line(&format!(
                "[小窗] GPU 接入：{:.0}% / 显存 {:.0}MB÷{:.0}MB",
                gpu_load, vram_used, vram_total
            ));
        } else if !gpu_avail && !gpu_logged && tick % 30 == 0 {
            crate::log_line("[小窗] GPU 暂不可用（netd 无 NVML 或管道未连上）");
        }

        // 每一拍都重读设置：设置页里勾掉某项，1 秒内小窗就变
        let show_net = setting_bool("TaskbarShowNet", true);
        let show_cpu = setting_bool("TaskbarShowCpu", true);
        let show_mem = setting_bool("TaskbarShowMemory", true);
        let show_gpu = setting_bool("TaskbarShowGpu", true);
        let show_vram = setting_bool("TaskbarShowVram", true);
        let show_disk = setting_bool("TaskbarShowDisk", false);
        let show_usage = setting_bool("TaskbarShowUsage", false);

        // 顺序：先网速（下行放大加粗、排最前），再竖分隔线，最后 CPU / 内存 / GPU / 显存 / 磁盘
        let has_gpu = gpu_avail && (show_gpu || show_vram);
        let vram_pct = if vram_total > 0.0 { (vram_used / vram_total * 100.0).clamp(0.0, 100.0) } else { 0.0 };
        let mut segments: Vec<Segment> = Vec::with_capacity(6);
        if show_net {
            segments.push(Segment {
                label: "↓".into(),
                value: rate(down),
                tone: Tone::Net,
                value_pt: NET_PT,
                divider_before: false,
                slot: Slot::RateNet,
            });
            segments.push(Segment {
                label: "↑".into(),
                value: rate(up),
                tone: Tone::Net,
                value_pt: VALUE_PT,
                divider_before: false,
                slot: Slot::Rate,
            });
        }
        if show_cpu || show_mem || has_gpu || show_disk || show_usage {
            // 网速区之后用一条竖线隔开；网速没开就由第一个指标自己顶上
            let mut first_of_group = true;
            if show_cpu {
                segments.push(Segment {
                    label: "CPU".into(),
                    value: format!("{:.0}%", cpu),
                    tone: load_tone(cpu),
                    value_pt: VALUE_PT,
                    divider_before: show_net && first_of_group,
                slot: Slot::Percent,
                });
                first_of_group = false;
            }
            if show_mem {
                segments.push(Segment {
                    label: "内存".into(),
                    value: format!("{:.0}%", mem),
                    tone: load_tone(mem),
                    value_pt: VALUE_PT,
                    divider_before: show_net && first_of_group,
                slot: Slot::Percent,
                });
                first_of_group = false;
            }
            if has_gpu && show_gpu {
                segments.push(Segment {
                    label: "GPU".into(),
                    value: format!("{:.0}%", gpu_load),
                    tone: load_tone(gpu_load),
                    value_pt: VALUE_PT,
                    divider_before: show_net && first_of_group,
                slot: Slot::Percent,
                });
                first_of_group = false;
            }
            if has_gpu && show_vram {
                segments.push(Segment {
                    label: "显存".into(),
                    value: format!("{:.0}%", vram_pct),
                    tone: load_tone(vram_pct),
                    value_pt: VALUE_PT,
                    divider_before: show_net && first_of_group,
                slot: Slot::Percent,
                });
                first_of_group = false;
            }
            if show_disk {
                segments.push(Segment {
                    label: "读".into(),
                    value: rate(disk_read),
                    tone: Tone::Value,
                    value_pt: VALUE_PT,
                    divider_before: show_net && first_of_group,
                slot: Slot::Rate,
                });
                segments.push(Segment {
                    label: "写".into(),
                    value: rate(disk_write),
                    tone: Tone::Value,
                    value_pt: VALUE_PT,
                    divider_before: false,
                slot: Slot::Rate,
                });
                first_of_group = false;
            }
            if show_usage {
                segments.push(Segment {
                    label: "磁盘".into(),
                    value: format!("{:.0}%", disk_act),
                    tone: load_tone(disk_act),
                    value_pt: VALUE_PT,
                    divider_before: show_net && first_of_group,
                slot: Slot::Percent,
                });
            }
        }

        let changed = with_state(|s| {
            let same = s.segments.len() == segments.len()
                && s
                    .segments
                    .iter()
                    .zip(segments.iter())
                    .all(|(a, b)| {
                        a.value == b.value
                            && a.tone == b.tone
                            && a.value_pt == b.value_pt
                            && a.label == b.label
                            && a.divider_before == b.divider_before
                    });
            if !same {
                s.segments = segments.clone();
            }
            !same
        })
        .unwrap_or(false);

        if changed {
            remeasure();
        }
        // 每 5 拍对齐一次任务栏（Explorer 重启/任务栏改高度）
        if tick % 5 == 0 {
            unsafe {
                if let Some(h) = with_state(|s| s.hwnd) {
                    if h != 0 && IsWindow(h) == 0 {
                        attach();
                    } else {
                        position();
                    }
                    if h != 0 {
                        InvalidateRect(h, std::ptr::null(), 0);
                    }
                }
            }
        }
    }
}

fn load_tone(v: f64) -> Tone {
    if v >= 90.0 {
        Tone::Crit
    } else if v >= 75.0 {
        Tone::Warn
    } else {
        Tone::Value
    }
}

fn rate(bytes: f64) -> String {
    if bytes < 1024.0 {
        format!("{:.0}B/s", bytes)
    } else if bytes < 1024.0 * 1024.0 {
        format!("{:.0}K/s", bytes / 1024.0)
    } else {
        format!("{:.1}M/s", bytes / (1024.0 * 1024.0))
    }
}

/// 文本变了才重新量宽度 → 调窗口大小。
fn remeasure() {
    unsafe {
        let hwnd = match with_state(|s| s.hwnd) {
            Some(h) if h != 0 => h,
            _ => return,
        };
        let (segments, light) = match with_state(|s| (s.segments.clone(), s.light)) {
            Some(v) => v,
            None => return,
        };
        let hdc = GetDC(hwnd);
        if hdc == 0 {
            return;
        }
        let dpi = {
            let d = GetDpiForWindow(hwnd);
            if d == 0 { 96 } else { d }
        };
        let font_label = make_font(LABEL_PT, 400, dpi);
        let font_value = make_font(VALUE_PT, 700, dpi);
        let font_net = make_font(NET_PT, 700, dpi);

        let mut width = PADDING;
        for seg in &segments {
            if seg.divider_before && width > PADDING {
                width += DIVIDER_W + DIVIDER_GAP;
            }
            let big = seg.value_pt >= NET_PT;
            let label_font = if big { font_net } else { font_label };
            let value_font = if big { font_net } else { font_value };
            let l = w(&seg.label);
            let v = w(&seg.value);
            SelectObject(hdc, label_font);
            let mut sz = Size::default();
            GetTextExtentPoint32W(hdc, l.as_ptr(), (l.len() - 1) as i32, &mut sz);
            width += sz.cx + LABEL_GAP;
            SelectObject(hdc, value_font);
            let mut sz2 = Size::default();
            GetTextExtentPoint32W(hdc, v.as_ptr(), (v.len() - 1) as i32, &mut sz2);
            let adv = {
                let sw = seg.slot_width(hdc, sz2.cx);
                if sw > 0 { sw } else { sz2.cx }
            };
            width += adv + ITEM_GAP;
        }
        width = (width - ITEM_GAP + PADDING).max(MIN_WIDTH);

        ReleaseDC(hwnd, hdc);
        DeleteObject(font_label as HGDIOBJ);
        DeleteObject(font_value as HGDIOBJ);
        DeleteObject(font_net as HGDIOBJ);

        let prev_w = with_state(|s| s.width).unwrap_or(0);
        with_state(|s| s.width = width);
        if prev_w != width {
            crate::log_line(&format!("[小窗] 宽度 {prev_w} → {width}"));
        }
        position();
        InvalidateRect(hwnd, std::ptr::null(), 0);
        let _ = light;
    }
}
