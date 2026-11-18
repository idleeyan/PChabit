//! PChabit 数据面探针（P3 可行性评估）。
//!
//! 目的：回答两个问题
//!   1) Rust 以只读方式打开宿主正在写的 WAL 库，能不能稳定读到同一份数据？
//!   2) 同样的聚合口径，Rust 侧耗时是多少（对照读取行数，判断值不值得迁）？
//!
//! 不接产品路径，只做度量；`pchabit-netd` 保持零 crate 依赖，发布流程不受影响。
//!
//! 用法：pchabit-dbcore [--db <path>] [--date YYYY-MM-DD]

use std::path::{Path, PathBuf};
use std::time::Instant;

use rusqlite::{Connection, OpenFlags};

fn main() {
    let mut db_path: Option<PathBuf> = None;
    let mut date = String::new();
    let mut args = std::env::args().skip(1);
    while let Some(a) = args.next() {
        match a.as_str() {
            "--db" => db_path = args.next().map(PathBuf::from),
            "--date" => date = args.next().unwrap_or_default(),
            _ => {}
        }
    }

    let db = db_path.unwrap_or_else(default_db_path);
    if !db.exists() {
        eprintln!("数据库不存在：{}", db.display());
        std::process::exit(2);
    }

    println!("库文件：{}", db.display());

    let conn = match open_readonly(&db) {
        Ok(c) => c,
        Err(e) => {
            eprintln!("打开失败：{e}");
            std::process::exit(3);
        }
    };

    let (start, end) = day_bounds(&date);
    println!("统计区间：{start} ~ {end}");

    // ---- 1. 当日应用会话聚合 ----
    let t0 = Instant::now();
    let mut stmt = conn
        .prepare(
            "SELECT ProcessName, COUNT(*), SUM(Duration) \
             FROM AppSessions WHERE StartTime >= ?1 AND StartTime < ?2 \
             GROUP BY ProcessName ORDER BY SUM(Duration) DESC",
        )
        .expect("prepare app sessions");

    let rows: Vec<(String, i64, i64)> = stmt
        .query_map([&start, &end], |r| {
            Ok((r.get::<_, String>(0)?, r.get::<_, i64>(1)?, r.get::<_, i64>(2)?))
        })
        .expect("query app sessions")
        .filter_map(|r| r.ok())
        .collect();
    let app_ms = t0.elapsed().as_secs_f64() * 1000.0;

    let total_ticks: i64 = rows.iter().map(|r| r.2).sum();
    println!(
        "\n[当日应用会话] {} 个进程 / 合计 {:.1} 分钟，读取+聚合 {:.2} ms",
        rows.len(),
        total_ticks as f64 / 10_000_000.0 / 60.0,
        app_ms
    );
    for (name, count, ticks) in rows.iter().take(10) {
        println!(
            "  {:<30} {:>5} 段  {:>8.1} 分钟",
            name,
            count,
            *ticks as f64 / 10_000_000.0 / 60.0
        );
    }

    // ---- 2. 最近硬件样本（对照宿主落库口径）----
    let t1 = Instant::now();
    let mut stmt = conn
        .prepare(
            "SELECT Timestamp, CpuLoadAvg, GpuLoadAvg, GpuTempMax, VramUsedMax, VramTotal, SampleCount \
             FROM HardwareSamples ORDER BY Timestamp DESC LIMIT 5",
        )
        .expect("prepare hardware samples");

    let hw: Vec<(String, f64, f64, f64, f64, f64, i64)> = stmt
        .query_map([], |r| {
            Ok((
                r.get::<_, String>(0)?,
                r.get::<_, f64>(1)?,
                r.get::<_, f64>(2)?,
                r.get::<_, f64>(3)?,
                r.get::<_, f64>(4)?,
                r.get::<_, f64>(5)?,
                r.get::<_, i64>(6)?,
            ))
        })
        .expect("query hardware")
        .filter_map(|r| r.ok())
        .collect();
    let hw_ms = t1.elapsed().as_secs_f64() * 1000.0;
    println!("\n[最近硬件样本] {} 行，查询 {:.2} ms", hw.len(), hw_ms);
    for (ts, cpu, gpu, temp, vused, vtotal, n) in &hw {
        println!(
            "  {ts}  CPU {cpu:>5.1}%  GPU {gpu:>5.1}%  {temp:>4.0}°C  显存 {vused:>6.0}/{vtotal:.0}MB  样本 {n}"
        );
    }

    // ---- 3. 库规模 ----
    let counts: Vec<(&str, i64)> = ["AppSessions", "HardwareSamples", "KeyboardSessions"]
        .into_iter()
        .filter_map(|t| {
            conn.query_row(&format!("SELECT COUNT(*) FROM {t}"), [], |r| r.get::<_, i64>(0))
                .ok()
                .map(|n| (t, n))
        })
        .collect();
    print!("\n[库规模] ");
    for (t, n) in counts {
        print!("{t}={n} ");
    }
    println!();
}

/// 只读打开：优先直接 READ_ONLY（WAL 需要 -shm 存在）；
/// 失败则把 db/-wal/-shm 复制到临时目录再读，避免影响宿主写入。
fn open_readonly(path: &Path) -> rusqlite::Result<Connection> {
    let direct = Connection::open_with_flags(path, OpenFlags::SQLITE_OPEN_READ_ONLY);
    if direct.is_ok() {
        return direct;
    }

    let tmp_dir = std::env::temp_dir().join("pchabit-dbcore-snapshot");
    let _ = std::fs::create_dir_all(&tmp_dir);
    let copy = tmp_dir.join("pchabit.db");
    let _ = std::fs::copy(path, &copy);
    for suffix in ["-wal", "-shm"] {
        let src = PathBuf::from(format!("{}{}", path.display(), suffix));
        let dst = PathBuf::from(format!("{}{}", copy.display(), suffix));
        if src.exists() {
            let _ = std::fs::copy(&src, &dst);
        }
    }
    Connection::open_with_flags(&copy, OpenFlags::SQLITE_OPEN_READ_ONLY)
}

fn default_db_path() -> PathBuf {
    let base = std::env::var("LOCALAPPDATA").unwrap_or_default();
    PathBuf::from(base).join("PChabit").join("Data").join("pchabit.db")
}

/// 与宿主一致：按本地日期切当日区间。
fn day_bounds(date: &str) -> (String, String) {
    let day = if date.is_empty() {
        // 由 SQLite 取本地当前日期，避免自己实现时区换算
        let now = std::time::SystemTime::now();
        let secs = now
            .duration_since(std::time::UNIX_EPOCH)
            .map(|d| d.as_secs())
            .unwrap_or(0);
        // 东八区
        let local = secs + 8 * 3600;
        let days = local / 86_400;
        civil_from_days(days as i64)
    } else {
        date.to_string()
    };

    (
        format!("{day} 00:00:00"),
        format!("{day} 23:59:59.9999999"),
    )
}

/// days since 1970-01-01 → "YYYY-MM-DD"（Howard Hinnant 的 civil_from_days）。
fn civil_from_days(z: i64) -> String {
    let z = z + 719_468;
    let era = if z >= 0 { z } else { z - 146_096 } / 146_097;
    let doe = z - era * 146_097;
    let yoe = (doe - doe / 1460 + doe / 36_524 - doe / 146_096) / 365;
    let y = yoe + era * 400;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let d = doy - (153 * mp + 2) / 5 + 1;
    let m = if mp < 10 { mp + 3 } else { mp - 9 };
    let year = if m <= 2 { y + 1 } else { y };
    format!("{year:04}-{m:02}-{d:02}")
}
