//! PChabit 数据面（P3 第一块）。
//!
//! 独立进程持有 `rusqlite`，只读打开宿主正在写的同一个 SQLite，把「应用统计」的
//! 查询 + 聚合整体搬到这里。`pchabit-netd` 保持零 crate 依赖，发布链路不受影响。
//!
//! 两个子命令：
//!   probe  —— 可行性探针（行数 / 耗时 / 口径抽样），人工看
//!   stats  —— 从 stdin 读一个 JSON 请求，向 stdout 写一行 JSON 报告
//!
//! stats 口径必须与 `AppStatsEngine.Compute` 逐条对齐：归一化、分类解析优先级、
//! 专注判定、环比文案、饼图 Top8 + 其他、小时分布。差异即 bug。

use std::collections::{HashMap, HashSet};
use std::io::Read;
use std::path::{Path, PathBuf};
use std::time::Instant;

use rusqlite::{Connection, OpenFlags};

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

    let db = db_path.unwrap_or_else(default_db_path);

    let result = match mode.as_str() {
        "stats" => run_stats(&db),
        _ => run_probe(&db, &date),
    };

    if let Err(e) = result {
        eprintln!("{e}");
        std::process::exit(3);
    }
}

// ==================== 探针 ====================

fn run_probe(db: &Path, date: &str) -> Result<(), String> {
    if !db.exists() {
        return Err(format!("数据库不存在：{}", db.display()));
    }
    println!("库文件：{}", db.display());

    let conn = open_readonly(db)?;
    let (start, end) = day_bounds(date);
    println!("统计区间：{start} ~ {end}");

    let t0 = Instant::now();
    let mut stmt = conn
        .prepare(
            "SELECT ProcessName, COUNT(*), SUM(Duration) FROM AppSessions \
             WHERE StartTime >= ?1 AND StartTime < ?2 \
             GROUP BY ProcessName ORDER BY SUM(Duration) DESC",
        )
        .map_err(|e| e.to_string())?;
    let rows: Vec<(String, i64, i64)> = stmt
        .query_map([&start, &end], |r| {
            Ok((r.get::<_, String>(0)?, r.get::<_, i64>(1)?, r.get::<_, i64>(2)?))
        })
        .map_err(|e| e.to_string())?
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

    let t1 = Instant::now();
    let mut stmt = conn
        .prepare(
            "SELECT Timestamp, CpuLoadAvg, GpuLoadAvg, GpuTempMax, VramUsedMax, VramTotal, SampleCount \
             FROM HardwareSamples ORDER BY Timestamp DESC LIMIT 5",
        )
        .map_err(|e| e.to_string())?;
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
        .map_err(|e| e.to_string())?
        .filter_map(|r| r.ok())
        .collect();
    let hw_ms = t1.elapsed().as_secs_f64() * 1000.0;
    println!("\n[最近硬件样本] {} 行，查询 {:.2} ms", hw.len(), hw_ms);
    for (ts, cpu, gpu, temp, vused, vtotal, n) in &hw {
        println!("  {ts}  CPU {cpu:>5.1}%  GPU {gpu:>5.1}%  {temp:>4.0}°C  显存 {vused:>6.0}/{vtotal:.0}MB  样本 {n}");
    }

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
    Ok(())
}

// ==================== 应用统计 ====================

struct Session {
    proc_name: String,
    app_name: Option<String>,
    category: Option<String>,
    start: f64, // 距 1970-01-01 的秒
    end: Option<f64>,
    hour: u32,
}

impl Session {
    fn minutes(&self) -> f64 {
        match self.end {
            Some(e) if e > self.start => (e - self.start) / 60.0,
            _ => 0.0,
        }
    }

    /// 展示名：首个非空 AppName，否则进程名。
    fn display_name(&self) -> String {
        match self.app_name.as_deref() {
            Some(n) if !n.trim().is_empty() => n.to_string(),
            _ => self.proc_name.clone(),
        }
    }
}

fn run_stats(db: &Path) -> Result<(), String> {
    let mut buf = String::new();
    std::io::stdin()
        .read_to_string(&mut buf)
        .map_err(|e| e.to_string())?;
    let req: serde_json::Value =
        serde_json::from_str(&buf).map_err(|e| format!("请求 JSON 解析失败: {e}"))?;

    let get = |k: &str| -> Result<String, String> {
        req.get(k)
            .and_then(|v| v.as_str())
            .map(|s| s.to_string())
            .ok_or_else(|| format!("缺少字段 {k}"))
    };

    let cur_start = get("curStart")?;
    let cur_end = get("curEnd")?;
    let prev_start = get("prevStart")?;
    let prev_end = get("prevEnd")?;
    let cat_filter = req
        .get("catFilter")
        .and_then(|v| v.as_str())
        .filter(|s| !s.is_empty())
        .map(|s| s.to_string());
    let search = req
        .get("search")
        .and_then(|v| v.as_str())
        .map(|s| s.trim().to_string())
        .filter(|s| !s.is_empty());

    if !db.exists() {
        return Err(format!("数据库不存在：{}", db.display()));
    }

    let conn = open_readonly(db)?;
    let t0 = Instant::now();
    let cat_map = load_category_map(&conn)?;
    let cur = load_sessions(&conn, &cur_start, &cur_end)?;
    let prev = load_sessions(&conn, &prev_start, &prev_end)?;

    let mut report = compute(cur, prev, &cat_map, cat_filter.as_deref(), search.as_deref());
    report["ms"] = serde_json::json!(t0.elapsed().as_secs_f64() * 1000.0);

    println!(
        "{}",
        serde_json::to_string(&report).map_err(|e| e.to_string())?
    );
    Ok(())
}

fn load_sessions(conn: &Connection, start: &str, end: &str) -> Result<Vec<Session>, String> {
    let mut stmt = conn
        .prepare(
            "SELECT ProcessName, AppName, Category, StartTime, EndTime FROM AppSessions \
             WHERE StartTime >= ?1 AND StartTime < ?2 ORDER BY StartTime",
        )
        .map_err(|e| e.to_string())?;

    let rows: Vec<Session> = stmt
        .query_map([start, end], |r| {
            let start_ts: String = r.get::<_, Option<String>>(3).unwrap_or(None).unwrap_or_default();
            let end_ts: Option<String> = r.get::<_, Option<String>>(4).unwrap_or(None);
            let parsed = parse_dt(&start_ts);
            Ok(Session {
                proc_name: r.get::<_, Option<String>>(0).unwrap_or(None).unwrap_or_default(),
                app_name: r.get::<_, Option<String>>(1).unwrap_or(None),
                category: r.get::<_, Option<String>>(2).unwrap_or(None),
                start: parsed.as_ref().map(|p| p.secs).unwrap_or(0.0),
                hour: parsed.as_ref().map(|p| p.hour).unwrap_or(0),
                end: end_ts.and_then(|s| parse_dt(&s)).map(|p| p.secs),
            })
        })
        .map_err(|e| e.to_string())?
        .filter_map(|r| r.ok())
        .collect();
    Ok(rows)
}

/// 分类映射：ProgramCategoryMappings × 启用中的 ProgramCategories。
fn load_category_map(conn: &Connection) -> Result<HashMap<String, (String, String)>, String> {
    let mut stmt = conn
        .prepare(
            "SELECT c.Name, c.Color, m.ProcessName FROM ProgramCategoryMappings m \
             JOIN ProgramCategories c ON c.Id = m.CategoryId WHERE c.IsActive = 1",
        )
        .map_err(|e| e.to_string())?;

    let mut map: HashMap<String, (String, String)> = HashMap::new();
    let rows = stmt
        .query_map([], |r| {
            Ok((
                r.get::<_, Option<String>>(0)?.unwrap_or_default(),
                r.get::<_, Option<String>>(1)?.unwrap_or_default(),
                r.get::<_, Option<String>>(2)?.unwrap_or_default(),
            ))
        })
        .map_err(|e| e.to_string())?;

    for (name, color, proc) in rows.filter_map(|r| r.ok()) {
        let normalized = normalize_process_name(&proc);
        if normalized.is_empty() {
            continue;
        }
        let color = if color.trim().is_empty() {
            default_color_for(&name)
        } else {
            color
        };
        map.insert(normalized, (name, color));
    }
    Ok(map)
}

fn compute(
    sessions: Vec<Session>,
    prev: Vec<Session>,
    cat_map: &HashMap<String, (String, String)>,
    cat_filter: Option<&str>,
    search: Option<&str>,
) -> serde_json::Value {
    // 过滤（分类 / 搜索）只作用于当前期会话
    let filtered: Vec<&Session> = sessions
        .iter()
        .filter(|s| {
            if let Some(cf) = cat_filter {
                let (name, _) = resolve_category(s, cat_map);
                if !name.eq_ignore_ascii_case(cf) {
                    return false;
                }
            }
            if let Some(q) = search {
                let display = s.display_name();
                if !contains_ignore_case(&display, q) && !contains_ignore_case(&s.proc_name, q) {
                    return false;
                }
            }
            true
        })
        .collect();

    let total_minutes: f64 = filtered.iter().map(|s| s.minutes()).sum();
    let active_apps = filtered
        .iter()
        .map(|s| s.proc_name.as_str())
        .collect::<HashSet<_>>()
        .len();

    // 按进程分组（加载时已按 StartTime 升序，故组内首个即最早一条，与 C# g.First() 一致）
    let mut order: Vec<String> = Vec::new();
    let mut groups: HashMap<String, Vec<&Session>> = HashMap::new();
    for s in &filtered {
        if s.proc_name.is_empty() {
            continue;
        }
        let key = s.proc_name.clone();
        if !groups.contains_key(&key) {
            order.push(key.clone());
        }
        groups.entry(key).or_default().push(s);
    }

    // 上一期按进程名（忽略大小写）汇总
    let mut prev_by_proc: HashMap<String, f64> = HashMap::new();
    for s in &prev {
        *prev_by_proc.entry(s.proc_name.to_lowercase()).or_insert(0.0) += s.minutes();
    }

    let mut rows: Vec<serde_json::Value> = Vec::new();
    for key in order {
        let group = &groups[&key];
        let first = group[0];
        let (category, color_hex) = resolve_category(first, cat_map);
        let duration: f64 = group.iter().map(|s| s.minutes()).sum();
        let focus_minutes: f64 = group
            .iter()
            .filter(|s| s.minutes() >= 25.0 && is_productive(&category))
            .map(|s| s.minutes())
            .sum();
        let prev_duration = *prev_by_proc.get(&key.to_lowercase()).unwrap_or(&0.0);
        let delta = duration - prev_duration;
        let (direction, delta_text) = if delta.abs() < 5.0 {
            ("flat", "—".to_string())
        } else if prev_duration <= 0.0 {
            ("up", "新增".to_string())
        } else {
            let pct = fmt_f0(delta / prev_duration * 100.0);
            if delta > 0.0 {
                ("up", format!("+{pct}%"))
            } else {
                ("down", format!("{pct}%"))
            }
        };

        let app_name = group
            .iter()
            .map(|s| s.display_name())
            .find(|n| n != &key && !n.trim().is_empty())
            .unwrap_or(key.clone());

        rows.push(serde_json::json!({
            "processName": key,
            "appName": app_name,
            "category": category,
            "categoryColorHex": color_hex,
            "durationMinutes": duration,
            "sessions": group.len(),
            "avgDurationMinutes": duration / group.len() as f64,
            "percentage": if total_minutes > 0.0 { duration / total_minutes * 100.0 } else { 0.0 },
            "focusMinutes": focus_minutes,
            "deltaText": delta_text,
            "deltaDirection": direction,
        }));
    }

    rows.sort_by(|a, b| {
        let da = a["durationMinutes"].as_f64().unwrap_or(0.0);
        let db_ = b["durationMinutes"].as_f64().unwrap_or(0.0);
        db_.partial_cmp(&da)
            .unwrap_or(std::cmp::Ordering::Equal)
            .then_with(|| {
                let pa = a["processName"].as_str().unwrap_or("").to_lowercase();
                let pb = b["processName"].as_str().unwrap_or("").to_lowercase();
                pa.cmp(&pb)
            })
    });

    let top_name = rows
        .first()
        .and_then(|r| r["appName"].as_str())
        .unwrap_or("无")
        .to_string();
    let top_minutes = rows
        .first()
        .and_then(|r| r["durationMinutes"].as_f64())
        .unwrap_or(0.0);

    let focus_sessions = filtered
        .iter()
        .filter(|s| {
            let (cat, _) = resolve_category(s, cat_map);
            s.minutes() >= 25.0 && is_productive(&cat)
        })
        .count();

    // 饼图：Top 8 + 其他
    const PIE_TOP_N: usize = 8;
    let mut pie: Vec<serde_json::Value> = Vec::new();
    for r in rows.iter().take(PIE_TOP_N) {
        pie.push(serde_json::json!({
            "name": r["appName"],
            "minutes": r["durationMinutes"],
            "colorHex": r["categoryColorHex"],
        }));
    }
    let other: f64 = rows
        .iter()
        .skip(PIE_TOP_N)
        .filter_map(|r| r["durationMinutes"].as_f64())
        .sum();
    if other > 0.0 {
        pie.push(serde_json::json!({"name": "其他", "minutes": other, "colorHex": "#8B5CF6"}));
    }

    // 分类构成
    let mut cat_order: Vec<String> = Vec::new();
    let mut cat_minutes: HashMap<String, f64> = HashMap::new();
    let mut cat_color: HashMap<String, String> = HashMap::new();
    for s in &filtered {
        let (name, color) = resolve_category(s, cat_map);
        if !cat_minutes.contains_key(&name) {
            cat_order.push(name.clone());
            cat_color.insert(name.clone(), color);
        }
        *cat_minutes.entry(name).or_insert(0.0) += s.minutes();
    }
    let mut shares: Vec<serde_json::Value> = cat_order
        .iter()
        .map(|name| {
            let minutes = cat_minutes[name];
            serde_json::json!({
                "category": name,
                "minutes": minutes,
                "percentage": if total_minutes > 0.0 { minutes / total_minutes * 100.0 } else { 0.0 },
                "colorHex": cat_color[name],
            })
        })
        .collect();
    shares.sort_by(|a, b| {
        b["minutes"]
            .as_f64()
            .unwrap_or(0.0)
            .partial_cmp(&a["minutes"].as_f64().unwrap_or(0.0))
            .unwrap_or(std::cmp::Ordering::Equal)
    });

    // 24 小时分布
    let mut hourly: Vec<serde_json::Value> = Vec::new();
    for h in 0..24u32 {
        let mins: f64 = filtered
            .iter()
            .filter(|s| s.hour == h)
            .map(|s| s.minutes())
            .sum();
        hourly.push(serde_json::json!({
            "hour": h,
            "label": format!("{h:02}:00"),
            "minutes": mins,
        }));
    }

    serde_json::json!({
        "ok": true,
        "totalMinutes": total_minutes,
        "activeApps": active_apps,
        "topAppName": top_name,
        "topAppMinutes": top_minutes,
        "focusSessions": focus_sessions,
        "rows": rows,
        "pieSlices": pie,
        "categoryShares": shares,
        "hourly": hourly,
    })
}

// ==================== 口径工具 ====================

/// 归一化进程名：去空白、去 .exe 后缀、小写。
fn normalize_process_name(process_name: &str) -> String {
    let p = process_name.trim();
    let p = if p.len() >= 4 && p[p.len() - 4..].eq_ignore_ascii_case(".exe") {
        &p[..p.len() - 4]
    } else {
        p
    };
    p.to_lowercase()
}

/// 分类兜底色（与 C# DefaultPalette 一致）。
fn default_color_for(category: &str) -> String {
    const PALETTE: &[(&[&str], &str)] = &[
        (&["开发", "开发工具"], "#512BD4"),
        (&["浏览", "浏览器"], "#0078D4"),
        (&["沟通", "通讯"], "#107C10"),
        (&["娱乐", "视频", "游戏"], "#FF8C00"),
        (&["办公", "办公软件"], "#00B7C3"),
    ];
    if !category.is_empty() {
        for (keys, color) in PALETTE {
            if keys.iter().any(|k| contains_ignore_case(category, k)) {
                return color.to_string();
            }
        }
    }
    "#6B7280".to_string()
}

/// 生产力分类判定（与 C# AnalyticsEngine.IsProductiveCategory 一致）。
fn is_productive(category: &str) -> bool {
    const PRODUCTIVE: &[&str] = &["开发", "开发工具", "办公", "办公软件", "AI 助手", "生产力"];
    !category.is_empty() && PRODUCTIVE.iter().any(|p| contains_ignore_case(category, p))
}

/// 分类解析唯一入口：映射表命中 → 会话快照 → 未分类。
fn resolve_category(s: &Session, cat_map: &HashMap<String, (String, String)>) -> (String, String) {
    let normalized = normalize_process_name(&s.proc_name);
    if !normalized.is_empty() {
        if let Some((name, color)) = cat_map.get(&normalized) {
            return (name.clone(), color.clone());
        }
    }
    let snapshot = s.category.as_deref().unwrap_or("").trim();
    if snapshot.is_empty() {
        ("未分类".to_string(), default_color_for(""))
    } else {
        (snapshot.to_string(), default_color_for(snapshot))
    }
}

fn contains_ignore_case(haystack: &str, needle: &str) -> bool {
    haystack.to_lowercase().contains(&needle.to_lowercase())
}

/// .NET `F0` 口径（四舍五入远离零）。
fn fmt_f0(v: f64) -> i64 {
    v.round() as i64
}

// ==================== 日期与开库 ====================

struct Dt {
    secs: f64,
    hour: u32,
}

/// "YYYY-MM-DD HH:MM:SS[.fffffff]" → 距 1970 的秒 + 小时。
fn parse_dt(s: &str) -> Option<Dt> {
    let bytes = s.as_bytes();
    if bytes.len() < 19 {
        return None;
    }
    let num = |from: usize, to: usize| -> Option<i64> { s.get(from..to)?.parse::<i64>().ok() };
    let y = num(0, 4)?;
    let mo = num(5, 7)?;
    let d = num(8, 10)?;
    let h = num(11, 13)?;
    let mi = num(14, 16)?;
    let sec = num(17, 19)?;
    let frac = if bytes.len() > 20 && bytes[19] == b'.' {
        let digits = (bytes.len() - 20).min(7);
        let v: f64 = s.get(20..20 + digits)?.parse::<f64>().ok()?;
        v / 10f64.powi(digits as i32)
    } else {
        0.0
    };

    let days = days_from_civil(y, mo, d);
    Some(Dt {
        secs: days as f64 * 86_400.0 + (h * 3_600 + mi * 60 + sec) as f64 + frac,
        hour: h as u32,
    })
}

fn days_from_civil(y: i64, m: i64, d: i64) -> i64 {
    let y = if m <= 2 { y - 1 } else { y };
    let era = if y >= 0 { y } else { y - 399 } / 400;
    let yoe = y - era * 400;
    let mp = if m > 2 { m - 3 } else { m + 9 };
    let doy = (153 * mp + 2) / 5 + d - 1;
    let doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
    era * 146_097 + doe - 719_468
}

/// 只读打开：优先直接 READ_ONLY；WAL 下失败则复制 db/-wal/-shm 到临时目录读快照。
fn open_readonly(path: &Path) -> Result<Connection, String> {
    if let Ok(c) = Connection::open_with_flags(path, OpenFlags::SQLITE_OPEN_READ_ONLY) {
        return Ok(c);
    }

    let tmp_dir = std::env::temp_dir().join("pchabit-datad-snapshot");
    std::fs::create_dir_all(&tmp_dir).map_err(|e| e.to_string())?;
    let copy = tmp_dir.join("pchabit.db");
    std::fs::copy(path, &copy).map_err(|e| e.to_string())?;
    for suffix in ["-wal", "-shm"] {
        let src = PathBuf::from(format!("{}{}", path.display(), suffix));
        let dst = PathBuf::from(format!("{}{}", copy.display(), suffix));
        if src.exists() {
            let _ = std::fs::copy(&src, &dst);
        }
    }
    Connection::open_with_flags(&copy, OpenFlags::SQLITE_OPEN_READ_ONLY)
        .map_err(|e| format!("快照模式打开失败: {e}"))
}

fn default_db_path() -> PathBuf {
    let base = std::env::var("LOCALAPPDATA").unwrap_or_default();
    PathBuf::from(base)
        .join("PChabit")
        .join("Data")
        .join("pchabit.db")
}

/// 探针用：本地日期切当日区间。
fn day_bounds(date: &str) -> (String, String) {
    let day = if date.is_empty() {
        let secs = std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .map(|d| d.as_secs())
            .unwrap_or(0);
        civil_from_days(((secs + 8 * 3600) / 86_400) as i64)
    } else {
        date.to_string()
    };
    (
        format!("{day} 00:00:00"),
        format!("{day} 23:59:59.9999999"),
    )
}

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
