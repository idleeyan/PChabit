//! 任务栏小窗（Rust 版）：把子窗口挂到 `Shell_TrayWnd` 左侧，用 GDI 画 CPU / 内存 / 网速，
//! 点击打开仪表盘。口径对齐 C# `Services/TaskbarWidget.cs`（色键透明、左侧 8px、灰度抗锯齿）。
//!
//! 只在显式开启时启动（`PChabitShell.exe --taskbar`），避免与 WPF 的小窗同时存在。

use std::sync::atomic::{AtomicBool, Ordering};
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
const WM_ERASEBKGND: u32 = 0x0014;
const WM_LBUTTONDOWN: u32 = 0x0201;
const WM_LBUTTONUP: u32 = 0x0202;
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

#[derive(Clone)]
struct Segment {
    label: String,
    value: String,
    tone: Tone,
    /// 数值字号（pt）：下行网速用更大的字，其余用常规字号
    value_pt: i32,
    /// 这一项前面要不要画一条竖分隔线（把「网速区」和「CPU/内存区」分开）
    divider_before: bool,
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
static CLICKED: AtomicBool = AtomicBool::new(false);
static APP: OnceLock<tauri::AppHandle> = OnceLock::new();

fn with_state<T>(f: impl FnOnce(&mut State) -> T) -> Option<T> {
    STATE.get().map(|m| f(&mut m.lock().unwrap()))
}

/// 启动小窗：建窗口线程 + 每秒刷新线程。`--taskbar` 才会走到这里。
pub fn start(app: tauri::AppHandle, light: bool) {
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
        h_cursor: 0,
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
        WM_LBUTTONDOWN => {
            with_state(|s| s.mouse_down = true);
            0
        }
        WM_LBUTTONUP => {
            let was_down = with_state(|s| std::mem::replace(&mut s.mouse_down, false)).unwrap_or(false);
            if was_down {
                CLICKED.store(true, Ordering::SeqCst);
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
        TextOutW(mem_dc, x, vy, v.as_ptr(), (v.len() - 1) as i32);
        x += sz2.cx + ITEM_GAP;
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

unsafe fn make_font(pt: i32, weight: i32, dpi: u32) -> HFONT {
    let px = -((pt as f64 * dpi as f64 / 96.0).round() as i32);
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
    let mut tick: u32 = 0;

    loop {
        std::thread::sleep(std::time::Duration::from_millis(1000));
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

        // 顺序：先网速（下行放大加粗、排最前），再竖分隔线，最后 CPU / 内存
        let segments = vec![
            Segment {
                label: "↓".into(),
                value: rate(down),
                tone: Tone::Net,
                value_pt: NET_PT,
                divider_before: false,
            },
            Segment {
                label: "↑".into(),
                value: rate(up),
                tone: Tone::Net,
                value_pt: VALUE_PT,
                divider_before: false,
            },
            Segment {
                label: "CPU".into(),
                value: format!("{:.0}%", cpu),
                tone: load_tone(cpu),
                value_pt: VALUE_PT,
                divider_before: true,
            },
            Segment {
                label: "内存".into(),
                value: format!("{:.0}%", mem),
                tone: load_tone(mem),
                value_pt: VALUE_PT,
                divider_before: false,
            },
        ];

        let changed = with_state(|s| {
            let same = s.segments.len() == segments.len()
                && s
                    .segments
                    .iter()
                    .zip(segments.iter())
                    .all(|(a, b)| a.value == b.value && a.tone == b.tone && a.value_pt == b.value_pt);
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

        if CLICKED.swap(false, Ordering::SeqCst) {
            if let Some(app) = APP.get() {
                let app2 = app.clone();
                let app3 = app.clone();
                let _ = app2.run_on_main_thread(move || {
                    crate::open_window_at(&app3, "app.html", "任务栏小窗点击");
                });
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
            width += sz2.cx + ITEM_GAP;
        }
        width = (width - ITEM_GAP + PADDING).max(MIN_WIDTH);

        ReleaseDC(hwnd, hdc);
        DeleteObject(font_label as HGDIOBJ);
        DeleteObject(font_value as HGDIOBJ);
        DeleteObject(font_net as HGDIOBJ);

        with_state(|s| s.width = width);
        position();
        InvalidateRect(hwnd, std::ptr::null(), 0);
        let _ = light;
    }
}
