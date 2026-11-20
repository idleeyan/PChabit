//! P4-1：Tauri 壳 —— 单窗口 + 侧边导航，四个只读页嵌在同源 iframe 里。
//!
//! 三条硬约束（都来自 P4 盘点的结论）：
//! 1. **图表资产一行不改**：页面本来就是 HTML，壳只负责把数据喂进去；
//! 2. **数据不绕 IPC 子进程**：壳直接 `use pchabit_datad`（datad 的 lib），进程内查库；
//! 3. **验证靠回写不靠截屏**：本环境截屏桌面与进程桌面不一致，页面绘制完成后
//!    用 `invoke('note')` 写日志，作为"真的画出来了"的证据。
//!
//! 页面必须从 asset 协议加载（`WebviewUrl::App`），`file://` 外部源拿不到 IPC。

#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use std::io::Write;
use std::path::PathBuf;
use tauri::Manager;

mod backup;
mod desktop_widget;
mod hotkey;
mod taskbar;
mod webdav;

/// 找壳自己的 `dist` 目录（页面资产由 build.rs 从 WPF 工程原样复制进来）。
fn dist_dir() -> PathBuf {
    let mut bases: Vec<PathBuf> = Vec::new();
    if let Ok(exe) = std::env::current_exe() {
        if let Some(dir) = exe.parent() {
            bases.push(dir.to_path_buf());
            if let Some(up2) = dir.parent().and_then(|p| p.parent()) {
                bases.push(up2.to_path_buf());
            }
        }
    }
    bases.push(PathBuf::from("."));

    for b in &bases {
        if b.join("dist").join("pages").exists() {
            return b.join("dist");
        }
    }
    PathBuf::from("dist")
}

pub(crate) fn log_line(msg: &str) {
    // 多线程同时写日志会把行写串（采集线程、小窗线程、IPC 线程都会写），这里串行化
    static LOG_LOCK: std::sync::Mutex<()> = std::sync::Mutex::new(());
    let _guard = LOG_LOCK.lock();
    let base = std::env::var("TEMP").unwrap_or_else(|_| ".".to_string());
    let path = PathBuf::from(base).join("pchabit-shell.log");
    if let Ok(mut f) = std::fs::OpenOptions::new().create(true).append(true).open(path) {
        let _ = writeln!(f, "[{}] {msg}", pchabit_datad::today_local());
    }
}

/// 全局 AppHandle：后台线程（备份/热键）要发通知时用。
static APP_HANDLE: std::sync::OnceLock<tauri::AppHandle> = std::sync::OnceLock::new();

/// 系统通知（`ShowNotifications` 关掉就只写日志）。
pub(crate) fn notify(title: &str, body: &str) {
    log_line(&format!("[通知] {title}：{body}"));
    if !setting_bool_default("ShowNotifications", true) {
        return;
    }
    let Some(app) = APP_HANDLE.get() else { return };
    use tauri_plugin_notification::NotificationExt;
    if let Err(e) = app.notification().builder().title(title).body(body).show() {
        log_line(&format!("[通知] 发系统通知失败：{e}"));
    }
}

/// 读 `settings.json` 里的布尔开关（缺文件/缺键用 `default`）。
pub(crate) fn setting_bool_default(key: &str, default: bool) -> bool {
    let p = PathBuf::from(std::env::var("LOCALAPPDATA").unwrap_or_default())
        .join("PChabit")
        .join("settings.json");
    let Ok(text) = std::fs::read_to_string(p) else {
        return default;
    };
    let Ok(v) = serde_json::from_str::<serde_json::Value>(&text) else {
        return default;
    };
    v.get(key).and_then(|x| x.as_bool()).unwrap_or(default)
}

/// 读 `settings.json` 里的布尔开关（缺文件/缺键一律 false）。
fn setting_bool(key: &str) -> bool {
    let p = PathBuf::from(std::env::var("LOCALAPPDATA").unwrap_or_default())
        .join("PChabit")
        .join("settings.json");
    let Ok(text) = std::fs::read_to_string(p) else {
        return false;
    };
    let Ok(v) = serde_json::from_str::<serde_json::Value>(&text) else {
        return false;
    };
    v.get(key).and_then(|x| x.as_bool()).unwrap_or(false)
}

/// 读 `settings.json` 里的整数（缺文件/缺键用 `default`）。
pub(crate) fn setting_i64(key: &str, default: i64) -> i64 {
    read_setting_value(key)
        .and_then(|x| x.as_i64())
        .unwrap_or(default)
}

/// 读 `settings.json` 里的字符串（缺文件/缺键用 `default`）。
pub(crate) fn setting_str(key: &str, default: &str) -> String {
    read_setting_value(key)
        .and_then(|x| x.as_str().map(|s| s.to_string()))
        .unwrap_or_else(|| default.to_string())
}

fn read_setting_value(key: &str) -> Option<serde_json::Value> {
    let p = PathBuf::from(std::env::var("LOCALAPPDATA").unwrap_or_default())
        .join("PChabit")
        .join("settings.json");
    let text = std::fs::read_to_string(p).ok()?;
    let v: serde_json::Value = serde_json::from_str(&text).ok()?;
    v.get(key).cloned()
}

/// 内部直接改写一个设置键（不做白名单校验，供程序自己回写时间戳/状态用）。
pub(crate) fn update_setting_file(key: &str, value: serde_json::Value) {
    let path = PathBuf::from(std::env::var("LOCALAPPDATA").unwrap_or_default())
        .join("PChabit")
        .join("settings.json");
    let Ok(text) = std::fs::read_to_string(&path) else {
        return;
    };
    let Ok(mut v) = serde_json::from_str::<serde_json::Value>(&text) else {
        return;
    };
    let Some(obj) = v.as_object_mut() else {
        return;
    };
    obj.insert(key.to_string(), value);
    if let Ok(pretty) = serde_json::to_string_pretty(&v) {
        let _ = std::fs::write(&path, pretty);
    }
}

/// 小窗跟随当前主题：`settings.json` 的 `CurrentTheme` 为 light 时用浅色配色。
fn read_theme_is_light() -> bool {
    let p = PathBuf::from(std::env::var("LOCALAPPDATA").unwrap_or_default())
        .join("PChabit")
        .join("settings.json");
    let Ok(text) = std::fs::read_to_string(p) else {
        return false;
    };
    let Ok(v) = serde_json::from_str::<serde_json::Value>(&text) else {
        return false;
    };
    v.get("CurrentTheme")
        .and_then(|t| t.as_str())
        .map(|t| t.eq_ignore_ascii_case("light"))
        .unwrap_or(false)
}

/// 本地当前小时（UTC+8），用于把"还没到的时段"和"真的零活跃"区分开。
#[allow(dead_code)]
fn current_hour() -> i64 {
    let secs = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_secs())
        .unwrap_or(0);
    ((secs + 8 * 3600) / 3600 % 24) as i64
}

fn analytics_for(start: &str, end: &str) -> Result<serde_json::Value, String> {
    analytics_period(start, end, start, end)
}

fn analytics_period(
    start: &str,
    end: &str,
    prev_start: &str,
    prev_end: &str,
) -> Result<serde_json::Value, String> {
    let db = pchabit_datad::default_db();
    let req = serde_json::json!({
        "curStart": format!("{start} 00:00:00.0000000"),
        "curEnd": format!("{end} 23:59:59.9999999"),
        "prevStart": format!("{prev_start} 00:00:00.0000000"),
        "prevEnd": format!("{prev_end} 23:59:59.9999999"),
        "label": "区间",
        "prevLabel": "上一区间",
    });
    pchabit_datad::analytics_json(&db, &req)
}

/// 分析页：完整 `analytics` 报告（近 7 天 vs 前 7 天）。
fn payload_analytics() -> serde_json::Value {
    let today = pchabit_datad::today_local();
    let cur_start = pchabit_datad::date_shift(&today, -6);
    let prev_end = pchabit_datad::date_shift(&today, -7);
    let prev_start = pchabit_datad::date_shift(&today, -13);
    match analytics_period(&cur_start, &today, &prev_start, &prev_end) {
        Ok(mut v) => {
            v["period"]["label"] = serde_json::json!("近 7 天");
            v["previous"]["label"] = serde_json::json!("前 7 天");
            log_line(&format!(
                "analytics: {} KPI / {} 洞察 / {} 天有数据",
                v["kpis"].as_array().map(|a| a.len()).unwrap_or(0),
                v["insights"].as_array().map(|a| a.len()).unwrap_or(0),
                v["dayCountWithData"].as_i64().unwrap_or(0)
            ));
            v
        }
        Err(e) => {
            log_line(&format!("analytics: 计算失败 {e}"));
            serde_json::json!({ "kpis": [], "insights": [], "period": {}, "previous": {} })
        }
    }
}

/// 硬件趋势：`__HW_DATA__ = { hours:[{label,cpu,gpu,temp,n}], summary }`
///
/// 直接从分析报告里取（`hardware.hours`），不再单独跑一次 analytics。
fn payload_hardware(report: &serde_json::Value) -> serde_json::Value {
    let hw = report.get("hardware").cloned().unwrap_or(serde_json::Value::Null);
    let hours: Vec<serde_json::Value> = hw["hours"]
        .as_array()
        .map(|arr| {
            arr.iter()
                .filter(|h| h["sampleCount"].as_i64().unwrap_or(0) > 0)
                .map(|h| {
                    serde_json::json!({
                        "label": h["label"].clone(),
                        "cpu": h["cpuLoadAvg"].clone(),
                        "gpu": h["gpuLoadAvg"].clone(),
                        "temp": h["gpuTempAvg"].clone(),
                        "n": h["sampleCount"].clone(),
                    })
                })
                .collect()
        })
        .unwrap_or_default();

    let minutes = hw["sampleMinutes"].as_i64().unwrap_or(0);
    let days = hw["sampleDays"].as_i64().unwrap_or(0);
    let mut summary = format!("样本 {days} 天 · {minutes} 个分钟样本");
    if let Some(v) = hw["cpuLoadAvg"].as_f64() {
        summary.push_str(&format!("　CPU 均 {v:.1}%"));
    }
    if let Some(v) = hw["cpuLoadP95"].as_f64() {
        summary.push_str(&format!(" / P95 {v:.1}%"));
    }
    if let Some(v) = hw["gpuLoadAvg"].as_f64() {
        summary.push_str(&format!("　GPU 均 {v:.1}%"));
    }
    if let Some(v) = hw["gpuTempMax"].as_f64() {
        summary.push_str(&format!("　峰温 {v:.0}°C"));
    }
    if minutes == 0 {
        summary = "暂无硬件样本".into();
    }
    serde_json::json!({ "hours": hours, "summary": summary })
}

/// 饼图两种模式，对齐 C# `AppStatsViewModel.BuildPieChartData`：
/// - `byCategory`：**全部**分类，不截断（C# 就是把 CategoryShares 原样传过去）
/// - `byApp`：Top 8 应用 + 「其他」（C# 里叫 PieSlices）
///
/// 分钟数保留 1 位小数，与 C# 的 `Math.Round(minutes, 1)` 一致。
/// 应用搜索下沉到数据层：分类页进程很多时由这里过滤，不再让前端全量过一遍。
#[tauri::command]
fn app_search(q: String) -> Result<serde_json::Value, String> {
    pchabit_datad::search_json(&pchabit_datad::default_db(), &q)
}

fn round1(v: f64) -> f64 {
    (v * 10.0).round() / 10.0
}

fn payload_category(report: &serde_json::Value) -> serde_json::Value {
    const OTHER_COLOR: &str = "#8B5CF6";

    let fallback = |report: &serde_json::Value| -> Vec<serde_json::Value> {
        let colors = pchabit_datad::category_colors(&pchabit_datad::default_db())
            .unwrap_or_default();
        report["categoryShares"]
            .as_array()
            .map(|arr| {
                arr.iter()
                    .map(|c| {
                        let name = c["category"].as_str().unwrap_or("未分类").to_string();
                        serde_json::json!({
                            "name": name.clone(),
                            "value": c["minutes"].as_f64().unwrap_or(0.0),
                            "color": colors.get(&name).cloned().unwrap_or_default(),
                        })
                    })
                    .collect()
            })
            .unwrap_or_default()
    };

    let today = pchabit_datad::today_local();
    let start = pchabit_datad::date_shift(&today, -6);
    let req = serde_json::json!({
        "curStart": format!("{start} 00:00:00.0000000"),
        "curEnd": format!("{today} 23:59:59.9999999"),
        "prevStart": format!("{start} 00:00:00.0000000"),
        "prevEnd": format!("{today} 23:59:59.9999999"),
    });
    let stats = match pchabit_datad::stats_json(&pchabit_datad::default_db(), &req) {
        Ok(v) => v,
        Err(e) => {
            log_line(&format!("category: stats 失败 {e}，退回报告 Top8"));
            let cats = fallback(report);
            return serde_json::json!({ "byCategory": cats, "byApp": cats });
        }
    };

    // 按分类：全部保留，只滤掉 0 分钟的
    let mut by_category: Vec<serde_json::Value> = stats["categoryShares"]
        .as_array()
        .map(|arr| {
            arr.iter()
                .filter(|c| c["minutes"].as_f64().unwrap_or(0.0) > 0.0)
                .map(|c| {
                    serde_json::json!({
                        "name": c["category"],
                        "value": round1(c["minutes"].as_f64().unwrap_or(0.0)),
                        "color": c["colorHex"],
                    })
                })
                .collect()
        })
        .unwrap_or_default();
    by_category.sort_by(|a, b| {
        b["value"].as_f64().unwrap_or(0.0)
            .partial_cmp(&a["value"].as_f64().unwrap_or(0.0))
            .unwrap_or(std::cmp::Ordering::Equal)
    });

    // 按应用：数据层已经给了 Top8+其他（pieSlices）
    let by_app: Vec<serde_json::Value> = stats["pieSlices"]
        .as_array()
        .map(|arr| {
            arr.iter()
                .map(|s| {
                    serde_json::json!({
                        "name": s["name"],
                        "value": round1(s["minutes"].as_f64().unwrap_or(0.0)),
                        "color": if s["name"].as_str() == Some("其他") {
                            OTHER_COLOR.to_string()
                        } else {
                            s["colorHex"].as_str().unwrap_or("").to_string()
                        },
                    })
                })
                .collect()
        })
        .unwrap_or_default();

    log_line(&format!(
        "category: 分类 {} 个 / 应用 {} 个（含其他）",
        by_category.len(),
        by_app.len()
    ));
    serde_json::json!({ "byCategory": by_category, "byApp": by_app })
}

/// 桑基图：`{ nodes:[{name,value,category}], links:[{source,target,value}] }`
fn payload_sankey() -> serde_json::Value {
    let today = pchabit_datad::today_local();
    let start = pchabit_datad::date_shift(&today, -6);
    match pchabit_datad::sankey_json(&pchabit_datad::default_db(), &start, &today, 15) {
        Ok(v) => v,
        Err(e) => {
            log_line(&format!("sankey: 计算失败 {e}"));
            serde_json::json!({ "nodes": [], "links": [] })
        }
    }
}

/// 时间线换天：offset = -1 昨天 / 0 今天 / 1 明天，返回与启动注入同构的载荷。
#[tauri::command]
fn timeline_day(offset: i64) -> serde_json::Value {
    let today = pchabit_datad::today_local();
    let date = pchabit_datad::date_shift(&today, offset);
    payload_timeline(&date, offset)
}

#[tauri::command]
fn note(msg: String) {
    log_line(&format!("[页面] {msg}"));
}

#[tauri::command]
fn notes_create(content: String, color: String) -> Result<String, String> {
    let id = pchabit_datad::notes_create(&content, &color)?;
    log_line(&format!(
        "[壳] 新建便签 {id}（{} 字 / {color}）",
        content.chars().count()
    ));
    Ok(id)
}

#[tauri::command]
fn notes_update(id: String, content: String) -> Result<String, String> {
    pchabit_datad::notes_update(&id, &content)?;
    log_line(&format!(
        "[壳] 修改便签 {id}（{} 字）",
        content.chars().count()
    ));
    Ok(id)
}

#[tauri::command]
fn notes_pin(id: String, pinned: bool) -> Result<String, String> {
    pchabit_datad::notes_pin(&id, pinned)?;
    log_line(&format!(
        "[壳] 便签 {id} {}",
        if pinned { "置顶" } else { "取消置顶" }
    ));
    Ok(id)
}

#[tauri::command]
fn notes_delete(id: String) -> Result<String, String> {
    pchabit_datad::notes_delete(&id)?;
    log_line(&format!("[壳] 删除便签 {id}（软删除）"));
    Ok(id)
}

#[tauri::command]
fn cleanup_data(days: i64) -> Result<String, String> {
    // 清理前归档：设置页勾了 ArchiveBeforeCleanup 就先落一份整库备份
    let mut prefix = String::new();
    if setting_bool_default("ArchiveBeforeCleanup", false) {
        match pchabit_datad::backup_now() {
            Ok((path, size)) => {
                log_line(&format!(
                    "[壳] 清理前归档：{path}（{} MB）",
                    (size as f64 / 1048576.0).round()
                ));
                prefix = format!("已先归档到 {path}；");
            }
            Err(e) => {
                log_line(&format!("[壳] 清理前归档失败（继续清理）：{e}"));
                prefix = format!("归档失败（{e}），仍继续清理；");
            }
        }
    }
    let (cutoff, deleted) = pchabit_datad::cleanup_old(days)?;
    log_line(&format!("[壳] 手动清理：删除 {deleted} 条（截止 {cutoff}，保留 {days} 天）"));
    Ok(format!("{prefix}已删除 {deleted} 条 {days} 天前的记录（截止 {cutoff}）"))
}

#[tauri::command]
fn backup_db() -> Result<String, String> {
    let (path, size) = pchabit_datad::backup_now()?;
    log_line(&format!("[壳] 手动备份：{path}（{} MB）", (size as f64 / 1048576.0).round()));
    Ok(format!("备份完成：{path}（{} MB）", (size as f64 / 1048576.0).round()))
}

/// 便签同步（WebDAV）：合并远端与本地，再把合并结果传回。
#[tauri::command]
fn notes_sync() -> Result<String, String> {
    if !setting_bool_default("StickyNotesSyncEnabled", false) {
        return Err("设置里未启用便签同步（StickyNotesSyncEnabled）".to_string());
    }
    let (imported, total) = backup::notes_sync()?;
    log_line(&format!("[便签] 同步完成：合并 {imported} 张，共 {total} 张"));
    Ok(format!("同步完成：合并 {imported} 张，本地共 {total} 张"))
}

/// 手动触发一次云备份（设置页按钮用）。
#[tauri::command]
fn cloud_backup_now() -> Result<String, String> {    let name = backup::cloud_backup()?;
    log_line(&format!("[云备份] 手动上传完成：{name}"));
    Ok(format!("已上传 {name}"))
}

/// 云端的备份状态：最新文件 + 份数 + 上次同步时间。
#[tauri::command]
fn cloud_latest_info() -> serde_json::Value {
    let latest = backup::cloud_latest();
    let count = backup::cloud_names().map(|n| n.len() as i64).unwrap_or(-1);
    let v = serde_json::json!({
        "ok": latest.is_ok(),
        "latest": latest.clone().unwrap_or_default(),
        "count": count,
        "countOk": count >= 0,
        "error": latest.err().unwrap_or_default(),
        "lastSync": setting_str("WebDAVLastSync", ""),
        "enabled": setting_bool_default("WebDAVEnabled", false),
        "url": setting_str("WebDAVUrl", ""),
        "user": setting_str("WebDAVUsername", ""),
    });
    log_line(&format!(
        "[云备份] 状态查询：启用={} 云端 {} 份 最新={} 上次同步={}",
        v["enabled"],
        if count >= 0 { count.to_string() } else { "?".to_string() },
        if v["latest"].as_str().unwrap_or("").is_empty() { "无" } else { v["latest"].as_str().unwrap_or("") },
        if v["lastSync"].as_str().unwrap_or("").is_empty() { "无" } else { v["lastSync"].as_str().unwrap_or("") }
    ));
    v
}

/// 云端的最新备份下载到本地临时目录（**不覆盖当前库**，供用户自己决定要不要替换）。
#[tauri::command]
fn cloud_restore_download() -> Result<String, String> {
    match backup::cloud_fetch_latest()? {
        Some(path) => {
            let size = std::fs::metadata(&path).map(|m| m.len()).unwrap_or(0);
            log_line(&format!(
                "[云备份] 已下载云端最新备份：{}（{} MB）",
                path.display(),
                (size as f64 / 1048576.0).round()
            ));
            Ok(format!(
                "已下载到 {}（{} MB）",
                path.display(),
                (size as f64 / 1048576.0).round()
            ))
        }
        None => Err("云端没有可用备份（先上传一次，或检查 WebDAV 设置）".to_string()),
    }
}

/// 前端要数据：一次同步查库 + 聚合，返回该页面需要的那份 JSON。
#[tauri::command]
fn page_data(page: String) -> Result<serde_json::Value, String> {
    let file = match page.as_str() {
        "category" => "piechart.html",
        "sankey" => "sankey.html",
        "analytics" => "analytics.html",
        "admin" => "categories.html",
        "detail" => "detail.html",
        "notes" => "notes.html",
        "web" => "web.html",
        "timeline" => "timeline.html",
        "dashboard" => "dashboard.html",
        "net" => "net.html",
        "input" => "input.html",
        "reports" => "reports.html",
        "data" => "data.html",
        "settings" => "settings.html",
        other => return Err(format!("未知页面：{other}")),
    };
    // 发布版把 dist 打进了 exe（tauri asset protocol），磁盘上没有 dist 目录也能工作；
    // 这里只在开发目录下做一次存在性提示，绝不因此拒绝返回数据。
    let asset = dist_dir().join("pages").join(file);
    if !asset.exists() && cfg!(debug_assertions) {
        log_line(&format!("[壳] 提示：{} 不在磁盘 dist 里（发布版走内嵌资源）", asset.display()));
    }

    let t0 = std::time::Instant::now();
    let data = match page.as_str() {
        "sankey" => payload_sankey(),
        "admin" => {
            let apps = pchabit_datad::app_categories_json(&pchabit_datad::default_db(), 7)
                .unwrap_or_else(|_| serde_json::json!({ "days": 7, "items": [] }));
            payload_admin(apps)
        }
        "settings" => payload_settings(),
        "notes" => payload_notes(),
        "web" => payload_web(),
        "timeline" => payload_timeline(&pchabit_datad::today_local(), 0),
        // 仪表盘自带硬件趋势，单独取数时要重算一次分析报告
        "dashboard" => payload_dashboard(&payload_analytics()),
        "net" => payload_net(),
        "input" => payload_input(),
        "reports" => payload_reports(),
        "data" => payload_data(),
        "detail" => serde_json::json!({
            "apps": pchabit_datad::app_categories_json(&pchabit_datad::default_db(), 0)
                .unwrap_or_else(|_| serde_json::json!({ "days": 0, "items": [] }))
        }),
        _ => {
            let report = payload_analytics();
            match page.as_str() {
                "category" => payload_category(&report),
                _ => report,
            }
        }
    };
    log_line(&format!(
        "page_data {page}: {:.1} ms",
        t0.elapsed().as_secs_f64() * 1000.0
    ));
    Ok(data)
}

/// 一次算好全部载荷：启动注入与手动刷新共用同一份逻辑。
fn all_pages() -> serde_json::Value {
    let t0 = std::time::Instant::now();
    let report = payload_analytics(); // 硬件与分类都从这份报告里取，避免重复计算
    // 进程清单只查一次，分类管理页与应用详情页共用
    let apps = pchabit_datad::app_categories_json(&pchabit_datad::default_db(), 7)
        .unwrap_or_else(|e| {
            log_line(&format!("apps: 进程列表读取失败 {e}"));
            serde_json::json!({ "days": 7, "items": [] })
        });
    let pages = serde_json::json!({
        "category": payload_category(&report),
        "sankey": payload_sankey(),
        "analytics": report,
        "admin": payload_admin(apps.clone()),
        "detail": serde_json::json!({ "apps": apps }),
        "settings": payload_settings(),
        "notes": payload_notes(),
        "web": payload_web(),
        "timeline": payload_timeline(&pchabit_datad::today_local(), 0),
        "dashboard": payload_dashboard(&report),
        "net": payload_net(),
        "input": payload_input(),
        "reports": payload_reports(),
        "data": payload_data(),
    });
    log_line(&format!(
        "全部载荷就绪（{} 页）：{:.1} ms",
        pages.as_object().map(|o| o.len()).unwrap_or(0),
        t0.elapsed().as_secs_f64() * 1000.0
    ));
    pages
}

/// 刷新：前端点按钮时重算全部载荷。
#[tauri::command]
fn pages_data() -> serde_json::Value {
    all_pages()
}

/// 单应用详情：近 N 天 vs 前 N 天（`days <= 0` = 全部历史），含会话聚合、键鼠、硬件归属、标题 Top5、日趋势。
#[tauri::command]
fn app_detail(
    process: String,
    app_name: String,
    category: String,
    days: Option<i64>,
) -> Result<serde_json::Value, String> {
    let today = pchabit_datad::today_local();
    let db = pchabit_datad::default_db();
    let days_eff = match days.unwrap_or(7) {
        d if d > 0 => d,
        _ => pchabit_datad::app_earliest_date(&db)
            .map(|e| (pchabit_datad::date_diff_days(&e, &today) + 1).max(1))
            .unwrap_or(30),
    };
    let cur_start = pchabit_datad::date_shift(&today, -(days_eff - 1));
    let prev_end = pchabit_datad::date_shift(&cur_start, -1);
    let prev_start = pchabit_datad::date_shift(&cur_start, -days_eff);
    let req = serde_json::json!({
        "curStart": format!("{cur_start} 00:00:00.0000000"),
        "curEnd": format!("{today} 23:59:59.9999999"),
        "prevStart": format!("{prev_start} 00:00:00.0000000"),
        "prevEnd": format!("{prev_end} 23:59:59.9999999"),
        "dayStart": format!("{cur_start} 00:00:00.0000000"),
        "dayEnd": format!("{today} 23:59:59.9999999"),
        "hwStart": cur_start,
        "hwEnd": today,
        "processName": process,
        "appName": app_name,
        "category": category,
    });
    let t0 = std::time::Instant::now();
    let v = pchabit_datad::detail_json(&pchabit_datad::default_db(), &req)?;
    log_line(&format!(
        "detail {}: {:.1} ms / {:.0} 分钟 / {} 会话",
        v["processName"].as_str().unwrap_or(""),
        t0.elapsed().as_secs_f64() * 1000.0,
        v["totalMinutes"].as_f64().unwrap_or(0.0),
        v["sessions"].as_i64().unwrap_or(0)
    ));
    Ok(v)
}

/// 应用详情左侧列表：按时间范围统计每个进程的时长（`days <= 0` = 全部历史）。
#[tauri::command]
fn apps_stats(days: i64) -> Result<serde_json::Value, String> {
    pchabit_datad::app_categories_json(&pchabit_datad::default_db(), days)
}

/// 便签（只读）：WPF 负责增删改，壳只展示，避免两处分头写。
fn payload_notes() -> serde_json::Value {
    let v = pchabit_datad::notes_json(&pchabit_datad::default_db()).unwrap_or_else(|e| {
        log_line(&format!("notes: 读取失败 {e}"));
        serde_json::json!({ "count": 0, "items": [] })
    });
    log_line(&format!(
        "notes: {} 条便签",
        v["count"].as_i64().unwrap_or(0)
    ));
    v
}

/// 网页访问（只读）：近 7 天会话聚合、域名排行、小时/日分布、最近记录、流量归因。
fn payload_web() -> serde_json::Value {
    web_payload(0)
}

/// 网页访问数据：`days <= 0` 表示全部历史（从库里最早一条到昨天/今天），否则最近 N 天。
fn web_payload(days: i64) -> serde_json::Value {
    let today = pchabit_datad::today_local();
    let db = pchabit_datad::default_db();
    let start = if days <= 0 {
        // 全部历史：直接从 WebSessions 取最早一天；一条都没有就退化成近 30 天
        pchabit_datad::web_earliest_date(&db).unwrap_or_else(|| pchabit_datad::date_shift(&today, -29))
    } else {
        pchabit_datad::date_shift(&today, -(days - 1))
    };
    match pchabit_datad::web_json(&db, &start, &today) {
        Ok(v) => {
            log_line(&format!(
                "web: {start}~{today} / {} 次访问 / {} 个域名 / {:.1} ms",
                v["summary"]["totalVisits"].as_i64().unwrap_or(0),
                v["summary"]["uniqueDomains"].as_i64().unwrap_or(0),
                v["ms"].as_f64().unwrap_or(0.0)
            ));
            v
        }
        Err(e) => {
            log_line(&format!("web: 读取失败 {e}"));
            serde_json::json!({
                "ok": false, "error": e, "summary": {}, "domains": [], "hourly": [],
                "daily": [], "recent": []
            })
        }
    }
}

/// 网页页切换时间范围：`days = 0` 全部，7 / 30 / 90 为最近 N 天。
#[tauri::command]
fn web_data(days: i64) -> Result<serde_json::Value, String> {
    let v = web_payload(days);
    if v["ok"].as_bool().unwrap_or(false) {
        Ok(v)
    } else {
        Err(v["error"].as_str().unwrap_or("读取失败").to_string())
    }
}

/// 仪表盘（只读）：今日 KPI + 24 小时活跃 + Top 应用 + 7 天趋势 + 硬件趋势（原独立页已并入）。
///
/// 硬件部分复用分析报告里的 `hardware`：`hw.hours` 画曲线、`hw.appLoads` 出负载榜。
fn payload_dashboard(report: &serde_json::Value) -> serde_json::Value {
    match pchabit_datad::dashboard_json(&pchabit_datad::default_db()) {
        Ok(mut v) => {
            let hw = payload_hardware(report);
            // 只有零星样本（如安装器、右键"选取应用"）不配上榜；先筛再取前 6
            let mut loads: Vec<serde_json::Value> = report["hardware"]["appLoads"]
                .as_array()
                .map(|a| {
                    a.iter()
                        .filter(|x| {
                            x["sampleCount"].as_i64().unwrap_or(0) >= 3
                                && x["overlapMinutes"].as_f64().unwrap_or(0.0) >= 1.0
                        })
                        .take(6)
                        .cloned()
                        .collect()
                })
                .unwrap_or_default();
            v["hw"] = hw;
            v["hwApps"] = serde_json::Value::Array(loads);
            log_line(&format!(
                "dashboard: 硬件 {} 个小时桶 / {} 个高负载应用",
                v["hw"]["hours"].as_array().map(|a| a.len()).unwrap_or(0),
                v["hwApps"].as_array().map(|a| a.len()).unwrap_or(0)
            ));
            log_line(&format!(
                "dashboard: {} {} / {:.1} 分钟 / {} 应用 / {:.1} ms",
                v["label"].as_str().unwrap_or(""),
                v["weekday"].as_str().unwrap_or(""),
                v["totalMinutes"].as_f64().unwrap_or(0.0),
                v["appCount"].as_i64().unwrap_or(0),
                v["ms"].as_f64().unwrap_or(0.0)
            ));
            v
        }
        Err(e) => {
            log_line(&format!("dashboard: 读取失败 {e}"));
            serde_json::json!({ "ok": false, "error": e, "topApps": [], "hourly": [], "trend": [],
                "hw": { "hours": [], "summary": "暂无硬件样本" }, "hwApps": [] })
        }
    }
}

/// 网络流量（只读）：默认全部历史，页面内可切范围。
fn payload_net() -> serde_json::Value {
    net_payload(0)
}

/// 网络流量按范围取数：`days = 0` 全部历史，`1` 今日。
fn net_payload(days: i64) -> serde_json::Value {
    let db = pchabit_datad::default_db();
    let today = pchabit_datad::today_local();
    let start = if days <= 0 {
        pchabit_datad::net_earliest_date(&db)
            .unwrap_or_else(|| pchabit_datad::date_shift(&today, -6))
    } else {
        pchabit_datad::date_shift(&today, -(days - 1))
    };
    match pchabit_datad::net_json(&db, &start, &today) {
        Ok(mut v) => {
            log_line(&format!(
                "net: {start}~{today} / 下 {} / 上 {} / {} 进程 / {:.1} ms",
                v["totalDownText"].as_str().unwrap_or(""),
                v["totalUpText"].as_str().unwrap_or(""),
                v["processes"].as_array().map(|a| a.len()).unwrap_or(0),
                v["ms"].as_f64().unwrap_or(0.0)
            ));
            v["days"] = serde_json::json!(days);
            v
        }
        Err(e) => {
            log_line(&format!("net: 读取失败 {e}"));
            serde_json::json!({
                "ok": false, "error": e, "processes": [], "hourlyDown": [], "daily": []
            })
        }
    }
}

/// 前端切范围时调：`days = 0` 表示全部历史。
#[tauri::command]
fn net_data(days: i64) -> Result<serde_json::Value, String> {
    let v = net_payload(days);
    if v["ok"].as_bool().unwrap_or(false) {
        Ok(v)
    } else {
        Err(v["error"].as_str().unwrap_or("读取失败").to_string())
    }
}

/// 键鼠活动（只读）：默认全部历史，页面内可切范围。
fn payload_input() -> serde_json::Value {
    input_payload(0)
}

/// 键鼠活动按范围取数：`days <= 0` = 全部历史（起点取库内最早一天）。
fn input_payload(days: i64) -> serde_json::Value {
    let db = pchabit_datad::default_db();
    let today = pchabit_datad::today_local();
    let start = if days <= 0 {
        pchabit_datad::input_earliest_date(&db).unwrap_or_else(|| pchabit_datad::date_shift(&today, -29))
    } else {
        pchabit_datad::date_shift(&today, -(days - 1))
    };
    match pchabit_datad::input_json(&db, &start, &today) {
        Ok(mut v) => {
            log_line(&format!(
                "input: {start}~{today} / {} 键 / {} 击 / {} 滚 / {:.1} ms",
                v["keys"].as_i64().unwrap_or(0),
                v["clicks"].as_i64().unwrap_or(0),
                v["scroll"].as_i64().unwrap_or(0),
                v["ms"].as_f64().unwrap_or(0.0)
            ));
            v["days"] = serde_json::json!(days);
            v
        }
        Err(e) => {
            log_line(&format!("input: 读取失败 {e}"));
            serde_json::json!({
                "ok": false, "error": e, "topKeys": [], "topClicks": [],
                "hourlyKeys": [], "hourlyClicks": [], "keyFreq": [], "shortcuts": []
            })
        }
    }
}

/// 前端切范围时调：`days = 0` 表示全部历史。
#[tauri::command]
fn input_data(days: i64) -> Result<serde_json::Value, String> {
    let v = input_payload(days);
    if v["ok"].as_bool().unwrap_or(false) {
        Ok(v)
    } else {
        Err(v["error"].as_str().unwrap_or("读取失败").to_string())
    }
}

/// 历史报告（只读）：`DailySummaries` 按自然周聚合。
fn payload_reports() -> serde_json::Value {
    match pchabit_datad::reports_json(&pchabit_datad::default_db()) {
        Ok(v) => {
            log_line(&format!(
                "reports: {} 周 / 最新 {} / {:.1} ms",
                v["count"].as_i64().unwrap_or(0),
                v["weeks"][0]["label"].as_str().unwrap_or(""),
                v["ms"].as_f64().unwrap_or(0.0)
            ));
            v
        }
        Err(e) => {
            log_line(&format!("reports: 读取失败 {e}"));
            serde_json::json!({ "ok": false, "error": e, "count": 0, "weeks": [] })
        }
    }
}

/// 数据概览（只读）：库文件体积、各表行数、备份记录。
fn payload_data() -> serde_json::Value {
    match pchabit_datad::data_json(&pchabit_datad::default_db()) {
        Ok(v) => {
            log_line(&format!(
                "data: {} + wal {} / {} 张表 / {} 条备份 / {:.1} ms",
                v["sizeText"].as_str().unwrap_or(""),
                v["walText"].as_str().unwrap_or(""),
                v["tables"].as_array().map(|a| a.len()).unwrap_or(0),
                v["backups"].as_array().map(|a| a.len()).unwrap_or(0),
                v["ms"].as_f64().unwrap_or(0.0)
            ));
            v
        }
        Err(e) => {
            log_line(&format!("data: 读取失败 {e}"));
            serde_json::json!({ "ok": false, "error": e, "tables": [], "backups": [] })
        }
    }
}

/// 时间线（只读）：某一天的「应用 × 时段」块，供 24 小时甘特图。
fn payload_timeline(date: &str, offset: i64) -> serde_json::Value {
    match pchabit_datad::timeline_json(&pchabit_datad::default_db(), date) {
        Ok(mut v) => {
            v["offset"] = serde_json::json!(offset);
            v["isToday"] = serde_json::json!(date == pchabit_datad::today_local());
            log_line(&format!(
                "timeline: {} {} / {} 个应用 / {} 段 / {:.1} ms",
                date,
                v["weekday"].as_str().unwrap_or(""),
                v["appCount"].as_i64().unwrap_or(0),
                v["segments"].as_array().map(|a| a.len()).unwrap_or(0),
                v["ms"].as_f64().unwrap_or(0.0)
            ));
            v
        }
        Err(e) => {
            log_line(&format!("timeline: 读取失败 {e}"));
            serde_json::json!({
                "ok": false, "error": e, "date": date, "apps": [], "segments": []
            })
        }
    }
}

/// 分类管理：分类清单（含停用项）+ 近 7 天出现过的进程及其当前分类。
fn payload_admin(apps: serde_json::Value) -> serde_json::Value {
    let db = pchabit_datad::default_db();
    let cats = pchabit_datad::categories_json(&db).unwrap_or_else(|e| {
        log_line(&format!("admin: 分类读取失败 {e}"));
        serde_json::Value::Array(vec![])
    });
    let apps = apps;
    let active = cats
        .as_array()
        .map(|a| a.iter().filter(|c| c["active"].as_bool().unwrap_or(false)).count())
        .unwrap_or(0);
    log_line(&format!(
        "admin: {active} 个启用分类（共 {} 条）/ {} 个进程",
        cats.as_array().map(|a| a.len()).unwrap_or(0),
        apps["items"].as_array().map(|a| a.len()).unwrap_or(0)
    ));
    serde_json::json!({ "categories": cats, "apps": apps })
}

/// 新建分类。
#[tauri::command]
fn create_category(name: String, color: String, icon: String) -> Result<serde_json::Value, String> {
    let db = pchabit_datad::default_db();
    let req = serde_json::json!({ "name": name, "color": color, "icon": icon });
    let v = pchabit_datad::create_category(&db, &req)?;
    log_line(&format!(
        "写库: 新建分类「{}」id={} 颜色 {}",
        v["name"].as_str().unwrap_or(""),
        v["id"].as_i64().unwrap_or(0),
        v["color"].as_str().unwrap_or("")
    ));
    Ok(v)
}

/// 启用/停用分类。
#[tauri::command]
fn set_category_active(id: i64, active: bool) -> Result<serde_json::Value, String> {
    let db = pchabit_datad::default_db();
    let req = serde_json::json!({ "id": id, "active": active });
    let v = pchabit_datad::set_category_active(&db, &req)?;
    log_line(&format!(
        "写库: 分类「{}」{}",
        v["name"].as_str().unwrap_or(""),
        if active { "启用" } else { "停用" }
    ));
    Ok(v)
}

/// 改一个进程的分类（写库）。
#[tauri::command]
fn set_category(process: String, category: String) -> Result<serde_json::Value, String> {
    let db = pchabit_datad::default_db();
    let req = serde_json::json!({ "process": process, "category": category });
    let v = pchabit_datad::set_app_category(&db, &req)?;
    log_line(&format!(
        "写库: {} -> {}（{}）",
        v["processName"].as_str().unwrap_or(""),
        v["category"].as_str().unwrap_or(""),
        if v["created"].as_bool().unwrap_or(false) {
            "新增映射"
        } else {
            "更新映射"
        }
    ));
    Ok(v)
}

#[tauri::command]
fn categories() -> Result<serde_json::Value, String> {
    pchabit_datad::categories_json(&pchabit_datad::default_db())
}

/// 设置文件里允许壳改写的项：**文件里已经存在的键都可以改**（见 `set_setting`），
/// 这张表只用于「文件里还没有、但允许新建」的键。类型按现有值判定。
const SETTING_WHITELIST: &[&str] = &[
    "DataRetentionDays",
    "RetentionDays",
    "MonitoringInterval",
    "IdleThreshold",
    "TaskbarEnabled",
    "TaskbarShowCpu",
    "TaskbarShowMemory",
    "TaskbarShowGpu",
    "TaskbarShowVram",
    "TaskbarShowNet",
    "TaskbarShowDisk",
    "TaskbarShowTemp",
    "TaskbarShowUsage",
    "DesktopWidgetEnabled",
    "AutoCleanupEnabled",
    "StartWithWindows",
    "MinimizeToTray",
    "ShowNotifications",
    "TrackKeyboard",
    "TrackMouse",
    "TrackWebBrowsing",
];

/// 只读键：时间戳/设备标识这类由程序自己维护的字段，设置页只展示不给改。
const SETTING_READONLY: &[&str] = &["WebDAVLastSync", "StickyNotesLastSync", "StickyNotesDeviceId"];

/// 导出周报 Excel：把「周汇总 / 近 7 天 / 今日应用」拼成工作簿，交给 datad 的 `xlsx` 子命令写盘。
/// 输出目录：`%LOCALAPPDATA%\PChabit\Exports\周报_YYYY-MM-DD.xlsx`。
#[tauri::command]
fn export_weekly() -> Result<String, String> {
    let db = pchabit_datad::default_db();
    let reports = pchabit_datad::reports_json(&db)?;
    let dash = pchabit_datad::dashboard_json(&db)?;

    let exports = std::path::PathBuf::from(std::env::var("LOCALAPPDATA").unwrap_or_default())
        .join("PChabit")
        .join("Exports");
    std::fs::create_dir_all(&exports).map_err(|e| format!("建导出目录失败：{e}"))?;
    let file = exports.join(format!("周报_{}.xlsx", pchabit_datad::today_local()));

    let header = vec!["周", "活跃分钟", "日均分钟", "专注分钟", "专注占比%", "按键", "点击", "应用切换", "网页分钟"];
    let mut rows: Vec<serde_json::Value> = Vec::new();
    for w in reports["weeks"].as_array().cloned().unwrap_or_default() {
        rows.push(serde_json::json!([
            w["label"].as_str().unwrap_or("-"),
            round1(w["activeMinutes"].as_f64().unwrap_or(0.0)),
            round1(w["avgActiveMinutes"].as_f64().unwrap_or(0.0)),
            round1(w["focusMinutes"].as_f64().unwrap_or(0.0)),
            round1(w["focusRatio"].as_f64().unwrap_or(0.0)),
            w["keys"].as_i64().unwrap_or(0),
            w["clicks"].as_i64().unwrap_or(0),
            w["switches"].as_i64().unwrap_or(0),
            round1(w["webMinutes"].as_f64().unwrap_or(0.0)),
        ]));
    }

    let mut trend_rows: Vec<serde_json::Value> = Vec::new();
    for t in dash["trend"].as_array().cloned().unwrap_or_default() {
        trend_rows.push(serde_json::json!([
            t["label"].as_str().unwrap_or("-"),
            round1(t["minutes"].as_f64().unwrap_or(0.0)),
            t["source"].as_str().unwrap_or("-"),
        ]));
    }

    let mut app_rows: Vec<serde_json::Value> = Vec::new();
    for a in dash["topApps"].as_array().cloned().unwrap_or_default() {
        app_rows.push(serde_json::json!([
            a["process"].as_str().unwrap_or("-"),
            round1(a["minutes"].as_f64().unwrap_or(0.0)),
            round1(a["share"].as_f64().unwrap_or(0.0)),
        ]));
    }

    let spec = serde_json::json!({
        "file": file.to_string_lossy(),
        "sheets": [
            {
                "name": "周汇总",
                "widths": [22, 12, 12, 12, 12, 12, 12, 12, 12],
                "sections": [
                    { "title": "PChabit 周汇总", "headers": header, "rows": rows }
                ]
            },
            {
                "name": "近7天",
                "widths": [12, 12, 12],
                "sections": [
                    { "title": "近 7 天活跃分钟", "headers": ["日期", "分钟", "来源"], "rows": trend_rows }
                ]
            },
            {
                "name": "今日应用",
                "widths": [28, 12, 12],
                "sections": [
                    { "title": "今日应用排行", "headers": ["应用", "分钟", "占比%"], "rows": app_rows }
                ]
            }
        ]
    });

    // datad 可能与壳同目录（publish\Shell 测试时），也可能在其上一级（D:\Tool\PChabit\Shell 旁边）
    let datad = std::env::current_exe()
        .ok()
        .and_then(|p| p.parent().map(|d| d.to_path_buf()))
        .and_then(|dir| {
            [
                dir.join("pchabit-datad.exe"),
                dir.parent().map(|p| p.join("pchabit-datad.exe")).unwrap_or_default(),
            ]
            .into_iter()
            .find(|c| c.exists())
        })
        .ok_or_else(|| "找不到 pchabit-datad.exe（壳目录或上一级）".to_string())?;

    use std::io::Write;
    #[allow(unused_mut)]
    let mut cmd = std::process::Command::new(&datad);
    cmd.arg("xlsx")
        .stdin(std::process::Stdio::piped())
        .stdout(std::process::Stdio::piped())
        .stderr(std::process::Stdio::piped());
    // datad 也是控制台程序：不指定 CREATE_NO_WINDOW，导出时会闪一个黑窗
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        const CREATE_NO_WINDOW: u32 = 0x0800_0000;
        cmd.creation_flags(CREATE_NO_WINDOW);
    }
    let mut child = cmd
        .spawn()
        .map_err(|e| format!("启动 datad 失败：{e}"))?;
    if let Some(stdin) = child.stdin.as_mut() {
        stdin
            .write_all(spec.to_string().as_bytes())
            .map_err(|e| format!("写请求失败：{e}"))?;
    }
    let out = child
        .wait_with_output()
        .map_err(|e| format!("等待导出失败：{e}"))?;
    let text = String::from_utf8_lossy(&out.stdout).to_string();
    let v: serde_json::Value = serde_json::from_str(text.trim())
        .map_err(|e| format!("导出结果解析失败（{e}）：{text}"))?;
    if v["ok"].as_bool() != Some(true) {
        return Err(format!("导出失败：{text}"));
    }
    let size = v["bytes"].as_i64().unwrap_or(0);
    log_line(&format!(
        "[壳] 导出周报：{}（{} KB / {} 行）",
        file.display(),
        (size as f64 / 1024.0).round(),
        v["rows"].as_i64().unwrap_or(0)
    ));
    Ok(file.to_string_lossy().to_string())
}

/// 写回 `%LOCALAPPDATA%\PChabit\settings.json`：先备份 `settings.json.bak`，再整份重写。
#[tauri::command]
fn set_setting(
    app: tauri::AppHandle,
    key: String,
    value: serde_json::Value,
) -> Result<String, String> {
    if SETTING_READONLY.contains(&key.as_str()) {
        return Err(format!("只读项不能修改：{key}"));
    }
    let path = std::path::PathBuf::from(std::env::var("LOCALAPPDATA").unwrap_or_default())
        .join("PChabit")
        .join("settings.json");
    let text = std::fs::read_to_string(&path).map_err(|e| format!("读取设置失败：{e}"))?;
    let mut v: serde_json::Value =
        serde_json::from_str(&text).map_err(|e| format!("解析设置失败：{e}"))?;
    let obj = v
        .as_object_mut()
        .ok_or_else(|| "设置文件不是对象".to_string())?;
    // 文件里已经有的键随便改（类型要对得上）；文件里没有的，只允许白名单里那几个新建
    match obj.get(&key) {
        Some(old_v) => {
            let same_type = matches!(
                (old_v, &value),
                (serde_json::Value::Bool(_), serde_json::Value::Bool(_))
                    | (serde_json::Value::Number(_), serde_json::Value::Number(_))
                    | (serde_json::Value::String(_), serde_json::Value::String(_))
                    | (serde_json::Value::Null, _)
            );
            if !same_type {
                return Err(format!(
                    "类型不匹配：{key} 现在是 {}，传进来的是 {}",
                    json_type_name(old_v),
                    json_type_name(&value)
                ));
            }
        }
        None => {
            if !SETTING_WHITELIST.contains(&key.as_str()) {
                return Err(format!("不允许修改的设置项：{key}"));
            }
        }
    }
    let old = obj.get(&key).cloned().unwrap_or(serde_json::Value::Null);
    obj.insert(key.clone(), value.clone());
    let bak = path.with_extension("json.bak");
    let _ = std::fs::write(&bak, &text);
    let pretty = serde_json::to_string_pretty(&v).map_err(|e| e.to_string())?;
    std::fs::write(&path, pretty).map_err(|e| format!("写入设置失败：{e}"))?;
    log_line(&format!(
        "[壳] 设置写入 {key}：{old} -> {value}（备份 {}）",
        bak.display()
    ));
    // 实时生效：不用重启，改完这一秒就变
    match key.as_str() {
        "TaskbarEnabled" => {
            let on = match &value {
                serde_json::Value::Bool(b) => *b,
                serde_json::Value::Number(n) => n.as_i64().unwrap_or(0) != 0,
                _ => true,
            };
            if on {
                let light = taskbar::system_uses_light_theme();
                taskbar::start(app.clone(), light);
                log_line("[壳] 设置：任务栏小窗已启动");
            } else {
                taskbar::stop();
            }
        }
        "StartWithWindows" => {
            let on = value.as_bool().unwrap_or(false);
            match apply_start_with_windows(on) {
                Ok(msg) => log_line(&format!("[壳] 开机自启：{msg}")),
                Err(e) => log_line(&format!("[壳] 开机自启设置失败：{e}")),
            }
        }
        "AutoStartMonitoring" => {
            let on = value.as_bool().unwrap_or(false);
            match apply_collector_task(on) {
                Ok(msg) => log_line(&format!("[壳] 采集宿主自启：{msg}")),
                Err(e) => log_line(&format!("[壳] 采集宿主自启设置失败：{e}")),
            }
        }
        "RetentionDays" => {
            // 老键：真正生效的是 DataRetentionDays，两个一起改，避免页面上两套口径
            let n = value.as_i64().unwrap_or(90).clamp(1, 3650);
            update_setting_file("DataRetentionDays", serde_json::json!(n));
            log_line(&format!("[壳] 保留天数同步为 {n} 天（DataRetentionDays）"));
        }
        _ => {}
    }
    Ok(format!("{key} = {value}"))
}

/// 「开机自启」落到 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 的 `PChabit` 项。
///
/// 走 `reg.exe` 而不是引 winreg：只为一次写注册表再加一个依赖不划算，
/// 用 `CREATE_NO_WINDOW` 起子进程，界面上不会闪黑框。
fn apply_start_with_windows(on: bool) -> Result<String, String> {
    use std::os::windows::process::CommandExt;
    const KEY: &str = r"HKCU\Software\Microsoft\Windows\CurrentVersion\Run";
    if on {
        let exe = std::env::current_exe().map_err(|e| format!("取自身路径失败：{e}"))?;
        let value = format!("\"{}\"", exe.display());
        let out = std::process::Command::new("reg")
            .args(["add", KEY, "/v", "PChabit", "/t", "REG_SZ", "/d", &value, "/f"])
            .creation_flags(0x0800_0000)
            .output()
            .map_err(|e| format!("调 reg 失败：{e}"))?;
        if out.status.success() {
            Ok(format!("已写入 Run（{}）", exe.display()))
        } else {
            Err(String::from_utf8_lossy(&out.stderr).trim().to_string())
        }
    } else {
        let out = std::process::Command::new("reg")
            .args(["delete", KEY, "/v", "PChabit", "/f"])
            .creation_flags(0x0800_0000)
            .output()
            .map_err(|e| format!("调 reg 失败：{e}"))?;
        // 本来就没有这一项也算成功（reg 会返回 1）
        Ok(if out.status.success() {
            "已从 Run 删除".to_string()
        } else {
            "Run 里本来就没有，无需删除".to_string()
        })
    }
}

/// 「采集宿主开机自启」落到计划任务「PChabit 采集宿主」的启用/禁用。
fn apply_collector_task(on: bool) -> Result<String, String> {
    use std::os::windows::process::CommandExt;
    let action = if on { "/enable" } else { "/disable" };
    let out = std::process::Command::new("schtasks")
        .args(["/change", "/tn", "PChabit 采集宿主", action])
        .creation_flags(0x0800_0000)
        .output()
        .map_err(|e| format!("调 schtasks 失败：{e}"))?;
    if out.status.success() {
        Ok(if on {
            "计划任务已启用".to_string()
        } else {
            "计划任务已禁用".to_string()
        })
    } else {
        Err(String::from_utf8_lossy(&out.stderr).trim().to_string())
    }
}

/// 后端低频服务（备份线程）统一从这里起：只起一次，重启壳不重复。
fn start_background_services() {
    static ONCE: std::sync::Once = std::sync::Once::new();
    ONCE.call_once(|| {
        backup::start();
        log_line("[壳] 后台服务：自动备份线程已启动");
    });
}

fn json_type_name(v: &serde_json::Value) -> &'static str {
    match v {
        serde_json::Value::Null => "null",
        serde_json::Value::Bool(_) => "布尔",
        serde_json::Value::Number(_) => "数字",
        serde_json::Value::String(_) => "字符串",
        serde_json::Value::Array(_) => "数组",
        serde_json::Value::Object(_) => "对象",
    }
}

fn payload_settings() -> serde_json::Value {
    let path = std::path::PathBuf::from(std::env::var("LOCALAPPDATA").unwrap_or_default())
        .join("PChabit")
        .join("settings.json");
    let text = match std::fs::read_to_string(&path) {
        Ok(t) => t,
        Err(e) => {
            log_line(&format!("settings: 读取失败 {e}"));
            return serde_json::json!({
                "path": path.display().to_string(),
                "items": [],
                "error": format!("{e}"),
            });
        }
    };
    let v: serde_json::Value = match serde_json::from_str(&text) {
        Ok(v) => v,
        Err(e) => {
            log_line(&format!("settings: 解析失败 {e}"));
            return serde_json::json!({
                "path": path.display().to_string(),
                "items": [],
                "error": format!("{e}"),
            });
        }
    };

    // 敏感项：只显示"已设置"，不回显任何内容（也不进日志）
    let secret_keys = ["password", "apikey", "token", "secret"];
    let items: Vec<serde_json::Value> = v
        .as_object()
        .map(|o| {
            o.iter()
                .map(|(k, val)| {
                    let lower = k.to_lowercase();
                    let is_secret = secret_keys.iter().any(|s| lower.contains(s));
                    let set = match val {
                        serde_json::Value::Null => false,
                        serde_json::Value::String(s) => !s.trim().is_empty(),
                        _ => true,
                    };
                    let read_only = SETTING_READONLY.contains(&k.as_str()) || k.ends_with("LastSync");
                    if is_secret {
                        serde_json::json!({
                            "key": k,
                            "kind": "secret",
                            "type": "secret",
                            "masked": true,
                            "set": set,
                            "readOnly": read_only,
                        })
                    } else {
                        let kind = match val {
                            serde_json::Value::Bool(_) => "bool",
                            serde_json::Value::Number(_) => "number",
                            _ => "string",
                        };
                        // 给前端一个更细的类型，决定用哪种控件
                        let ty = match val {
                            serde_json::Value::Bool(_) => "bool",
                            serde_json::Value::Number(n) => {
                                if n.is_i64() || n.is_u64() { "int" } else { "float" }
                            }
                            serde_json::Value::Null => "null",
                            _ => "string",
                        };
                        serde_json::json!({
                            "key": k,
                            "kind": kind,
                            "type": ty,
                            "value": val,
                            "readOnly": read_only,
                        })
                    }
                })
                .collect()
        })
        .unwrap_or_default();

    // 采集内核健康度：设置页顺带看一眼 netd 是否活着（没有 settings 键可读，靠管道问）
    let mut items = items;
    let health = pchabit_collect::netd::Pipe::connect(2)
        .and_then(|mut p| p.cmd("HEALTH"))
        .unwrap_or_else(|| "未连接（守护进程没起）".to_string());
    items.insert(
        0,
        serde_json::json!({
            "key": "采集内核（netd）",
            "kind": "text",
            "value": health,
            "set": true,
        }),
    );

    let masked = items.iter().filter(|i| i["masked"].as_bool().unwrap_or(false)).count();
    log_line(&format!(
        "settings: {} 项（{} 项敏感已打码）",
        items.len(),
        masked
    ));
    serde_json::json!({
        "path": path.display().to_string(),
        "count": items.len(),
        "maskedCount": masked,
        "items": items,
    })
}

// 壳的版本号（与 WPF 版对齐，发布时一起改）
const VERSION: &str = "4.12.32";

// ===== 桌面便签（WPF 的 StickyNotes 悬浮窗在 Rust 侧的替代实现）=====

/// 桌面上该摆哪些便签。
#[tauri::command]
fn notes_desktop() -> serde_json::Value {
    match pchabit_datad::notes_desktop_json(&pchabit_datad::default_db()) {
        Ok(v) => v,
        Err(e) => {
            log_line(&format!("notes_desktop: 读取失败 {e}"));
            serde_json::json!({ "count": 0, "items": [] })
        }
    }
}

/// 单张便签的内容（桌面便签窗口首次加载时取）。
#[tauri::command]
fn note_get(id: String) -> serde_json::Value {
    let db = pchabit_datad::default_db();
    match pchabit_datad::notes_json(&db) {
        Ok(v) => v["items"]
            .as_array()
            .and_then(|a| a.iter().find(|n| n["id"].as_str() == Some(id.as_str())))
            .cloned()
            .unwrap_or(serde_json::json!({ "id": id, "content": "", "color": "yellow", "pinned": false })),
        Err(e) => {
            log_line(&format!("note_get: 读取失败 {e}"));
            serde_json::json!({ "id": id, "content": "", "color": "yellow", "pinned": false })
        }
    }
}

/// 把当前窗口的位置/尺寸回写库（移动、缩放后各调一次）。
fn save_note_pos(app: &tauri::AppHandle, id: &str) -> Result<(), String> {
    let label = format!("note-{id}");
    let Some(w) = app.get_webview_window(&label) else {
        return Ok(());
    };
    let pos = w.outer_position().map_err(|e| e.to_string())?;
    let size = w.inner_size().map_err(|e| e.to_string())?;
    let scale = w.scale_factor().unwrap_or(1.0);
    if scale <= 0.0 {
        return Ok(());
    }
    pchabit_datad::notes_set_pos(
        id,
        pos.x as f64 / scale,
        pos.y as f64 / scale,
        size.width as f64 / scale,
        size.height as f64 / scale,
    )
}

#[tauri::command]
fn note_pos(app: tauri::AppHandle, id: String) -> Result<(), String> {
    let r = save_note_pos(&app, &id);
    if let Err(e) = &r {
        log_line(&format!("[便签] 回写位置失败 {id}：{e}"));
    }
    r
}

/// 放到桌面 / 收回列表。
#[tauri::command]
fn note_desktop_set(app: tauri::AppHandle, id: String, on: bool) -> Result<(), String> {
    pchabit_datad::notes_set_desktop(&id, on)?;
    if on {
        open_note_window(&app, &id)?;
    } else if let Some(w) = app.get_webview_window(&format!("note-{id}")) {
        let _ = w.close();
    }
    log_line(&format!("[便签] {id} {}", if on { "放到桌面" } else { "收回列表" }));
    Ok(())
}

/// 建一张桌面便签窗口（无边框、不进任务栏，钉住的置顶）。
fn open_note_window(app: &tauri::AppHandle, id: &str) -> Result<(), String> {
    let label = format!("note-{id}");
    if app.get_webview_window(&label).is_some() {
        return Ok(());
    }
    let list = pchabit_datad::notes_desktop_json(&pchabit_datad::default_db())?;
    let note = list["items"]
        .as_array()
        .and_then(|a| a.iter().find(|n| n["id"].as_str() == Some(id)))
        .cloned()
        .ok_or_else(|| format!("便签不在桌面上：{id}"))?;
    let url = format!("pages/note-desktop.html?note={id}");
    let w = tauri::WebviewWindowBuilder::new(app, &label, tauri::WebviewUrl::App(url.into()))
        .title("便签")
        .inner_size(
            note["width"].as_f64().unwrap_or(260.0),
            note["height"].as_f64().unwrap_or(200.0),
        )
        .position(
            note["left"].as_f64().unwrap_or(120.0),
            note["top"].as_f64().unwrap_or(120.0),
        )
        .decorations(false)
        .skip_taskbar(true)
        .always_on_top(note["pinned"].as_bool().unwrap_or(false))
        .initialization_script(&inject_script())
        .build()
        .map_err(|e| e.to_string())?;

    let app2 = app.clone();
    let id2 = id.to_string();
    let app3 = app.clone();
    w.on_window_event(move |e| match e {
        tauri::WindowEvent::Moved(_) | tauri::WindowEvent::Resized(_) => {
            let _ = save_note_pos(&app2, &id2);
        }
        tauri::WindowEvent::Focused(false) => {
            // 设置页 StickyNotesQuickDismissOnFocusLost：点到别处就自动收起（隐藏，不是关掉）
            if crate::setting_bool_default("StickyNotesQuickDismissOnFocusLost", false) {
                if let Some(win) = app3.get_webview_window(&format!("note-{id2}")) {
                    let _ = win.hide();
                    log_line(&format!("[便签] {id2} 失焦自动收起"));
                }
            }
        }
        _ => {}
    });
    log_line(&format!("[便签] 桌面窗口已建 {id}"));
    Ok(())
}

/// 开机/开窗时把桌面上的便签全部摆出来。
fn open_note_windows(app: &tauri::AppHandle) {
    let list = match pchabit_datad::notes_desktop_json(&pchabit_datad::default_db()) {
        Ok(v) => v,
        Err(e) => {
            log_line(&format!("[便签] 读桌面便签失败：{e}"));
            return;
        }
    };
    let items = list["items"].as_array().cloned().unwrap_or_default();
    for n in &items {
        if let Some(id) = n["id"].as_str() {
            if let Err(e) = open_note_window(app, id) {
                log_line(&format!("[便签] 开窗失败 {id}：{e}"));
            }
        }
    }
    if !items.is_empty() {
        log_line(&format!("[便签] 桌面便签 {} 张", items.len()));
    }
}


/// 壳里托管的采集宿主（`--host` 时才有值）
struct HostState(std::sync::Mutex<Option<pchabit_collect::Host>>);

impl HostState {
    fn stop(&self) {
        if let Ok(mut guard) = self.0.lock() {
            if let Some(h) = guard.take() {
                h.stop();
            }
        }
    }
}

/// 建主窗口（headless 下由托盘按需调用，所以抽成函数）。
fn make_window(
    app: &tauri::AppHandle,
    inject: &str,
    url: &str,
) -> Result<tauri::WebviewWindow, Box<dyn std::error::Error>> {
    let w = tauri::WebviewWindowBuilder::new(app, "main", tauri::WebviewUrl::App(url.into()))
        .title("PChabit（P5 采集宿主）")
        .inner_size(1440.0, 940.0)
        .min_inner_size(1000.0, 640.0)
        .center()
        .initialization_script(inject)
        .build()?;
    // 标题栏/任务栏的窗口图标（bundle 关掉时不会自动带）
    let _ = w.set_icon(tauri::image::Image::from_bytes(include_bytes!("../icons/icon.ico"))?);
    Ok(w)
}

/// 注入脚本（窗口创建时才需要，故做成函数，托盘开窗时现算）。
fn inject_script() -> String {
    format!(
        "window.__PAGES__ = {};window.__VER__ = '{}';",
        all_pages(),
        VERSION
    )
}

/// headless 的托盘，菜单项对齐 WPF `TrayService.ShowContextMenu`
/// （显示窗口 / 新建便签 / 便签列表 / — / 退出）。
/// 托盘必须活到进程结束，否则 Drop 会把图标摘掉，所以在这里 forget。
fn add_tray(app: tauri::AppHandle) -> Result<(), Box<dyn std::error::Error>> {
    use tauri::menu::{Menu, MenuItem, PredefinedMenuItem};
    use tauri::tray::TrayIconBuilder;

    let show = MenuItem::with_id(&app, "show", "显示窗口", true, None::<&str>)?;
    let new_note = MenuItem::with_id(&app, "newnote", "新建便签", true, None::<&str>)?;
    let note_list = MenuItem::with_id(&app, "notes", "便签列表", true, None::<&str>)?;
    let sep = PredefinedMenuItem::separator(&app)?;
    let quit = MenuItem::with_id(&app, "quit", "退出采集", true, None::<&str>)?;
    let menu = Menu::with_items(&app, &[&show, &new_note, &note_list, &sep, &quit])?;
    let icon = tauri::image::Image::from_bytes(include_bytes!("../icons/icon.ico"))?;

    let tray = TrayIconBuilder::with_id("pchabit")
        .icon(icon)
        .tooltip("PChabit 采集宿主（后台运行中）")
        .menu(&menu)
        .show_menu_on_left_click(false)
        // 左键单击按常规认知就是「打开主窗口」（右键才是菜单）
        .on_tray_icon_event(|tray, event| {
            use tauri::tray::{MouseButton, MouseButtonState, TrayIconEvent};
            if let TrayIconEvent::Click {
                button: MouseButton::Left,
                button_state: MouseButtonState::Up,
                ..
            } = event
            {
                let app = tray.app_handle();
                let app2 = app.clone();
                let _ = app.run_on_main_thread(move || tray_show(&app2, "托盘左键"));
            }
        })
        .on_menu_event(|app, event| match event.id().as_ref() {
            "show" => tray_show(app, "托盘菜单"),
            "newnote" => {
                // 托盘「新建便签」：真建一张、放桌面、直接把窗口摆出来（光标进去就能打字）。
                // 之前只是把便签页里的输入框聚焦，等于没建，用户看着像没反应。
                let app2 = app.clone();
                let _ = app.run_on_main_thread(move || match tray_new_note(&app2) {
                    Ok(id) => log_line(&format!("[壳] 托盘：已新建桌面便签 {id}")),
                    Err(e) => log_line(&format!("[壳] 托盘新建便签失败：{e}")),
                });
            }
            "notes" => {
                // 有窗口就复用（直接跳便签页），没有才新建，避免每次点都关掉仪表盘
                if let Some(w) = app.get_webview_window("main") {
                    let _ = w.show();
                    let _ = w.unminimize();
                    let _ = w.set_focus();
                    let _ = w.eval("location.href='app.html?p=notes'");
                    log_line("[壳] 托盘：便签列表（复用已有窗口）");
                } else {
                    open_window_at(app, "app.html?p=notes", "托盘：便签列表");
                }
            }
            "quit" => {
                if let Some(state) = app.try_state::<HostState>() {
                    state.stop();
                }
                log_line("[壳] 托盘：退出采集宿主");
                app.exit(0);
            }
            _ => {}
        })
        .build(&app)?;

    std::mem::forget(tray);
    Ok(())
}

/// 托盘的「打开主窗口」：有窗口就亮出来，没有就现建一个（左键与菜单「显示」共用）。
pub(crate) fn tray_show(app: &tauri::AppHandle, from: &str) {
    if let Some(w) = app.get_webview_window("main") {
        let _ = w.show();
        let _ = w.unminimize();
        let _ = w.set_focus();
        log_line(&format!("[壳] {from}：显示已有窗口"));
    } else {
        match make_window(app, &inject_script(), "app.html") {
            Ok(_) => log_line(&format!("[壳] {from}：已打开仪表盘窗口")),
            Err(e) => log_line(&format!("[壳] {from}开窗失败：{e}")),
        }
    }
}

/// 托盘「新建便签」：写库 → 标记为桌面便签 → 开悬浮窗，返回新便签 id。
pub(crate) fn tray_new_note(app: &tauri::AppHandle) -> Result<String, String> {
    let id = pchabit_datad::notes_create_blank("yellow")?;
    // 新纸条给个错开的起始位置（库里的默认是 -1，会贴到屏幕左上角）
    let n = pchabit_datad::notes_desktop_json(&pchabit_datad::default_db())
        .ok()
        .and_then(|v| v["count"].as_u64())
        .unwrap_or(0) as f64;
    let step = (n % 6.0) * 26.0;
    pchabit_datad::notes_set_pos(&id, 260.0 + step, 200.0 + step, 300.0, 220.0)?;
    pchabit_datad::notes_set_desktop(&id, true)?;
    open_note_window(app, &id)?;
    if let Some(w) = app.get_webview_window(&format!("note-{id}")) {
        let _ = w.set_focus();
    }
    Ok(id)
}

/// 托盘里「打开某个页面」：先关掉旧窗口再按目标地址开一个（换页只需重建，代价极小）。
pub(crate) fn open_window_at(app: &tauri::AppHandle, url: &str, what: &str) {
    if let Some(w) = app.get_webview_window("main") {
        let _ = w.close();
        std::thread::sleep(std::time::Duration::from_millis(300));
    }
    match make_window(app, &inject_script(), url) {
        Ok(_) => log_line(&format!("[壳] {what}（已开窗）")),
        Err(e) => log_line(&format!("[壳] {what} 开窗失败：{e}")),
    }
}

/// 单实例锁：占一个本地端口当互斥量（第二实例绑不上就转给第一个实例）。
/// 不引新依赖，进程死了端口自动释放。
fn take_single_instance() -> Option<std::net::TcpListener> {
    std::net::TcpListener::bind("127.0.0.1:18765").ok()
}

/// 第二个实例被锁挡住时：让已经在跑的那个把窗口亮出来，然后自己退出。
/// 桌面/任务栏快捷方式点一下就是这个路径，否则会「点了没反应」。
fn signal_existing_instance(cmd: Option<&str>) -> bool {
    use std::io::{Read, Write};
    match std::net::TcpStream::connect_timeout(
        &std::net::SocketAddr::from(([127, 0, 0, 1], 18765)),
        std::time::Duration::from_millis(800),
    ) {
        Ok(mut s) => {
            let _ = s.set_write_timeout(Some(std::time::Duration::from_millis(800)));
            let line = format!("{}\n", cmd.unwrap_or("SHOW").trim().to_uppercase());
            let _ = s.write_all(line.as_bytes());
            let _ = s.flush();
            // 等一句 ACK，读不到也算发出去过
            let _ = s.set_read_timeout(Some(std::time::Duration::from_millis(800)));
            let mut buf = [0u8; 16];
            let _ = s.read(&mut buf);
            true
        }
        Err(_) => false,
    }
}

fn main() {
    // 命令行先读：第二实例可能带 `--cmd TASKBAR_OFF` 这类控制指令
    let raw_args: Vec<String> = std::env::args().skip(1).collect();
    let ctl_cmd = raw_args
        .iter()
        .position(|a| a == "--cmd")
        .and_then(|i| raw_args.get(i + 1))
        .cloned();

    let Some(instance_lock) = take_single_instance() else {
        let ok = signal_existing_instance(ctl_cmd.as_deref());
        log_line(&format!(
            "[壳] 已有实例在跑（127.0.0.1:18765 被占用），{}",
            if ok { "已把指令转给它，本次退出" } else { "本次退出" }
        ));
        return;
    };

    // 命令行：pchabit-shell.exe [页面] [额外 query，如 auto=1] [--host] [--headless]
    let host_mode = raw_args.iter().any(|a| a == "--host");
    // --headless：不建窗口，只跑采集宿主（开机自启/后台常驻用）
    let headless = raw_args.iter().any(|a| a == "--headless");
    // --taskbar：额外在任务栏左侧画 Rust 小窗。
    // 显式带参数、或 settings.json 里 TaskbarEnabled=true（用户在设置页开过）就启动。
    let with_taskbar =
        raw_args.iter().any(|a| a == "--taskbar") || setting_bool("TaskbarEnabled");
    // --desktop：额外画 Rust 桌面硬件悬浮窗（设置页 DesktopWidgetEnabled 同样生效）
    let with_desktop =
        raw_args.iter().any(|a| a == "--desktop") || setting_bool("DesktopWidgetEnabled");
    // 窗口模式默认带托盘：关窗=隐到托盘，托盘「退出」才真退（--no-tray 关掉）
    let window_tray = !raw_args.iter().any(|a| a == "--no-tray");
    let positional: Vec<String> = raw_args
        .iter()
        .filter(|a| !a.starts_with("--"))
        .cloned()
        .collect();
    let start_page = positional.first().cloned().unwrap_or_default();
    let extra = positional.get(1).cloned().unwrap_or_default();

    // 五页数据在窗口创建时一次算好，注入 window.__PAGES__：
    // 渲染阶段不再走 IPC，避免跨 frame + 异步回调那堆坑。
    let pages = all_pages();
    let goal_hours = setting_i64("DailyUsageGoalHours", 6).clamp(0, 24);
    let inject = format!(
        "window.__PAGES__ = {pages};window.__VER__ = '{VERSION}';window.__GOAL_HOURS__ = {goal_hours};"
    );

    let app = tauri::Builder::default()
        .plugin(tauri_plugin_notification::init())
        .invoke_handler(tauri::generate_handler![
            page_data,
            pages_data,
            web_data,
            apps_stats,
            input_data,
            net_data,
            note,
            set_category,
            create_category,
            set_category_active,
            app_detail,
            categories,
            timeline_day,
            cleanup_data,
            backup_db,
            set_setting,
            cloud_backup_now,
            notes_sync,
            cloud_latest_info,
            cloud_restore_download,
            notes_create,
            notes_update,
            notes_pin,
            notes_delete,
            notes_desktop,
            note_get,
            note_pos,
            note_desktop_set,
            export_weekly,
            app_search
        ])
        .on_page_load(|webview, payload| {
            let stage = match payload.event() {
                tauri::webview::PageLoadEvent::Started => "页面开始加载",
                tauri::webview::PageLoadEvent::Finished => "页面加载完成",
            };
            log_line(&format!("{stage} [{}]", webview.label()));
        })
        .setup(move |app| {
            // 后台线程（备份/热键）发通知要用的全局句柄
            let _ = APP_HANDLE.set(app.handle().clone());
            // 采集宿主随壳启动：单写者约束在 Host 内部（WPF 在跑就拒绝）
            app.manage(HostState(std::sync::Mutex::new(None)));

            // 单实例端口的服务端：第二个进程点快捷方式时发 "SHOW"，这里把窗口亮出来
            {
                use std::io::{Read, Write};
                let h = app.handle().clone();
                std::thread::spawn(move || {
                    for stream in instance_lock.incoming() {
                        let Ok(mut s) = stream else { continue };
                        let mut buf = Vec::new();
                        let _ = s.set_read_timeout(Some(std::time::Duration::from_millis(500)));
                        let _ = s.read_to_end(&mut buf);
                        let cmd = String::from_utf8_lossy(&buf).trim().to_uppercase();
                        if cmd.starts_with("SHOW") {
                            let _ = s.write_all(b"OK\n");
                            let h2 = h.clone();
                            let _ = h.run_on_main_thread(move || {
                                match h2.get_webview_window("main") {
                                    Some(w) => {
                                        let _ = w.show();
                                        let _ = w.unminimize();
                                        let _ = w.set_focus();
                                        log_line("[壳] 第二实例请求：已有窗口已亮出来");
                                    }
                                    None => match make_window(&h2, &inject_script(), "app.html") {
                                        Ok(_) => log_line("[壳] 第二实例请求：已新开窗口"),
                                        Err(e) => log_line(&format!("[壳] 第二实例开窗失败：{e}")),
                                    },
                                }
                            });
                        } else if cmd.starts_with("TASKBAR_OFF") || cmd.starts_with("TASKBAR_ON") {
                            let _ = s.write_all(b"OK\n");
                            let on = cmd.starts_with("TASKBAR_ON");
                            let h2 = h.clone();
                            let _ = h.run_on_main_thread(move || {
                                if on {
                                    let light = taskbar::system_uses_light_theme();
                                    taskbar::start(h2.clone(), light);
                                    log_line("[壳] 控制指令：任务栏小窗已启动");
                                } else {
                                    taskbar::stop();
                                }
                            });
                        } else if !cmd.is_empty() {
                            let _ = s.write_all(b"OK\n");
                            log_line(&format!("[壳] 单实例通道收到：{cmd}"));
                        }
                    }
                });
            }

            // headless 不建窗口：只在后台跑采集宿主，托盘负责开窗与退出
            let window = if headless {
                log_line(&format!(
                    "[壳] headless 模式：不建窗口，{}",
                    if host_mode { "只跑采集宿主" } else { "但没有 --host，等于什么都不做" }
                ));
                match add_tray(app.handle().clone()) {
                    Ok(_) => log_line("[壳] 托盘已建立：打开仪表盘 / 退出采集"),
                    Err(e) => log_line(&format!("[壳] 托盘建立失败：{e}")),
                }

                // 自检（无 GUI 环境下点不到托盘菜单）：PCHABIT_OPEN_WIN=1 时 8 秒后
                // 走托盘「打开仪表盘」同一条建窗代码，用页加载日志证明它能画出来。
                if std::env::var("PCHABIT_OPEN_WIN").as_deref() == Ok("1") {
                    let h = app.handle().clone();
                    std::thread::spawn(move || {
                        std::thread::sleep(std::time::Duration::from_secs(8));
                        let h2 = h.clone();
                        let r = h.run_on_main_thread(move || {
                            match make_window(&h2, &inject_script(), "app.html") {
                                Ok(_) => log_line("[壳] 自检：headless 下窗口已打开"),
                                Err(e) => log_line(&format!("[壳] 自检开窗失败：{e}")),
                            }
                        });
                        if let Err(e) = r {
                            log_line(&format!("[壳] 自检调度失败：{e}"));
                        }
                    });
                }

                None
            } else {
                let url = if start_page.is_empty() {
                    "app.html".to_string()
                } else if extra.is_empty() {
                    format!("app.html?p={start_page}")
                } else {
                    format!("app.html?p={start_page}&{extra}")
                };
                if window_tray {
                    match add_tray(app.handle().clone()) {
                        Ok(_) => log_line("[壳] 托盘已建立（窗口模式）：关窗隐到托盘"),
                        Err(e) => log_line(&format!("[壳] 托盘建立失败：{e}")),
                    }
                }
                let w = make_window(app.handle(), &inject, &url)?;

                // 桌面便签：主窗口起来之后按库里的 IsOnDesktop 摆出来
                open_note_windows(app.handle());

                // 自检：PCHABIT_CLOSE_TEST=1 时 10 秒后自己发一次关窗，
                // 验证「关窗→隐到托盘」而不是退出（本机点不了 X 按钮）。
                if std::env::var("PCHABIT_CLOSE_TEST").as_deref() == Ok("1") {
                    let h = app.handle().clone();
                    std::thread::spawn(move || {
                        std::thread::sleep(std::time::Duration::from_secs(10));
                        let h2 = h.clone();
                        let _ = h.run_on_main_thread(move || {
                            if let Some(w) = h2.get_webview_window("main") {
                                log_line("[壳] 自检：发一次关窗请求");
                                let _ = w.close();
                            }
                        });
                    });
                }
                Some(w)
            };

            if host_mode {
                let db = pchabit_collect::default_db();
                let log: pchabit_collect::host::LogFn = std::sync::Arc::new(|m: String| {
                    log_line(&format!("[collect] {m}"));
                });
                let cfg = pchabit_collect::Config {
                    db: db.clone(),
                    hooks: true,
                    log,
                    run_for: None,
                    websync: true,
                    ws_port: 8765,
                };
                match pchabit_collect::Host::start(cfg) {
                    Ok(h) => {
                        log_line(&format!(
                            "[collect] 采集宿主已随壳启动，库 {}",
                            db.display()
                        ));
                        if let Some(state) = app.try_state::<HostState>() {
                            *state.0.lock().unwrap() = Some(h);
                        }
                    }
                    Err(e) => log_line(&format!("[collect] 采集宿主未启动：{e}")),
                }
            } else {
                log_line("[collect] 未带 --host，本次只做只读展示");
            }

            if let Some(window) = window {
                window.on_window_event({
                    let handle = app.handle().clone();
                    move |e| {
                        if let tauri::WindowEvent::CloseRequested { api, .. } = e {
                            // 「关闭窗口时最小化到托盘」关掉后，点× 就是真退出（连带采集宿主）
                            let to_tray = window_tray && setting_bool_default("MinimizeToTray", true);
                            if to_tray {
                                // 托盘在，关窗只是收起来：采集继续，托盘可再打开
                                api.prevent_close();
                                if let Some(w) = handle.get_webview_window("main") {
                                    let _ = w.hide();
                                    log_line("[壳] 关窗→隐到托盘（采集继续，托盘「退出」才真退）");
                                }
                            } else {
                                if let Some(state) = handle.try_state::<HostState>() {
                                    state.stop();
                                }
                                log_line("[壳] 关窗→按设置直接退出");
                                handle.exit(0);
                            }
                        }
                    }
                });
            }

            if with_taskbar {
                // 深浅跟 Windows 任务栏走，不跟应用内主题走
                let light = taskbar::system_uses_light_theme();
                taskbar::start(app.handle().clone(), light);
                log_line(&format!(
                    "[小窗] 启动 Rust 任务栏小窗（{} 主题）",
                    if light { "浅色" } else { "深色" }
                ));
            }

            if with_desktop {
                desktop_widget::start(app.handle().clone());
                log_line("[桌面] 启动 Rust 桌面硬件悬浮窗");
            }

            // 自动备份 / 云备份的低频线程（只起一次）
            start_background_services();

            // 全局热键：Ctrl+Alt+N 新建便签、Ctrl+Alt+B 打开主窗口
            {
                let h = app.handle().clone();
                let h2 = h.clone();
                hotkey::start(
                    move || match tray_new_note(&h) {
                        Ok(id) => log_line(&format!("[热键] 新建便签 {id}")),
                        Err(e) => log_line(&format!("[热键] 新建便签失败：{e}")),
                    },
                    move || tray_show(&h2, "热键"),
                );
            }

            Ok(())
        })
        .build(tauri::generate_context!())
        .expect("Tauri 构建失败");

    // headless 没有窗口，默认会因「最后一个窗口关闭」直接退出；这里挡掉，
    // 让它一直当后台采集宿主，直到进程被结束。
    let app = app;
    app.run(move |handle, event| {
        match event {
            tauri::RunEvent::ExitRequested { api, .. } => {
                // headless 没有窗口，默认会因「最后一个窗口关闭」退出；挡掉它
                if headless {
                    api.prevent_exit();
                }
            }
            tauri::RunEvent::Exit => {
                // 任何退出路径（托盘、任务管理器正常关、系统注销）都先让宿主收口
                if let Some(state) = handle.try_state::<HostState>() {
                    state.stop();
                }
                log_line("[壳] 退出：采集宿主已收口");
            }
            _ => {}
        }
    });
}
