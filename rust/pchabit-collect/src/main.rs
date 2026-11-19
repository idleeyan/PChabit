//! 采集宿主的命令行入口：用于自测（跑 N 秒后退出并打印统计）。
//!
//! 用法：
//!   pchabit-collect --db <path> --seconds 30 [--no-hooks] [--no-ws] [--ws-port 8765]
//!   pchabit-collect --check                     # 只做环境检查（netd 可连、库可写、端口是否空闲）
//!   pchabit-collect --db <path> --daily 2026-11-18   # 重算某天日汇总（对账用）

use std::sync::Arc;
use std::time::Duration;

use pchabit_collect::{default_db, Config, Host};

fn main() {
    let mut args = std::env::args().skip(1);
    let mut db = default_db();
    let mut seconds: Option<u64> = None;
    let mut hooks = true;
    let mut websync = true;
    let mut ws_port: u16 = 8765;
    let mut check = false;
    let mut daily: Option<String> = None;
    let mut compare: Option<String> = None;

    while let Some(a) = args.next() {
        match a.as_str() {
            "--db" => {
                if let Some(v) = args.next() {
                    db = std::path::PathBuf::from(v);
                }
            }
            "--seconds" => {
                seconds = args.next().and_then(|v| v.parse().ok());
            }
            "--no-hooks" => hooks = false,
            "--no-ws" => websync = false,
            "--ws-port" => {
                if let Some(v) = args.next() {
                    ws_port = v.parse().unwrap_or(8765);
                }
            }
            "--check" => check = true,
            "--daily" => daily = args.next(),
            "--compare" => compare = args.next(),
            _ => {}
        }
    }

    let log = Arc::new(|msg: String| {
        println!("[{}] {msg}", pchabit_collect::timefmt::stamp7(&pchabit_collect::timefmt::now()));
    });

    // 对账：把库复制一份，在副本上重算该日汇总，再和生产库里已存的日汇总比字段。
    if let Some(date) = compare {
        let copy = std::env::temp_dir().join(format!(
            "pchabit_compare_{}.db",
            pchabit_collect::timefmt::stamp7(&pchabit_collect::timefmt::now()).replace([':', '.', ' '], "-")
        ));
        if let Err(e) = std::fs::copy(&db, &copy) {
            eprintln!("复制库失败：{e}");
            std::process::exit(1);
        }
        for suffix in ["-wal", "-shm"] {
            let src = std::path::PathBuf::from(format!("{}{suffix}", db.display()));
            let dst = std::path::PathBuf::from(format!("{}{suffix}", copy.display()));
            if src.exists() {
                let _ = std::fs::copy(&src, &dst);
            }
        }

        let now = pchabit_collect::timefmt::stamp7(&pchabit_collect::timefmt::now());
        let log_fn: pchabit_collect::host::LogFn = log.clone();
        let store_copy = match pchabit_collect::Store::open(&copy) {
            Ok(s) => s,
            Err(e) => {
                eprintln!("打开副本失败：{e}");
                std::process::exit(1);
            }
        };
        let recomputed = match pchabit_collect::daily::aggregate_day(&store_copy, &date, &now, &log_fn) {
            Ok(_) => store_copy.daily_existing(&date),
            Err(e) => {
                eprintln!("副本重算失败：{e}");
                std::process::exit(1);
            }
        };

        let store_prod = match pchabit_collect::Store::open(&db) {
            Ok(s) => s,
            Err(e) => {
                eprintln!("打开生产库失败：{e}");
                std::process::exit(1);
            }
        };
        let stored = store_prod.daily_existing(&date);

        println!("对账 {date}：");
        match (stored, recomputed) {
            (Some(a), Some(b)) => {
                let d_keys = b.total_keys - a.total_keys;
                let d_clicks = b.clicks - a.clicks;
                let d_active = b.active_minutes - a.active_minutes;
                println!(
                    "  键数      已存 {:>8} / 重算 {:>8} / 差 {:>+8}",
                    a.total_keys, b.total_keys, d_keys
                );
                println!(
                    "  点击数    已存 {:>8} / 重算 {:>8} / 差 {:>+8}",
                    a.clicks, b.clicks, d_clicks
                );
                println!(
                    "  活跃分钟  已存 {:>8.1} / 重算 {:>8.1} / 差 {:+8.1}",
                    a.active_minutes, b.active_minutes, d_active
                );
                let oh = a.top_apps.matches("\"process\"").count();
                let rh = b.top_apps.matches("\"process\"").count();
                println!("  应用条目  已存 {:>8} / 重算 {:>8}", oh, rh);
                let same = d_keys == 0 && d_clicks == 0 && d_active.abs() < 0.05 && oh == rh;
                println!("  结论：{}", if same { "一致" } else { "有差异（原始表与日汇总不同步，可能当天还没汇总）" });
            }
            (None, Some(_)) => println!("  生产库没有这天的日汇总行（重算已生成）"),
            (Some(_), None) => println!("  副本重算没有产出日汇总行"),
            (None, None) => println!("  两边都没有这天的日汇总行"),
        }
        let _ = std::fs::remove_file(&copy);
        for suffix in ["-wal", "-shm"] {
            let _ = std::fs::remove_file(format!("{}{suffix}", copy.display()));
        }
        return;
    }

    if let Some(date) = daily {
        let store = match pchabit_collect::Store::open(&db) {
            Ok(s) => s,
            Err(e) => {
                eprintln!("打开库失败：{e}");
                std::process::exit(1);
            }
        };
        let now = pchabit_collect::timefmt::stamp7(&pchabit_collect::timefmt::now());
        let log_fn: pchabit_collect::host::LogFn = log.clone();
        match pchabit_collect::daily::aggregate_day(&store, &date, &now, &log_fn) {
            Ok((k, c, n)) => println!("日汇总完成：{date} 键 {k} / 点击 {c} / 应用 {n}"),
            Err(e) => {
                eprintln!("日汇总失败：{e}");
                std::process::exit(1);
            }
        }
        return;
    }

    if check {
        println!("库：{}", db.display());
        println!("库存在：{}", db.exists());
        match pchabit_collect::netd::find_netd() {
            Some(p) => println!("netd：{}", p.display()),
            None => println!("netd：未找到"),
        }
        println!("PChabit.exe 在跑：{}", pchabit_collect::win::process_running("PChabit.exe"));
        println!(
            "扩展通道端口 {ws_port} 空闲：{}",
            std::net::TcpListener::bind(("127.0.0.1", ws_port)).is_ok()
        );
        return;
    }

    let cfg = Config {
        db,
        hooks,
        log: log.clone(),
        run_for: seconds,
        websync,
        ws_port,
    };

    let host = match Host::start(cfg) {
        Ok(h) => h,
        Err(e) => {
            eprintln!("启动失败：{e}");
            std::process::exit(1);
        }
    };

    match seconds {
        Some(s) => {
            // run_for 会让线程自己停；这里等到停止后再收尾
            while host.is_running() {
                std::thread::sleep(Duration::from_millis(500));
            }
            host.stop();
            println!("自测结束：跑了 {s} 秒");
        }
        None => {
            println!("常驻模式，Ctrl+C 退出");
            loop {
                std::thread::sleep(Duration::from_secs(1));
            }
        }
    }
}
