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

fn log_line(msg: &str) {
    let base = std::env::var("TEMP").unwrap_or_else(|_| ".".to_string());
    let path = PathBuf::from(base).join("pchabit-shell.log");
    if let Ok(mut f) = std::fs::OpenOptions::new().create(true).append(true).open(path) {
        let _ = writeln!(f, "[{}] {msg}", pchabit_datad::today_local());
    }
}

/// 本地当前小时（UTC+8），用于把"还没到的时段"和"真的零活跃"区分开。
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

/// 每小时活动：`__ACT_DATA__ = { bars:[{label,value,level,future}], summary }`
fn payload_activity() -> serde_json::Value {
    let today = pchabit_datad::today_local();
    let report = match analytics_for(&today, &today) {
        Ok(v) => v,
        Err(e) => return serde_json::json!({ "bars": [], "summary": format!("数据不可用：{e}") }),
    };
    let now_h = current_hour();
    let bars: Vec<serde_json::Value> = report["hourHeat"]
        .as_array()
        .map(|arr| {
            arr.iter()
                .map(|h| {
                    let mins = h["minutes"].as_f64().unwrap_or(0.0);
                    let level = h["intensityLevel"].as_i64().unwrap_or(0);
                    let hour = h["hour"].as_i64().unwrap_or(0);
                    let grade = if mins <= 0.0 {
                        "low"
                    } else if level >= 3 {
                        "high"
                    } else if level >= 2 {
                        "mid"
                    } else {
                        "low"
                    };
                    serde_json::json!({
                        "label": h["label"].clone(),
                        "value": mins.round(),
                        "level": grade,
                        "future": hour > now_h,
                    })
                })
                .collect()
        })
        .unwrap_or_default();

    let total: f64 = bars.iter().filter_map(|b| b["value"].as_f64()).sum();
    let summary = if total <= 0.0 {
        "今日暂无活跃记录".to_string()
    } else {
        let h = (total / 60.0).floor() as i64;
        let m = (total % 60.0).round() as i64;
        let peak = bars.iter().filter_map(|b| b["value"].as_f64()).fold(0.0, f64::max);
        format!("今日累计 {h}小时{m}分 · 峰值 {peak:.0} 分/小时")
    };
    serde_json::json!({ "bars": bars, "summary": summary })
}

/// 饼图两种模式，对齐 C# `AppStatsViewModel.BuildPieChartData`：
/// - `byCategory`：**全部**分类，不截断（C# 就是把 CategoryShares 原样传过去）
/// - `byApp`：Top 8 应用 + 「其他」（C# 里叫 PieSlices）
///
/// 分钟数保留 1 位小数，与 C# 的 `Math.Round(minutes, 1)` 一致。
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

/// 前端要数据：一次同步查库 + 聚合，返回该页面需要的那份 JSON。
#[tauri::command]
fn page_data(page: String) -> Result<serde_json::Value, String> {
    let file = match page.as_str() {
        "hardware" => "hardware-trend.html",
        "activity" => "activity-trend.html",
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
        other => return Err(format!("未知页面：{other}")),
    };
    let asset = dist_dir().join("pages").join(file);
    if !asset.exists() {
        return Err(format!(
            "缺少页面资产：{}（构建时 build.rs 会从 WPF 工程复制）",
            asset.display()
        ));
    }

    let t0 = std::time::Instant::now();
    let data = match page.as_str() {
        "activity" => payload_activity(),
        "sankey" => payload_sankey(),
        "admin" => {
            let apps = pchabit_datad::app_categories_json(&pchabit_datad::default_db(), 7)
                .unwrap_or_else(|_| serde_json::json!({ "days": 7, "items": [] }));
            payload_admin(apps)
        }
        "settings" => serde_json::json!({ "settings": payload_settings() }),
        "notes" => payload_notes(),
        "web" => payload_web(),
        "timeline" => payload_timeline(&pchabit_datad::today_local(), 0),
        "dashboard" => payload_dashboard(),
        "net" => payload_net(),
        "input" => payload_input(),
        "reports" => payload_reports(),
        "data" => payload_data(),
        "detail" => serde_json::json!({
            "apps": pchabit_datad::app_categories_json(&pchabit_datad::default_db(), 7)
                .unwrap_or_else(|_| serde_json::json!({ "days": 7, "items": [] }))
        }),
        _ => {
            let report = payload_analytics();
            match page.as_str() {
                "hardware" => payload_hardware(&report),
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
        "hardware": payload_hardware(&report),
        "activity": payload_activity(),
        "category": payload_category(&report),
        "sankey": payload_sankey(),
        "analytics": report,
        "admin": payload_admin(apps.clone()),
        "detail": serde_json::json!({ "apps": apps }),
        "settings": payload_settings(),
        "notes": payload_notes(),
        "web": payload_web(),
        "timeline": payload_timeline(&pchabit_datad::today_local(), 0),
        "dashboard": payload_dashboard(),
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

/// 单应用详情：近 7 天 vs 前 7 天，含会话聚合、键鼠、硬件归属、标题 Top5、日趋势。
#[tauri::command]
fn app_detail(process: String, app_name: String, category: String) -> Result<serde_json::Value, String> {
    let today = pchabit_datad::today_local();
    let cur_start = pchabit_datad::date_shift(&today, -6);
    let prev_end = pchabit_datad::date_shift(&today, -7);
    let prev_start = pchabit_datad::date_shift(&today, -13);
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
    let today = pchabit_datad::today_local();
    let start = pchabit_datad::date_shift(&today, -6);
    match pchabit_datad::web_json(&pchabit_datad::default_db(), &start, &today) {
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

/// 仪表盘（只读）：今日 KPI + 24 小时活跃 + Top 应用 + 7 天趋势。
fn payload_dashboard() -> serde_json::Value {
    match pchabit_datad::dashboard_json(&pchabit_datad::default_db()) {
        Ok(v) => {
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
            serde_json::json!({ "ok": false, "error": e, "topApps": [], "hourly": [], "trend": [] })
        }
    }
}

/// 网络流量（只读）：近 7 天按进程聚合 + 24 小时下行 + 日趋势。
fn payload_net() -> serde_json::Value {
    let today = pchabit_datad::today_local();
    let start = pchabit_datad::date_shift(&today, -6);
    match pchabit_datad::net_json(&pchabit_datad::default_db(), &start, &today) {
        Ok(v) => {
            log_line(&format!(
                "net: {start}~{today} / 下 {} / 上 {} / {} 进程 / {:.1} ms",
                v["totalDownText"].as_str().unwrap_or(""),
                v["totalUpText"].as_str().unwrap_or(""),
                v["processes"].as_array().map(|a| a.len()).unwrap_or(0),
                v["ms"].as_f64().unwrap_or(0.0)
            ));
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

/// 键鼠活动（只读）：近 7 天按键 / 点击 / 滚动 / 分布 / 排行。
fn payload_input() -> serde_json::Value {
    let today = pchabit_datad::today_local();
    let start = pchabit_datad::date_shift(&today, -6);
    match pchabit_datad::input_json(&pchabit_datad::default_db(), &start, &today) {
        Ok(v) => {
            log_line(&format!(
                "input: {start}~{today} / {} 键 / {} 击 / {} 滚 / {:.1} ms",
                v["keys"].as_i64().unwrap_or(0),
                v["clicks"].as_i64().unwrap_or(0),
                v["scroll"].as_i64().unwrap_or(0),
                v["ms"].as_f64().unwrap_or(0.0)
            ));
            v
        }
        Err(e) => {
            log_line(&format!("input: 读取失败 {e}"));
            serde_json::json!({
                "ok": false, "error": e, "topKeys": [], "topClicks": [],
                "hourlyKeys": [], "hourlyClicks": []
            })
        }
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

/// 设置概览（只读）：读 `%LOCALAPPDATA%\PChabit\settings.json`，敏感项打码。
///
/// 设置写成两份会互相覆盖，所以壳只显示不改；真正的写入仍归 WPF 版。
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
                    if is_secret {
                        serde_json::json!({
                            "key": k,
                            "kind": "secret",
                            "masked": true,
                            "set": set,
                        })
                    } else {
                        let kind = match val {
                            serde_json::Value::Bool(_) => "bool",
                            serde_json::Value::Number(_) => "number",
                            _ => "string",
                        };
                        serde_json::json!({ "key": k, "kind": kind, "value": val })
                    }
                })
                .collect()
        })
        .unwrap_or_default();

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
const VERSION: &str = "4.3.0";

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

fn main() {
    // 命令行：pchabit-shell.exe [页面] [额外 query，如 auto=1] [--host]
    let raw_args: Vec<String> = std::env::args().skip(1).collect();
    let host_mode = raw_args.iter().any(|a| a == "--host");
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
    let inject = format!(
        "window.__PAGES__ = {pages};window.__VER__ = '{VERSION}';"
    );

    tauri::Builder::default()
        .invoke_handler(tauri::generate_handler![
            page_data,
            pages_data,
            note,
            set_category,
            create_category,
            set_category_active,
            app_detail,
            categories,
            timeline_day
        ])
        .on_page_load(|webview, payload| {
            let stage = match payload.event() {
                tauri::webview::PageLoadEvent::Started => "页面开始加载",
                tauri::webview::PageLoadEvent::Finished => "页面加载完成",
            };
            log_line(&format!("{stage} [{}]", webview.label()));
        })
        .setup(move |app| {
            let url = if start_page.is_empty() {
                "app.html".to_string()
            } else if extra.is_empty() {
                format!("app.html?p={start_page}")
            } else {
                format!("app.html?p={start_page}&{extra}")
            };

            let window =
                tauri::WebviewWindowBuilder::new(app, "main", tauri::WebviewUrl::App(url.into()))
                    .title("PChabit（P5 采集宿主）")
                    .inner_size(1180.0, 720.0)
                    .min_inner_size(900.0, 560.0)
                    .center()
                    .initialization_script(&inject)
                    .build()?;

            // 采集宿主随壳启动：单写者约束在 Host 内部（WPF 在跑就拒绝）
            app.manage(HostState(std::sync::Mutex::new(None)));
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

            window.on_window_event({
                let handle = app.handle().clone();
                move |e| {
                    if let tauri::WindowEvent::CloseRequested { .. } = e {
                        if let Some(state) = handle.try_state::<HostState>() {
                            state.stop();
                        }
                    }
                }
            });

            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("Tauri 启动失败");
}
