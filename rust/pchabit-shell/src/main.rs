//! P4-1：Tauri 壳 —— 只读页逐页迁移。
//!
//! 三条硬约束（都来自 P4 盘点的结论）：
//! 1. **图表资产一行不改**：四个页面本来就是 HTML，壳只负责把数据喂进去；
//! 2. **数据不绕 IPC**：壳直接 `use pchabit_datad`（datad 的 lib），在进程内查库；
//! 3. **验证靠日志不靠截屏**：本环境截屏桌面与进程桌面不一致，所以每次注入都写日志。

#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use std::io::Write;
use std::path::PathBuf;
use std::sync::atomic::{AtomicU32, Ordering};

use tauri::Manager;

static SEQ: AtomicU32 = AtomicU32::new(0);

/// 找壳自己的 `dist` 目录（页面资产由 build.rs 从 WPF 工程原样复制进来）。
fn dist_dir() -> PathBuf {
    let mut bases: Vec<PathBuf> = Vec::new();
    if let Ok(exe) = std::env::current_exe() {
        if let Some(dir) = exe.parent() {
            bases.push(dir.to_path_buf()); // target/debug
            if let Some(up2) = dir.parent().and_then(|p| p.parent()) {
                bases.push(up2.to_path_buf()); // crate 根
            }
        }
    }
    bases.push(PathBuf::from("."));

    for b in &bases {
        let cand = b.join("dist").join("pages");
        if cand.exists() {
            return b.join("dist");
        }
    }
    PathBuf::from("dist")
}

fn log_path() -> PathBuf {
    let base = std::env::var("TEMP").unwrap_or_else(|_| ".".to_string());
    PathBuf::from(base).join("pchabit-shell.log")
}

fn log_line(msg: &str) {
    if let Ok(mut f) = std::fs::OpenOptions::new()
        .create(true)
        .append(true)
        .open(log_path())
    {
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
    let db = pchabit_datad::default_db();
    let req = serde_json::json!({
        "curStart": format!("{start} 00:00:00.0000000"),
        "curEnd": format!("{end} 23:59:59.9999999"),
        "prevStart": format!("{start} 00:00:00.0000000"),
        "prevEnd": format!("{end} 23:59:59.9999999"),
        "label": "区间",
        "prevLabel": "区间",
    });
    pchabit_datad::analytics_json(&db, &req)
}

/// 硬件趋势：`__HW_DATA__ = { hours:[{label,cpu,gpu,temp,n}], summary }`
fn payload_hardware() -> serde_json::Value {
    let today = pchabit_datad::today_local();
    let start = pchabit_datad::date_shift(&today, -6);
    let report = match analytics_for(&start, &today) {
        Ok(v) => v,
        Err(e) => return serde_json::json!({ "hours": [], "summary": format!("数据不可用：{e}") }),
    };
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

    log_line(&format!("hardware: {} 小时桶 / {} 分钟样本", hours.len(), minutes));
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
                        // 没到的小时给灰底占位，与"真的是 0"区分开
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
        format!("今日累计 {h}小时{m}分 · 峰值 {:.0} 分/小时", bars.iter().filter_map(|b| b["value"].as_f64()).fold(0.0, f64::max))
    };

    log_line(&format!("activity: 24 桶 / 合计 {total:.0} 分钟"));
    serde_json::json!({ "bars": bars, "summary": summary })
}

/// 分类构成（饼图页面吃 `renderPieChart([{name,value,color}])`）。
fn payload_category() -> Vec<serde_json::Value> {
    let today = pchabit_datad::today_local();
    let start = pchabit_datad::date_shift(&today, -6);
    let report = match analytics_for(&start, &today) {
        Ok(v) => v,
        Err(e) => {
            log_line(&format!("category: 计算失败 {e}"));
            return Vec::new();
        }
    };
    let colors = pchabit_datad::category_colors(&pchabit_datad::default_db()).unwrap_or_default();
    let items: Vec<serde_json::Value> = report["categoryShares"]
        .as_array()
        .map(|arr| {
            arr.iter()
                .map(|c| {
                    let name = c["category"].as_str().unwrap_or("未分类").to_string();
                    let color = colors.get(&name).cloned().unwrap_or_default();
                    serde_json::json!({
                        "name": name,
                        "value": c["minutes"].as_f64().unwrap_or(0.0).round(),
                        "color": color,
                    })
                })
                .collect()
        })
        .unwrap_or_default();
    log_line(&format!("category: {} 个分类", items.len()));
    items
}

/// 页面定义：`dist/pages` 下的资产文件名 + 注入的 JS（数据落到页面约定的全局变量）。
fn build_page(page: &str) -> Result<(String, String, String), String> {
    let (asset, title, script) = match page {
        "hardware" => (
            "hardware-trend.html",
            "硬件趋势",
            format!(
                "(function(d){{ window.__HW_DATA__ = d; \
                  document.addEventListener('DOMContentLoaded', function(){{ \
                    try {{ if (window.draw) draw(d); \
                      __TAURI__.core.invoke('note', {{msg: 'hardware 绘制 hours=' + (d.hours||[]).length}}); \
                    }} catch (e) {{ __TAURI__.core.invoke('note', {{msg: 'hardware 报错 ' + e.message}}); }} \
                  }}); }})({});",
                serde_json::to_string(&payload_hardware()).unwrap_or_else(|_| "null".into())
            ),
        ),
        "activity" => (
            "activity-trend.html",
            "每小时活动",
            format!(
                "(function(d){{ window.__ACT_DATA__ = d; \
                  document.addEventListener('DOMContentLoaded', function(){{ \
                    try {{ if (window.draw) draw(d); \
                      __TAURI__.core.invoke('note', {{msg: 'activity 绘制 bars=' + (d.bars||[]).length}}); \
                    }} catch (e) {{ __TAURI__.core.invoke('note', {{msg: 'activity 报错 ' + e.message}}); }} \
                  }}); }})({});",
                serde_json::to_string(&payload_activity()).unwrap_or_else(|_| "null".into())
            ),
        ),
        "category" => (
            "piechart.html",
            "分类构成",
            format!(
                "(function(d){{ document.addEventListener('DOMContentLoaded', function(){{ \
                    setTimeout(function(){{ try {{ var r = renderPieChart(d); \
                      __TAURI__.core.invoke('note', {{msg: 'category ' + r}}); \
                    }} catch (e) {{ __TAURI__.core.invoke('note', {{msg: 'category 报错 ' + e.message}}); }} }}, 0); }}); }})({});",
                serde_json::to_string(&payload_category()).unwrap_or_else(|_| "[]".into())
            ),
        ),
        other => return Err(format!("未知页面：{other}")),
    };

    let asset_path = dist_dir().join("pages").join(asset);
    let rel = format!("pages/{}", asset);
    if !asset_path.exists() {
        return Err(format!("缺少页面资产：{}（请先构建，build.rs 会复制）", asset_path.display()));
    }
    Ok((rel, title.to_string(), script))
}

#[tauri::command]
fn note(msg: String) {
    log_line(&format!("[页面] {msg}"));
}

#[tauri::command]
fn open_page(app: tauri::AppHandle, page: String) -> Result<String, String> {
    let (rel, title, script) = build_page(&page)?;
    let label = format!("view-{}", SEQ.fetch_add(1, Ordering::SeqCst));

    tauri::WebviewWindowBuilder::new(&app, &label, tauri::WebviewUrl::App(rel.into()))
        .title(format!("PChabit · {title}"))
        .inner_size(1000.0, 620.0)
        .center()
        .visible(true)
        .initialization_script(&script)
        .build()
        .map_err(|e| e.to_string())?;

    log_line(&format!("已打开页面 {page} -> {label}"));
    Ok(format!("已打开：{title}"))
}

fn main() {
    tauri::Builder::default()
        .invoke_handler(tauri::generate_handler![open_page, note])
        .on_page_load(|webview, payload| {
            let stage = match payload.event() {
                tauri::webview::PageLoadEvent::Started => "页面开始加载",
                tauri::webview::PageLoadEvent::Finished => "页面加载完成",
            };
            log_line(&format!("{stage} [{}]", webview.label()));
        })
        .setup(|app| {
            tauri::WebviewWindowBuilder::new(app, "main", tauri::WebviewUrl::App("launcher.html".into()))
                .title("PChabit Shell（P4-1）")
                .inner_size(760.0, 520.0)
                .center()
                .build()?;

            // 命令行直开某页，方便不点鼠标就验收：`pchabit-shell.exe hardware`
            if let Some(page) = std::env::args().nth(1) {
                let handle = app.handle().clone();
                if let Err(e) = open_page(handle, page) {
                    log_line(&format!("命令行开页失败：{e}"));
                }
            }

            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("Tauri 启动失败");
}
