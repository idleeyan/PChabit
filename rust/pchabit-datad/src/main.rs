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

mod analytics;

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
        "detail" => run_detail(&db),
        "xlsx" => run_xlsx(),
        "analytics" => run_analytics(&db),
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

pub(crate) struct Session {
    pub(crate) proc_name: String,
    pub(crate) app_name: Option<String>,
    pub(crate) category: Option<String>,
    pub(crate) start: f64, // 距 1970-01-01 的秒
    pub(crate) end: Option<f64>,
    pub(crate) hour: u32,
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

fn run_analytics(db: &Path) -> Result<(), String> {
    let mut buf = String::new();
    std::io::stdin()
        .read_to_string(&mut buf)
        .map_err(|e| e.to_string())?;
    let req: serde_json::Value =
        serde_json::from_str(&buf).map_err(|e| format!("请求 JSON 解析失败: {e}"))?;

    if !db.exists() {
        return Err(format!("数据库不存在：{}", db.display()));
    }

    let conn = open_readonly(db)?;
    let t0 = Instant::now();
    let mut report = analytics::run(&conn, &req)?;
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
pub(crate) fn load_category_map(conn: &Connection) -> Result<HashMap<String, (String, String)>, String> {
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

// ==================== 单应用详情 ====================

struct DetailSession {
    proc_name: String,
    title: Option<String>,
    start: f64,
    end: Option<f64>,
    hour: u32,
    day: i64, // 距 1970 的天数
}

impl DetailSession {
    fn minutes(&self) -> f64 {
        match self.end {
            Some(e) if e > self.start => (e - self.start) / 60.0,
            _ => 0.0,
        }
    }
}

fn run_detail(db: &Path) -> Result<(), String> {
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
    let day_start = get("dayStart")?;
    let day_end = get("dayEnd")?;
    let hw_start = get("hwStart")?;
    let hw_end = get("hwEnd")?;
    let process_name = get("processName")?;
    let app_name = req
        .get("appName")
        .and_then(|v| v.as_str())
        .unwrap_or("")
        .to_string();
    let category = req
        .get("category")
        .and_then(|v| v.as_str())
        .unwrap_or("")
        .to_string();
    let cat_for_focus = req
        .get("catForFocus")
        .and_then(|v| v.as_str())
        .filter(|s| !s.is_empty())
        .unwrap_or(&category)
        .to_string();

    if !db.exists() {
        return Err(format!("数据库不存在：{}", db.display()));
    }

    let conn = open_readonly(db)?;
    let t0 = Instant::now();
    let norm = normalize_process_name(&process_name);

    let cur = load_process_sessions(&conn, &cur_start, &cur_end, &process_name, &norm)?;
    let prev = load_process_sessions(&conn, &prev_start, &prev_end, &process_name, &norm)?;
    let key_presses = sum_key_presses(&conn, &process_name, &norm, &day_start, &day_end)?;
    let (clicks, distance, scrolls) =
        sum_mouse(&conn, &process_name, &norm, &day_start, &day_end)?;
    let hw = collect_process_samples(&conn, &cur, &norm, &process_name, &hw_start, &hw_end)?;

    let mut report = compute_detail(
        &cur, &prev, &process_name, &app_name, &category, &cat_for_focus, key_presses,
        &hw, clicks, distance, scrolls,
        &cur_start, &cur_end,
    );
    report["ms"] = serde_json::json!(t0.elapsed().as_secs_f64() * 1000.0);

    println!(
        "{}",
        serde_json::to_string(&report).map_err(|e| e.to_string())?
    );
    Ok(())
}

fn load_process_sessions(
    conn: &Connection,
    start: &str,
    end: &str,
    process_name: &str,
    norm: &str,
) -> Result<Vec<DetailSession>, String> {
    let mut stmt = conn
        .prepare(
            "SELECT ProcessName, WindowTitle, StartTime, EndTime FROM AppSessions \
             WHERE StartTime >= ?1 AND StartTime < ?2 ORDER BY StartTime",
        )
        .map_err(|e| e.to_string())?;

    let rows: Vec<DetailSession> = stmt
        .query_map([start, end], |r| {
            let p = r.get::<_, Option<String>>(0).unwrap_or(None).unwrap_or_default();
            let start_ts: String = r
                .get::<_, Option<String>>(2)
                .unwrap_or(None)
                .unwrap_or_default();
            let end_ts: Option<String> = r.get::<_, Option<String>>(3).unwrap_or(None);
            let parsed = parse_dt(&start_ts);
            Ok(DetailSession {
                proc_name: p,
                title: r.get::<_, Option<String>>(1).unwrap_or(None),
                start: parsed.as_ref().map(|p| p.secs).unwrap_or(0.0),
                hour: parsed.as_ref().map(|p| p.hour).unwrap_or(0),
                day: parsed
                    .as_ref()
                    .map(|p| (p.secs / 86_400.0).floor() as i64)
                    .unwrap_or(0),
                end: end_ts.and_then(|s| parse_dt(&s)).map(|p| p.secs),
            })
        })
        .map_err(|e| e.to_string())?
        .filter_map(|r| r.ok())
        .collect();

    // 与 C# 一致：进程名忽略大小写相等，或归一化后相等
    Ok(rows
        .into_iter()
        .filter(|s| {
            s.proc_name.eq_ignore_ascii_case(process_name)
                || normalize_process_name(&s.proc_name) == norm
        })
        .collect())
}

fn sum_key_presses(
    conn: &Connection,
    process_name: &str,
    norm: &str,
    day_start: &str,
    day_end: &str,
) -> Result<i64, String> {
    conn.query_row(
        "SELECT COALESCE(SUM(TotalKeyPresses), 0) FROM KeyboardSessions \
         WHERE ProcessName IS NOT NULL AND (ProcessName = ?1 OR lower(ProcessName) = ?2) \
           AND Date >= ?3 AND Date < ?4",
        rusqlite::params![process_name, norm, day_start, day_end],
        |r| r.get::<_, i64>(0),
    )
    .map_err(|e| e.to_string())
}

fn sum_mouse(
    conn: &Connection,
    process_name: &str,
    norm: &str,
    day_start: &str,
    day_end: &str,
) -> Result<(f64, f64, f64), String> {
    let mut stmt = conn
        .prepare(
            "SELECT COALESCE(LeftClickCount,0) + COALESCE(RightClickCount,0) + COALESCE(MiddleClickCount,0), \
                    COALESCE(TotalMoveDistance,0), COALESCE(ScrollCount,0) FROM MouseSessions \
             WHERE ProcessName IS NOT NULL AND (ProcessName = ?1 OR lower(ProcessName) = ?2) \
               AND Date >= ?3 AND Date < ?4",
        )
        .map_err(|e| e.to_string())?;

    let rows = stmt
        .query_map(rusqlite::params![process_name, norm, day_start, day_end], |r| {
            Ok((
                r.get::<_, f64>(0).unwrap_or(0.0),
                r.get::<_, f64>(1).unwrap_or(0.0),
                r.get::<_, f64>(2).unwrap_or(0.0),
            ))
        })
        .map_err(|e| e.to_string())?;

    let mut sum = (0.0, 0.0, 0.0);
    for row in rows.filter_map(|r| r.ok()) {
        sum.0 += row.0;
        sum.1 += row.1;
        sum.2 += row.2;
    }
    Ok(sum)
}

/// 按分钟样本最长重叠归属该进程会话（与 C# CollectProcessSamples 一致）。
fn collect_process_samples(
    conn: &Connection,
    sessions: &[DetailSession],
    norm: &str,
    process_name: &str,
    hw_start: &str,
    hw_end: &str,
) -> Result<Vec<(f64, f64, Option<f64>)>, String> {
    let closed: Vec<&DetailSession> = sessions.iter().filter(|s| s.minutes() > 0.0).collect();
    if closed.is_empty() {
        return Ok(Vec::new());
    }

    let mut stmt = conn
        .prepare(
            "SELECT Timestamp, CpuLoadAvg, GpuLoadAvg, GpuTempMax FROM HardwareSamples \
             WHERE Timestamp >= ?1 AND Timestamp < ?2 ORDER BY Timestamp",
        )
        .map_err(|e| e.to_string())?;

    let samples: Vec<(f64, Option<f64>, Option<f64>, Option<f64>)> = stmt
        .query_map([hw_start, hw_end], |r| {
            let ts: String = r.get::<_, String>(0).unwrap_or_default();
            Ok((
                parse_dt(&ts).map(|p| p.secs).unwrap_or(f64::NAN),
                r.get::<_, Option<f64>>(1).unwrap_or(None),
                r.get::<_, Option<f64>>(2).unwrap_or(None),
                r.get::<_, Option<f64>>(3).unwrap_or(None),
            ))
        })
        .map_err(|e| e.to_string())?
        .filter_map(|r| r.ok())
        .filter(|s| !s.0.is_nan())
        .collect();

    let mut result = Vec::new();
    for (ts, cpu, gpu, temp) in samples {
        let s_start = ts;
        let s_end = ts + 60.0;
        let mut best: Option<&DetailSession> = None;
        let mut best_overlap = 0.0f64;

        for s in &closed {
            // 未结束的会话不参与归属（C# 只取 EndTime 有值的 closed 集合）
            let Some(s_end_ts) = s.end else { continue };
            let overlap_start = s.start.max(s_start);
            let overlap_end = s_end_ts.min(s_end);
            let overlap = (overlap_end - overlap_start) / 60.0;
            if overlap <= 0.0 {
                continue;
            }
            if overlap > best_overlap {
                best_overlap = overlap;
                best = Some(s);
            }
        }

        let Some(best) = best else { continue };
        let is_target = best.proc_name.eq_ignore_ascii_case(process_name)
            || normalize_process_name(&best.proc_name) == norm;
        if !is_target {
            continue;
        }
        if cpu.is_some() || gpu.is_some() || temp.is_some() {
            result.push((cpu.unwrap_or(0.0), gpu.unwrap_or(0.0), temp));
        }
    }
    Ok(result)
}

fn compute_detail(
    cur: &[DetailSession],
    prev: &[DetailSession],
    process_name: &str,
    app_name: &str,
    category: &str,
    cat_for_focus: &str,
    key_presses: i64,
    hw: &[(f64, f64, Option<f64>)],
    mouse_clicks: f64,
    mouse_distance: f64,
    mouse_scrolls: f64,
    cur_start: &str,
    cur_end: &str,
) -> serde_json::Value {
    let total: f64 = cur.iter().map(|s| s.minutes()).sum();
    let prev_total: f64 = prev.iter().map(|s| s.minutes()).sum();
    let session_count = cur.len();
    let avg = if session_count > 0 {
        total / session_count as f64
    } else {
        0.0
    };

    let focus_min: f64 = if is_productive(cat_for_focus) {
        cur.iter()
            .filter(|s| s.minutes() >= 25.0)
            .map(|s| s.minutes())
            .sum()
    } else {
        0.0
    };
    let focus_count = if is_productive(cat_for_focus) {
        cur.iter().filter(|s| s.minutes() >= 25.0).count()
    } else {
        0
    };
    let longest = cur.iter().map(|s| s.minutes()).fold(0.0f64, f64::max);

    let (direction, delta_text) = if (total - prev_total).abs() < 5.0 {
        ("flat", "—".to_string())
    } else if prev_total <= 0.0 {
        ("up", "新增".to_string())
    } else {
        let pct = fmt_f0((total - prev_total) / prev_total * 100.0);
        if total > prev_total {
            ("up", format!("+{pct}%"))
        } else {
            ("down", format!("{pct}%"))
        }
    };

    // 窗口标题 Top5（按去空白标题分组，忽略大小写）
    let mut title_order: Vec<String> = Vec::new();
    let mut title_minutes: HashMap<String, f64> = HashMap::new();
    for s in cur {
        let Some(t) = s.title.as_deref() else { continue };
        let trimmed = t.trim();
        if trimmed.is_empty() {
            continue;
        }
        let key = trimmed.to_lowercase();
        if !title_minutes.contains_key(&key) {
            title_order.push(key.clone());
        }
        *title_minutes.entry(key).or_insert(0.0) += s.minutes();
    }
    let mut titles: Vec<(String, f64)> = title_order
        .into_iter()
        .map(|k| {
            let minutes = title_minutes[&k];
            (k, minutes)
        })
        .collect();
    titles.sort_by(|a, b| {
        b.1.partial_cmp(&a.1)
            .unwrap_or(std::cmp::Ordering::Equal)
            .then_with(|| a.0.cmp(&b.0))
    });
    let top_titles: Vec<String> = titles
        .iter()
        .take(5)
        .map(|(t, _)| truncate_title(t, 48))
        .collect();

    // 小时分布（标签是两位小时数，与 C# AppDetailEngine 一致，不带 ":00"）
    let mut hour_mins = [0.0f64; 24];
    for s in cur {
        if s.end.is_none() {
            continue;
        }
        hour_mins[s.hour as usize % 24] += s.minutes();
    }
    let mut hourly = Vec::with_capacity(24);
    for h in 0..24usize {
        hourly.push(serde_json::json!({
            "hour": h,
            "label": format!("{h:02}"),
            "minutes": hour_mins[h],
        }));
    }

    // 日趋势：周期内逐日（标签 M/d，强度按最大日四档）
    let start_day = parse_dt(cur_start).map(|p| (p.secs / 86_400.0).floor() as i64).unwrap_or(0);
    let end_day = parse_dt(cur_end).map(|p| (p.secs / 86_400.0).floor() as i64).unwrap_or(start_day);
    let mut day_minutes: HashMap<i64, f64> = HashMap::new();
    for s in cur {
        if s.end.is_none() {
            continue;
        }
        *day_minutes.entry(s.day).or_insert(0.0) += s.minutes();
    }
    let max_day = day_minutes
        .values()
        .cloned()
        .fold(0.0f64, f64::max)
        .max(1.0);

    let mut daily = Vec::new();
    let mut d = start_day;
    while d < end_day {
        let mins = *day_minutes.get(&d).unwrap_or(&0.0);
        let level = if mins <= 0.0 {
            0
        } else if mins < max_day * 0.25 {
            1
        } else if mins < max_day * 0.5 {
            2
        } else if mins < max_day * 0.75 {
            3
        } else {
            4
        };
        let (y, m, dd) = ymd_from_days(d);
        let _ = y;
        daily.push(serde_json::json!({
            "dateLabel": format!("{m}/{dd}"),
            "minutes": mins,
            "intensityLevel": level,
        }));
        d += 1;
    }

    // 硬件均值：只取三项中至少一项有值的样本
    let hw_rows: Vec<&(f64, f64, Option<f64>)> = hw
        .iter()
        .filter(|(c, g, t)| *c > 0.0 || *g > 0.0 || t.is_some())
        .collect();
    let cpu = if hw_rows.is_empty() {
        None
    } else {
        Some(round1(hw_rows.iter().map(|x| x.0).sum::<f64>() / hw_rows.len() as f64))
    };
    let gpu = if hw_rows.is_empty() {
        None
    } else {
        Some(round1(hw_rows.iter().map(|x| x.1).sum::<f64>() / hw_rows.len() as f64))
    };
    let temp = if hw.is_empty() {
        None
    } else {
        hw.iter()
            .filter_map(|x| x.2)
            .fold(None::<f64>, |acc, v| Some(acc.map_or(v, |a| a.max(v))))
            .map(round1)
    };

    let keys_per_hour = if key_presses > 0 && total > 0.0 {
        Some(round1(key_presses as f64 / (total / 60.0)))
    } else {
        None
    };

    let has_key_mouse = key_presses > 0
        || mouse_clicks > 0.0
        || mouse_distance > 0.0
        || mouse_scrolls > 0.0;

    serde_json::json!({
        "ok": true,
        "processName": process_name,
        "appName": if app_name.trim().is_empty() { process_name.to_string() } else { app_name.to_string() },
        "category": if category.trim().is_empty() { "未分类".to_string() } else { category.to_string() },
        "totalMinutes": total,
        "sessions": session_count,
        "avgMinutes": avg,
        "focusMinutes": focus_min.max(0.0),
        "focusCount": focus_count,
        "deltaText": delta_text,
        "deltaDirection": direction,
        "longestSessionMinutes": longest,
        "topWindowTitles": top_titles,
        "hourly": hourly,
        "dailyTrend": daily,
        "keyPresses": if key_presses > 0 { serde_json::json!(key_presses) } else { serde_json::Value::Null },
        "keysPerHour": keys_per_hour,
        "mouseClicks": if mouse_clicks > 0.0 { serde_json::json!(mouse_clicks) } else { serde_json::Value::Null },
        "mouseMoveDistance": if mouse_distance > 0.0 { serde_json::json!(mouse_distance) } else { serde_json::Value::Null },
        "mouseScrolls": if mouse_scrolls > 0.0 { serde_json::json!(mouse_scrolls) } else { serde_json::Value::Null },
        "cpuLoadAvg": cpu,
        "gpuLoadAvg": gpu,
        "gpuTempMax": temp,
        "hasInput": key_presses > 0,
        "hasKeyMouse": has_key_mouse,
        "hasHardware": !hw.is_empty() && (cpu.is_some() || gpu.is_some() || temp.is_some()),
        "hasData": session_count > 0,
    })
}

fn truncate_title(title: &str, max: usize) -> String {
    let chars: Vec<char> = title.chars().collect();
    if chars.len() <= max {
        title.to_string()
    } else {
        let mut s: String = chars[..max].iter().collect();
        s.push('…');
        s
    }
}

pub(crate) fn round1(v: f64) -> f64 {
    (v * 10.0).round() / 10.0
}

fn ymd_from_days(z: i64) -> (i64, i64, i64) {
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
    (year, m, d)
}

// ==================== XLSX 写出 ====================

/// 通用工作簿写出：C# 负责组织数据（它持有领域对象），Rust 负责生成文件。
///
/// 请求：
/// ```json
/// {"file":"C:\\x\\a.xlsx","sheets":[{"name":"概览","title":"标题","widths":[20,50],
///   "kv":[["导出时间","2026-11-18 20:00:00"]],"headers":["ID","进程名"],"rows":[[1,"a.exe"]]}]}
/// ```
/// 支持：标题合并居中加粗、键值区块、表头灰底加粗、列宽、冻结表头。数值写为数字并带 0.00 格式。
fn run_xlsx() -> Result<(), String> {
    use rust_xlsxwriter::{Format, Workbook};

    let mut buf = String::new();
    std::io::stdin()
        .read_to_string(&mut buf)
        .map_err(|e| e.to_string())?;
    let req: serde_json::Value =
        serde_json::from_str(&buf).map_err(|e| format!("请求 JSON 解析失败: {e}"))?;

    let file = req
        .get("file")
        .and_then(|v| v.as_str())
        .ok_or_else(|| "缺少字段 file".to_string())?
        .to_string();

    let t0 = Instant::now();
    let mut wb = Workbook::new();

    let title_fmt = Format::new().set_bold().set_font_size(16);
    let num_fmt = Format::new().set_num_format("0.00");
    let header_fmt = Format::new()
        .set_bold()
        .set_background_color(rust_xlsxwriter::Color::RGB(0xD9D9D9));

    let sheets = req
        .get("sheets")
        .and_then(|v| v.as_array())
        .ok_or_else(|| "缺少字段 sheets".to_string())?
        .clone();

    let mut written_rows = 0usize;

    for s in sheets {
        let name = s.get("name").and_then(|v| v.as_str()).unwrap_or("Sheet");
        let ws = wb.add_worksheet();
        ws.set_name(name).map_err(|e| e.to_string())?;

        let widths: Vec<f64> = s
            .get("widths")
            .and_then(|v| v.as_array())
            .map(|a| a.iter().filter_map(|x| x.as_f64()).collect())
            .unwrap_or_default();
        for (i, w) in widths.iter().enumerate() {
            ws.set_column_width(i as u16, *w).map_err(|e| e.to_string())?;
        }

        let mut row: u32 = 0;
        let mut header_rows = 0u16;

        // 区块化：一个工作表可含多个「标题/键值/表头/数据」段（如硬件表的应用段 + 小时段）
        let sections: Vec<serde_json::Value> = match s.get("sections").and_then(|v| v.as_array()) {
            Some(arr) if !arr.is_empty() => arr.clone(),
            // 兼容单区块写法
            _ => vec![s.clone()],
        };

        for (si, sec) in sections.iter().enumerate() {
            if si > 0 {
                row += 1; // 段之间空一行
            }

            if let Some(title) = sec.get("title").and_then(|v| v.as_str()) {
                let cols = widths.len().max(4) as u16;
                ws.merge_range(row, 0, row, cols - 1, title, &title_fmt)
                    .map_err(|e| e.to_string())?;
                row += 2;
            }

            if let Some(kv) = sec.get("kv").and_then(|v| v.as_array()) {
                for item in kv {
                    if let Some(arr) = item.as_array() {
                        if let Some(label) = arr.first().and_then(|v| v.as_str()) {
                            ws.write_string(row, 0, label).map_err(|e| e.to_string())?;
                        }
                        if let Some(v) = arr.get(1) {
                            write_cell(ws, row, 1, v, &num_fmt, &header_fmt)?;
                        }
                    }
                    row += 1;
                }
                row += 1;
            }

            if let Some(headers) = sec.get("headers").and_then(|v| v.as_array()) {
                if !headers.is_empty() {
                    for (c, h) in headers.iter().enumerate() {
                        ws.write_string_with_format(row, c as u16, h.as_str().unwrap_or(""), &header_fmt)
                            .map_err(|e| e.to_string())?;
                    }
                    if header_rows == 0 {
                        header_rows = row as u16 + 1;
                    }
                    row += 1;
                }
            }

            if let Some(rows) = sec.get("rows").and_then(|v| v.as_array()) {
                for r in rows {
                    if let Some(cells) = r.as_array() {
                        for (c, v) in cells.iter().enumerate() {
                            write_cell(ws, row, c as u16, v, &num_fmt, &header_fmt)?;
                        }
                        written_rows += 1;
                    }
                    row += 1;
                }
            }
        }

        if header_rows > 0 {
            ws.set_freeze_panes(header_rows as u32, 0)
                .map_err(|e| e.to_string())?;
        }
    }

    if let Some(parent) = std::path::Path::new(&file).parent() {
        let _ = std::fs::create_dir_all(parent);
    }

    wb.save(&file).map_err(|e| format!("保存失败: {e}"))?;

    let bytes = std::fs::metadata(&file).map(|m| m.len()).unwrap_or(0);
    let payload = serde_json::json!({
        "ok": true,
        "path": file,
        "bytes": bytes,
        "rows": written_rows,
        "ms": t0.elapsed().as_secs_f64() * 1000.0,
    });
    println!("{}", serde_json::to_string(&payload).map_err(|e| e.to_string())?);
    Ok(())
}

fn write_cell(
    ws: &mut rust_xlsxwriter::Worksheet,
    row: u32,
    col: u16,
    v: &serde_json::Value,
    num_fmt: &rust_xlsxwriter::Format,
    _header_fmt: &rust_xlsxwriter::Format,
) -> Result<(), String> {
    match v {
        serde_json::Value::Null => Ok(()),
        serde_json::Value::Bool(b) => ws
            .write_string(row, col, if *b { "是" } else { "否" })
            .map_err(|e| e.to_string())
            .map(|_| ()),
        serde_json::Value::Number(n) => {
            if let Some(f) = n.as_f64() {
                ws.write_number_with_format(row, col, f, num_fmt)
                    .map_err(|e| e.to_string())
                    .map(|_| ())
            } else {
                ws.write_string(row, col, &n.to_string())
                    .map_err(|e| e.to_string())
                    .map(|_| ())
            }
        }
        serde_json::Value::String(s) => ws
            .write_string(row, col, s)
            .map_err(|e| e.to_string())
            .map(|_| ()),
        other => ws
            .write_string(row, col, &other.to_string())
            .map_err(|e| e.to_string())
            .map(|_| ()),
    }
}

// ==================== 口径工具 ====================

/// 归一化进程名：去空白、去 .exe 后缀、小写。
pub(crate) fn normalize_process_name(process_name: &str) -> String {
    let p = process_name.trim();
    let p = if p.len() >= 4 && p[p.len() - 4..].eq_ignore_ascii_case(".exe") {
        &p[..p.len() - 4]
    } else {
        p
    };
    p.to_lowercase()
}

/// 分类兜底色（与 C# DefaultPalette 一致）。
pub(crate) fn default_color_for(category: &str) -> String {
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
pub(crate) fn is_productive(category: &str) -> bool {
    const PRODUCTIVE: &[&str] = &["开发", "开发工具", "办公", "办公软件", "AI 助手", "生产力"];
    !category.is_empty() && PRODUCTIVE.iter().any(|p| contains_ignore_case(category, p))
}

/// 分类解析唯一入口：映射表命中 → 会话快照 → 未分类。
pub(crate) fn resolve_category(s: &Session, cat_map: &HashMap<String, (String, String)>) -> (String, String) {
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
pub(crate) fn fmt_f0(v: f64) -> i64 {
    v.round() as i64
}

// ==================== 日期与开库 ====================

pub(crate) struct Dt {
    pub(crate) secs: f64,
    pub(crate) hour: u32,
}

/// "YYYY-MM-DD HH:MM[:SS[.fffffff]]" → 距 1970 的秒 + 小时。
/// 注意：HardwareSamples.Timestamp 是分钟粒度（`2026-11-18 20:14`），必须兼容无秒的写法。
pub(crate) fn parse_dt(s: &str) -> Option<Dt> {
    let bytes = s.as_bytes();
    if bytes.len() < 16 {
        return None;
    }
    let num = |from: usize, to: usize| -> Option<i64> { s.get(from..to)?.parse::<i64>().ok() };
    let y = num(0, 4)?;
    let mo = num(5, 7)?;
    let d = num(8, 10)?;
    let h = num(11, 13)?;
    let mi = num(14, 16)?;
    let sec = if bytes.len() >= 19 { num(17, 19)? } else { 0 };
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

pub(crate) fn civil_from_days(z: i64) -> String {
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
