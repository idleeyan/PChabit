//! 桌面硬件悬浮窗（Rust 版）：分层窗口（per-pixel alpha）+ GDI 取字形 → 自建 ARGB 缓冲。
//!
//! 口径对齐 C# `Services/DesktopWidget.cs`（LiteMonitor 风格）：无边框置顶卡片、可拖动、
//! 双击打开仪表盘、位置记进 `settings.json` 的 `DesktopWidgetLeft/Top`。
//! 渲染走 `UpdateLayeredWindow(ULW_ALPHA)`：GDI 只负责画字（白字黑底），
//! 再按亮度当 alpha 合成到圆角卡片上，避免色键方案的杂边。

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Mutex, OnceLock};

type HWND = isize;
type HDC = isize;
type HGDIOBJ = isize;
type HFONT = isize;
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

#[repr(C)]
#[derive(Copy, Clone, Default)]
struct Size {
    cx: i32,
    cy: i32,
}

#[repr(C)]
#[derive(Copy, Clone, Default)]
struct Point {
    x: i32,
    y: i32,
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
    hbr_background: isize,
    lpsz_menu_name: *const u16,
    lpsz_class_name: *const u16,
}

#[repr(C)]
#[derive(Copy, Clone, Default)]
struct BitmapInfoHeader {
    size: u32,
    width: i32,
    height: i32,
    planes: u16,
    bit_count: u16,
    compression: u32,
    size_image: u32,
    x_pels: i32,
    y_pels: i32,
    clr_used: u32,
    clr_important: u32,
}

#[repr(C)]
#[derive(Copy, Clone, Default)]
struct BitmapInfo {
    header: BitmapInfoHeader,
    colors: [u32; 3],
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
    fn RegisterClassW(wc: *const WndClassW) -> u16;
    fn CreateWindowExW(
        ex: u32,
        class: *const u16,
        name: *const u16,
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
    fn SetWindowPos(hwnd: HWND, after: HWND, x: i32, y: i32, cx: i32, cy: i32, flags: u32) -> i32;
    fn ShowWindow(hwnd: HWND, cmd: i32) -> i32;
    fn GetCursorPos(p: *mut Point) -> i32;
    fn SetCapture(hwnd: HWND) -> HWND;
    fn ReleaseCapture() -> i32;
    fn UpdateLayeredWindow(
        hwnd: HWND,
        screen_dc: HDC,
        dst: *const Point,
        size: *const Size,
        src_dc: HDC,
        src: *const Point,
        key: u32,
        blend: *const BlendFn,
        flags: u32,
    ) -> i32;
    fn GetDC(hwnd: HWND) -> HDC;
    fn ReleaseDC(hwnd: HWND, hdc: HDC) -> i32;
    fn GetDpiForWindow(hwnd: HWND) -> u32;
    fn GetMessageW(msg: *mut Msg, hwnd: HWND, min: u32, max: u32) -> i32;
    fn TranslateMessage(msg: *const Msg) -> i32;
    fn DispatchMessageW(msg: *const Msg) -> isize;
    fn PostQuitMessage(code: i32);
    fn GetSystemTimes(idle: *mut u64, kernel: *mut u64, user: *mut u64) -> i32;
    fn SystemParametersInfoW(action: u32, ui: u32, pv: *mut Rect, win_ini: u32) -> i32;
}

#[repr(C)]
#[derive(Copy, Clone, Default)]
struct BlendFn {
    blend_op: u8,
    blend_flags: u8,
    source_constant_alpha: u8,
    alpha_format: u8,
}

#[link(name = "gdi32")]
extern "system" {
    fn CreateFontW(
        height: i32,
        width: i32,
        esc: i32,
        orient: i32,
        weight: i32,
        italic: u32,
        underline: u32,
        strike: u32,
        charset: u32,
        out_prec: u32,
        clip_prec: u32,
        quality: u32,
        pitch: u32,
        face: *const u16,
    ) -> HFONT;
    fn DeleteObject(obj: HGDIOBJ) -> i32;
    fn CreateCompatibleDC(hdc: HDC) -> HDC;
    fn DeleteDC(hdc: HDC) -> i32;
    fn SelectObject(hdc: HDC, obj: HGDIOBJ) -> HGDIOBJ;
    fn CreateDIBSection(
        hdc: HDC,
        info: *const BitmapInfo,
        usage: u32,
        bits: *mut *mut u8,
        section: isize,
        offset: u32,
    ) -> isize;
    fn TextOutW(hdc: HDC, x: i32, y: i32, text: *const u16, len: i32) -> i32;
    fn SetTextColor(hdc: HDC, color: u32) -> u32;
    fn SetBkMode(hdc: HDC, mode: i32) -> i32;
    fn GetTextExtentPoint32W(hdc: HDC, text: *const u16, len: i32, size: *mut Size) -> i32;
    fn PatBlt(hdc: HDC, x: i32, y: i32, w: i32, h: i32, rop: u32) -> i32;
}

#[link(name = "kernel32")]
extern "system" {
    fn GetModuleHandleW(name: *const u16) -> HINSTANCE;
    fn GlobalMemoryStatusEx(status: *mut MemoryStatusEx) -> i32;
}

const WS_POPUP: u32 = 0x8000_0000;
const WS_EX_LAYERED: u32 = 0x0008_0000;
const WS_EX_TOOLWINDOW: u32 = 0x0000_0080;
const WS_EX_NOACTIVATE: u32 = 0x0800_0000;
const WS_EX_TOPMOST: u32 = 0x0000_0008;
const SWP_NOACTIVATE: u32 = 0x0010;
const SWP_SHOWWINDOW: u32 = 0x0040;
const ULW_ALPHA: u32 = 0x2;
const AC_SRC_ALPHA: u8 = 0x1;
const WM_LBUTTONDOWN: u32 = 0x0201;
const WM_LBUTTONUP: u32 = 0x0202;
const WM_MOUSEMOVE: u32 = 0x0200;
const WM_LBUTTONDBLCLK: u32 = 0x0203;
const WM_DESTROY: u32 = 0x0002;
const CS_DBLCLKS: u32 = 0x0008;
const MK_LBUTTON: usize = 0x0001;
const SPI_GETWORKAREA: u32 = 0x0030;
const BLACKNESS: u32 = 0x0000_0042;
const BITMAPINFOHEADER_SIZE: u32 = 40;

const CLASS_NAME: &str = "PChabitDesktopWidgetRust";
const BASE_W: i32 = 300;
const BASE_H: i32 = 176;
const PAD: i32 = 16;
const RADIUS: i32 = 14;

fn wide(s: &str) -> Vec<u16> {
    let mut v: Vec<u16> = s.encode_utf16().collect();
    v.push(0);
    v
}

struct Snapshot {
    cpu: f64,
    mem: f64,
    down: f64,
    up: f64,
}

struct State {
    hwnd: HWND,
    width: i32,
    height: i32,
    dragging: bool,
    drag_origin: (i32, i32),
    win_origin: (i32, i32),
    snap: Snapshot,
}

static STATE: OnceLock<Mutex<State>> = OnceLock::new();
static APP: OnceLock<tauri::AppHandle> = OnceLock::new();
static OPEN_REQ: AtomicBool = AtomicBool::new(false);

fn with_state<T>(f: impl FnOnce(&mut State) -> T) -> Option<T> {
    STATE.get().map(|m| f(&mut m.lock().unwrap()))
}

/// 启动桌面组件。`--desktop` 时调用。
pub fn start(app: tauri::AppHandle) {
    let _ = APP.set(app);
    let (w, h, x, y) = read_geometry();
    STATE.get_or_init(|| {
        Mutex::new(State {
            hwnd: 0,
            width: w,
            height: h,
            dragging: false,
            drag_origin: (0, 0),
            win_origin: (x, y),
            snap: Snapshot { cpu: 0.0, mem: 0.0, down: 0.0, up: 0.0 },
        })
    });

    std::thread::Builder::new()
        .name("desktop-widget".into())
        .spawn(move || unsafe {
            create_window();
            let mut msg = Msg::default();
            while GetMessageW(&mut msg, 0, 0, 0) > 0 {
                TranslateMessage(&msg);
                DispatchMessageW(&msg);
            }
        })
        .ok();

    std::thread::Builder::new()
        .name("desktop-metrics".into())
        .spawn(move || tick_loop())
        .ok();
}

/// 读 `settings.json` 里的位置与尺寸；没配过就放在主屏右下角往内缩一点。
fn read_geometry() -> (i32, i32, i32, i32) {
    let p = std::path::PathBuf::from(std::env::var("LOCALAPPDATA").unwrap_or_default())
        .join("PChabit")
        .join("settings.json");
    let v: serde_json::Value = std::fs::read_to_string(p)
        .ok()
        .and_then(|t| serde_json::from_str(&t).ok())
        .unwrap_or(serde_json::Value::Null);
    let width = v.get("DesktopWidgetWidth").and_then(|x| x.as_i64()).unwrap_or(BASE_W as i64) as i32;
    let height = v.get("DesktopWidgetHeight").and_then(|x| x.as_i64()).unwrap_or(BASE_H as i64) as i32;
    let mut work = Rect::default();
    unsafe {
        SystemParametersInfoW(SPI_GETWORKAREA, 0, &mut work, 0);
    }
    let left = v
        .get("DesktopWidgetLeft")
        .and_then(|x| x.as_i64())
        .map(|x| x as i32)
        .unwrap_or(work.right - width - 24);
    let top = v
        .get("DesktopWidgetTop")
        .and_then(|x| x.as_i64())
        .map(|x| x as i32)
        .unwrap_or(work.top + 24);
    (width, height, left, top)
}

unsafe fn create_window() {
    let instance = GetModuleHandleW(std::ptr::null());
    let class = wide(CLASS_NAME);
    let wc = WndClassW {
        style: CS_DBLCLKS,
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

    let (w, h, x, y) = with_state(|s| (s.width, s.height, s.win_origin.0, s.win_origin.1)).unwrap_or((BASE_W, BASE_H, 0, 0));
    // 设置页的两个开关在创建时生效：置顶、鼠标穿透（改完需重启壳）
    let mut ex_style = WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
    if crate::setting_bool_default("DesktopWidgetTopmost", true) {
        ex_style |= WS_EX_TOPMOST;
    }
    if crate::setting_bool_default("DesktopWidgetClickThrough", false) {
        ex_style |= 0x0000_0020; // WS_EX_TRANSPARENT：鼠标事件穿透到桌面
    }
    let hwnd = CreateWindowExW(
        ex_style,
        class.as_ptr(),
        std::ptr::null(),
        WS_POPUP,
        x,
        y,
        w,
        h,
        0,
        0,
        instance,
        0,
    );
    if hwnd == 0 {
        crate::log_line("[桌面] 窗口创建失败");
        return;
    }
    with_state(|s| s.hwnd = hwnd);
    ShowWindow(hwnd, 4); // SW_SHOWNOACTIVATE
    render();
    crate::log_line(&format!("[桌面] 悬浮窗已创建 {w}×{h} @({x},{y})"));
}

unsafe extern "system" fn wnd_proc(hwnd: HWND, msg: u32, wp: WPARAM, lp: LPARAM) -> LRESULT {
    match msg {
        WM_LBUTTONDOWN => {
            let mut p = Point::default();
            GetCursorPos(&mut p);
            SetCapture(hwnd);
            with_state(|s| {
                s.dragging = true;
                s.drag_origin = (p.x, p.y);
            });
            0
        }
        WM_MOUSEMOVE => {
            if wp & MK_LBUTTON == 0 {
                return 0;
            }
            let mut p = Point::default();
            GetCursorPos(&mut p);
            let (dx, dy, w, h) = match with_state(|s| {
                if !s.dragging {
                    return None;
                }
                let dx = p.x - s.drag_origin.0;
                let dy = p.y - s.drag_origin.1;
                s.win_origin = (s.win_origin.0 + dx, s.win_origin.1 + dy);
                s.drag_origin = (p.x, p.y);
                Some((s.win_origin.0, s.win_origin.1, s.width, s.height))
            }) {
                Some(Some(v)) => v,
                _ => return 0,
            };
            let _ = dx;
            SetWindowPos(hwnd, 0, x_of(w, h, dx), y_of(w, h, dy), w, h, SWP_NOACTIVATE);
            0
        }
        WM_LBUTTONUP => {
            ReleaseCapture();
            with_state(|s| s.dragging = false);
            save_position();
            0
        }
        WM_LBUTTONDBLCLK => {
            OPEN_REQ.store(true, Ordering::SeqCst);
            0
        }
        WM_DESTROY => {
            PostQuitMessage(0);
            0
        }
        _ => DefWindowProcW(hwnd, msg, wp, lp),
    }
}

// 拖动时 win_origin 已被更新，SetWindowPos 直接用新坐标（两个小函数只是让调用点对齐签名）
fn x_of(_w: i32, _h: i32, x: i32) -> i32 {
    x
}
fn y_of(_w: i32, _h: i32, y: i32) -> i32 {
    y
}

fn save_position() {
    let (x, y, w, h) = match with_state(|s| (s.win_origin.0, s.win_origin.1, s.width, s.height)) {
        Some(v) => v,
        None => return,
    };
    let path = std::path::PathBuf::from(std::env::var("LOCALAPPDATA").unwrap_or_default())
        .join("PChabit")
        .join("settings.json");
    let Ok(text) = std::fs::read_to_string(&path) else { return };
    let Ok(mut v) = serde_json::from_str::<serde_json::Value>(&text) else { return };
    if let Some(obj) = v.as_object_mut() {
        obj.insert("DesktopWidgetLeft".into(), serde_json::json!(x));
        obj.insert("DesktopWidgetTop".into(), serde_json::json!(y));
        obj.insert("DesktopWidgetWidth".into(), serde_json::json!(w));
        obj.insert("DesktopWidgetHeight".into(), serde_json::json!(h));
    }
    if let Ok(t) = serde_json::to_string_pretty(&v) {
        let _ = std::fs::write(path, t);
    }
}

// ---------- 渲染 ----------

const CARD_R: u8 = 32;
const CARD_G: u8 = 35;
const CARD_B: u8 = 41;
const CARD_A: f32 = 0.92;

/// 画一帧：GDI 把字画进黑底 DIB（白字），再按亮度合成到圆角卡片上。
unsafe fn render() {
    let hwnd = match with_state(|s| s.hwnd) {
        Some(h) if h != 0 => h,
        _ => return,
    };
    let (w, h, snap) = match with_state(|s| {
        (s.width, s.height, Snapshot { cpu: s.snap.cpu, mem: s.snap.mem, down: s.snap.down, up: s.snap.up })
    }) {
        Some(v) => v,
        None => return,
    };
    if w <= 0 || h <= 0 {
        return;
    }

    let screen_dc = GetDC(0);
    if screen_dc == 0 {
        return;
    }
    let mem_dc = CreateCompatibleDC(screen_dc);
    let mut info = BitmapInfo::default();
    info.header.size = BITMAPINFOHEADER_SIZE;
    info.header.width = w;
    info.header.height = -h; // top-down
    info.header.planes = 1;
    info.header.bit_count = 32;
    info.header.compression = 0; // BI_RGB
    let mut bits: *mut u8 = std::ptr::null_mut();
    let dib = CreateDIBSection(mem_dc, &info, 0, &mut bits, 0, 0);
    if dib == 0 || bits.is_null() {
        DeleteDC(mem_dc);
        ReleaseDC(0, screen_dc);
        return;
    }
    let old = SelectObject(mem_dc, dib);

    // 1) 黑底 + 白字（alpha 由亮度当蒙版）
    let _ = PatBlt(mem_dc, 0, 0, w, h, BLACKNESS);
    SetBkMode(mem_dc, 1);
    let dpi = {
        let d = GetDpiForWindow(hwnd);
        if d == 0 { 96 } else { d }
    };
    let scale = dpi as f32 / 96.0;
    let font_label = make_font((12.0 * scale) as i32, 400, dpi);
    let font_value = make_font((15.0 * scale) as i32, 700, dpi);
    let font_title = make_font((11.0 * scale) as i32, 600, dpi);

    // 逐项按设置开关拼行（设置页 DesktopWidgetShow* 改了下一拍就生效）
    let mut rows: Vec<(&str, String)> = Vec::new();
    if crate::setting_bool_default("DesktopWidgetShowCpu", true) {
        rows.push(("CPU", format!("{:.0}%", snap.cpu)));
    }
    if crate::setting_bool_default("DesktopWidgetShowMemory", true) {
        rows.push(("内存", format!("{:.0}%", snap.mem)));
    }
    if crate::setting_bool_default("DesktopWidgetShowNet", true) {
        rows.push(("下载", rate(snap.down)));
        rows.push(("上传", rate(snap.up)));
    }

    let pad = (PAD as f32 * scale) as i32;
    let row_h = ((h - pad * 2) / (rows.len() as i32 + 1)).max(18);
    SetTextColor(mem_dc, 0x00FF_FFFF);
    SelectObject(mem_dc, font_title);
    draw_text(mem_dc, pad, pad, "PChabit · 硬件");
    for (i, (label, value)) in rows.iter().enumerate() {
        let y = pad + row_h * (i as i32 + 1) - 4;
        SelectObject(mem_dc, font_label);
        SetTextColor(mem_dc, 0x00B0_B0B0);
        draw_text(mem_dc, pad, y, label);
        SelectObject(mem_dc, font_value);
        SetTextColor(mem_dc, 0x00FF_FFFF);
        let vw = text_width(mem_dc, value);
        draw_text(mem_dc, w - pad - vw, y - 2, value);
    }

    // 2) 合成：圆角卡片底 + 白字亮度当 alpha
    let pixels = std::slice::from_raw_parts_mut(bits, (w * h * 4) as usize);
    for y in 0..h {
        for x in 0..w {
            let inside = rounded_inside(x, y, w, h, (RADIUS as f32 * scale) as i32);
            let i = ((y * w + x) * 4) as usize;
            let lum = pixels[i + 2] as f32 / 255.0; // GDI 画的白字红通道
            let (mut r, mut g, mut b, mut a) = (CARD_R as f32, CARD_G as f32, CARD_B as f32, CARD_A);
            if inside {
                // 文字叠在卡片上：颜色取白，强度取亮度
                r = CARD_R as f32 + (255.0 - CARD_R as f32) * lum;
                g = CARD_G as f32 + (255.0 - CARD_G as f32) * lum;
                b = CARD_B as f32 + (255.0 - CARD_B as f32) * lum;
                a = CARD_A + (1.0 - CARD_A) * lum;
            } else {
                a = 0.0;
            }
            // 预乘 alpha
            pixels[i] = (b * a) as u8;
            pixels[i + 1] = (g * a) as u8;
            pixels[i + 2] = (r * a) as u8;
            pixels[i + 3] = (a * 255.0) as u8;
        }
    }

    let size = Size { cx: w, cy: h };
    let src = Point { x: 0, y: 0 };
    let dst = Point {
        x: with_state(|s| s.win_origin.0).unwrap_or(0),
        y: with_state(|s| s.win_origin.1).unwrap_or(0),
    };
    let blend = BlendFn { blend_op: 0, blend_flags: 0, source_constant_alpha: 255, alpha_format: AC_SRC_ALPHA };
    let _ = UpdateLayeredWindow(hwnd, screen_dc, &dst, &size, mem_dc, &src, 0, &blend, ULW_ALPHA);

    SelectObject(mem_dc, old);
    DeleteObject(dib as HGDIOBJ);
    DeleteDC(mem_dc);
    ReleaseDC(0, screen_dc);
    DeleteObject(font_label as HGDIOBJ);
    DeleteObject(font_value as HGDIOBJ);
    DeleteObject(font_title as HGDIOBJ);
}

fn rounded_inside(x: i32, y: i32, w: i32, h: i32, r: i32) -> bool {
    let (cx, cy) = if x < r && y < r {
        (r, r)
    } else if x >= w - r && y < r {
        (w - r - 1, r)
    } else if x < r && y >= h - r {
        (r, h - r - 1)
    } else if x >= w - r && y >= h - r {
        (w - r - 1, h - r - 1)
    } else {
        return true;
    };
    let (dx, dy) = ((x - cx) as f32, (y - cy) as f32);
    dx * dx + dy * dy <= (r * r) as f32
}

unsafe fn make_font(px: i32, weight: i32, _dpi: u32) -> HFONT {
    CreateFontW(
        -px,
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
        wide("Microsoft YaHei UI").as_ptr(),
    )
}

unsafe fn draw_text(hdc: HDC, x: i32, y: i32, text: &str) {
    let t = wide(text);
    TextOutW(hdc, x, y, t.as_ptr(), (t.len() - 1) as i32);
}

unsafe fn text_width(hdc: HDC, text: &str) -> i32 {
    let t = wide(text);
    let mut sz = Size::default();
    GetTextExtentPoint32W(hdc, t.as_ptr(), (t.len() - 1) as i32, &mut sz);
    sz.cx
}

// ---------- 采样 ----------

fn tick_loop() {
    let mut meter = pchabit_collect::netif::NetMeter::new();
    let mut last_idle = 0u64;
    let mut last_kernel = 0u64;
    let mut last_user = 0u64;
    let mut first = true;

    loop {
        std::thread::sleep(std::time::Duration::from_millis(1000));
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
            let total = (kernel.saturating_sub(last_kernel) + user.saturating_sub(last_user)) as f64;
            if total > 0.0 { ((total - di) / total * 100.0).clamp(0.0, 100.0) } else { 0.0 }
        };
        last_idle = idle;
        last_kernel = kernel;
        last_user = user;

        let mut ms = MemoryStatusEx { length: std::mem::size_of::<MemoryStatusEx>() as u32, ..Default::default() };
        let mem = unsafe {
            if GlobalMemoryStatusEx(&mut ms) != 0 { ms.memory_load as f64 } else { 0.0 }
        };

        let (down, up) = match meter.as_mut() {
            Some(m) => match m.sample(1.0) {
                Some((u, d)) => (d as f64, u as f64),
                None => (0.0, 0.0),
            },
            None => (0.0, 0.0),
        };

        with_state(|s| {
            s.snap = Snapshot { cpu, mem, down, up };
        });
        unsafe { render() };

        if OPEN_REQ.swap(false, Ordering::SeqCst) {
            if let Some(app) = APP.get() {
                let a1 = app.clone();
                let a2 = app.clone();
                let _ = a1.run_on_main_thread(move || {
                    crate::open_window_at(&a2, "app.html", "桌面组件双击");
                });
            }
        }
    }
}

fn rate(bytes: f64) -> String {
    if bytes < 1024.0 {
        format!("{:.0} B/s", bytes)
    } else if bytes < 1024.0 * 1024.0 {
        format!("{:.0} K/s", bytes / 1024.0)
    } else {
        format!("{:.1} M/s", bytes / (1024.0 * 1024.0))
    }
}
