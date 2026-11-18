//! P4-1：Tauri 壳的第一个真实页面（硬件趋势）。
//!
//! 数据不再走 IPC 子进程，而是**直接在壳进程内 `use pchabit_datad`**（把 datad 拆成
//! lib + bin 的意义就在这里）：启动时算好 JSON，通过 `initialization_script` 注入成
//! `window.__HW_DATA__`，页面自己的 `DOMContentLoaded` 会画图——和 WPF 里
//! `NavigateToString` + 注入的机制一致，页面资产一行不改。
//!
//! 渲染验证：本环境截屏桌面与进程桌面不一致，所以不靠截图，而是
//! ① 壳把载荷摘要写日志；② 用 `on_page_load` 回调记录 webview 的 Started/Finished 事件。

#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use std::io::Write;
use std::path::PathBuf;

/// 找现有 WPF 工程的 Assets 目录：优先发布目录，其次仓库路径。
fn assets_dir() -> PathBuf {
    let candidates = [
        PathBuf::from(r"D:\Tool\PChabit\Assets"),
        PathBuf::from(r"E:\SYNC\My VS\PChabit\src\PChabit.App\Assets"),
        PathBuf::from(r"E:\SYNC\My VS\PChabit\publish\Assets"),
    ];
    for c in candidates {
        if c.exists() {
            return c;
        }
    }
    PathBuf::from("Assets")
}

fn log_path() -> PathBuf {
    let base = std::env::var("TEMP").unwrap_or_else(|_| ".".to_string());
    PathBuf::from(base).join("pchabit-shell.log")
}

fn log_line(msg: &str) {
    let stamp = pchabit_datad::today_local();
    if let Ok(mut f) = std::fs::OpenOptions::new()
        .create(true)
        .append(true)
        .open(log_path())
    {
        let _ = writeln!(f, "[{stamp}] {msg}");
    }
}

/// 近 7 天硬件趋势载荷：形状对齐现有 `hardware-trend.html` 的 `__HW_DATA__`。
fn hardware_payload() -> serde_json::Value {
    let db = pchabit_datad::default_db();
    let today = pchabit_datad::today_local();
    let start_day = pchabit_datad::date_shift(&today, -6);
    let req = serde_json::json!({
        "curStart": format!("{start_day} 00:00:00.0000000"),
        "curEnd": format!("{today} 23:59:59.9999999"),
        "prevStart": format!("{start_day} 00:00:00.0000000"),
        "prevEnd": format!("{today} 23:59:59.9999999"),
        "label": "近 7 天",
        "prevLabel": "近 7 天",
    });

    let report = match pchabit_datad::analytics_json(&db, &req) {
        Ok(v) => v,
        Err(e) => {
            log_line(&format!("载荷计算失败：{e}"));
            return serde_json::json!({ "hours": [], "summary": format!("数据不可用：{e}") });
        }
    };

    let hw = report.get("hardware").cloned().unwrap_or(serde_json::Value::Null);
    let hours = hw
        .get("hours")
        .and_then(|h| h.as_array())
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
                .collect::<Vec<_>>()
        })
        .unwrap_or_default();

    let days = hw["sampleDays"].as_i64().unwrap_or(0);
    let minutes = hw["sampleMinutes"].as_i64().unwrap_or(0);
    let cpu = hw["cpuLoadAvg"].as_f64();
    let cpu95 = hw["cpuLoadP95"].as_f64();
    let gpu = hw["gpuLoadAvg"].as_f64();
    let temp = hw["gpuTempMax"].as_f64();

    let mut summary = format!("样本 {days} 天 · {minutes} 个分钟样本");
    if let Some(v) = cpu {
        summary.push_str(&format!("　CPU 均 {v:.1}%"));
    }
    if let Some(v) = cpu95 {
        summary.push_str(&format!(" / P95 {v:.1}%"));
    }
    if let Some(v) = gpu {
        summary.push_str(&format!("　GPU 均 {v:.1}%"));
    }
    if let Some(v) = temp {
        summary.push_str(&format!("　峰温 {v:.0}°C"));
    }
    if minutes == 0 {
        summary = "暂无硬件样本".to_string();
    }

    log_line(&format!(
        "载荷就绪：小时桶 {} 个 / 样本 {} 分钟 / CPU {:?}",
        hours.len(),
        minutes,
        cpu
    ));

    serde_json::json!({ "hours": hours, "summary": summary })
}

fn main() {
    let assets = assets_dir();
    let payload = hardware_payload();
    let injection = format!(
        "window.__HW_DATA__ = {};",
        serde_json::to_string(&payload).unwrap_or_else(|_| "null".to_string())
    );

    tauri::Builder::default()
        .on_page_load(|_webview, payload| {
            let stage = match payload.event() {
                tauri::webview::PageLoadEvent::Started => "页面开始加载",
                tauri::webview::PageLoadEvent::Finished => "页面加载完成",
            };
            log_line(stage);
        })
        .setup(move |app| {
            let entry = assets.join("Analytics").join("hardware-trend.html");
            let url = if entry.exists() {
                let path = entry.to_string_lossy().replace('\\', "/");
                tauri::WebviewUrl::External(format!("file:///{path}").parse().unwrap())
            } else {
                log_line("找不到 hardware-trend.html，回退占位页");
                tauri::WebviewUrl::App("index.html".into())
            };

            tauri::WebviewWindowBuilder::new(app, "main", url)
                .title("PChabit · 硬件趋势（P4-1）")
                .inner_size(1000.0, 560.0)
                .center()
                .visible(true)
                .initialization_script(&injection)
                .build()?;

            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("Tauri 启动失败");
}
