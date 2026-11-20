//! 应用图标：从可执行文件里取 32×32 图标，转成 `data:image/bmp;base64,...`。
//!
//! 旧版由 `AppIconService`（WPF + System.Drawing）完成，这里改用 GDI 直取，
//! 不引入 `windows` / `image` 之类的 crate，保持零依赖。
//!
//! 取图流程：进程名 → exe 完整路径（进程快照 / App Paths 注册表 / 常见目录）
//! → `SHGetFileInfoW` 拿 HICON → `DrawIconEx` 画进 32bpp DIB section → 拼 BMP（无压缩）
//! → base64。BMP 不压缩，所以不需要 zlib；32×32 一张约 4 KB，base64 后 5.4 KB。
//!
//! 结果按进程名缓存（`Mutex<HashMap>`），同一进程只取一次。

#![allow(non_snake_case, non_camel_case_types)]

use std::collections::HashMap;
use std::os::raw::{c_int, c_void};
use std::sync::{Mutex, OnceLock};

const SHGFI_ICON: u32 = 0x0000_0100;
const FILE_ATTRIBUTE_NORMAL: u32 = 0x0000_0080;
const DIB_RGB_COLORS: u32 = 0;
const DI_NORMAL: u32 = 0x0003;
const PROCESS_QUERY_LIMITED_INFORMATION: u32 = 0x1000;
const TH32CS_SNAPPROCESS: u32 = 0x0000_0002;
const MAX_PATH: usize = 260;

#[repr(C)]
struct SHFILEINFOW {
    hIcon: *mut c_void,
    iIcon: c_int,
    dwAttributes: u32,
    szDisplayName: [u16; MAX_PATH],
    szTypeName: [u16; 80],
}

#[repr(C)]
struct BITMAPINFOHEADER {
    biSize: u32,
    biWidth: i32,
    biHeight: i32,
    biPlanes: u16,
    biBitCount: u16,
    biCompression: u32,
    biSizeImage: u32,
    biXPelsPerMeter: i32,
    biYPelsPerMeter: i32,
    biClrUsed: u32,
    biClrImportant: u32,
}

#[repr(C)]
struct BITMAPINFO {
    bmiHeader: BITMAPINFOHEADER,
    bmiColors: [u32; 1],
}

#[repr(C)]
#[derive(Clone, Copy)]
struct PROCESSENTRY32W {
    dwSize: u32,
    cntUsage: u32,
    th32ProcessID: u32,
    th32DefaultHeapID: usize,
    th32ModuleID: u32,
    cntThreads: u32,
    th32ParentProcessID: u32,
    pcPriClassBase: i32,
    dwFlags: u32,
    szExeFile: [u16; MAX_PATH],
}

#[link(name = "shell32")]
extern "system" {
    fn SHGetFileInfoW(
        pszPath: *const u16,
        dwFileAttributes: u32,
        psfi: *mut SHFILEINFOW,
        cbFileInfo: u32,
        uFlags: u32,
    ) -> usize;
}

#[link(name = "user32")]
extern "system" {
    fn DestroyIcon(h: *mut c_void) -> i32;
    fn GetDC(h: *mut c_void) -> *mut c_void;
    fn ReleaseDC(h: *mut c_void, dc: *mut c_void) -> c_int;
    fn DrawIconEx(
        hdc: *mut c_void,
        x: i32,
        y: i32,
        hicon: *mut c_void,
        cx: i32,
        cy: i32,
        istep: u32,
        hbr: *mut c_void,
        flags: u32,
    ) -> i32;
}

#[link(name = "gdi32")]
extern "system" {
    fn CreateCompatibleDC(hdc: *mut c_void) -> *mut c_void;
    fn CreateDIBSection(
        hdc: *mut c_void,
        pbmi: *const BITMAPINFO,
        usage: u32,
        ppv: *mut *mut c_void,
        hsec: *mut c_void,
        offset: u32,
    ) -> *mut c_void;
    fn SelectObject(hdc: *mut c_void, obj: *mut c_void) -> *mut c_void;
    fn DeleteObject(obj: *mut c_void) -> i32;
    fn DeleteDC(hdc: *mut c_void) -> i32;
}

#[link(name = "kernel32")]
extern "system" {
    fn CreateToolhelp32Snapshot(flags: u32, pid: u32) -> *mut c_void;
    fn Process32FirstW(snap: *mut c_void, entry: *mut PROCESSENTRY32W) -> i32;
    fn Process32NextW(snap: *mut c_void, entry: *mut PROCESSENTRY32W) -> i32;
    fn OpenProcess(access: u32, inherit: i32, pid: u32) -> *mut c_void;
    fn QueryFullProcessImageNameW(h: *mut c_void, flags: u32, buf: *mut u16, size: *mut u32) -> i32;
    fn CloseHandle(h: *mut c_void) -> i32;
}

#[link(name = "advapi32")]
extern "system" {
    fn RegOpenKeyExW(hkey: usize, sub: *const u16, opt: u32, sam: u32, out: *mut usize) -> i32;
    fn RegEnumKeyExW(
        hkey: usize,
        idx: u32,
        name: *mut u16,
        name_len: *mut u32,
        reserved: *mut u32,
        class: *mut u16,
        class_len: *mut u32,
        ft: *mut c_void,
    ) -> i32;
    fn RegQueryValueExW(
        hkey: usize,
        value: *const u16,
        reserved: *mut u32,
        ty: *mut u32,
        data: *mut c_void,
        size: *mut u32,
    ) -> i32;
    fn RegCloseKey(hkey: usize) -> i32;
    fn RegGetValueW(
        hkey: usize,
        sub: *const u16,
        value: *const u16,
        flags: u32,
        ty: *mut u32,
        data: *mut c_void,
        size: *mut u32,
    ) -> i32;
}

const HKEY_LOCAL_MACHINE: usize = 0x8000_0002;
const HKEY_CURRENT_USER: usize = 0x8000_0001;
const KEY_READ: u32 = 0x0002_0019;

fn wide(s: &str) -> Vec<u16> {
    s.encode_utf16().chain(std::iter::once(0)).collect()
}

fn from_wide(buf: &[u16]) -> String {
    let end = buf.iter().position(|&c| c == 0).unwrap_or(buf.len());
    String::from_utf16_lossy(&buf[..end])
}

/// 从进程快照里找同名进程，取完整 exe 路径（正在运行的应用命中率最高）。
fn path_from_snapshot(name_lower: &str) -> Option<String> {
    unsafe {
        let snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if snap.is_null() || snap as isize == -1 {
            return None;
        }
        let mut entry = PROCESSENTRY32W {
            dwSize: std::mem::size_of::<PROCESSENTRY32W>() as u32,
            cntUsage: 0,
            th32ProcessID: 0,
            th32DefaultHeapID: 0,
            th32ModuleID: 0,
            cntThreads: 0,
            th32ParentProcessID: 0,
            pcPriClassBase: 0,
            dwFlags: 0,
            szExeFile: [0; MAX_PATH],
        };
        let mut found: Option<String> = None;
        if Process32FirstW(snap, &mut entry) != 0 {
            loop {
                let exe = from_wide(&entry.szExeFile);
                if exe.to_lowercase() == name_lower {
                    let h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, 0, entry.th32ProcessID);
                    if !h.is_null() {
                        let mut buf = [0u16; MAX_PATH * 2];
                        let mut size = buf.len() as u32;
                        if QueryFullProcessImageNameW(h, 0, buf.as_mut_ptr(), &mut size) != 0 {
                            found = Some(from_wide(&buf));
                        }
                        CloseHandle(h);
                    }
                    if found.is_some() {
                        break;
                    }
                }
                if Process32NextW(snap, &mut entry) == 0 {
                    break;
                }
            }
        }
        CloseHandle(snap);
        found
    }
}

/// 注册表 App Paths：Chrome / Edge / Office 等常见软件都有登记。
fn path_from_app_paths(exe_name: &str, sam: usize) -> Option<String> {
    let sub = wide(&format!(
        "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\App Paths\\{exe_name}"
    ));
    let mut buf = [0u16; MAX_PATH * 2];
    let mut size = (buf.len() * 2) as u32;
    let mut ty: u32 = 0;
    unsafe {
        let rc = RegGetValueW(
            sam,
            sub.as_ptr(),
            std::ptr::null(),
            0x0000_0002, // RRF_RT_REG_SZ
            &mut ty,
            buf.as_mut_ptr() as *mut c_void,
            &mut size,
        );
        if rc == 0 {
            let p = from_wide(&buf);
            if !p.is_empty() && std::path::Path::new(&p).exists() {
                return Some(p);
            }
        }
    }
    None
}

/// 常见安装目录兜底（很多绿色软件只在这里能找到）。
fn path_from_common_dirs(exe_name: &str) -> Option<String> {
    let sys = std::env::var("SystemRoot").unwrap_or_else(|_| r"C:////Windows".to_string());
    let roots = [
        std::env::var("PROGRAMFILES").unwrap_or_default(),
        std::env::var("PROGRAMFILES(X86)").unwrap_or_default(),
        format!("{sys}\\System32"),
        std::env::var("LOCALAPPDATA")
            .map(|p| format!("{p}\\Programs"))
            .unwrap_or_default(),
        std::env::var("LOCALAPPDATA").unwrap_or_default(),
        std::env::var("APPDATA").unwrap_or_default(),
    ];
    for root in roots.iter().filter(|r| !r.is_empty()) {
        let p = std::path::Path::new(root).join(exe_name);
        if p.exists() {
            return Some(p.display().to_string());
        }
        // 再往下找一层，覆盖 "Program Files\Vendor\App\app.exe" 这类布局
        if let Ok(rd) = std::fs::read_dir(root) {
            for ent in rd.flatten().take(60) {
                let cand = ent.path().join(exe_name);
                if cand.exists() {
                    return Some(cand.display().to_string());
                }
            }
        }
    }
    None
}

/// 扫描卸载项里的 `DisplayIcon`，建立「exe 名 → 路径」映射。
/// 很多 Electron 应用、绿色软件既没登记 App Paths，也不在运行时进程表里，
/// 但卸载信息里会留一个 `C:////...////app.exe,0`。
fn uninstall_map() -> &'static HashMap<String, String> {
    static MAP: OnceLock<HashMap<String, String>> = OnceLock::new();
    MAP.get_or_init(|| {
        let mut map = HashMap::new();
        for (sam, root) in [
            (
                HKEY_LOCAL_MACHINE,
                "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall",
            ),
            (
                HKEY_LOCAL_MACHINE,
                "SOFTWARE\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall",
            ),
            (
                HKEY_CURRENT_USER,
                "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall",
            ),
        ] {
            scan_uninstall(sam, root, &mut map);
        }
        map
    })
}

fn scan_uninstall(sam: usize, root: &str, map: &mut HashMap<String, String>) {
    unsafe {
        let wroot = wide(root);
        let mut hkey: usize = 0;
        if RegOpenKeyExW(sam, wroot.as_ptr(), 0, KEY_READ, &mut hkey) != 0 {
            return;
        }
        let mut idx = 0u32;
        loop {
            let mut name = [0u16; 256];
            let mut len = name.len() as u32;
            let rc = RegEnumKeyExW(
                hkey,
                idx,
                name.as_mut_ptr(),
                &mut len,
                std::ptr::null_mut(),
                std::ptr::null_mut(),
                std::ptr::null_mut(),
                std::ptr::null_mut(),
            );
            if rc != 0 {
                break;
            }
            idx += 1;
            let sub = format!("{root}\\{}", from_wide(&name));
            let wsub = wide(&sub);
            let mut sub_key: usize = 0;
            if RegOpenKeyExW(sam, wsub.as_ptr(), 0, KEY_READ, &mut sub_key) != 0 {
                continue;
            }
            let mut buf = [0u16; 1024];
            let mut size = (buf.len() * 2) as u32;
            let mut ty: u32 = 0;
            let val = wide("DisplayIcon");
            let rc2 = RegQueryValueExW(
                sub_key,
                val.as_ptr(),
                std::ptr::null_mut(),
                &mut ty,
                buf.as_mut_ptr() as *mut c_void,
                &mut size,
            );
            if rc2 == 0 {
                let raw = from_wide(&buf);
                let path = raw
                    .split(',')
                    .next()
                    .unwrap_or("")
                    .trim()
                    .trim_matches('"')
                    .to_string();
                if path.to_lowercase().ends_with(".exe") {
                    if let Some(file) = std::path::Path::new(&path)
                        .file_name()
                        .and_then(|n| n.to_str())
                    {
                        map.entry(file.to_lowercase()).or_insert(path);
                    }
                }
            }
            RegCloseKey(sub_key);
        }
        RegCloseKey(hkey);
    }
}

/// 开始菜单快捷方式：用户装过的软件几乎都会在这里留一个 .lnk，
/// 里面记录着目标 exe 路径（UTF-16LE）。比递归搜目录便宜得多。
fn shortcut_map() -> &'static HashMap<String, String> {
    static MAP: OnceLock<HashMap<String, String>> = OnceLock::new();
    MAP.get_or_init(|| {
        let mut map = HashMap::new();
        let dirs = [
            std::env::var("ProgramData")
                .map(|p| format!("{p}\\Microsoft\\Windows\\Start Menu\\Programs")),
            std::env::var("APPDATA")
                .map(|p| format!("{p}\\Microsoft\\Windows\\Start Menu\\Programs")),
        ];
        for d in dirs.iter().flatten() {
            walk_shortcuts(std::path::Path::new(d), &mut map, 0);
        }
        map
    })
}

fn walk_shortcuts(dir: &std::path::Path, map: &mut HashMap<String, String>, depth: u8) {
    if depth > 4 {
        return;
    }
    let Ok(rd) = std::fs::read_dir(dir) else {
        return;
    };
    for ent in rd.flatten() {
        let p = ent.path();
        if p.is_dir() {
            walk_shortcuts(&p, map, depth + 1);
            continue;
        }
        let is_lnk = p
            .extension()
            .and_then(|x| x.to_str())
            .map(|s| s.eq_ignore_ascii_case("lnk"))
            .unwrap_or(false);
        if !is_lnk {
            continue;
        }
        if let Ok(bytes) = std::fs::read(&p) {
            if let Some((name, path)) = parse_lnk_target(&bytes) {
                map.entry(name).or_insert(path);
            }
        }
    }
}

/// 从 .lnk 字节里找 "*.exe" 的 UTF-16LE 片段，向前回溯成完整路径。
/// 不解析完整的 Shell Link 结构（那需要 COM），只做够用的一次扫描。
fn parse_lnk_target(bytes: &[u8]) -> Option<(String, String)> {
    let units: Vec<u16> = bytes
        .chunks_exact(2)
        .map(|c| u16::from_le_bytes([c[0], c[1]]))
        .collect();
    let dot = '.' as u16;
    let e = 'e' as u16;
    let x = 'x' as u16;
    for i in 0..units.len().saturating_sub(4) {
        if units[i] != dot || units[i + 1] != e || units[i + 2] != x || units[i + 3] != e {
            continue;
        }
        let mut start = i;
        while start > 0 {
            let ch = char::from_u32(units[start - 1] as u32).unwrap_or('\u{0}');
            let ok = ch.is_alphanumeric() || "\\/:._- ()[]{}~".contains(ch);
            if !ok {
                break;
            }
            start -= 1;
        }
        let s: String = units[start..i + 4]
            .iter()
            .map(|&u| char::from_u32(u as u32).unwrap_or('?'))
            .collect();
        if s.len() > 10 && (s.contains(":\\") || s.starts_with("\\\\")) {
            if let Some(f) = std::path::Path::new(&s).file_name().and_then(|n| n.to_str()) {
                return Some((f.to_lowercase(), s));
            }
        }
    }
    None
}

fn resolve_exe(proc_name: &str) -> Option<String> {
    let name = proc_name.to_lowercase();
    let with_ext = if name.ends_with(".exe") {
        name.clone()
    } else {
        format!("{name}.exe")
    };
    path_from_snapshot(&with_ext)
        .or_else(|| path_from_app_paths(&with_ext, HKEY_CURRENT_USER))
        .or_else(|| path_from_app_paths(&with_ext, HKEY_LOCAL_MACHINE))
        .or_else(|| uninstall_map().get(&with_ext).cloned())
        .or_else(|| shortcut_map().get(&with_ext).cloned())
        .or_else(|| path_from_common_dirs(&with_ext))
}

/// HICON → BMP 字节（32×32，32bpp，无压缩，行序自下而上，符合 BMP 规范）。
fn hicon_to_bmp(hicon: *mut c_void) -> Option<Vec<u8>> {
    const W: i32 = 32;
    const H: i32 = 32;
    unsafe {
        let screen = GetDC(std::ptr::null_mut());
        if screen.is_null() {
            return None;
        }
        let dc = CreateCompatibleDC(screen);
        ReleaseDC(std::ptr::null_mut(), screen);
        if dc.is_null() {
            return None;
        }
        let mut bi = BITMAPINFO {
            bmiHeader: BITMAPINFOHEADER {
                biSize: std::mem::size_of::<BITMAPINFOHEADER>() as u32,
                biWidth: W,
                biHeight: H, // 正数：内存首行是最底行，正好等于 BMP 的行序
                biPlanes: 1,
                biBitCount: 32,
                biCompression: 0, // BI_RGB
                biSizeImage: (W * H * 4) as u32,
                biXPelsPerMeter: 0,
                biYPelsPerMeter: 0,
                biClrUsed: 0,
                biClrImportant: 0,
            },
            bmiColors: [0],
        };
        let mut bits: *mut c_void = std::ptr::null_mut();
        let dib = CreateDIBSection(dc, &bi, DIB_RGB_COLORS, &mut bits, std::ptr::null_mut(), 0);
        if dib.is_null() || bits.is_null() {
            DeleteDC(dc);
            return None;
        }
        let old = SelectObject(dc, dib);
        // 清成全透明，再画图标（DI_NORMAL 会按图标自带 alpha 合成）
        std::ptr::write_bytes(bits as *mut u8, 0, (W * H * 4) as usize);
        DrawIconEx(dc, 0, 0, hicon, W, H, 0, std::ptr::null_mut(), DI_NORMAL);

        let px = std::slice::from_raw_parts(bits as *const u8, (W * H * 4) as usize);
        // 有些旧图标没有 alpha 通道，画完全透明 → 统一补成不透明，否则前端看不见
        let has_alpha = px.chunks_exact(4).any(|p| p[3] > 8);
        let mut pixels = px.to_vec();
        if !has_alpha {
            for p in pixels.chunks_exact_mut(4) {
                p[3] = 255;
            }
        }

        SelectObject(dc, old);
        DeleteObject(dib);
        DeleteDC(dc);

        // BITMAPFILEHEADER(14) + BITMAPINFOHEADER(40)
        let pixel_bytes = (W * H * 4) as u32;
        let mut out = Vec::with_capacity(54 + pixel_bytes as usize);
        out.extend_from_slice(b"BM");
        out.extend_from_slice(&(54 + pixel_bytes).to_le_bytes());
        out.extend_from_slice(&[0, 0, 0, 0]);
        out.extend_from_slice(&54u32.to_le_bytes());
        out.extend_from_slice(&40u32.to_le_bytes());
        out.extend_from_slice(&W.to_le_bytes());
        out.extend_from_slice(&H.to_le_bytes());
        out.extend_from_slice(&1u16.to_le_bytes());
        out.extend_from_slice(&32u16.to_le_bytes());
        out.extend_from_slice(&0u32.to_le_bytes());
        out.extend_from_slice(&pixel_bytes.to_le_bytes());
        out.extend_from_slice(&0i32.to_le_bytes());
        out.extend_from_slice(&0i32.to_le_bytes());
        out.extend_from_slice(&0u32.to_le_bytes());
        out.extend_from_slice(&0u32.to_le_bytes());
        out.extend_from_slice(&pixels);
        Some(out)
    }
}

fn base64(data: &[u8]) -> String {
    const T: &[u8; 64] = b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    let mut out = String::with_capacity((data.len() + 2) / 3 * 4);
    for chunk in data.chunks(3) {
        let b0 = chunk[0] as u32;
        let b1 = *chunk.get(1).unwrap_or(&0) as u32;
        let b2 = *chunk.get(2).unwrap_or(&0) as u32;
        let n = (b0 << 16) | (b1 << 8) | b2;
        out.push(T[(n >> 18) as usize & 63] as char);
        out.push(T[(n >> 12) as usize & 63] as char);
        out.push(if chunk.len() > 1 {
            T[(n >> 6) as usize & 63] as char
        } else {
            '='
        });
        out.push(if chunk.len() > 2 {
            T[n as usize & 63] as char
        } else {
            '='
        });
    }
    out
}

fn cache() -> &'static Mutex<HashMap<String, Option<String>>> {
    static CACHE: OnceLock<Mutex<HashMap<String, Option<String>>>> = OnceLock::new();
    CACHE.get_or_init(|| Mutex::new(HashMap::new()))
}

/// 取某个进程（如 `chrome.exe`）的图标，返回 `data:image/bmp;base64,...`。
/// 找不到 exe 或取图失败返回 `None`，调用方按「无图标」处理即可。
pub fn icon_for(proc_name: &str) -> Option<String> {
    let key = proc_name.to_lowercase();
    if let Ok(map) = cache().lock() {
        if let Some(v) = map.get(&key) {
            return v.clone();
        }
    }
    let url = resolve_exe(&key).and_then(|path| {
        let wpath = wide(&path);
        let mut info = SHFILEINFOW {
            hIcon: std::ptr::null_mut(),
            iIcon: 0,
            dwAttributes: 0,
            szDisplayName: [0; MAX_PATH],
            szTypeName: [0; 80],
        };
        unsafe {
            let rc = SHGetFileInfoW(
                wpath.as_ptr(),
                FILE_ATTRIBUTE_NORMAL,
                &mut info,
                std::mem::size_of::<SHFILEINFOW>() as u32,
                SHGFI_ICON,
            );
            if rc == 0 || info.hIcon.is_null() {
                return None;
            }
            let bmp = hicon_to_bmp(info.hIcon);
            DestroyIcon(info.hIcon);
            bmp.map(|b| format!("data:image/bmp;base64,{}", base64(&b)))
        }
    });
    if let Ok(mut map) = cache().lock() {
        map.insert(key, url.clone());
    }
    url
}

/// 批量取图：给详情页 / 分类页一次要几十个应用用。
pub fn icons_for(procs: &[String]) -> HashMap<String, String> {
    let mut out = HashMap::new();
    for p in procs.iter().take(200) {
        if p.trim().is_empty() {
            continue;
        }
        if let Some(url) = icon_for(p) {
            out.insert(p.clone(), url);
        }
    }
    out
}
