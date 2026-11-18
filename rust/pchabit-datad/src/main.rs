//! `pchabit-datad` 命令行：解析参数后交给库执行。
//!
//! 使用方式（stdin 一行 JSON → stdout 一行 JSON）：
//!   stats / detail / analytics / xlsx / sankey
//!   categories / appcats / setcat          ← 分类管理（setcat 会写库）
//! 探针：
//!   probe --db <path> --date <yyyy-MM-dd>

use std::path::PathBuf;

fn main() {
    let mut args = std::env::args().skip(1);
    let mode = args.next().unwrap_or_else(|| "probe".to_string());

    let mut db_path: Option<PathBuf> = None;
    let mut date = String::new();
    while let Some(a) = args.next() {
        match a.as_str() {
            "--db" => db_path = args.next().map(PathBuf::from),
            "--date" => date = args.next().unwrap_or_default(),
            _ => {}
        }
    }

    let db = db_path.unwrap_or_else(pchabit_datad::default_db);

    if let Err(e) = pchabit_datad::dispatch(&mode, &db, &date) {
        eprintln!("{e}");
        std::process::exit(3);
    }
}
