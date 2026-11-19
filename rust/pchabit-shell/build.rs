//! 构建前把现有图表资产**原样复制**到 `dist/pages`。
//!
//! 为什么要复制：页面必须从 Tauri 的 asset 协议（`tauri://localhost`）加载，
//! 才能用 `invoke` 回写渲染结果；`file://` 外部源拿不到 IPC。
//! 复制的是仓库里的原始文件，内容一个字节都不改。

use std::fs;
use std::path::{Path, PathBuf};

fn main() {
    let crate_root = PathBuf::from(env!("CARGO_MANIFEST_DIR"));
    let assets = [
        crate_root.join("assets"),
        crate_root.join(r"..\..\publish\Assets"),
        PathBuf::from(r"D:\Tool\PChabit\Assets"),
    ];
    let src = assets
        .iter()
        .find(|p| p.exists())
        .cloned()
        .unwrap_or_else(|| assets[0].clone());

    let out = crate_root.join("dist").join("pages");
    if let Err(e) = fs::create_dir_all(&out) {
        println!("cargo:warning=创建 dist/pages 失败：{e}");
    }

    let files = [
        (r"Analytics\hardware-trend.html", "hardware-trend.html"),
        (r"PieChart\piechart.html", "piechart.html"),
        (r"PieChart\echarts.min.js", "echarts.min.js"),
        (r"Sankey\sankey.html", "sankey.html"),
    ];

    for (from, to) in files {
        let src_file = src.join(from);
        let dst_file = out.join(to);
        println!("cargo:rerun-if-changed={}", src_file.display());
        match fs::copy(&src_file, &dst_file) {
            Ok(_) => {}
            Err(e) => println!("cargo:warning=复制 {from} 失败：{e}"),
        }
    }

    tauri_build::build();

}

#[allow(dead_code)]
fn touch(_p: &Path) {}
