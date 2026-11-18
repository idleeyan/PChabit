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

/// 分类构成（饼图吃 `renderPieChart([{name,value,color}])`）
fn payload_category() -> serde_json::Value {
    let today = pchabit_datad::today_local();
    let start = pchabit_datad::date_shift(&today, -6);
    let report = match analytics_for(&start, &today) {
        Ok(v) => v,
        Err(e) => return serde_json::json!([]),
    };
    let colors =
        pchabit_datad::category_colors(&pchabit_datad::default_db()).unwrap_or_default();
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
    let total: f64 = items.iter().filter_map(|i| i["value"].as_f64()).sum();
    log_line(&format!(
        "category: {} 个分类 / 合计 {:.0} 分钟",
        items.len(),
        total
    ));
    serde_json::Value::Array(items)
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
        "hardware" => payload_hardware(),
        "activity" => payload_activity(),
        "category" => payload_category(),
        _ => payload_sankey(),
    };
    log_line(&format!(
        "page_data {page}: {:.1} ms",
        t0.elapsed().as_secs_f64() * 1000.0
    ));
    Ok(data)
}

fn main() {
    let start_page = std::env::args().nth(1).unwrap_or_default();

    // 四页数据在窗口创建时一次算好，注入 window.__PAGES__：
    // 渲染阶段不再走 IPC，避免跨 frame + 异步回调那堆坑。
    let t0 = std::time::Instant::now();
    let pages = serde_json::json!({
        "hardware": payload_hardware(),
        "activity": payload_activity(),
        "category": payload_category(),
        "sankey": payload_sankey(),
    });
    let inject = format!("window.__PAGES__ = {pages};");
    log_line(&format!(
        "四页载荷就绪：{:.1} ms",
        t0.elapsed().as_secs_f64() * 1000.0
    ));

    tauri::Builder::default()
        .invoke_handler(tauri::generate_handler![page_data, note])
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
            } else {
                format!("app.html?p={start_page}")
            };

            tauri::WebviewWindowBuilder::new(app, "main", tauri::WebviewUrl::App(url.into()))
                .title("PChabit（P4-1 壳）")
                .inner_size(1180.0, 720.0)
                .min_inner_size(900.0, 560.0)
                .center()
                .initialization_script(&inject)
                .build()?;

            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("Tauri 启动失败");
}
