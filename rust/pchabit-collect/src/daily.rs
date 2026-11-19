//! 日汇总（`DailySummaries` + `AppDailyStats`）：把一天的行为表二次聚合成一行。
//!
//! 口径逐条照抄 WPF 的 `DataCollectionService.Maintenance.cs` 与
//! `DataCollectionService.Analysis.cs`（还有 `ActivityLabeler`），改一处就会跟历史行对不上。

use crate::dt;
use crate::store::{AppDailyOut, AppSessRow, DailyOut, Store, WebSessRow};
use crate::host::LogFn;

/// 标签字面量与顺序（与 C# `ActivityLabels.All` 一致，JSON 键顺序也照它来）。
pub const LABELS: [&str; 11] = [
    "work-code",
    "work-doc",
    "meeting",
    "learn",
    "browse-info",
    "browse-fun",
    "game",
    "comms",
    "admin",
    "idle",
    "other",
];

const MEETING_PROCESSES: [&str; 10] = [
    "teams", "ms-teams", "zoom", "feishu", "lark", "dingtalk", "wemeet", "voov", "webex", "skype",
];
const COMMS_PROCESSES: [&str; 8] = [
    "wechat", "weixin", "qq", "tim", "telegram", "slack", "discord", "feishu",
];
const GAME_PROCESSES: [&str; 8] = [
    "steam",
    "epicgameslauncher",
    "unity",
    "unreal",
    "leagueclient",
    "csgo",
    "valorant",
    "minecraft",
];
const CODE_PROCESSES: [&str; 19] = [
    "devenv",
    "code",
    "idea64",
    "idea",
    "pycharm",
    "webstorm",
    "rider",
    "rider64",
    "sublime_text",
    "windowsterminal",
    "wt",
    "cmd",
    "powershell",
    "pwsh",
    "cursor",
    "zed",
    "clion",
    "goland",
    "phpstorm",
];
const DOC_PROCESSES: [&str; 11] = [
    "winword",
    "excel",
    "powerpnt",
    "wps",
    "et",
    "wpp",
    "notion",
    "obsidian",
    "typora",
    "acrobat",
    "acrord32",
];
const MEETING_TITLE_HINTS: [&str; 10] = [
    "会议", "meeting", "zoom", "teams", "腾讯会议", "飞书会议", "钉钉", "通话", "call", "webinar",
];
const LEARN_TITLE_HINTS: [&str; 12] = [
    "文档",
    "docs",
    "documentation",
    "教程",
    "课程",
    "lesson",
    "tutorial",
    "wiki",
    "learn",
    "书",
    "book",
    "api",
];
const FUN_TITLE_HINTS: [&str; 15] = [
    "抖音",
    "bilibili",
    "b站",
    "youtube",
    "netflix",
    "爱奇艺",
    "优酷",
    "腾讯视频",
    "淘宝",
    "京东",
    "pdd",
    "微博",
    "小说",
    "游戏",
    "直播",
];
const ADMIN_PROCESSES: [&str; 13] = [
    "explorer",
    "taskmgr",
    "control",
    "regedit",
    "ms-settings",
    "settings",
    "appinstaller",
    "totalcmd",
    "listary",
    "everything",
    "winrar",
    "7zfm",
    "geek",
];
const BROWSER_PROCESSES: [&str; 10] = [
    "chrome",
    "msedge",
    "firefox",
    "brave",
    "opera",
    "iexplore",
    "360chrome",
    "qqbrowser",
    "sogouexplorer",
    "librewolf",
];

/// 生产力分类关键词（`AnalyticsEngine.IsProductiveCategory`）。
const PRODUCTIVE: [&str; 6] = ["开发", "开发工具", "办公", "办公软件", "AI 助手", "生产力"];

pub fn is_productive_category(cat: &str) -> bool {
    !cat.is_empty() && PRODUCTIVE.iter().any(|p| contains_ci(cat, p))
}

fn contains_ci(hay: &str, needle: &str) -> bool {
    if hay.is_empty() || needle.is_empty() {
        return false;
    }
    hay.to_lowercase().contains(&needle.to_lowercase())
}

fn any_of(hay: &str, needles: &[&str]) -> bool {
    needles.iter().any(|n| hay.contains(n))
}

fn any_of_ci(hay: &str, needles: &[&str]) -> bool {
    needles.iter().any(|n| contains_ci(hay, n))
}

/// 归一化进程名：去首尾空白、去 `.exe`、转小写（`AppStatsEngine.NormalizeProcessName`）。
pub fn normalize_process(p: &str) -> String {
    let t = p.trim();
    let t = if t.to_lowercase().ends_with(".exe") {
        &t[..t.len() - 4]
    } else {
        t
    };
    t.to_lowercase()
}

/// 一条会话的标签输入。
pub struct LabelInput {
    pub process: String,
    pub app_cat: String,
    pub title: String,
    pub web_cat: String,
    pub keys_per_min: f64,
    pub minutes: f64,
}

/// 行为语义标签（照抄 `ActivityLabeler.Label`，顺序与判定条件完全一致）。
pub fn label(i: &LabelInput) -> &'static str {
    let process = i.process.trim().to_lowercase();
    let app_cat = i.app_cat.trim();
    let web_cat = i.web_cat.trim();
    let title = &i.title;
    let minutes = i.minutes.max(0.0);
    let kpm = i.keys_per_min.max(0.0);

    // 1) 无有效输入的长块 → idle
    let looks_no_input = app_cat.is_empty()
        && !any_of(&process, &BROWSER_PROCESSES)
        && !any_of(&process, &CODE_PROCESSES)
        && !any_of(&process, &DOC_PROCESSES);
    if minutes >= 5.0 && kpm < 1.0 && web_cat.is_empty() && looks_no_input {
        return "idle";
    }
    // 2) 会议
    if any_of(&process, &MEETING_PROCESSES) || any_of_ci(title, &MEETING_TITLE_HINTS) {
        return "meeting";
    }
    // 3) 游戏（「娱乐」可能是视频，只有进程或「游戏」分类才算）
    if (any_of(&process, &GAME_PROCESSES) || app_cat.contains("游戏") || app_cat == "娱乐")
        && (any_of(&process, &GAME_PROCESSES) || app_cat.contains("游戏"))
    {
        return "game";
    }
    // 4) 即时通讯
    if any_of(&process, &COMMS_PROCESSES)
        || app_cat.contains("沟通")
        || app_cat.contains("聊天")
        || app_cat.contains("社交")
    {
        return "comms";
    }
    // 5) 工作模式（高键入）
    if any_of(&process, &CODE_PROCESSES) || app_cat.contains("开发") {
        return "work-code";
    }
    if any_of(&process, &DOC_PROCESSES) || app_cat.contains("办公") || app_cat.contains("文档") {
        return "work-doc";
    }
    if app_cat.contains("AI")
        || app_cat.contains("ai ")
        || process.contains("copilot")
        || process.contains("claude")
        || process.contains("chatgpt")
        || process.contains("doubao")
        || process.contains("kimi")
        || process.contains("mimo")
    {
        return if kpm >= 25.0 { "work-code" } else { "learn" };
    }
    // 6) 网页语义
    if let Some(l) = label_web(web_cat, title, kpm) {
        return l;
    }
    // 7) 浏览器会话但无网页分类
    if any_of(&process, &BROWSER_PROCESSES) {
        if any_of_ci(title, &LEARN_TITLE_HINTS) {
            return "learn";
        }
        if any_of_ci(title, &FUN_TITLE_HINTS) {
            return "browse-fun";
        }
        return "browse-info";
    }
    // 8) 系统杂务
    if any_of(&process, &ADMIN_PROCESSES)
        || app_cat.contains("系统")
        || app_cat.contains("工具")
        || app_cat.contains("下载")
    {
        return "admin";
    }
    // 9) 媒体
    if app_cat.contains("媒体")
        || app_cat.contains("影音")
        || app_cat.contains("视频")
        || process.contains("potplayer")
        || process.contains("vlc")
        || process.contains("spotify")
        || process.contains("cloudmusic")
    {
        return "browse-fun";
    }
    "other"
}

fn label_web(web_cat: &str, title: &str, kpm: f64) -> Option<&'static str> {
    if web_cat.is_empty() && title.is_empty() {
        return None;
    }
    if web_cat.contains("开发") || web_cat.contains("文档") {
        return Some(if kpm >= 20.0 { "work-code" } else { "learn" });
    }
    if web_cat.contains("搜索")
        || web_cat.contains("新闻")
        || web_cat.contains("资讯")
        || web_cat.contains("办公")
        || web_cat.contains("邮件")
        || web_cat.contains("浏览")
    {
        return Some("browse-info");
    }
    if web_cat.contains("视频")
        || web_cat.contains("社交")
        || web_cat.contains("购物")
        || web_cat.contains("娱乐")
    {
        return Some("browse-fun");
    }
    if any_of_ci(title, &LEARN_TITLE_HINTS) {
        return Some("learn");
    }
    if any_of_ci(title, &FUN_TITLE_HINTS) {
        return Some("browse-fun");
    }
    if !web_cat.is_empty() {
        return Some("browse-info");
    }
    None
}

/// 会话有效分钟：ActiveDuration 优先，否则墙钟（与任务栏「今日」同口径）。
fn active_minutes(a: &AppSessRow) -> f64 {
    if a.active_ticks > 0 {
        return (a.active_ticks as f64) / 600_000_000.0;
    }
    match (&a.start, &a.end) {
        (s, Some(e)) => match (dt::parse_stamp(s), dt::parse_stamp(e)) {
            (Some(st), Some(en)) if en > st => ((en - st) as f64) / 60_000.0,
            _ => 0.0,
        },
        _ => 0.0,
    }
}

fn web_minutes(w: &WebSessRow) -> f64 {
    if w.active_ticks > 0 {
        return (w.active_ticks as f64) / 600_000_000.0;
    }
    (w.duration_ticks as f64) / 600_000_000.0
}

fn round1(v: f64) -> f64 {
    (v * 10.0).round() / 10.0
}

fn round2(v: f64) -> f64 {
    (v * 100.0).round() / 100.0
}

fn mean(values: impl Iterator<Item = f64>) -> Option<f64> {
    let mut sum = 0.0;
    let mut n = 0u32;
    for v in values {
        sum += v;
        n += 1;
    }
    if n > 0 {
        Some(round2(sum / n as f64))
    } else {
        None
    }
}

fn max_of(values: impl Iterator<Item = f64>) -> Option<f64> {
    let mut m: Option<f64> = None;
    for v in values {
        m = Some(match m {
            Some(x) if x >= v => x,
            _ => v,
        });
    }
    m.map(round2)
}

/// 最近秩法 P95：升序第 `ceil(0.95*n)` 项。
fn percentile95(mut v: Vec<f64>) -> Option<f64> {
    if v.is_empty() {
        return None;
    }
    v.sort_by(|a, b| a.partial_cmp(b).unwrap_or(std::cmp::Ordering::Equal));
    let idx = ((0.95 * v.len() as f64).ceil() as usize).saturating_sub(1);
    Some(round2(v[idx.min(v.len() - 1)]))
}

/// 夜间分钟（23:00–次日 06:00 与 [start,end) 的重叠，按日切开算）。
fn night_minutes_between(start_ms: i64, end_ms: i64) -> f64 {
    if end_ms <= start_ms {
        return 0.0;
    }
    let mut total = 0.0;
    let day0 = start_ms / 86_400_000 * 86_400_000;
    let last = end_ms / 86_400_000 * 86_400_000;
    let mut day = day0;
    while day <= last {
        let ws = day + 23 * 3_600_000;
        let we = ws + 3_600_000;
        total += overlap_min(start_ms, end_ms, ws, we);
        let ns = day;
        let ne = day + 6 * 3_600_000;
        total += overlap_min(start_ms, end_ms, ns, ne);
        day += 86_400_000;
    }
    round1(total)
}

fn overlap_min(a0: i64, a1: i64, b0: i64, b1: i64) -> f64 {
    let s = a0.max(b0);
    let e = a1.min(b1);
    if e > s {
        (e - s) as f64 / 60_000.0
    } else {
        0.0
    }
}

/// 聚合某一天：写 `DailySummaries` 一行 + `AppDailyStats` 每个进程一行。
/// 返回 `(按键, 点击, 普遍化应用数)` 供日志用。
pub fn aggregate_day(store: &Store, date: &str, now: &str, log: &LogFn) -> Result<(i64, i64, usize), String> {
    let next = next_day(date)?;
    let from = format!("{date} 00:00:00");
    let to = format!("{next} 00:00:00");

    // ---- 键鼠 ----
    let kb_hours = store.keyboard_hours(&from, &to);
    let total_keys: i64 = kb_hours.iter().map(|(_, k)| *k).sum();
    let mut hourly_keys = [0i64; 24];
    for (hour, keys) in &kb_hours {
        if *hour >= 0 && *hour < 24 {
            hourly_keys[*hour as usize] += *keys;
        }
    }
    let clicks = store.mouse_clicks(&from, &to);

    // ---- 应用会话 ----
    let apps = store.app_sessions_in(&from, &to);
    let mut cat_map: Vec<(String, String)> = store
        .prog_cat_map()
        .into_iter()
        .map(|(p, c)| (normalize_process(&p), c))
        .filter(|(p, _)| !p.is_empty())
        .collect();
    cat_map.sort_by(|a, b| a.0.len().cmp(&b.0.len())); // 无所谓，只做精确查
    let cat_of = |s: &AppSessRow| -> String {
        let key = normalize_process(&s.process);
        if let Some((_, c)) = cat_map.iter().find(|(p, _)| p == &key) {
            return c.clone();
        }
        s.category.clone().unwrap_or_else(|| "未分类".to_string())
    };

    let mut active_total = 0.0;
    let mut focus_total = 0.0;
    let mut per_proc: Vec<(String, f64, f64, Vec<f64>)> = Vec::new(); // (进程, 分钟, 专注, 24 小时)
    for s in &apps {
        let m = active_minutes(s);
        active_total += m;
        let cat = cat_of(s);
        let productive = is_productive_category(&cat);
        if productive {
            focus_total += m;
        }
        // 小时分布：跨小时按重叠裁剪
        let (st, en) = match (dt::parse_stamp(&s.start), s.end.as_deref().and_then(dt::parse_stamp)) {
            (Some(a), Some(b)) => (a, b),
            _ => continue,
        };
        let mut hourly = vec![0.0f64; 24];
        if en > st {
            let day_start = dt::parse_stamp(&from).unwrap_or(0);
            for h in 0..24 {
                let hs = day_start + h as i64 * 3_600_000;
                let he = hs + 3_600_000;
                if st < he && en > hs {
                    hourly[h] += overlap_min(st, en, hs, he);
                }
            }
        }
        // AppDailyStats 的专注口径带「单会话 ≥25 分钟」门槛（DailySummaries 不带）
        let focus_for_app = if m >= 25.0 && productive { m } else { 0.0 };
        let key = normalize_process(&s.process);
        if key.is_empty() {
            continue;
        }
        if let Some(row) = per_proc.iter_mut().find(|(p, _, _, _)| p == &key) {
            row.1 += m;
            row.2 += focus_for_app;
            for h in 0..24 {
                row.3[h] += hourly[h];
            }
        } else {
            per_proc.push((key, m, focus_for_app, hourly));
        }
    }

    // TopApps：按 ProcessName（原始大小写）分组取前 10
    let mut top: Vec<(String, f64)> = Vec::new();
    for s in &apps {
        let m = active_minutes(s);
        if let Some(row) = top.iter_mut().find(|(p, _)| p == &s.process) {
            row.1 += m;
        } else {
            top.push((s.process.clone(), m));
        }
    }
    top.sort_by(|a, b| b.1.partial_cmp(&a.1).unwrap_or(std::cmp::Ordering::Equal));
    top.truncate(10);
    let top_json = format!(
        "[{}]",
        top.iter()
            .map(|(n, m)| format!("{{\"Name\":\"{}\",\"Minutes\":{}}}", json_escape(n), m))
            .collect::<Vec<_>>()
            .join(",")
    );
    let hourly_keys_json = format!(
        "[{}]",
        hourly_keys.iter().map(|k| k.to_string()).collect::<Vec<_>>().join(",")
    );

    // ---- 网页会话 ----
    let webs = store.web_sessions_in(&from, &to);
    let web_pages = webs.len() as i64;
    let web_duration_ticks: i64 = webs.iter().map(|w| w.duration_ticks).sum();
    let web_active_ticks: i64 = webs.iter().map(|w| w.active_ticks).sum();
    let web_minutes_val = if web_active_ticks > 0 {
        round1(web_active_ticks as f64 / 600_000_000.0)
    } else {
        0.0
    };

    // ---- 硬件日指标 ----
    let hw = store.hardware_day(date);
    let cpu_avg = mean(hw.iter().filter_map(|r| r.0));
    let cpu_p95 = percentile95(hw.iter().filter_map(|r| r.0).collect());
    let gpu_avg = mean(hw.iter().filter_map(|r| r.1));
    let gpu_temp_max = max_of(hw.iter().filter_map(|r| r.2));
    let mem_avg = mean(hw.iter().filter_map(|r| r.3));

    // ---- 标签分钟（浏览器应用会话在有网页会话时让位，避免双计）----
    let keys_per_min = if active_total > 0.0 {
        total_keys as f64 / active_total
    } else {
        0.0
    };
    let mut label_inputs: Vec<LabelInput> = Vec::new();
    for s in &apps {
        let m = active_minutes(s);
        if m <= 0.0 {
            continue;
        }
        let proc_lower = s.process.to_lowercase();
        let is_browser = BROWSER_PROCESSES.iter().any(|b| proc_lower.contains(b));
        if is_browser && !webs.is_empty() {
            continue;
        }
        label_inputs.push(LabelInput {
            process: s.process.clone(),
            // 标签用的是会话快照分类（C# BuildLabelInputs 传 s.Category），不是映射解析结果
            app_cat: s.category.clone().unwrap_or_default(),
            title: s.title.clone(),
            web_cat: String::new(),
            keys_per_min: keys_per_min,
            minutes: m,
        });
    }
    for w in &webs {
        let m = web_minutes(w);
        if m <= 0.0 {
            continue;
        }
        label_inputs.push(LabelInput {
            process: w.browser.clone(),
            app_cat: String::new(),
            title: w.title.clone(),
            web_cat: w.category.clone().unwrap_or_default(),
            keys_per_min: keys_per_min * 0.5,
            minutes: m,
        });
    }
    let mut label_minutes = [0.0f64; 11];
    for inp in &label_inputs {
        let m = inp.minutes.max(0.0);
        if m <= 0.0 {
            continue;
        }
        if let Some(idx) = LABELS.iter().position(|l| *l == label(inp)) {
            label_minutes[idx] = round1(label_minutes[idx] + m);
        }
    }
    let label_json = format!(
        "{{{}}}",
        LABELS
            .iter()
            .enumerate()
            .map(|(i, l)| format!("\"{l}\":{}", label_minutes[i]))
            .collect::<Vec<_>>()
            .join(",")
    );

    // ---- 夜间 / 首末活跃 ----
    let mut night = 0.0;
    let mut first_ms: Option<i64> = None;
    let mut last_ms: Option<i64> = None;
    for s in &apps {
        let (st, en) = match (dt::parse_stamp(&s.start), s.end.as_deref().and_then(dt::parse_stamp)) {
            (Some(a), Some(b)) if b > a => (a, b),
            _ => continue,
        };
        night += overlap_night(st, en);
        if first_ms.map_or(true, |f| st < f) {
            first_ms = Some(st);
        }
        if last_ms.map_or(true, |l| en > l) {
            last_ms = Some(en);
        }
    }
    let night_minutes_val = round1(night);
    let hhmm = |ms: i64| {
        let s = dt::stamp7_ms(ms);
        s[11..16].to_string()
    };
    let first_active = first_ms.map(hhmm);
    let last_active = last_ms.map(hhmm);

    let out = DailyOut {
        date: date.to_string(),
        total_keys,
        clicks,
        active_minutes: active_total,
        top_apps_json: top_json,
        hourly_keys_json,
        last_updated: now.to_string(),
        web_pages,
        web_duration_ticks,
        web_active_ticks,
        app_switches: (apps.len() as i64 - 1).max(0),
        focus_minutes: focus_total,
        web_minutes: web_minutes_val,
        cpu_avg,
        cpu_p95,
        gpu_avg,
        gpu_temp_max,
        mem_avg,
        metrics_version: 3,
        label_json,
        night_minutes: night_minutes_val,
        first_active,
        last_active,
    };
    store.daily_upsert(&out)?;

    // ---- AppDailyStats ----
    per_proc.sort_by(|a, b| b.1.partial_cmp(&a.1).unwrap_or(std::cmp::Ordering::Equal));
    let app_count = per_proc.len();
    for (proc_name, minutes, focus, hourly) in &per_proc {
        let hourly_json = format!(
            "[{}]",
            hourly
                .iter()
                .map(|m| format!("{}", round1(*m)))
                .collect::<Vec<_>>()
                .join(",")
        );
        let sessions = apps
            .iter()
            .filter(|s| normalize_process(&s.process) == *proc_name)
            .count() as i64;
        let row = AppDailyOut {
            process: proc_name.clone(),
            minutes: *minutes,
            sessions,
            focus: *focus,
            hourly_json,
        };
        if let Err(e) = store.app_daily_upsert(date, &row, now) {
            log(format!("AppDailyStats 写入失败（{proc_name}）：{e}"));
        }
    }

    Ok((total_keys, clicks, app_count))
}

fn overlap_night(st: i64, en: i64) -> f64 {
    night_minutes_between(st, en)
}

pub fn next_day(date: &str) -> Result<String, String> {
    let ms = dt::parse_stamp(&format!("{date} 00:00:00")).ok_or_else(|| format!("日期解析失败：{date}"))?;
    Ok(dt::date_ms(ms + 86_400_000))
}

fn json_escape(s: &str) -> String {
    s.replace('\\', "\\\\").replace('"', "\\\"")
}

/// 昨天是否需要（重新）聚合：照抄 WPF 的 `incomplete` 判定。
pub fn needs_aggregate(store: &Store, date: &str) -> bool {
    match store.daily_existing(date) {
        None => true,
        Some(d) => {
            let top_empty = d.top_apps.trim().is_empty() || d.top_apps.trim() == "[]";
            (d.total_keys <= 0 && d.clicks <= 0 && d.active_minutes <= 0.0 && top_empty)
                || (d.focus_minutes.is_none() && d.active_minutes > 0.0)
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn normalize_process_name() {
        assert_eq!(normalize_process("Code.EXE"), "code");
        assert_eq!(normalize_process("  VSCode.exe  "), "vscode");
        assert_eq!(normalize_process("chrome"), "chrome");
    }

    #[test]
    fn p95_is_nearest_rank() {
        let v: Vec<f64> = (1..=20).map(|x| x as f64).collect();
        assert_eq!(percentile95(v), Some(19.0));
    }

    #[test]
    fn labels_match_reference_cases() {
        let code = LabelInput {
            process: "WindowsTerminal.exe".into(),
            app_cat: "开发".into(),
            title: "pwsh".into(),
            web_cat: String::new(),
            keys_per_min: 30.0,
            minutes: 10.0,
        };
        assert_eq!(label(&code), "work-code");

        let fun = LabelInput {
            process: "chrome.exe".into(),
            app_cat: String::new(),
            title: "抖音 - 记录美好生活".into(),
            web_cat: "视频".into(),
            keys_per_min: 1.0,
            minutes: 20.0,
        };
        assert_eq!(label(&fun), "browse-fun");

        let idle = LabelInput {
            process: "screensaver.exe".into(),
            app_cat: String::new(),
            title: String::new(),
            web_cat: String::new(),
            keys_per_min: 0.0,
            minutes: 30.0,
        };
        assert_eq!(label(&idle), "idle");
    }

    #[test]
    fn night_window_overlap() {
        // 22:00 → 次日 07:00 命中夜间 7 小时（23:00-24:00 + 00:00-06:00）
        let base = dt::parse_stamp("2026-11-18 00:00:00").unwrap();
        let st = base + 22 * 3_600_000;
        let en = base + 24 * 3_600_000 + 7 * 3_600_000;
        assert_eq!(night_minutes_between(st, en), 420.0);
    }
}
