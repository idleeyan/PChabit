//! 浏览器扩展通道：`127.0.0.1:8765` 上的 WebSocket 服务。
//!
//! 为什么要自己做：扩展固定连 `ws://localhost:8765`（`extensions/tai-browser-extension/background.js`
//! 的 `DEFAULT_WS_PORT`），WPF 侧用 `HttpListener` 起的那个服务在采集宿主模式下不该再占端口。
//! 这里手写最小 WebSocket（握手只需要 SHA-1+Base64，帧解析只处理文本/关闭/心跳），
//! 不引第三方 crate。
//!
//! 会话口径照抄 `DataCollectionService.Web.cs`：
//! - `pageView` / `tabActivate` 开新会话（同 tab 的旧会话先收尾落库）；
//! - `pageClose` / `tabClose` 收尾；
//! - `heartbeat` / `visibility` / `idle` 只改活跃状态（活跃时长按事件间隔累加）；
//! - `scroll` 记最大滚动百分比并视为活跃，`click` 记点击数，`formSubmit` 置表单标记；
//! - 退出/断连时把在场会话收尾，未收尾的会话每 30 s 兜底 upsert 一次（进程被杀也不丢）。

use std::collections::HashMap;
use std::io::{ErrorKind, Read, Write};
use std::net::{TcpListener, TcpStream};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

use serde_json::Value;

use crate::dt;
use crate::host::LogFn;
use crate::sha1::{base64, sha1};
use crate::store::{Store, WebRow};

const WS_GUID: &str = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
const CATALOG_TTL: u64 = 300;

// ---------------------------------------------------------------- 会话引擎

#[derive(Clone)]
struct Act {
    id: String,
    start_ms: i64,
    last_active_ms: i64,
    acc_active_ms: i64,
    active: bool,
    idle: bool,
    visible: bool,
    foreground: bool,
    url: String,
    title: String,
    domain: String,
    favicon: Option<String>,
    browser: String,
    tab_id: i64,
    scroll: i64,
    clicks: i64,
    form: bool,
    category: (Option<String>, Option<i64>, Option<String>),
}

pub struct Engine {
    sessions: HashMap<String, Act>,
    cats: Vec<(i64, String, bool)>,
    maps: Vec<(String, i64)>,
    cat_at: Option<Instant>,
    /// 当日搜索次数（与 WPF 的 `_todaySearchCount` 同口径：只数「有 query 的 search 事件」）
    searches: i64,
    search_date: String,
}

impl Default for Engine {
    fn default() -> Self {
        Engine {
            sessions: HashMap::new(),
            cats: Vec::new(),
            maps: Vec::new(),
            cat_at: None,
            searches: 0,
            search_date: String::new(),
        }
    }
}

impl Engine {
    pub fn new() -> Engine {
        Engine::default()
    }

    /// 当日搜索次数（`date` 是 `yyyy-MM-dd`；跨日自动归零，与 WPF 一致）。
    pub fn search_count(&self, date: &str) -> i64 {
        if self.search_date == date {
            self.searches
        } else {
            0
        }
    }

    fn ensure_catalog(&mut self, store: &Store) {
        let stale = self
            .cat_at
            .map(|t| t.elapsed().as_secs() >= CATALOG_TTL)
            .unwrap_or(true);
        if !stale {
            return;
        }
        let (cats, mut maps) = store.web_catalog();
        maps.sort_by(|a, b| b.0.len().cmp(&a.0.len()));
        self.cats = cats;
        self.maps = maps;
        self.cat_at = Some(Instant::now());
    }

    /// 域名 → (分类名, 分类 Id, 来源)。映射优先（Mapping），关键词兜底（Fallback）。
    fn resolve(&self, domain: &str) -> (Option<String>, Option<i64>, Option<String>) {
        let lower = domain.to_ascii_lowercase();
        if !lower.is_empty() {
            for (pattern, cat_id) in &self.maps {
                let p = pattern.to_ascii_lowercase();
                let hit = if let Some(suffix) = p.strip_prefix("*.") {
                    lower == suffix || lower.ends_with(&format!(".{suffix}"))
                } else {
                    lower == p || lower.ends_with(&format!(".{p}"))
                };
                if hit {
                    if let Some((id, name, _)) = self
                        .cats
                        .iter()
                        .find(|(cid, _, act)| *cid == *cat_id && *act)
                    {
                        return (
                            Some(name.clone()),
                            Some(*id),
                            Some("Mapping".to_string()),
                        );
                    }
                }
            }
        }
        let fallback = default_category(&lower);
        match self
            .cats
            .iter()
            .find(|(_, name, active)| name == fallback && *active)
        {
            Some((id, name, _)) => (
                Some(name.clone()),
                Some(*id),
                Some("Fallback".to_string()),
            ),
            None => (Some(fallback.to_string()), None, Some("Fallback".to_string())),
        }
    }

    /// 处理一条扩展消息。`client` 是这条连接的短 id。
    pub fn on_message(&mut self, store: &Store, client: &str, text: &str, log: &LogFn) {
        let v: Value = match serde_json::from_str(text) {
            Ok(v) => v,
            Err(e) => {
                log(format!("浏览器消息不是合法 JSON，已丢弃（{client}）：{e}"));
                return;
            }
        };
        self.ensure_catalog(store);
        self.dispatch(store, client, &v, log);
    }

    fn dispatch(&mut self, store: &Store, client: &str, v: &Value, log: &LogFn) {
        let ty = str_of(v, "type").to_ascii_lowercase();
        let tab_id = v["tabId"].as_i64().unwrap_or(0);
        let key = format!("{client}_{tab_id}");
        let now = msg_time(v);
        let url = str_of(v, "url");
        let title = str_of(v, "title");
        let browser = {
            let b = str_of(v, "browser");
            if b.is_empty() {
                "Unknown".to_string()
            } else {
                b
            }
        };

        match ty.as_str() {
            "connection" => {
                log(format!("浏览器扩展已连接：{browser}（{client}）"));
            }
            "browser_sync_ready" => {
                let ver = str_of(v, "extVersion");
                log(format!("浏览器同步就绪：{browser} v{ver}"));
            }
            "pageview" | "tabactivate" | "tabswitch" => {
                if url.is_empty() {
                    return;
                }
                self.close(&key, store, now, log);
                let domain = domain_of(&url);
                let category = self.resolve(&domain);
                let act = Act {
                    id: uuid(),
                    start_ms: now,
                    last_active_ms: now,
                    acc_active_ms: 0,
                    active: !(v["idle"].as_bool().unwrap_or(false)),
                    idle: v["idle"].as_bool().unwrap_or(false),
                    visible: v["visible"].as_bool().unwrap_or(true),
                    foreground: true,
                    url: url.clone(),
                    title: title.clone(),
                    domain: domain.clone(),
                    favicon: opt_str(v, "favIconUrl"),
                    browser: browser.clone(),
                    tab_id,
                    scroll: 0,
                    clicks: 0,
                    form: false,
                    category,
                };
                self.sessions.insert(key, act);
                self.history(store, &browser, &url, &title, now);
            }
            "navigation" => {
                if url.is_empty() {
                    return;
                }
                self.history(store, &browser, &url, &title, now);
                if !self.sessions.contains_key(&key) {
                    let domain = domain_of(&url);
                    let category = self.resolve(&domain);
                    let act = Act {
                        id: uuid(),
                        start_ms: now,
                        last_active_ms: now,
                        acc_active_ms: 0,
                        active: true,
                        idle: false,
                        visible: true,
                        foreground: true,
                        url: url.clone(),
                        title: title.clone(),
                        domain,
                        favicon: opt_str(v, "favIconUrl"),
                        browser: browser.clone(),
                        tab_id,
                        scroll: 0,
                        clicks: 0,
                        form: false,
                        category,
                    };
                    self.sessions.insert(key, act);
                }
            }
            "pageclose" | "tabclose" => {
                self.close(&key, store, now, log);
            }
            "heartbeat" => {
                if !self.sessions.contains_key(&key) {
                    if url.is_empty() {
                        return;
                    }
                    let domain = domain_of(&url);
                    let category = self.resolve(&domain);
                    let act = Act {
                        id: uuid(),
                        start_ms: now,
                        last_active_ms: now,
                        acc_active_ms: 0,
                        active: true,
                        idle: false,
                        visible: true,
                        foreground: true,
                        url: url.clone(),
                        title: title.clone(),
                        domain,
                        favicon: opt_str(v, "favIconUrl"),
                        browser: browser.clone(),
                        tab_id,
                        scroll: 0,
                        clicks: 0,
                        form: false,
                        category,
                    };
                    self.sessions.insert(key.clone(), act);
                }
                let idle = v["idle"]
                    .as_bool()
                    .unwrap_or_else(|| !v["active"].as_bool().unwrap_or(true));
                let visible = v["visible"]
                    .as_bool()
                    .or_else(|| v["active"].as_bool())
                    .unwrap_or(true);
                if let Some(a) = self.sessions.get_mut(&key) {
                    let fg = a.foreground;
                    apply_state(a, now, idle, visible, fg);
                }
            }
            "visibility" => {
                if let Some(a) = self.sessions.get_mut(&key) {
                    let visible = v["visible"].as_bool().unwrap_or(true);
                    let fg = a.foreground;
                    let idle = a.idle;
                    apply_state(a, now, idle, visible, fg);
                }
            }
            "idle" => {
                if let Some(a) = self.sessions.get_mut(&key) {
                    let idle = v["idle"].as_bool().unwrap_or(true);
                    let visible = a.visible;
                    let fg = a.foreground;
                    apply_state(a, now, idle, visible, fg);
                }
            }
            "scroll" => {
                if let Some(a) = self.sessions.get_mut(&key) {
                    if let Some(p) = v["percentage"].as_i64() {
                        if p > a.scroll {
                            a.scroll = p.clamp(0, 100);
                        }
                    }
                    resume(a, now);
                }
            }
            "click" => {
                if let Some(a) = self.sessions.get_mut(&key) {
                    a.clicks += 1;
                    resume(a, now);
                }
            }
            "formsubmit" => {
                if let Some(a) = self.sessions.get_mut(&key) {
                    a.form = true;
                }
            }
            // search：WPF 口径是「会话在场 + 带 query」才计数（不存词原文）
            "search" => {
                if self.sessions.contains_key(&key) {
                    let q = v["query"].as_str().unwrap_or("").trim();
                    let q = if q.is_empty() { v["data"]["query"].as_str().unwrap_or("").trim() } else { q };
                    if !q.is_empty() {
                        let today = dt::date_ms(now);
                        if self.search_date != today {
                            self.search_date = today;
                            self.searches = 0;
                        }
                        self.searches += 1;
                    }
                }
            }
            "pong" | "status" => {}
            t if t.starts_with("bookmarks") || t.starts_with("browser_sync") => {}
            _ => {}
        }
    }

    fn history(&self, store: &Store, browser: &str, url: &str, title: &str, now_ms: i64) {
        if let Err(e) = store.history_add(browser, url, title, now_ms, dt::local_ms_now()) {
            let _ = e; // 历史重复插入忽略；真失败不让会话链断掉
        }
    }

    /// 收尾并落一行（已结束）。
    fn close(&mut self, key: &str, store: &Store, now_ms: i64, log: &LogFn) {
        if let Some(a) = self.sessions.remove(key) {
            let row = finish_row(&a, now_ms);
            match store.web_upsert(&row) {
                Ok(_) => log(format!(
                    "网页会话入库 {} / {:.1}s / {}",
                    row.url,
                    row.duration_ticks as f64 / 10_000_000.0,
                    row.category_name.unwrap_or_default()
                )),
                Err(e) => log(format!("网页会话入库失败：{e}")),
            }
        }
    }

    /// 周期兜底：就业中会话写成「未结束」的行（EndTime 为空、IsActiveTab=1）。
    pub fn save_active(&mut self, store: &Store, log: &LogFn) {
        if self.sessions.is_empty() {
            return;
        }
        let now = dt::local_ms_now();
        for a in self.sessions.values() {
            let row = live_row(a, now);
            if let Err(e) = store.web_upsert(&row) {
                log(format!("网页会话兜底写入失败：{e}"));
            }
        }
    }

    pub fn finalize_client(&mut self, store: &Store, client: &str, log: &LogFn) {
        let keys: Vec<String> = self
            .sessions
            .keys()
            .filter(|k| k.starts_with(&format!("{client}_")))
            .cloned()
            .collect();
        let now = dt::local_ms_now();
        for k in keys {
            if let Some(a) = self.sessions.remove(&k) {
                let row = finish_row(&a, now);
                if let Err(e) = store.web_upsert(&row) {
                    log(format!("断连收尾会话失败：{e}"));
                }
            }
        }
    }

    pub fn finalize_all(&mut self, store: &Store, log: &LogFn) -> usize {
        let now = dt::local_ms_now();
        let n = self.sessions.len();
        for (_, a) in self.sessions.drain() {
            let row = finish_row(&a, now);
            if let Err(e) = store.web_upsert(&row) {
                log(format!("退出前收尾网页会话失败：{e}"));
            }
        }
        n
    }

    pub fn active_count(&self) -> usize {
        self.sessions.len()
    }
}

fn apply_state(a: &mut Act, now: i64, idle: bool, visible: bool, foreground: bool) {
    if a.active {
        let delta = now - a.last_active_ms;
        if delta > 0 {
            a.acc_active_ms += delta;
        }
    }
    a.idle = idle;
    a.visible = visible;
    a.foreground = foreground;
    a.active = !idle && visible && foreground;
    if a.active {
        a.last_active_ms = now;
    }
}

fn resume(a: &mut Act, now: i64) {
    if !a.active {
        a.idle = false;
        a.active = !a.idle && a.visible && a.foreground;
        a.last_active_ms = now;
    } else {
        a.last_active_ms = now;
    }
}

fn duration_ms(a: &Act, now: i64) -> i64 {
    (now - a.start_ms).max(0)
}

fn active_ms(a: &Act, now: i64) -> (i64, i64) {
    let wall = duration_ms(a, now);
    let mut active = a.acc_active_ms + if a.active { (now - a.last_active_ms).max(0) } else { 0 };
    if active > wall {
        active = wall;
    }
    (active, wall - active)
}

fn live_row(a: &Act, now: i64) -> WebRow {
    let (active, idle) = active_ms(a, now);
    WebRow {
        id: a.id.clone(),
        start: dt::stamp7_ms(a.start_ms),
        end: None,
        duration_ticks: duration_ms(a, now) * 10_000,
        url: a.url.clone(),
        title: a.title.clone(),
        domain: a.domain.clone(),
        favicon: a.favicon.clone(),
        browser: a.browser.clone(),
        tab_id: a.tab_id,
        scroll_depth: a.scroll,
        click_count: a.clicks,
        has_form: a.form,
        is_active_tab: true,
        active_ticks: active * 10_000,
        idle_ticks: idle * 10_000,
        category_id: a.category.1,
        category_name: a.category.0.clone(),
        category_source: a.category.2.clone(),
    }
}

fn finish_row(a: &Act, now: i64) -> WebRow {
    let mut row = live_row(a, now);
    row.end = Some(dt::stamp7_ms(now));
    row.is_active_tab = false;
    row
}

fn msg_time(v: &Value) -> i64 {
    let raw = str_of(v, "timestamp");
    dt::iso_to_local_ms(&raw).unwrap_or_else(dt::local_ms_now)
}

fn str_of(v: &Value, key: &str) -> String {
    match v.get(key) {
        Some(Value::String(s)) => s.clone(),
        Some(Value::Null) | None => String::new(),
        Some(other) => other.to_string(),
    }
}

fn opt_str(v: &Value, key: &str) -> Option<String> {
    match v.get(key) {
        Some(Value::String(s)) if !s.is_empty() => Some(s.clone()),
        _ => None,
    }
}

fn domain_of(url: &str) -> String {
    // 只取 host：跳过 scheme 与路径/查询，`doubao://settings/` 这类自定义 scheme 取 scheme 后主体
    let rest = match url.split_once("://") {
        Some((_, r)) => r,
        None => url,
    };
    let host = rest.split(['/', '?', '#']).next().unwrap_or("");
    let host = host.split('@').next_back().unwrap_or(host);
    host.split(':').next().unwrap_or(host).to_ascii_lowercase()
}

fn default_category(domain: &str) -> &'static str {
    if domain.is_empty() {
        return "浏览";
    }
    let d = domain;
    if d.contains("google") || d.contains("baidu") || d.contains("bing") || d.contains("sogou") {
        "搜索"
    } else if d.contains("github")
        || d.contains("gitlab")
        || d.contains("stackoverflow")
        || d.contains("csdn")
        || d.contains("juejin")
        || d.contains("segmentfault")
    {
        "开发"
    } else if d.contains("youtube")
        || d.contains("bilibili")
        || d.contains("netflix")
        || d.contains("youku")
        || d.contains("iqiyi")
        || d.contains("douyin")
    {
        "视频"
    } else if d.contains("twitter")
        || d.contains("weibo")
        || d.contains("facebook")
        || d.contains("instagram")
        || d.contains("linkedin")
        || d.contains("zhihu")
        || d.contains("xiaohongshu")
        || d.contains("douban")
    {
        "社交"
    } else if d.contains("amazon")
        || d.contains("taobao")
        || d.contains("jd")
        || d.contains("tmall")
        || d.contains("pinduoduo")
    {
        "购物"
    } else if d.contains("mail") || d.contains("outlook") || d.contains("gmail") {
        "邮件"
    } else if d.contains("notion")
        || d.contains("docs.qq")
        || d.contains("yuque")
        || d.contains("confluence")
        || d.contains("feishu")
        || d.contains("dingtalk")
    {
        "办公"
    } else if d.contains("news")
        || d.contains("bbc")
        || d.contains("cnn")
        || d.contains("sina")
        || d.contains("sohu")
        || d.contains("163")
    {
        "新闻"
    } else {
        "浏览"
    }
}

fn uuid() -> String {
    use std::time::{SystemTime, UNIX_EPOCH};
    let nanos = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_nanos() as u64)
        .unwrap_or(0);
    let pid = std::process::id() as u64;
    let seq = SEQ.fetch_add(1, Ordering::Relaxed) as u64;
    let a = (nanos ^ (pid << 32)) & 0xffff_ffff;
    let b = (nanos >> 32) ^ seq;
    format!(
        "{:08x}-{:04x}-{:04x}-{:04x}-{:012x}",
        a as u32,
        (b & 0xffff) as u16,
        ((b >> 16) & 0xffff) as u16,
        (seq & 0xffff) as u16,
        (nanos & 0xffff_ffff_ffff) as u64
    )
}

static SEQ: std::sync::atomic::AtomicUsize = std::sync::atomic::AtomicUsize::new(0);

// ---------------------------------------------------------------- WebSocket 服务

pub struct Server {
    stop: Arc<AtomicBool>,
}

impl Server {
    pub fn stop(&self) {
        self.stop.store(true, Ordering::SeqCst);
    }
}

/// 起监听线程。端口被占（例如 WPF 版在跑）时返回 Err，由调用方决定降级。
pub fn start(
    port: u16,
    store: Arc<Mutex<Store>>,
    engine: Arc<Mutex<Engine>>,
    log: LogFn,
) -> Result<Server, String> {
    let listener = TcpListener::bind(("127.0.0.1", port))
        .map_err(|e| format!("端口 {port} 绑定失败：{e}"))?;
    listener
        .set_nonblocking(true)
        .map_err(|e| format!("设置非阻塞失败：{e}"))?;
    log(format!("浏览器扩展通道已监听 127.0.0.1:{port}"));

    let stop = Arc::new(AtomicBool::new(false));
    let stop2 = Arc::clone(&stop);
    std::thread::spawn(move || {
        while !stop2.load(Ordering::SeqCst) {
            match listener.accept() {
                Ok((stream, addr)) => {
                    let store = Arc::clone(&store);
                    let engine = Arc::clone(&engine);
                    let log = Arc::clone(&log);
                    let stop3 = Arc::clone(&stop2);
                    std::thread::spawn(move || {
                        handle_conn(stream, addr, store, engine, log, stop3);
                    });
                }
                Err(e) if e.kind() == ErrorKind::WouldBlock => {
                    std::thread::sleep(Duration::from_millis(50));
                }
                Err(e) => {
                    log(format!("浏览器通道 accept 失败：{e}"));
                    std::thread::sleep(Duration::from_millis(200));
                }
            }
        }
        log("浏览器扩展通道已停止".to_string());
    });

    Ok(Server { stop })
}

fn handle_conn(
    mut stream: TcpStream,
    addr: std::net::SocketAddr,
    store: Arc<Mutex<Store>>,
    engine: Arc<Mutex<Engine>>,
    log: LogFn,
    stop: Arc<AtomicBool>,
) {
    // Windows 上 accept 出来的 socket 会继承监听 socket 的非阻塞标志，
    // 不显式改回阻塞的话 read_exact 立刻 WouldBlock → 每条连接刚握手就被关掉
    // （表现为扩展每隔两三秒重连一次）。
    let _ = stream.set_nonblocking(false);
    let client = uuid();
    let client = client.replace('-', "");
    let client = client[..8].to_string();

    if !handshake(&mut stream, &log) {
        return;
    }
    let _ = addr;

    loop {
        if !stop_still(&stop) {
            break;
        }
        let frame = match read_message(&mut stream) {
            Some(f) => f,
            None => break,
        };
        let text = match String::from_utf8(frame) {
            Ok(t) => t,
            Err(_) => continue,
        };
        let store_guard = store.lock().unwrap();
        let mut eng = engine.lock().unwrap();
        eng.on_message(&store_guard, &client, &text, &log);
    }

    let store_guard = store.lock().unwrap();
    let mut eng = engine.lock().unwrap();
    eng.finalize_client(&store_guard, &client, &log);
}

fn stop_still(stop: &Arc<AtomicBool>) -> bool {
    !stop.load(Ordering::SeqCst)
}

fn handshake(stream: &mut TcpStream, log: &LogFn) -> bool {
    let mut buf = Vec::new();
    let mut tmp = [0u8; 1024];
    loop {
        match stream.read(&mut tmp) {
            Ok(0) => return false,
            Ok(n) => {
                buf.extend_from_slice(&tmp[..n]);
                if buf.windows(4).any(|w| w == b"\r\n\r\n") {
                    break;
                }
                if buf.len() > 64 * 1024 {
                    return false;
                }
            }
            Err(_) => return false,
        }
    }
    let text = String::from_utf8_lossy(&buf);
    let is_ws = text
        .to_ascii_lowercase()
        .contains("upgrade: websocket");
    if !is_ws {
        // 普通 GET：给个说明页，避免浏览器访问时挂住
        let body = "<html><meta charset=\"utf-8\"><title>PChabit</title>\
                    <body style=\"font-family:system-ui;background:#1a1a2e;color:#eee;padding:32px\">\
                    浏览器扩展通道运行中（PChabit 采集宿主）。</body></html>";
        let resp = format!(
            "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {}\r\nConnection: close\r\n\r\n{}",
            body.len(),
            body
        );
        let _ = stream.write_all(resp.as_bytes());
        return false;
    }

    let key = text
        .lines()
        .find(|l| {
            let l = l.to_ascii_lowercase();
            l.starts_with("sec-websocket-key:")
        })
        .and_then(|l| l.split(':').nth(1))
        .map(|s| s.trim().to_string());
    let key = match key {
        Some(k) => k,
        None => return false,
    };

    let accept = base64(&sha1(format!("{key}{WS_GUID}").as_bytes()));
    let resp = format!(
        "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n\
         Sec-WebSocket-Accept: {accept}\r\nSec-WebSocket-Version: 13\r\n\r\n"
    );
    if stream.write_all(resp.as_bytes()).is_err() {
        log("浏览器通道握手写回失败".to_string());
        return false;
    }
    log(format!("浏览器扩展握手成功（{key}）"));
    true
}

/// 读一条完整消息（拼接分片），控制帧就地处理。返回 None 表示连接结束。
fn read_message(stream: &mut TcpStream) -> Option<Vec<u8>> {
    let mut data = Vec::new();
    loop {
        let mut head = [0u8; 2];
        if stream.read_exact(&mut head).is_err() {
            return None;
        }
        let fin = head[0] & 0x80 != 0;
        let opcode = head[0] & 0x0f;
        let masked = head[1] & 0x80 != 0;
        let mut len = (head[1] & 0x7f) as u64;
        if len == 126 {
            let mut ext = [0u8; 2];
            if stream.read_exact(&mut ext).is_err() {
                return None;
            }
            len = u16::from_be_bytes(ext) as u64;
        } else if len == 127 {
            let mut ext = [0u8; 8];
            if stream.read_exact(&mut ext).is_err() {
                return None;
            }
            len = u64::from_be_bytes(ext);
        }
        if len > 8 * 1024 * 1024 {
            return None;
        }
        let mut mask = [0u8; 4];
        if masked && stream.read_exact(&mut mask).is_err() {
            return None;
        }
        let mut payload = vec![0u8; len as usize];
        if stream.read_exact(&mut payload).is_err() {
            return None;
        }
        if masked {
            for (i, b) in payload.iter_mut().enumerate() {
                *b ^= mask[i % 4];
            }
        }

        match opcode {
            0x8 => return None,                       // close
            0x9 => {
                let _ = write_frame(stream, 0xA, &payload); // ping → pong
                continue;
            }
            0xA => continue,                          // pong
            _ => {}
        }
        data.extend_from_slice(&payload);
        if fin {
            return Some(data);
        }
    }
}

fn write_frame(stream: &mut TcpStream, opcode: u8, payload: &[u8]) -> std::io::Result<()> {
    let mut out = Vec::with_capacity(payload.len() + 10);
    out.push(0x80 | opcode);
    if payload.len() < 126 {
        out.push(payload.len() as u8);
    } else if payload.len() <= 0xffff {
        out.push(126);
        out.extend_from_slice(&(payload.len() as u16).to_be_bytes());
    } else {
        out.push(127);
        out.extend_from_slice(&(payload.len() as u64).to_be_bytes());
    }
    out.extend_from_slice(payload);
    stream.write_all(&out)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn domain_extraction() {
        assert_eq!(domain_of("https://www.douyin.com/?recommend=1"), "www.douyin.com");
        assert_eq!(domain_of("https://github.com/idleeyan/PChabit"), "github.com");
        assert_eq!(domain_of("doubao://settings/"), "settings");
        assert_eq!(domain_of("http://127.0.0.1:8765/x"), "127.0.0.1");
    }

    #[test]
    fn keyword_categories() {
        assert_eq!(default_category("www.baidu.com"), "搜索");
        assert_eq!(default_category("github.com"), "开发");
        assert_eq!(default_category("www.bilibili.com"), "视频");
        assert_eq!(default_category("unknown.example"), "浏览");
    }

    #[test]
    fn search_counter_needs_active_session_and_query() {
        let dir = std::env::temp_dir().join(format!("pchabit_ws_{}", uuid()));
        std::fs::create_dir_all(&dir).unwrap();
        let store = crate::store::Store::open(&dir.join("t.db")).unwrap();
        let log: crate::host::LogFn = std::sync::Arc::new(|_m: String| {});
        let mut eng = Engine::new();
        let now = crate::dt::local_ms_now();
        let today = crate::dt::date_ms(now);

        // 没会话时 search 不计数
        eng.on_message(
            &store,
            "c1",
            r#"{"type":"search","tabId":5,"query":"rust","browser":"b"}"#,
            &log,
        );
        assert_eq!(eng.search_count(&today), 0);

        // 开会话 + 带 query 才计数
        eng.on_message(
            &store,
            "c1",
            r#"{"type":"pageView","tabId":5,"url":"https://www.bing.com/search?q=rust","browser":"b"}"#,
            &log,
        );
        eng.on_message(
            &store,
            "c1",
            r#"{"type":"search","tabId":5,"query":"rust","browser":"b"}"#,
            &log,
        );
        assert_eq!(eng.search_count(&today), 1);
        // 无 query 的不计数
        eng.on_message(
            &store,
            "c1",
            r#"{"type":"search","tabId":5,"browser":"b"}"#,
            &log,
        );
        assert_eq!(eng.search_count(&today), 1);
        assert_eq!(eng.search_count("2020-01-01"), 0);
        let _ = std::fs::remove_dir_all(&dir);
    }
}
