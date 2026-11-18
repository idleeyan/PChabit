//! P4-0：Tauri 2 骨架可行性探针。
//!
//! 目标不是做界面，而是先回答一个问题：这台机器能否拉取并编译 Tauri 2。
//! 窗口里直接加载现有的 ECharts 页面（`Assets/Sankey/sankey.html`），
//! 用最小代价证明「现有 HTML 资产可以原样搬进新壳」。

#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

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

fn main() {
    let assets = assets_dir();

    tauri::Builder::default()
        .setup(move |app| {
            let entry = assets.join("Sankey").join("sankey.html");
            let url = if entry.exists() {
                let path = entry.to_string_lossy().replace('\\', "/");
                tauri::WebviewUrl::External(format!("file:///{path}").parse().unwrap())
            } else {
                tauri::WebviewUrl::App("index.html".into())
            };

            tauri::WebviewWindowBuilder::new(app, "main", url)
                .title("PChabit Shell (P4-0 探针)")
                .inner_size(1200.0, 800.0)
                .center()
                .visible(true)
                .build()?;

            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("Tauri 启动失败");
}
