//! PChabit 数据面（lib）：查询 + 聚合 + 报表写出。
//!
//! 既供 `pchabit-datad` 命令行使用，也供 Tauri 壳（P4）直接 `use`，
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

use std::collections::{BTreeMap, HashMap, HashSet};
use std::io::Read;
use std::path::{Path, PathBuf};
use std::time::Instant;

use rusqlite::{Connection, OpenFlags};

/// 子命令调度：bin 与壳共用同一套实现。
pub fn dispatch(mode: &str, db: &Path, date: &str) -> Result<(), String> {
    match mode {
        "stats" => run_stats(db),
        "detail" => run_detail(db),
        "xlsx" => run_xlsx(),
        "analytics" => run_analytics(db),
        "sankey" => run_sankey(db, date),
        "categories" => run_categories(db),
        "appcats" => run_appcats(db),
        "search" => run_search(db, date.to_string()),
        "setcat" => run_setcat(db),
        "newcat" => run_newcat(db),
        "activecat" => run_activecat(db),
        "notes" => run_notes(db),
        "notes-new" => run_notes_new(),
        "notes-rm" => run_notes_rm(date),
        "web" => run_web(db, date),
        "timeline" => run_timeline(db, date),
        "dashboard" => run_json_cmd(dashboard_json(db)),
        "net" => run_net(db, date),
        "input" => run_input(db, date),
        "reports" => run_json_cmd(reports_json(db)),
        "data" => run_json_cmd(data_json(db)),
        _ => run_probe(db, date),
    }
}

fn run_notes(db: &Path) -> Result<(), String> {
    let v = notes_json(db)?;
    println!("{}", serde_json::to_string(&v).map_err(|e| e.to_string())?);
    Ok(())
}

/// `notes-new` 子命令：建一张空白便签，打印 `{"id":"..."}`（自测/排障用）。
fn run_notes_new() -> Result<(), String> {
    let id = notes_create_blank("yellow")?;
    notes_set_pos(&id, 260.0, 200.0, 300.0, 220.0)?;
    notes_set_desktop(&id, true)?;
    println!("{{\"id\":\"{id}\"}}");
    Ok(())
}

/// `notes-rm <id>` 子命令：软删一张便签（排障/清残留用）。
fn run_notes_rm(id: &str) -> Result<(), String> {
    if id.trim().is_empty() {
        return Err("用法：notes-rm <便签 id>".to_string());
    }
    notes_delete(id.trim())?;
    println!("{{\"ok\":true,\"id\":\"{}\"}}", id.trim());
    Ok(())
}

/// `web` 子命令：`--date` 传 `2026-11-18` 或 `2026-11-12~2026-11-18`，缺省近 7 天。
fn run_web(db: &Path, range: &str) -> Result<(), String> {
    let today = today_local();
    let (start, end) = match range.split_once('~') {
        Some((a, b)) => (a.trim().to_string(), b.trim().to_string()),
        None if !range.trim().is_empty() => (range.trim().to_string(), range.trim().to_string()),
        None => (date_shift(&today, -6), today.clone()),
    };
    let v = web_json(db, &start, &end)?;
    println!("{}", serde_json::to_string(&v).map_err(|e| e.to_string())?);
    Ok(())
}

/// 统一打印入口：命令自己算好 JSON，这里只负责序列化。
fn run_json_cmd(v: Result<serde_json::Value, String>) -> Result<(), String> {
    let v = v?;
    println!("{}", serde_json::to_string(&v).map_err(|e| e.to_string())?);
    Ok(())
}

/// `--date a~b` 或单日，缺省近 7 天。
fn parse_range(range: &str) -> (String, String) {
    let today = today_local();
    match range.split_once('~') {
        Some((a, b)) => (a.trim().to_string(), b.trim().to_string()),
        None if !range.trim().is_empty() => (range.trim().to_string(), range.trim().to_string()),
        None => (date_shift(&today, -6), today),
    }
}

fn run_net(db: &Path, range: &str) -> Result<(), String> {
    let (start, end) = parse_range(range);
    run_json_cmd(net_json(db, &start, &end))
}

fn run_input(db: &Path, range: &str) -> Result<(), String> {
    let (start, end) = parse_range(range);
    run_json_cmd(input_json(db, &start, &end))
}

/// `timeline` 子命令：`--date` 传 `2026-11-18`，缺省今天。
fn run_timeline(db: &Path, date: &str) -> Result<(), String> {
    let v = timeline_json(db, date)?;
    println!("{}", serde_json::to_string(&v).map_err(|e| e.to_string())?);
    Ok(())
}

/// `sankey` 子命令：`--date` 传 `2026-11-18` 或 `2026-11-12~2026-11-18` 区间。
fn run_sankey(db: &Path, range: &str) -> Result<(), String> {
    let today = today_local();
    let (start, end) = match range.split_once('~') {
        Some((a, b)) => (a.trim().to_string(), b.trim().to_string()),
        None if !range.trim().is_empty() => (range.trim().to_string(), range.trim().to_string()),
        None => (date_shift(&today, -6), today.clone()),
    };
    let payload = sankey_json(db, &start, &end, 15)?;
    println!("{}", serde_json::to_string(&payload).map_err(|e| e.to_string())?);
    Ok(())
}

/// 分析页报告（库 API）：不打印，直接返回 JSON，供壳 / 测试调用。
pub fn analytics_json(db: &Path, req: &serde_json::Value) -> Result<serde_json::Value, String> {
    if !db.exists() {
        return Err(format!("数据库不存在：{}", db.display()));
    }
    let conn = open_readonly(db)?;
    let t0 = Instant::now();
    let mut report = analytics::run(&conn, req)?;
    report["ms"] = serde_json::json!(t0.elapsed().as_secs_f64() * 1000.0);
    Ok(report)
}

/// 默认数据库路径（%LOCALAPPDATA%\PChabit\Data\pchabit.db）。
pub fn default_db_path_public() -> PathBuf {
    default_db_path()
}

/// 数据保留清理（与采集宿主 `cleanup_before` 同一套判据，供壳的数据管理页手动触发）。
/// `DailySummaries` 不删；返回删除行数。
pub fn cleanup_old(retention_days: i64) -> Result<(String, i64), String> {
    let path = default_db();
    let conn = Connection::open(&path).map_err(|e| format!("打开库失败：{e}"))?;
    let days = retention_days.clamp(1, 3650);
    let cutoff = date_shift(&today_local(), -days);
    let sample_cutoff = format!("{cutoff} 00:00");
    let mut deleted = 0i64;
    for (sql, arg) in [
        ("DELETE FROM KeyboardSessions WHERE Date < ?1", cutoff.as_str()),
        ("DELETE FROM MouseSessions WHERE Date < ?1", cutoff.as_str()),
        ("DELETE FROM WebSessions WHERE StartTime < ?1", cutoff.as_str()),
        ("DELETE FROM AppSessions WHERE StartTime < ?1", cutoff.as_str()),
        ("DELETE FROM AppDailyStats WHERE Date < ?1", cutoff.as_str()),
        ("DELETE FROM HardwareSamples WHERE Timestamp < ?1", sample_cutoff.as_str()),
    ] {
        if let Ok(n) = conn.execute(sql, rusqlite::params![arg]) {
            deleted += n as i64;
        }
    }
    Ok((cutoff, deleted))
}

/// 立即备份：`VACUUM INTO` 生成一致性快照到 `Backups\pchabit_backup_<时间戳>.db`。
pub fn backup_now() -> Result<(String, u64), String> {
    let src = default_db();
    // 备份目录跟着实际库走：`PCHABIT_DB` 指向副本时不会往生产备份目录写。
    let dir = src
        .parent()
        .map(|p| p.join("Backups"))
        .unwrap_or_else(|| PathBuf::from("."));
    std::fs::create_dir_all(&dir).map_err(|e| format!("建备份目录失败：{e}"))?;
    let stamp = {
        let secs = std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .map(|d| d.as_secs())
            .unwrap_or(0);
        let (y, m, d) = civil_parts(((secs + 8 * 3600) / 86_400) as i64);
        let hh = ((secs + 8 * 3600) % 86_400) / 3600;
        let mm = ((secs + 8 * 3600) % 3600) / 60;
        let ss = (secs + 8 * 3600) % 60;
        format!("{y:04}{m:02}{d:02}_{hh:02}{mm:02}{ss:02}")
    };
    let dst = dir.join(format!("pchabit_backup_{stamp}.db"));
    let conn = Connection::open(&src).map_err(|e| format!("打开库失败：{e}"))?;
    conn.execute("VACUUM INTO ?1", rusqlite::params![dst.to_string_lossy()])
        .map_err(|e| format!("备份失败：{e}"))?;
    let size = std::fs::metadata(&dst).map(|m| m.len()).unwrap_or(0);
    Ok((dst.to_string_lossy().to_string(), size))
}

/// `civil_from_days` 的年月日分量，供时间戳拼名用。
fn civil_parts(z: i64) -> (i64, i64, i64) {
    let date = civil_from_days(z);
    let y: i64 = date.get(0..4).and_then(|s| s.parse().ok()).unwrap_or(1970);
    let m: i64 = date.get(5..7).and_then(|s| s.parse().ok()).unwrap_or(1);
    let d: i64 = date.get(8..10).and_then(|s| s.parse().ok()).unwrap_or(1);
    (y, m, d)
}

/// 默认数据库路径（%LOCALAPPDATA%\PChabit\Data\pchabit.db）。
///
/// 可用 `PCHABIT_DB` 环境变量覆盖——自测写库时指向库副本，避免碰真实数据。
pub fn default_db() -> PathBuf {
    match std::env::var("PCHABIT_DB") {
        Ok(p) if !p.trim().is_empty() => PathBuf::from(p),
        _ => default_db_path(),
    }
}

/// 本地今天的日期（`yyyy-MM-dd`），固定按 UTC+8 计算，不引第三方日期库。
pub fn today_local() -> String {
    let secs = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_secs())
        .unwrap_or(0);
    civil_from_days(((secs + 8 * 3600) / 86_400) as i64)
}

/// 日期加减天数，输入输出都是 `yyyy-MM-dd`。
pub fn date_shift(date: &str, days: i64) -> String {
    let y: i64 = date.get(0..4).and_then(|s| s.parse().ok()).unwrap_or(1970);
    let m: i64 = date.get(5..7).and_then(|s| s.parse().ok()).unwrap_or(1);
    let d: i64 = date.get(8..10).and_then(|s| s.parse().ok()).unwrap_or(1);
    civil_from_days(days_from_civil(y, m, d) + days)
}

/// 分类配色表（`ProgramCategories.Name -> Color`），供图表复用应用自身的色板。
pub fn category_colors(db: &Path) -> Result<std::collections::BTreeMap<String, String>, String> {
    let conn = open_readonly(db)?;
    let mut stmt = conn
        .prepare("SELECT Name, Color FROM ProgramCategories")
        .map_err(|e| e.to_string())?;
    let rows = stmt
        .query_map([], |r| {
            Ok((
                r.get::<_, Option<String>>(0)?.unwrap_or_default(),
                r.get::<_, Option<String>>(1)?.unwrap_or_default(),
            ))
        })
        .map_err(|e| e.to_string())?;
    let mut map = std::collections::BTreeMap::new();
    for row in rows.flatten() {
        if !row.0.trim().is_empty() && !row.1.trim().is_empty() {
            map.insert(row.0, row.1);
        }
    }
    Ok(map)
}

/// 应用切换关系（桑基图）：节点 = 应用使用分钟数，连线 = 切换次数。
///
/// 口径与 C# `SankeyAggregator` 对齐：
/// - 按 `StartTime` 升序遍历会话，进程名去掉 `.exe` 作为显示名；
/// - 相邻两次会话进程不同、且间隔 ≤ 30 秒，记一次切换；
/// - 时长前 `top_n` 的应用为独立节点，其余并入「其他应用」；
/// - 正反向切换到同一对节点时合并计数，自环丢弃。
pub fn sankey_json(
    db: &Path,
    start_day: &str,
    end_day: &str,
    top_n: usize,
) -> Result<serde_json::Value, String> {
    use std::collections::{BTreeMap, HashMap};

    let t0 = Instant::now();
    let conn = open_readonly(db)?;
    let start = format!("{start_day} 00:00:00");
    let end_exclusive = format!("{} 00:00:00", date_shift(end_day, 1));

    let mut stmt = conn
        .prepare(
            "SELECT StartTime, EndTime, Duration, ProcessName, Category \
             FROM AppSessions WHERE StartTime >= ?1 AND StartTime < ?2 ORDER BY StartTime",
        )
        .map_err(|e| e.to_string())?;

    struct Row {
        start: f64,
        end: f64,
        minutes: f64,
        name: String,
        category: String,
    }

    let rows: Vec<Row> = stmt
        .query_map([&start, &end_exclusive], |r| {
            let st: String = r.get::<_, Option<String>>(0)?.unwrap_or_default();
            let et: String = r.get::<_, Option<String>>(1)?.unwrap_or_default();
            let ticks: i64 = r.get::<_, Option<i64>>(2)?.unwrap_or(0);
            let proc: String = r.get::<_, Option<String>>(3)?.unwrap_or_default();
            let cat: String = r.get::<_, Option<String>>(4)?.unwrap_or_default();
            Ok(Row {
                start: parse_dt(&st).map(|d| d.secs).unwrap_or(-1.0),
                end: parse_dt(&et).map(|d| d.secs).unwrap_or(-1.0),
                minutes: ticks as f64 / 6e8,
                name: proc
                    .trim_end_matches(".exe")
                    .trim_end_matches(".EXE")
                    .to_string(),
                category: cat,
            })
        })
        .map_err(|e| e.to_string())?
        .flatten()
        .filter(|r| !r.name.trim().is_empty())
        .collect();

    let mut usage: BTreeMap<String, f64> = BTreeMap::new();
    let mut cat_hits: HashMap<String, BTreeMap<String, usize>> = HashMap::new();
    let mut transitions: BTreeMap<(String, String), i64> = BTreeMap::new();

    let mut prev: Option<(String, f64)> = None;
    for r in &rows {
        *usage.entry(r.name.clone()).or_insert(0.0) += r.minutes;
        if !r.category.trim().is_empty() {
            *cat_hits
                .entry(r.name.clone())
                .or_default()
                .entry(r.category.clone())
                .or_insert(0) += 1;
        }
        if let Some((pname, pend)) = &prev {
            if pname != &r.name && r.start >= 0.0 && *pend >= 0.0 && r.start - *pend <= 30.0 {
                *transitions
                    .entry((pname.clone(), r.name.clone()))
                    .or_insert(0) += 1;
            }
        }
        prev = Some((r.name.clone(), r.end));
    }

    let mut sorted: Vec<(String, f64)> = usage.into_iter().collect();
    sorted.sort_by(|a, b| b.1.partial_cmp(&a.1).unwrap_or(std::cmp::Ordering::Equal));

    let top: Vec<String> = sorted.iter().take(top_n).map(|(k, _)| k.clone()).collect();
    let mut nodes = Vec::new();
    let mut other_minutes = 0.0;
    for (name, mins) in &sorted {
        if top.contains(name) {
            let category = cat_hits
                .get(name)
                .and_then(|m| m.iter().max_by_key(|(_, c)| **c).map(|(k, _)| k.clone()))
                .unwrap_or_else(|| "其他".to_string());
            nodes.push(serde_json::json!({
                "name": name,
                "value": mins.round() as i64,
                "category": category,
            }));
        } else {
            other_minutes += mins;
        }
    }
    nodes.push(serde_json::json!({
        "name": "其他应用",
        "value": other_minutes.round() as i64,
        "category": "其他",
    }));

    let other = "其他应用".to_string();
    let mut merged: BTreeMap<(String, String), i64> = BTreeMap::new();
    for ((a, b), count) in &transitions {
        let src = if top.contains(a) { a.clone() } else { other.clone() };
        let dst = if top.contains(b) { b.clone() } else { other.clone() };
        if src == dst {
            continue;
        }
        if let Some(v) = merged.get_mut(&(dst.clone(), src.clone())) {
            *v += count;
            continue;
        }
        *merged.entry((src, dst)).or_insert(0) += count;
    }

    let mut links: Vec<serde_json::Value> = merged
        .into_iter()
        .map(|((s, d), c)| serde_json::json!({ "source": s, "target": d, "value": c }))
        .collect();
    links.sort_by(|a, b| {
        b["value"]
            .as_i64()
            .unwrap_or(0)
            .cmp(&a["value"].as_i64().unwrap_or(0))
    });

    let total_switch: i64 = links.iter().filter_map(|l| l["value"].as_i64()).sum();
    let summary = format!(
        "{} ~ {} · {} 个应用 · {} 条流向 · 总切换 {} 次",
        start_day,
        end_day,
        nodes.len(),
        links.len(),
        total_switch
    );

    Ok(serde_json::json!({
        "nodes": nodes,
        "links": links,
        "summary": summary,
        "ms": t0.elapsed().as_secs_f64() * 1000.0,
    }))
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

pub fn run_stats(db: &Path) -> Result<(), String> {
    let mut buf = String::new();
    std::io::stdin()
        .read_to_string(&mut buf)
        .map_err(|e| e.to_string())?;
    let req: serde_json::Value =
        serde_json::from_str(&buf).map_err(|e| format!("请求 JSON 解析失败: {e}"))?;

    let report = stats_json(db, &req)?;
    println!(
        "{}",
        serde_json::to_string(&report).map_err(|e| e.to_string())?
    );
    Ok(())
}

/// 应用统计报告（`stats` 子命令的计算体，壳也直接调它）。
pub fn stats_json(db: &Path, req: &serde_json::Value) -> Result<serde_json::Value, String> {
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
    Ok(report)
}

pub fn run_analytics(db: &Path) -> Result<(), String> {
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

pub fn run_detail(db: &Path) -> Result<(), String> {
    let mut buf = String::new();
    std::io::stdin()
        .read_to_string(&mut buf)
        .map_err(|e| e.to_string())?;
    let req: serde_json::Value =
        serde_json::from_str(&buf).map_err(|e| format!("请求 JSON 解析失败: {e}"))?;

    let report = detail_json(db, &req)?;
    println!(
        "{}",
        serde_json::to_string(&report).map_err(|e| e.to_string())?
    );
    Ok(())
}

/// 单应用详情（`detail` 子命令的计算体，壳也直接调它）。
pub fn detail_json(db: &Path, req: &serde_json::Value) -> Result<serde_json::Value, String> {
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
    Ok(report)
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
pub fn run_xlsx() -> Result<(), String> {
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
///
/// 注意：不能直接 `p[p.len()-4..]` 判断后缀——进程名可能以中文结尾（如「某工具」），
/// 按字节切会落在多字节字符中间直接 panic。先按字节比较尾部，再切。
pub(crate) fn normalize_process_name(process_name: &str) -> String {
    let p = process_name.trim();
    let bytes = p.as_bytes();
    let stripped = if bytes.len() >= 4 && bytes[bytes.len() - 4..].eq_ignore_ascii_case(b".exe") {
        &p[..p.len() - 4]
    } else {
        p
    };
    stripped.to_lowercase()
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

// ==================== 分类管理（读 + 写） ====================
//
// 写口径对齐 C# `CategoryService`：按 ProcessName 大小写不敏感匹配，命中则改 CategoryId
// 并写 UpdatedAt，未命中则新增一行（CreatedAt/UpdatedAt 都写）。
// 分类按 `IsActive = 1` 过滤，名称即唯一键（与 C# `CategoryExistsAsync` 一致）。

/// 分类清单（含停用项）：`[{id,name,color,icon,sortOrder,active}]`
pub fn categories_json(db: &Path) -> Result<serde_json::Value, String> {
    if !db.exists() {
        return Err(format!("数据库不存在：{}", db.display()));
    }
    let conn = open_readonly(db)?;
    let mut stmt = conn
        .prepare(
            "SELECT Id, Name, Color, Icon, SortOrder, IsActive FROM ProgramCategories \
             ORDER BY IsActive DESC, SortOrder, Name",
        )
        .map_err(|e| e.to_string())?;
    let rows = stmt
        .query_map([], |r| {
            Ok(serde_json::json!({
                "id": r.get::<_, i64>(0).unwrap_or(0),
                "name": r.get::<_, Option<String>>(1).unwrap_or_default(),
                "color": r.get::<_, Option<String>>(2).unwrap_or_default(),
                "icon": r.get::<_, Option<String>>(3).unwrap_or_default(),
                "sortOrder": r.get::<_, Option<i64>>(4).unwrap_or(None).unwrap_or(0),
                "active": r.get::<_, Option<i64>>(5).unwrap_or(None).unwrap_or(0) != 0,
            }))
        })
        .map_err(|e| e.to_string())?;
    Ok(serde_json::Value::Array(
        rows.filter_map(|r| r.ok()).collect(),
    ))
}

/// `ceil` 到 1 位小数，对齐 C# 的 `Math.Round(x, 1)`
fn round1_appcats(v: f64) -> f64 {
    round1(v)
}

/// 近 N 天里出现过的进程 + 当前分类：`[{processName,appName,minutes,category,categoryColorHex,mapped}]`
pub fn app_categories_json(db: &Path, days: i64) -> Result<serde_json::Value, String> {
    let today = today_local();
    let start = date_shift(&today, -(days.max(1) - 1));
    let req = serde_json::json!({
        "curStart": format!("{start} 00:00:00.0000000"),
        "curEnd": format!("{today} 23:59:59.9999999"),
        "prevStart": format!("{start} 00:00:00.0000000"),
        "prevEnd": format!("{today} 23:59:59.9999999"),
    });
    let stats = stats_json(db, &req)?;

    // 哪些进程已经显式映射过（决定"自动推断"还是"用户设定"）
    let mapped: std::collections::HashSet<String> = {
        let conn = open_readonly(db)?;
        let mut stmt = conn
            .prepare("SELECT ProcessName FROM ProgramCategoryMappings")
            .map_err(|e| e.to_string())?;
        let rows = stmt
            .query_map([], |r| {
                let p: Option<String> = r.get(0)?;
                Ok(normalize_process_name(&p.unwrap_or_default()))
            })
            .map_err(|e| e.to_string())?;
        rows.filter_map(|r| r.ok()).collect()
    };

    let items: Vec<serde_json::Value> = stats["rows"]
        .as_array()
        .map(|rows| {
            rows.iter()
                .map(|r| {
                    let proc = r["processName"].as_str().unwrap_or("").to_string();
                    serde_json::json!({
                        "processName": proc.clone(),
                        "appName": r["appName"],
                        "minutes": round1_appcats(r["durationMinutes"].as_f64().unwrap_or(0.0)),
                        "category": r["category"],
                        "categoryColorHex": r["categoryColorHex"],
                        "mapped": mapped.contains(&normalize_process_name(&proc)),
                    })
                })
                .collect()
        })
        .unwrap_or_default();

    Ok(serde_json::json!({ "days": days, "items": items }))
}

/// 改一个进程的分类：`{process, category}` → `{ok, processName, category, created}`
pub fn set_app_category(db: &Path, req: &serde_json::Value) -> Result<serde_json::Value, String> {
    let process = req
        .get("process")
        .and_then(|v| v.as_str())
        .map(|s| s.trim().to_string())
        .filter(|s| !s.is_empty())
        .ok_or_else(|| "缺少 process".to_string())?;
    let category = req
        .get("category")
        .and_then(|v| v.as_str())
        .map(|s| s.trim().to_string())
        .filter(|s| !s.is_empty())
        .ok_or_else(|| "缺少 category".to_string())?;
    if !db.exists() {
        return Err(format!("数据库不存在：{}", db.display()));
    }

    let conn = write_conn(db)?;

    let cat_id: i64 = conn
        .query_row(
            "SELECT Id FROM ProgramCategories WHERE Name = ?1 AND IsActive = 1 LIMIT 1",
            rusqlite::params![category],
            |r| r.get(0),
        )
        .map_err(|e| format!("找不到启用的分类「{category}」：{e}"))?;

    let now = now_stamp();
    let existing: Option<i64> = conn
        .query_row(
            "SELECT Id FROM ProgramCategoryMappings WHERE LOWER(ProcessName) = LOWER(?1) LIMIT 1",
            rusqlite::params![process],
            |r| r.get(0),
        )
        .ok();

    let created = match existing {
        Some(id) => {
            conn.execute(
                "UPDATE ProgramCategoryMappings SET CategoryId = ?1, UpdatedAt = ?2 WHERE Id = ?3",
                rusqlite::params![cat_id, now, id],
            )
            .map_err(|e| format!("更新映射失败：{e}"))?;
            false
        }
        None => {
            conn.execute(
                "INSERT INTO ProgramCategoryMappings (ProcessName, CategoryId, CreatedAt, UpdatedAt) \
                 VALUES (?1, ?2, ?3, ?3)",
                rusqlite::params![process, cat_id, now],
            )
            .map_err(|e| format!("新增映射失败：{e}"))?;
            true
        }
    };

    let color: String = conn
        .query_row(
            "SELECT Color FROM ProgramCategories WHERE Id = ?1",
            rusqlite::params![cat_id],
            |r| r.get::<_, Option<String>>(0),
        )
        .unwrap_or_default()
        .unwrap_or_default();

    Ok(serde_json::json!({
        "ok": true,
        "processName": process,
        "category": category,
        "categoryColorHex": color,
        "created": created,
    }))
}

/// 新建分类：`{name, color?, icon?, description?}` → `{ok, id, name, color, icon}`
///
/// 口径对齐 C# `CategoryService.CreateCategoryAsync`：名称唯一（只在启用分类里查重，与
/// `CategoryExistsAsync` 一致）、`IsSystem = 0`、`IsActive = 1`，SortOrder 排在最后。
pub fn create_category(db: &Path, req: &serde_json::Value) -> Result<serde_json::Value, String> {
    let name = req
        .get("name")
        .and_then(|v| v.as_str())
        .map(|s| s.trim().to_string())
        .filter(|s| !s.is_empty())
        .ok_or_else(|| "缺少 name".to_string())?;
    let color = req
        .get("color")
        .and_then(|v| v.as_str())
        .map(|s| s.trim().to_string())
        .filter(|s| !s.is_empty())
        .unwrap_or_else(|| default_color_for(&name));
    let icon = req
        .get("icon")
        .and_then(|v| v.as_str())
        .map(|s| s.trim().to_string())
        .filter(|s| !s.is_empty())
        .unwrap_or_else(|| "📁".to_string());
    let description = req
        .get("description")
        .and_then(|v| v.as_str())
        .map(|s| s.to_string())
        .unwrap_or_default();
    if !db.exists() {
        return Err(format!("数据库不存在：{}", db.display()));
    }

    let conn = write_conn(db)?;
    let exists: bool = conn
        .query_row(
            "SELECT COUNT(*) FROM ProgramCategories WHERE Name = ?1 AND IsActive = 1",
            rusqlite::params![name],
            |r| r.get::<_, i64>(0),
        )
        .map_err(|e| e.to_string())?
        > 0;
    if exists {
        return Err(format!("分类「{name}」已存在"));
    }

    let sort_order: i64 = conn
        .query_row(
            "SELECT COALESCE(MAX(SortOrder), 0) FROM ProgramCategories",
            [],
            |r| r.get(0),
        )
        .unwrap_or(0)
        + 1;
    let now = now_stamp();
    conn.execute(
        "INSERT INTO ProgramCategories \
         (Name, Description, Color, Icon, SortOrder, IsSystem, IsActive, CreatedAt) \
         VALUES (?1, ?2, ?3, ?4, ?5, 0, 1, ?6)",
        rusqlite::params![name, description, color, icon, sort_order, now],
    )
    .map_err(|e| format!("新增分类失败：{e}"))?;
    let id: i64 = conn.last_insert_rowid();

    Ok(serde_json::json!({
        "ok": true,
        "id": id,
        "name": name,
        "color": color,
        "icon": icon,
        "sortOrder": sort_order,
    }))
}

/// 启用/停用分类：`{id, active}` → `{ok, id, name, active}`
///
/// 停用对标 C# `CategoryService.DeleteCategoryAsync`（只把 `IsActive` 置 0，不删行）。
pub fn set_category_active(db: &Path, req: &serde_json::Value) -> Result<serde_json::Value, String> {
    let id = req
        .get("id")
        .and_then(|v| v.as_i64())
        .ok_or_else(|| "缺少 id".to_string())?;
    let active = req
        .get("active")
        .and_then(|v| v.as_bool())
        .ok_or_else(|| "缺少 active".to_string())?;
    if !db.exists() {
        return Err(format!("数据库不存在：{}", db.display()));
    }

    let conn = write_conn(db)?;
    let name: String = conn
        .query_row(
            "SELECT Name FROM ProgramCategories WHERE Id = ?1",
            rusqlite::params![id],
            |r| r.get::<_, Option<String>>(0),
        )
        .map_err(|e| format!("找不到分类 Id={id}：{e}"))?
        .unwrap_or_default();

    conn.execute(
        "UPDATE ProgramCategories SET IsActive = ?1, UpdatedAt = ?2 WHERE Id = ?3",
        rusqlite::params![if active { 1 } else { 0 }, now_stamp(), id],
    )
    .map_err(|e| format!("更新分类状态失败：{e}"))?;

    Ok(serde_json::json!({ "ok": true, "id": id, "name": name, "active": active }))
}

/// 可写连接：复用一个 busy_timeout，避免与正在运行的采集器抢锁直接失败。
fn write_conn(db: &Path) -> Result<Connection, String> {
    let conn = Connection::open(db).map_err(|e| format!("打开数据库失败：{e}"))?;
    conn.busy_timeout(std::time::Duration::from_secs(5))
        .map_err(|e| e.to_string())?;
    Ok(conn)
}

fn run_categories(db: &Path) -> Result<(), String> {
    let v = categories_json(db)?;
    println!("{}", serde_json::to_string(&v).map_err(|e| e.to_string())?);
    Ok(())
}

/// 应用搜索下沉到数据层：`search <关键字>`（关键字为空则等同 `appcats`）。
/// 匹配口径与壳里原来的前端过滤一致：进程名或应用名包含关键字，大小写不敏感。
/// 应用搜索（数据层）：进程名或应用名包含关键字，大小写不敏感。
pub fn search_json(db: &Path, keyword: &str) -> Result<serde_json::Value, String> {
    let apps = app_categories_json(db, 7)?;
    let k = keyword.to_lowercase();
    let items: Vec<serde_json::Value> = apps["items"]
        .as_array()
        .cloned()
        .unwrap_or_default()
        .into_iter()
        .filter(|a| {
            if k.is_empty() {
                return true;
            }
            let proc = a["processName"].as_str().unwrap_or("").to_lowercase();
            let app = a["appName"].as_str().unwrap_or("").to_lowercase();
            proc.contains(&k) || app.contains(&k)
        })
        .collect();
    Ok(serde_json::json!({ "ok": true, "q": keyword, "count": items.len(), "items": items }))
}

fn run_search(db: &Path, keyword: String) -> Result<(), String> {
    let out = search_json(db, &keyword)?;
    println!("{}", serde_json::to_string(&out).map_err(|e| e.to_string())?);
    Ok(())
}

fn run_appcats(db: &Path) -> Result<(), String> {
    let mut buf = String::new();
    std::io::stdin()
        .read_to_string(&mut buf)
        .map_err(|e| e.to_string())?;
    let days = serde_json::from_str::<serde_json::Value>(&buf)
        .ok()
        .and_then(|v| v.get("days").and_then(|d| d.as_i64()))
        .unwrap_or(7);
    let v = app_categories_json(db, days)?;
    println!("{}", serde_json::to_string(&v).map_err(|e| e.to_string())?);
    Ok(())
}

fn run_setcat(db: &Path) -> Result<(), String> {
    let mut buf = String::new();
    std::io::stdin()
        .read_to_string(&mut buf)
        .map_err(|e| e.to_string())?;
    let req: serde_json::Value =
        serde_json::from_str(&buf).map_err(|e| format!("请求 JSON 解析失败: {e}"))?;
    let v = set_app_category(db, &req)?;
    println!("{}", serde_json::to_string(&v).map_err(|e| e.to_string())?);
    Ok(())
}

fn run_newcat(db: &Path) -> Result<(), String> {
    let mut buf = String::new();
    std::io::stdin()
        .read_to_string(&mut buf)
        .map_err(|e| e.to_string())?;
    let req: serde_json::Value =
        serde_json::from_str(&buf).map_err(|e| format!("请求 JSON 解析失败: {e}"))?;
    let v = create_category(db, &req)?;
    println!("{}", serde_json::to_string(&v).map_err(|e| e.to_string())?);
    Ok(())
}

fn run_activecat(db: &Path) -> Result<(), String> {
    let mut buf = String::new();
    std::io::stdin()
        .read_to_string(&mut buf)
        .map_err(|e| e.to_string())?;
    let req: serde_json::Value =
        serde_json::from_str(&buf).map_err(|e| format!("请求 JSON 解析失败: {e}"))?;
    let v = set_category_active(db, &req)?;
    println!("{}", serde_json::to_string(&v).map_err(|e| e.to_string())?);
    Ok(())
}

// ==================== 仪表盘 / 网络 / 键鼠 / 周报 / 数据概览 ====================
//
// 这四块都是「只读 + 直接聚合」，没有跨表口径微调的空间，SQL 怎么写就怎么算，
// 但每个字段的含义必须写在注释里，避免后续又出现「看着像 bug」的误读。

/// 会话时长（分钟）：`ActiveDuration > 0` 用它，否则用 `Duration`，与 web 页同口径。
fn session_minutes(active: i64, duration: i64) -> f64 {
    let ticks = if active > 0 { active } else { duration };
    ticks as f64 / 600_000_000.0
}

/// 仪表盘：今日 KPI + 24 小时活跃 + Top 应用 + 今日对比昨日 + 7 天趋势 + 当前应用。
pub fn dashboard_json(db: &Path) -> Result<serde_json::Value, String> {
    if !db.exists() {
        return Err(format!("数据库不存在：{}", db.display()));
    }
    let conn = open_readonly(db)?;
    let t0 = Instant::now();
    let today = today_local();
    let yesterday = date_shift(&today, -1);
    let (ts0, ts1) = day_bounds(&today);
    let (ys0, ys1) = day_bounds(&yesterday);

    let mut total = 0.0f64;
    let mut sessions = 0i64;
    let mut apps: HashMap<String, f64> = HashMap::new();
    let mut hourly = vec![0.0f64; 24];
    let mut focus_count = 0i64;
    let mut first = String::new();
    let mut last = String::new();

    {
        let mut stmt = conn
            .prepare(
                "SELECT ProcessName, AppName, StartTime, EndTime, Duration, ActiveDuration \
                 FROM AppSessions WHERE StartTime >= ?1 AND StartTime < ?2 ORDER BY StartTime",
            )
            .map_err(|e| e.to_string())?;
        let mut rows = stmt.query([&ts0, &ts1]).map_err(|e| e.to_string())?;
        while let Some(row) = rows.next().map_err(|e| e.to_string())? {
            let proc: String = row.get::<_, Option<String>>(0).unwrap_or(None).unwrap_or_default();
            let app: String = row.get::<_, Option<String>>(1).unwrap_or(None).unwrap_or_default();
            let st: String = row.get::<_, Option<String>>(3).unwrap_or(None).unwrap_or_default();
            let dur: i64 = row.get::<_, Option<i64>>(4).unwrap_or(None).unwrap_or(0);
            let act: i64 = row.get::<_, Option<i64>>(5).unwrap_or(None).unwrap_or(0);
            let mins = session_minutes(act, dur);
            if mins <= 0.0 {
                continue;
            }
            total += mins;
            sessions += 1;
            if mins >= 25.0 {
                focus_count += 1;
            }
            let key = if app.trim().is_empty() {
                let n = normalize_process_name(&proc);
                if n.is_empty() { "未知应用".to_string() } else { n }
            } else {
                app.trim().to_string()
            };
            *apps.entry(key).or_insert(0.0) += mins;
            if let Some(d) = parse_dt(&st) {
                hourly[d.hour as usize] += mins;
            }
            if first.is_empty() {
                first = st.clone();
            }
            last = st.clone();
        }
    }

    let mut yesterday_total = 0.0f64;
    {
        let mut stmt = conn
            .prepare(
                "SELECT Duration, ActiveDuration FROM AppSessions WHERE StartTime >= ?1 AND StartTime < ?2",
            )
            .map_err(|e| e.to_string())?;
        let mut rows = stmt.query([&ys0, &ys1]).map_err(|e| e.to_string())?;
        while let Some(row) = rows.next().map_err(|e| e.to_string())? {
            let dur: i64 = row.get::<_, Option<i64>>(0).unwrap_or(None).unwrap_or(0);
            let act: i64 = row.get::<_, Option<i64>>(1).unwrap_or(None).unwrap_or(0);
            yesterday_total += session_minutes(act, dur);
        }
    }

    let mut top: Vec<serde_json::Value> = Vec::new();
    let mut app_vec: Vec<(String, f64)> = apps.into_iter().collect();
    app_vec.sort_by(|a, b| b.1.partial_cmp(&a.1).unwrap_or(std::cmp::Ordering::Equal));
    let app_count = app_vec.len();
    for (name, mins) in app_vec.iter().take(8) {
        top.push(serde_json::json!({
            "name": name,
            "minutes": round1(*mins),
            "share": if total > 0.0 { round1(mins / total * 100.0) } else { 0.0 },
        }));
    }

    // 键鼠：KeyboardSessions / MouseSessions 按 Date 过滤（Date 是 "YYYY-MM-DD"）
    let mut keys = 0i64;
    let mut clicks = 0i64;
    let mut scroll = 0i64;
    {
        let mut stmt = conn
            .prepare("SELECT COALESCE(SUM(TotalKeyPresses),0) FROM KeyboardSessions WHERE substr(Date,1,10) = ?1")
            .map_err(|e| e.to_string())?;
        keys = stmt.query_row([&today], |r| r.get::<_, Option<i64>>(0)).unwrap_or(None).unwrap_or(0);
        let mut stmt2 = conn
            .prepare(
                "SELECT COALESCE(SUM(LeftClickCount+RightClickCount+MiddleClickCount),0), \
                 COALESCE(SUM(ScrollCount),0) FROM MouseSessions WHERE substr(Date,1,10) = ?1",
            )
            .map_err(|e| e.to_string())?;
        if let Ok((c, s)) = stmt2.query_row([&today], |r| {
            Ok((
                r.get::<_, Option<i64>>(0)?.unwrap_or_default(),
                r.get::<_, Option<i64>>(1)?.unwrap_or_default(),
            ))
        }) {
            clicks = c;
            scroll = s;
        }
    }

    // 今日网页 / 网络 / 硬件：**不从 DailySummaries 读**。
    // `DailySummaries` 的今日行要等当天结束才回填（实测今天这行 ActiveMinutes=0、WebMinutes=0、
    // MetricsVersion 还落后一天），拿它做「今日」KPI 会全天显示 0。所以这三项直接现算，
    // 日汇总只在 7 天趋势里用。
    let mut web_minutes = 0.0f64;
    if let Ok(Some(ticks)) = conn.query_row(
        "SELECT COALESCE(SUM(CASE WHEN ActiveDuration > 0 THEN ActiveDuration ELSE Duration END),0) \
         FROM WebSessions WHERE StartTime >= ?1 AND StartTime < ?2",
        [&ts0, &ts1],
        |r| r.get::<_, Option<i64>>(0),
    ) {
        web_minutes = ticks as f64 / 600_000_000.0;
    }

    let mut net_down = 0i64;
    let mut net_up = 0i64;
    if let Ok((d, u)) = conn.query_row(
        "SELECT COALESCE(SUM(BytesDown),0), COALESCE(SUM(BytesUp),0) \
         FROM ProcessNetworkUsages WHERE Date = ?1",
        [&today],
        |r| {
            Ok((
                r.get::<_, Option<i64>>(0)?.unwrap_or_default(),
                r.get::<_, Option<i64>>(1)?.unwrap_or_default(),
            ))
        },
    ) {
        net_down = d;
        net_up = u;
    }

    let mut cpu_avg: Option<f64> = None;
    let mut gpu_avg: Option<f64> = None;
    let mut gpu_temp: Option<f64> = None;
    if let Ok((c, g, gt)) = conn.query_row(
        "SELECT AVG(CpuLoadAvg), AVG(GpuLoadAvg), MAX(GpuTempMax) FROM HardwareSamples \
         WHERE Timestamp >= ?1 AND Timestamp < ?2",
        [&format!("{today} 00:00"), &format!("{} 00:00", date_shift(&today, 1))],
        |r| {
            Ok((
                r.get::<_, Option<f64>>(0)?,
                r.get::<_, Option<f64>>(1)?,
                r.get::<_, Option<f64>>(2)?,
            ))
        },
    ) {
        cpu_avg = c;
        gpu_avg = g;
        gpu_temp = gt;
    }

    // 7 天趋势：DailySummaries 有行就用它，没行就现场从会话算
    let mut trend: Vec<serde_json::Value> = Vec::new();
    for i in (0..7).rev() {
        let d = date_shift(&today, -i);
        let mut mins = 0.0f64;
        let mut from_summary = false;
        if let Ok(v) = conn.query_row(
            "SELECT ActiveMinutes FROM DailySummaries WHERE Date = ?1",
            [&d],
            |r| r.get::<_, Option<f64>>(0),
        ) {
            if let Some(m) = v {
                mins = m;
                from_summary = true;
            }
        }
        if !from_summary {
            let (a, b) = day_bounds(&d);
            let mut stmt = conn
                .prepare("SELECT Duration, ActiveDuration FROM AppSessions WHERE StartTime >= ?1 AND StartTime < ?2")
                .map_err(|e| e.to_string())?;
            let mut rows = stmt.query([&a, &b]).map_err(|e| e.to_string())?;
            while let Some(row) = rows.next().map_err(|e| e.to_string())? {
                let dur: i64 = row.get::<_, Option<i64>>(0).unwrap_or(None).unwrap_or(0);
                let act: i64 = row.get::<_, Option<i64>>(1).unwrap_or(None).unwrap_or(0);
                mins += session_minutes(act, dur);
            }
        }
        trend.push(serde_json::json!({
            "date": d,
            "label": d.get(5..10).unwrap_or("").to_string(),
            "minutes": round1(mins),
            "source": if from_summary { "summary" } else { "sessions" },
        }));
    }

    // 最近活动：今日 StartTime 最大的一条会话（DB 里会话是闭区间，没有「进行中」状态，
    // 所以只给「最近一次活动」，不假装是「当前应用」）
    let mut recent = serde_json::Value::Null;
    if let Ok((app, st)) = conn.query_row(
        "SELECT COALESCE(NULLIF(AppName,''), ProcessName), StartTime FROM AppSessions \
         WHERE StartTime >= ?1 AND StartTime < ?2 ORDER BY StartTime DESC LIMIT 1",
        [&ts0, &ts1],
        |r| {
            Ok((
                r.get::<_, Option<String>>(0).unwrap_or(None).unwrap_or_default(),
                r.get::<_, Option<String>>(1).unwrap_or(None).unwrap_or_default(),
            ))
        },
    ) {
        if !app.trim().is_empty() {
            recent = serde_json::json!({
                "app": app,
                "at": st.get(11..16).unwrap_or(""),
            });
        }
    }

    let delta = if yesterday_total > 0.0 {
        round1((total - yesterday_total) / yesterday_total * 100.0)
    } else {
        0.0
    };

    Ok(serde_json::json!({
        "ok": true,
        "date": today,
        "label": format!("{}月{}日",
            today.get(5..7).unwrap_or("?").trim_start_matches('0'),
            today.get(8..10).unwrap_or("?").trim_start_matches('0')),
        "weekday": weekday_label(&today),
        "totalMinutes": round1(total),
        "yesterdayMinutes": round1(yesterday_total),
        "deltaPercent": delta,
        "appCount": app_count,
        "sessions": sessions,
        "focusCount": focus_count,
        "firstActive": first.get(11..16).unwrap_or("").to_string(),
        "lastActive": last.get(11..16).unwrap_or("").to_string(),
        "keys": keys,
        "clicks": clicks,
        "scroll": scroll,
        "hourly": hourly.iter().map(|v| round1(*v)).collect::<Vec<f64>>(),
        "topApps": top,
        "webMinutes": round1(web_minutes),
        "netDown": net_down,
        "netDownText": human_bytes(net_down),
        "netUpText": human_bytes(net_up),
        "cpuLoadAvg": cpu_avg,
        "gpuLoadAvg": gpu_avg,
        "gpuTempMax": gpu_temp,
        "trend": trend,
        "recent": recent,
        "ms": t0.elapsed().as_secs_f64() * 1000.0,
    }))
}

/// 网络流量：`--date a~b`（缺省近 7 天）。上行/下行都来自 `ProcessNetworkUsages`，
/// 峰值来自 `NetworkTrafficSamples`（采样流量的瞬时峰值，两者统计口径不同）。
pub fn net_json(db: &Path, start: &str, end: &str) -> Result<serde_json::Value, String> {
    if !db.exists() {
        return Err(format!("数据库不存在：{}", db.display()));
    }
    let conn = open_readonly(db)?;
    let t0 = Instant::now();

    let mut total_up = 0i64;
    let mut total_down = 0i64;
    let mut by_proc: HashMap<String, (i64, i64)> = HashMap::new();
    let mut hourly_down = vec![0i64; 24];
    let mut daily: BTreeMap<String, (i64, i64)> = BTreeMap::new();

    {
        let mut stmt = conn
            .prepare(
                "SELECT Date, Hour, ProcessName, BytesUp, BytesDown FROM ProcessNetworkUsages \
                 WHERE Date >= ?1 AND Date <= ?2 ORDER BY Date, Hour",
            )
            .map_err(|e| e.to_string())?;
        let mut rows = stmt.query([start, end]).map_err(|e| e.to_string())?;
        while let Some(row) = rows.next().map_err(|e| e.to_string())? {
            let d: String = row.get::<_, Option<String>>(0).unwrap_or(None).unwrap_or_default();
            let h: i64 = row.get::<_, Option<i64>>(1).unwrap_or(None).unwrap_or(0);
            let proc: String = row.get::<_, Option<String>>(2).unwrap_or(None).unwrap_or_default();
            let up: i64 = row.get::<_, Option<i64>>(3).unwrap_or(None).unwrap_or(0);
            let down: i64 = row.get::<_, Option<i64>>(4).unwrap_or(None).unwrap_or(0);
            total_up += up;
            total_down += down;
            if (0..24).contains(&h) {
                hourly_down[h as usize] += down;
            }
            let e = by_proc.entry(normalize_process_name(&proc)).or_insert((0, 0));
            e.0 += up;
            e.1 += down;
            let de = daily.entry(d).or_insert((0, 0));
            de.0 += up;
            de.1 += down;
        }
    }

    let mut procs: Vec<serde_json::Value> = Vec::new();
    let mut v: Vec<(String, i64, i64)> =
        by_proc.into_iter().map(|(k, (u, d))| (k, u, d)).collect();
    v.sort_by(|a, b| b.2.cmp(&a.2));
    for (name, up, down) in v.iter().take(15) {
        procs.push(serde_json::json!({
            "name": name,
            "up": up,
            "down": down,
            "downText": human_bytes(*down),
            "upText": human_bytes(*up),
            "share": if total_down > 0 { round1(*down as f64 / total_down as f64 * 100.0) } else { 0.0 },
        }));
    }

    // 峰值取 99 分位，不取 MAX：`NetworkTrafficSamples.PeakDownBps` 里有明显脏样本
    // （实测 7 天内 MAX 到 7e11 B/s，约 700 GB/s），MAX 会把整张卡片带成荒谬值。
    let peak_down = percentile(&conn, "PeakDownBps", start, end, 0.99).unwrap_or(0.0);
    let peak_up = percentile(&conn, "PeakUpBps", start, end, 0.99).unwrap_or(0.0);

    Ok(serde_json::json!({
        "ok": true,
        "range": { "start": start, "end": end },
        "totalUp": total_up,
        "totalDown": total_down,
        "totalDownText": human_bytes(total_down),
        "totalUpText": human_bytes(total_up),
        "peakDownText": format!("{}/s（99 分位）", human_bytes(peak_down as i64)),
        "peakUpText": format!("{}/s（99 分位）", human_bytes(peak_up as i64)),
        "hourlyDown": hourly_down,
        "daily": daily.into_iter().map(|(d, (u, dn))| serde_json::json!({
            "date": d,
            "label": d.get(5..10).unwrap_or("").to_string(),
            "up": u,
            "down": dn,
            "downText": human_bytes(dn),
        })).collect::<Vec<serde_json::Value>>(),
        "processes": procs,
        "ms": t0.elapsed().as_secs_f64() * 1000.0,
    }))
}

/// 键鼠统计：`KeyboardSessions` / `MouseSessions` 按日期区间聚合。
pub fn input_json(db: &Path, start: &str, end: &str) -> Result<serde_json::Value, String> {
    if !db.exists() {
        return Err(format!("数据库不存在：{}", db.display()));
    }
    let conn = open_readonly(db)?;
    let t0 = Instant::now();

    let mut keys = 0i64;
    let mut bursts = 0i64;
    let mut key_rows = 0i64;
    let mut typed_by_proc: HashMap<String, i64> = HashMap::new();
    let mut hourly_keys = vec![0i64; 24];
    {
        let mut stmt = conn
            .prepare(
                "SELECT Hour, ProcessName, TotalKeyPresses, TypingBursts FROM KeyboardSessions \
                 WHERE substr(Date,1,10) >= ?1 AND substr(Date,1,10) <= ?2",
            )
            .map_err(|e| e.to_string())?;
        let mut rows = stmt.query([start, end]).map_err(|e| e.to_string())?;
        while let Some(row) = rows.next().map_err(|e| e.to_string())? {
            let h: i64 = row.get::<_, Option<i64>>(0).unwrap_or(None).unwrap_or(0);
            let proc: String = row.get::<_, Option<String>>(1).unwrap_or(None).unwrap_or_default();
            let k: i64 = row.get::<_, Option<i64>>(2).unwrap_or(None).unwrap_or(0);
            let b: String = row.get::<_, Option<String>>(3).unwrap_or(None).unwrap_or_default();
            keys += k;
            key_rows += 1;
            if !b.trim().is_empty() && b.trim() != "[]" {
                bursts += 1;
            }
            if (0..24).contains(&h) {
                hourly_keys[h as usize] += k;
            }
            *typed_by_proc.entry(normalize_process_name(&proc)).or_insert(0) += k;
        }
    }

    let mut clicks = 0i64;
    let mut scroll = 0i64;
    let mut move_dist = 0.0f64;
    let mut move_events = 0i64;
    let mut click_by_proc: HashMap<String, i64> = HashMap::new();
    let mut hourly_clicks = vec![0i64; 24];
    {
        let mut stmt = conn
            .prepare(
                "SELECT Hour, ProcessName, LeftClickCount, RightClickCount, MiddleClickCount, \
                 ScrollCount, TotalMoveDistance, MoveEventCount FROM MouseSessions \
                 WHERE substr(Date,1,10) >= ?1 AND substr(Date,1,10) <= ?2",
            )
            .map_err(|e| e.to_string())?;
        let mut rows = stmt.query([start, end]).map_err(|e| e.to_string())?;
        while let Some(row) = rows.next().map_err(|e| e.to_string())? {
            let h: i64 = row.get::<_, Option<i64>>(0).unwrap_or(None).unwrap_or(0);
            let proc: String = row.get::<_, Option<String>>(1).unwrap_or(None).unwrap_or_default();
            let l: i64 = row.get::<_, Option<i64>>(2).unwrap_or(None).unwrap_or(0);
            let r: i64 = row.get::<_, Option<i64>>(3).unwrap_or(None).unwrap_or(0);
            let m: i64 = row.get::<_, Option<i64>>(4).unwrap_or(None).unwrap_or(0);
            let s: i64 = row.get::<_, Option<i64>>(5).unwrap_or(None).unwrap_or(0);
            let dist: f64 = row.get::<_, Option<f64>>(6).unwrap_or(None).unwrap_or(0.0);
            let mv: i64 = row.get::<_, Option<i64>>(7).unwrap_or(None).unwrap_or(0);
            let c = l + r + m;
            clicks += c;
            scroll += s;
            move_dist += dist;
            move_events += mv;
            if (0..24).contains(&h) {
                hourly_clicks[h as usize] += c;
            }
            *click_by_proc.entry(normalize_process_name(&proc)).or_insert(0) += c;
        }
    }

    let mut top_keys: Vec<serde_json::Value> = {
        let mut v: Vec<(String, i64)> = typed_by_proc.into_iter().collect();
        v.sort_by(|a, b| b.1.cmp(&a.1));
        v.into_iter().take(10).map(|(n, k)| serde_json::json!({ "name": n, "keys": k })).collect()
    };
    if top_keys.is_empty() {
        top_keys = Vec::new();
    }

    let mut top_clicks: Vec<serde_json::Value> = {
        let mut v: Vec<(String, i64)> = click_by_proc.into_iter().collect();
        v.sort_by(|a, b| b.1.cmp(&a.1));
        v.into_iter().take(10).map(|(n, c)| serde_json::json!({ "name": n, "clicks": c })).collect()
    };
    if top_clicks.is_empty() {
        top_clicks = Vec::new();
    }

    Ok(serde_json::json!({
        "ok": true,
        "range": { "start": start, "end": end },
        "keys": keys,
        "keyRows": key_rows,
        "clicks": clicks,
        "scroll": scroll,
        "bursts": bursts,
        "moveDistance": round1(move_dist),
        "moveText": human_meters(move_dist),
        "moveEvents": move_events,
        "keysPerHour": if key_rows > 0 { round1(keys as f64 / key_rows as f64) } else { 0.0 },
        "hourlyKeys": hourly_keys,
        "hourlyClicks": hourly_clicks,
        "topKeys": top_keys,
        "topClicks": top_clicks,
        "ms": t0.elapsed().as_secs_f64() * 1000.0,
    }))
}

/// 周报（历史报告）：把 `DailySummaries` 按自然周汇总。
pub fn reports_json(db: &Path) -> Result<serde_json::Value, String> {
    if !db.exists() {
        return Err(format!("数据库不存在：{}", db.display()));
    }
    let conn = open_readonly(db)?;
    let t0 = Instant::now();

    let mut stmt = conn
        .prepare(
            "SELECT Date, ActiveMinutes, FocusMinutesV2, WebMinutes, TotalKeys, \
             TotalMouseClicks, AppSwitches FROM DailySummaries ORDER BY Date",
        )
        .map_err(|e| e.to_string())?;
    let mut rows = stmt.query([]).map_err(|e| e.to_string())?;

    let mut weeks: BTreeMap<String, (f64, f64, f64, i64, i64, i64, i64)> = BTreeMap::new();
    while let Some(row) = rows.next().map_err(|e| e.to_string())? {
        let d: String = row.get::<_, Option<String>>(0).unwrap_or(None).unwrap_or_default();
        let active: f64 = row.get::<_, Option<f64>>(1).unwrap_or(None).unwrap_or(0.0);
        let focus: f64 = row.get::<_, Option<f64>>(2).unwrap_or(None).unwrap_or(0.0);
        let web: f64 = row.get::<_, Option<f64>>(3).unwrap_or(None).unwrap_or(0.0);
        let keys: i64 = row.get::<_, Option<i64>>(4).unwrap_or(None).unwrap_or(0);
        let clicks: i64 = row.get::<_, Option<i64>>(5).unwrap_or(None).unwrap_or(0);
        let sw: i64 = row.get::<_, Option<i64>>(6).unwrap_or(None).unwrap_or(0);
        if d.len() < 10 {
            continue;
        }
        // 周一为一周起点
        let weekday = parse_dt(&format!("{d} 00:00:00.0000000"))
            .map(|x| (x.secs / 86400.0).round() as i64)
            .unwrap_or(0);
        let back = (weekday + 3).rem_euclid(7); // 1970-01-01 周四
        let monday = date_shift(&d, -back);
        let e = weeks.entry(monday).or_insert((0.0, 0.0, 0.0, 0, 0, 0, 0));
        e.0 += active;
        e.1 += focus;
        e.2 += web;
        e.3 += keys;
        e.4 += clicks;
        e.5 += sw;
        e.6 += 1;
    }

    let mut list: Vec<serde_json::Value> = weeks
        .iter()
        .map(|(start, (active, focus, web, keys, clicks, sw, days))| {
            serde_json::json!({
                "start": start,
                "end": date_shift(start, 6),
                "label": format!("{} ~ {}",
                    start.get(5..10).unwrap_or(""),
                    date_shift(start, 6).get(5..10).unwrap_or("")),
                "activeMinutes": round1(*active),
                "focusMinutes": round1(*focus),
                "webMinutes": round1(*web),
                "keys": keys,
                "clicks": clicks,
                "switches": sw,
                "days": days,
                "avgActiveMinutes": if *days > 0 { round1(active / *days as f64) } else { 0.0 },
                "focusRatio": if *active > 0.0 { round1(focus / active * 100.0) } else { 0.0 },
            })
        })
        .collect();
    list.reverse();

    Ok(serde_json::json!({
        "ok": true,
        "weeks": list,
        "count": list.len(),
        "ms": t0.elapsed().as_secs_f64() * 1000.0,
    }))
}

/// 数据概览：库文件大小、关键表行数、备份记录（全部只读）。
pub fn data_json(db: &Path) -> Result<serde_json::Value, String> {
    if !db.exists() {
        return Err(format!("数据库不存在：{}", db.display()));
    }
    let conn = open_readonly(db)?;
    let t0 = Instant::now();

    let tables = [
        "AppSessions",
        "WebSessions",
        "KeyboardSessions",
        "MouseSessions",
        "HardwareSamples",
        "ProcessNetworkUsages",
        "NetworkTrafficSamples",
        "BrowserHistoryItems",
        "StickyNotes",
        "DailySummaries",
        "AppDailyStats",
    ];
    let mut counts: Vec<serde_json::Value> = Vec::new();
    for t in tables {
        let n: i64 = conn
            .query_row(&format!("SELECT COUNT(*) FROM {t}"), [], |r| r.get::<_, i64>(0))
            .unwrap_or(-1);
        counts.push(serde_json::json!({ "table": t, "rows": n }));
    }

    let mut backups: Vec<serde_json::Value> = Vec::new();
    if let Ok(mut stmt) = conn.prepare(
        "SELECT FilePath, CreatedAt, FileSize, IsAutomatic, Description FROM BackupRecords \
         ORDER BY CreatedAt DESC LIMIT 10",
    ) {
        if let Ok(mut rows) = stmt.query([]) {
            while let Ok(Some(row)) = rows.next() {
                let path: String = row.get::<_, Option<String>>(0).unwrap_or(None).unwrap_or_default();
                let created: String = row.get::<_, Option<String>>(1).unwrap_or(None).unwrap_or_default();
                let size: i64 = row.get::<_, Option<i64>>(2).unwrap_or(None).unwrap_or(0);
                let auto: i64 = row.get::<_, Option<i64>>(3).unwrap_or(None).unwrap_or(0);
                let desc: String = row.get::<_, Option<String>>(4).unwrap_or(None).unwrap_or_default();
                let exists = Path::new(&path).exists();
                backups.push(serde_json::json!({
                    "path": path,
                    "createdAt": created,
                    "sizeText": human_bytes(size),
                    "automatic": auto != 0,
                    "description": desc,
                    "exists": exists,
                }));
            }
        }
    }

    let db_size = std::fs::metadata(db).map(|m| m.len() as i64).unwrap_or(0);
    let wal_size = std::fs::metadata(format!("{}-wal", db.display()))
        .map(|m| m.len() as i64)
        .unwrap_or(0);
    let shm_size = std::fs::metadata(format!("{}-shm", db.display()))
        .map(|m| m.len() as i64)
        .unwrap_or(0);

    Ok(serde_json::json!({
        "ok": true,
        "path": db.display().to_string(),
        "sizeText": human_bytes(db_size),
        "walText": human_bytes(wal_size),
        "shmText": human_bytes(shm_size),
        "totalText": human_bytes(db_size + wal_size),
        "tables": counts,
        "backups": backups,
        "ms": t0.elapsed().as_secs_f64() * 1000.0,
    }))
}

/// 采样表某列的 p 分位（p=0.99 即「99% 的采样不超过它」）。无数据返回 None。
fn percentile(conn: &Connection, col: &str, start: &str, end: &str, p: f64) -> Option<f64> {
    let lo = format!("{start} 00:00");
    let hi = format!("{} 00:00", date_shift(end, 1));
    let total: i64 = conn
        .query_row(
            "SELECT COUNT(*) FROM NetworkTrafficSamples WHERE Timestamp >= ?1 AND Timestamp < ?2",
            [&lo, &hi],
            |r| r.get::<_, i64>(0),
        )
        .ok()?;
    if total <= 0 {
        return None;
    }
    let offset = ((total as f64) * p).floor() as i64;
    let sql = format!(
        "SELECT {col} FROM NetworkTrafficSamples WHERE Timestamp >= ?1 AND Timestamp < ?2 \
         ORDER BY {col} LIMIT 1 OFFSET ?3"
    );
    conn.query_row(&sql, rusqlite::params![&lo, &hi, offset], |r| {
        r.get::<_, Option<f64>>(0)
    })
    .ok()
    .flatten()
}

fn human_bytes(v: i64) -> String {
    let f = v as f64;
    if f >= 1024.0 * 1024.0 * 1024.0 {
        format!("{:.2} GB", f / 1024.0 / 1024.0 / 1024.0)
    } else if f >= 1024.0 * 1024.0 {
        format!("{:.1} MB", f / 1024.0 / 1024.0)
    } else if f >= 1024.0 {
        format!("{:.1} KB", f / 1024.0)
    } else {
        format!("{v} B")
    }
}

fn human_meters(px: f64) -> String {
    // 钩子记的是像素位移；按 96 DPI 约 1 厘米 ≈ 37.8 像素粗算，仅用于展示
    let meters = px / 3779.5;
    if meters >= 1000.0 {
        format!("{:.1} 公里", meters / 1000.0)
    } else {
        format!("{:.1} 米", meters)
    }
}

// ==================== 时间线（某一天的会话甘特） ====================
//
// 只读 `AppSessions`，把一天的会话压成「应用 × 时段」的块，供壳里画 24 小时甘特图。
// 相邻且间隔 ≤ 60 秒的同应用会话合并成一段（否则一次切窗口就碎成几百段），
// 跨零点的会话按当日边界裁剪；时长一律由起止时间算，不读 `Duration` 列。

/// 时间线：`{ok, date, label, apps[], segments[], totalMinutes, rawSessions, ms}`
pub fn timeline_json(db: &Path, date: &str) -> Result<serde_json::Value, String> {
    if !db.exists() {
        return Err(format!("数据库不存在：{}", db.display()));
    }
    let date = if date.trim().is_empty() {
        today_local()
    } else {
        date.trim().to_string()
    };
    let day_start = parse_dt(&format!("{date} 00:00:00.0000000"))
        .ok_or_else(|| format!("日期解析失败：{date}"))?;
    let next = date_shift(&date, 1);
    let start_ts = format!("{date} 00:00:00.0000000");
    let end_ts = format!("{next} 00:00:00.0000000");

    let conn = open_readonly(db)?;
    let t0 = Instant::now();

    // 分类色板（启用分类）
    let mut colors: BTreeMap<String, String> = BTreeMap::new();
    {
        let mut stmt = conn
            .prepare("SELECT Name, Color FROM ProgramCategories WHERE IsActive = 1")
            .map_err(|e| e.to_string())?;
        let rows = stmt
            .query_map([], |r| {
                Ok((
                    r.get::<_, Option<String>>(0)?.unwrap_or_default(),
                    r.get::<_, Option<String>>(1)?.unwrap_or_default(),
                ))
            })
            .map_err(|e| e.to_string())?;
        for row in rows.flatten() {
            if !row.0.trim().is_empty() && !row.1.trim().is_empty() {
                colors.insert(row.0, row.1);
            }
        }
    }
    // 进程 → 分类（映射表口径）
    let cat_map = load_category_map(&conn)?;

    let mut lanes: BTreeMap<String, Vec<(f64, f64)>> = BTreeMap::new();
    let mut app_cat: HashMap<String, String> = HashMap::new();
    let mut raw_count: usize = 0;

    {
        let mut stmt = conn
            .prepare(
                "SELECT ProcessName, AppName, Category, StartTime, EndTime FROM AppSessions \
                 WHERE StartTime >= ?1 AND StartTime < ?2 ORDER BY StartTime",
            )
            .map_err(|e| e.to_string())?;
        let mut rows = stmt.query([&start_ts, &end_ts]).map_err(|e| e.to_string())?;
        while let Some(row) = rows.next().map_err(|e| e.to_string())? {
            let proc: String = row.get::<_, Option<String>>(0).unwrap_or(None).unwrap_or_default();
            let app_name: String = row.get::<_, Option<String>>(1).unwrap_or(None).unwrap_or_default();
            let cat: String = row.get::<_, Option<String>>(2).unwrap_or(None).unwrap_or_default();
            let st: String = row.get::<_, Option<String>>(3).unwrap_or(None).unwrap_or_default();
            let et: Option<String> = row.get::<_, Option<String>>(4).unwrap_or(None);

            let Some(s) = parse_dt(&st) else { continue };
            let e = et
                .as_deref()
                .and_then(parse_dt)
                .map(|d| d.secs)
                .unwrap_or(s.secs + 60.0);
            let s_min = ((s.secs - day_start.secs) / 60.0).clamp(0.0, 1440.0);
            let e_min = ((e - day_start.secs) / 60.0).clamp(0.0, 1440.0);
            if e_min <= s_min {
                continue;
            }
            raw_count += 1;

            let key = if app_name.trim().is_empty() {
                let n = normalize_process_name(&proc);
                if n.is_empty() { "未知应用".to_string() } else { n }
            } else {
                app_name.trim().to_string()
            };

            if !app_cat.contains_key(&key) {
                let fallback = cat_map
                    .get(&normalize_process_name(&proc))
                    .map(|v| v.0.clone())
                    .unwrap_or_default();
                let resolved = if cat.trim().is_empty() { fallback } else { cat.trim().to_string() };
                app_cat.insert(key.clone(), resolved);
            }

            let list = lanes.entry(key).or_default();
            match list.last_mut() {
                Some(last) if s_min - last.1 <= 1.0 => {
                    if e_min > last.1 {
                        last.1 = e_min;
                    }
                }
                _ => list.push((s_min, e_min)),
            }
        }
    }

    // 应用汇总（按总时长倒序）
    let mut apps: Vec<serde_json::Value> = Vec::new();
    let mut segments: Vec<serde_json::Value> = Vec::new();
    let mut total: f64 = 0.0;
    for (name, blocks) in &lanes {
        let mut mins: f64 = 0.0;
        for (a, b) in blocks {
            mins += b - a;
            segments.push(serde_json::json!({
                "app": name,
                "start": round1(*a),
                "end": round1(*b),
                "minutes": round1(b - a),
            }));
        }
        total += mins;
        let cat = app_cat.get(name).cloned().unwrap_or_default();
        let color = colors
            .get(&cat)
            .cloned()
            .unwrap_or_else(|| category_fallback_color(&cat).to_string());
        apps.push(serde_json::json!({
            "name": name,
            "category": if cat.is_empty() { "未分类".to_string() } else { cat },
            "color": color,
            "minutes": round1(mins),
            "blocks": blocks.len(),
        }));
    }
    apps.sort_by(|a, b| {
        b["minutes"].as_f64().unwrap_or(0.0)
            .partial_cmp(&a["minutes"].as_f64().unwrap_or(0.0))
            .unwrap_or(std::cmp::Ordering::Equal)
    });
    segments.sort_by(|a, b| {
        a["start"].as_f64().unwrap_or(0.0)
            .partial_cmp(&b["start"].as_f64().unwrap_or(0.0))
            .unwrap_or(std::cmp::Ordering::Equal)
    });

    let hour = date.get(11..13).unwrap_or("00");
    Ok(serde_json::json!({
        "ok": true,
        "date": date,
        "label": format!("{}月{}日",
            date.get(5..7).unwrap_or("?").trim_start_matches('0'),
            date.get(8..10).unwrap_or("?").trim_start_matches('0')),
        "weekday": weekday_label(&date),
        "apps": apps,
        "segments": segments,
        "totalMinutes": round1(total),
        "appCount": apps.len(),
        "rawSessions": raw_count,
        "ms": t0.elapsed().as_secs_f64() * 1000.0,
        // hour 只用来占位，壳里按本地时间选默认日期
        "hourHint": hour,
    }))
}

/// 分类色兜底：没有配置颜色时按分类名给一个稳定色。
fn category_fallback_color(cat: &str) -> &'static str {
    match cat {
        "开发" => "#3B82F6",
        "浏览" => "#06B6D4",
        "沟通" => "#10B981",
        "办公" => "#F59E0B",
        "娱乐" => "#EF4444",
        "设计" => "#8B5CF6",
        "媒体创作" => "#EC4899",
        "云盘下载" => "#14B8A6",
        "系统工具" => "#64748B",
        "学习" => "#84CC16",
        _ => "#475569",
    }
}

fn weekday_label(date: &str) -> &'static str {
    let Some(d) = parse_dt(&format!("{date} 00:00:00.0000000")) else {
        return "";
    };
    let days = (d.secs / 86_400.0).round() as i64;
    // 1970-01-01 是周四
    match (days + 4).rem_euclid(7) {
        0 => "周日",
        1 => "周一",
        2 => "周二",
        3 => "周三",
        4 => "周四",
        5 => "周五",
        _ => "周六",
    }
}

// ==================== 便签（只读） ====================
//
// 便签：`StickyNotes` 里 `IsDeleted = 0` 的行，置顶优先、更新时间倒序。

/// 便签清单：`[{id, color, content, pinned, archived, createdAt, updatedAt}]`
/// 便签可用的颜色（与 WPF 的色板一致，非法值回落 yellow）。
const NOTE_COLORS: &[&str] = &["yellow", "green", "pink", "blue", "purple"];

fn note_color(color: &str) -> &'static str {
    NOTE_COLORS
        .iter()
        .find(|c| **c == color.trim().to_lowercase())
        .copied()
        .unwrap_or("yellow")
}

/// 生成一个 GUID 形状的 Id（不引 uuid：纳秒 + pid 做种子的 xorshift）。
fn new_guid() -> String {
    let nanos = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_nanos())
        .unwrap_or(0);
    let mut seed = nanos ^ ((std::process::id() as u128) << 64) ^ 0x9e37_79b9_7f4a_7c15;
    let mut hex = String::with_capacity(32);
    for _ in 0..32 {
        seed ^= seed << 13;
        seed ^= seed >> 7;
        seed ^= seed << 17;
        hex.push(std::char::from_digit((seed % 16) as u32, 16).unwrap_or('0'));
    }
    format!(
        "{}-{}-4{}-{}-{}",
        &hex[0..8],
        &hex[8..12],
        &hex[13..16],
        &hex[16..20],
        &hex[20..32]
    )
}

/// 新建便签（写库）。返回新 Id；内容为空直接拒绝。
pub fn notes_create(content: &str, color: &str) -> Result<String, String> {
    let text = content.trim();
    if text.is_empty() {
        return Err("便签内容不能为空".to_string());
    }
    insert_note(content, color)
}

/// 建一张空白便签（托盘「新建便签」用：先给一张空纸，用户直接打字）。
pub fn notes_create_blank(color: &str) -> Result<String, String> {
    insert_note("", color)
}

fn insert_note(content: &str, color: &str) -> Result<String, String> {
    let conn = open_write()?;
    let id = new_guid();
    let now = now_stamp();
    conn.execute(
        "INSERT INTO StickyNotes \
         (Id, Content, Color, SortOrder, IsPinned, IsArchived, IsDeleted, CreatedAt, UpdatedAt, \
          DeletedAt, DeviceId, Version, IsOnDesktop, WindowLeft, WindowTop, WindowWidth, WindowHeight) \
         VALUES (?1, ?2, ?3, 0, 0, 0, 0, ?4, ?4, NULL, '', 1, 0, -1, -1, 0, 0)",
        rusqlite::params![id, content, note_color(color), now],
    )
    .map_err(|e| format!("新建便签失败：{e}"))?;
    Ok(id)
}

/// 改便签内容（`Version` 自增，`UpdatedAt` 刷新）。
pub fn notes_update(id: &str, content: &str) -> Result<(), String> {
    let conn = open_write()?;
    let n = conn
        .execute(
            "UPDATE StickyNotes SET Content = ?2, UpdatedAt = ?3, Version = Version + 1 \
             WHERE Id = ?1 AND IsDeleted = 0",
            rusqlite::params![id, content, now_stamp()],
        )
        .map_err(|e| format!("修改便签失败：{e}"))?;
    if n == 0 {
        return Err(format!("便签不存在或已删除：{id}"));
    }
    Ok(())
}

/// 置顶/取消置顶。
pub fn notes_pin(id: &str, pinned: bool) -> Result<(), String> {
    let conn = open_write()?;
    let n = conn
        .execute(
            "UPDATE StickyNotes SET IsPinned = ?2, UpdatedAt = ?3, Version = Version + 1 \
             WHERE Id = ?1 AND IsDeleted = 0",
            rusqlite::params![id, if pinned { 1 } else { 0 }, now_stamp()],
        )
        .map_err(|e| format!("置顶便签失败：{e}"))?;
    if n == 0 {
        return Err(format!("便签不存在或已删除：{id}"));
    }
    Ok(())
}

/// 软删除（`IsDeleted = 1` + `DeletedAt`），与 WPF 的删除语义一致，便于同步。
pub fn notes_delete(id: &str) -> Result<(), String> {
    let conn = open_write()?;
    let now = now_stamp();
    let n = conn
        .execute(
            "UPDATE StickyNotes SET IsDeleted = 1, DeletedAt = ?2, UpdatedAt = ?2, \
             Version = Version + 1 WHERE Id = ?1 AND IsDeleted = 0",
            rusqlite::params![id, now],
        )
        .map_err(|e| format!("删除便签失败：{e}"))?;
    if n == 0 {
        return Err(format!("便签不存在或已删除：{id}"));
    }
    Ok(())
}

/// 写用连接：默认库路径（受 `PCHABIT_DB` 影响），WAL 由 SQLite 自己维护。
fn open_write() -> Result<Connection, String> {
    let path = default_db();
    if !path.exists() {
        return Err(format!("数据库不存在：{}", path.display()));
    }
    let conn = Connection::open(&path).map_err(|e| format!("打开库失败：{e}"))?;
    conn.busy_timeout(std::time::Duration::from_millis(3000))
        .map_err(|e| format!("设置超时失败：{e}"))?;
    Ok(conn)
}

pub fn notes_json(db: &Path) -> Result<serde_json::Value, String> {
    if !db.exists() {
        return Err(format!("数据库不存在：{}", db.display()));
    }
    let conn = open_readonly(db)?;
    let mut stmt = conn
        .prepare(
            "SELECT Id, Color, Content, IsPinned, IsArchived, CreatedAt, UpdatedAt \
             FROM StickyNotes WHERE IsDeleted = 0 \
             ORDER BY IsPinned DESC, UpdatedAt DESC",
        )
        .map_err(|e| e.to_string())?;
    let rows = stmt
        .query_map([], |r| {
            Ok(serde_json::json!({
                "id": r.get::<_, Option<String>>(0).unwrap_or_default(),
                "color": r.get::<_, Option<String>>(1).unwrap_or_default(),
                "content": r.get::<_, Option<String>>(2).unwrap_or_default(),
                "pinned": r.get::<_, Option<i64>>(3).unwrap_or(None).unwrap_or(0) != 0,
                "archived": r.get::<_, Option<i64>>(4).unwrap_or(None).unwrap_or(0) != 0,
                "createdAt": r.get::<_, Option<String>>(5).unwrap_or_default(),
                "updatedAt": r.get::<_, Option<String>>(6).unwrap_or_default(),
            }))
        })
        .map_err(|e| e.to_string())?;
    let items: Vec<serde_json::Value> = rows.filter_map(|r| r.ok()).collect();
    Ok(serde_json::json!({ "count": items.len(), "items": items }))
}

/// 桌面上的便签（`IsOnDesktop = 1`）：带位置和尺寸，壳启动时按这个把窗口摆出来。
pub fn notes_desktop_json(db: &Path) -> Result<serde_json::Value, String> {
    if !db.exists() {
        return Err(format!("数据库不存在：{}", db.display()));
    }
    let conn = open_readonly(db)?;
    let mut stmt = conn
        .prepare(
            "SELECT Id, Color, Content, IsPinned, COALESCE(WindowLeft,0), COALESCE(WindowTop,0), \
             COALESCE(WindowWidth,260), COALESCE(WindowHeight,200) \
             FROM StickyNotes WHERE IsDeleted = 0 AND IsOnDesktop = 1 \
             ORDER BY UpdatedAt DESC",
        )
        .map_err(|e| e.to_string())?;
    let rows = stmt
        .query_map([], |r| {
            Ok(serde_json::json!({
                "id": r.get::<_, Option<String>>(0).unwrap_or_default(),
                "color": r.get::<_, Option<String>>(1).unwrap_or_default(),
                "content": r.get::<_, Option<String>>(2).unwrap_or_default(),
                "pinned": r.get::<_, Option<i64>>(3).unwrap_or(None).unwrap_or(0) != 0,
                "left": r.get::<_, Option<f64>>(4).unwrap_or(None).unwrap_or(0.0),
                "top": r.get::<_, Option<f64>>(5).unwrap_or(None).unwrap_or(0.0),
                "width": r.get::<_, Option<f64>>(6).unwrap_or(None).unwrap_or(260.0),
                "height": r.get::<_, Option<f64>>(7).unwrap_or(None).unwrap_or(200.0),
            }))
        })
        .map_err(|e| e.to_string())?;
    let items: Vec<serde_json::Value> = rows.filter_map(|r| r.ok()).collect();
    Ok(serde_json::json!({ "count": items.len(), "items": items }))
}

/// 便签窗口移动/缩放后回写位置（桌面便签的唯一状态）。
pub fn notes_set_pos(id: &str, left: f64, top: f64, width: f64, height: f64) -> Result<(), String> {
    let conn = open_write()?;
    let n = conn
        .execute(
            "UPDATE StickyNotes SET WindowLeft = ?2, WindowTop = ?3, WindowWidth = ?4, \
             WindowHeight = ?5 WHERE Id = ?1 AND IsDeleted = 0",
            rusqlite::params![id, left, top, width, height],
        )
        .map_err(|e| format!("回写便签位置失败：{e}"))?;
    if n == 0 {
        return Err(format!("便签不存在或已删除：{id}"));
    }
    Ok(())
}

/// 是否放在桌面上（`IsOnDesktop`）。
pub fn notes_set_desktop(id: &str, on: bool) -> Result<(), String> {
    let conn = open_write()?;
    let n = conn
        .execute(
            "UPDATE StickyNotes SET IsOnDesktop = ?2, UpdatedAt = ?3 WHERE Id = ?1 AND IsDeleted = 0",
            rusqlite::params![id, if on { 1 } else { 0 }, now_stamp()],
        )
        .map_err(|e| format!("设置桌面便签失败：{e}"))?;
    if n == 0 {
        return Err(format!("便签不存在或已删除：{id}"));
    }
    Ok(())
}

/// `yyyy-MM-dd HH:mm:ss.fffffff`（本地时间，固定 UTC+8）
fn now_stamp() -> String {
    let ms = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_millis() as i64)
        .unwrap_or(0);
    let local = ms + 8 * 3_600_000;
    let days = local / 86_400_000;
    let rem = local % 86_400_000;
    let h = rem / 3_600_000;
    let m = (rem / 60_000) % 60;
    let s = (rem / 1000) % 60;
    let frac = rem % 1000 * 10_000;
    format!(
        "{} {h:02}:{m:02}:{s:02}.{frac:07}",
        civil_from_days(days)
    )
}

// ==================== 网页访问（只读） ====================
//
// 口径逐条对齐 WPF `WebDetailsViewModel`：
// - 有效浏览时长：`ActiveDuration > 0` 时用它，否则退回 `Duration`（都是 100ns ticks，1 分钟 = 6e8）
// - 汇总里 `focusRatio = 有效分钟 / 自然分钟`
// - 小时分布用 `Duration`（不是有效时长），与 WPF 一致
// - 域名分类：先查 `WebsiteDomainMappings`（最长 pattern 优先、后缀匹配），再退回内置关键词
// - 流量归因：只算 chrome/msedge/firefox 的下行字节，按「同日同小时各域名有效秒数」权重拆分

struct WebRow {
    domain: String,
    title: String,
    url: String,
    start_time: String,
    date: String,
    hour: i64,
    duration_ticks: i64,
    active_ticks: i64,
    category_name: String,
    clicks: i64,
}

impl WebRow {
    fn eff_seconds(&self) -> f64 {
        let ticks = if self.active_ticks > 0 {
            self.active_ticks
        } else {
            self.duration_ticks
        };
        ticks as f64 / 10_000_000.0
    }
    fn duration_minutes(&self) -> f64 {
        self.duration_ticks as f64 / 600_000_000.0
    }
}

/// 从 `yyyy-MM-dd HH:mm:ss.fffffff` 取出日期与小时。
fn parse_web_stamp(s: &str) -> Option<(String, i64)> {
    if s.len() < 13 {
        return None;
    }
    let date = s.get(0..10)?.to_string();
    if date.len() != 10 {
        return None;
    }
    let hour: i64 = s.get(11..13)?.parse().ok()?;
    if !(0..=23).contains(&hour) {
        return None;
    }
    Some((date, hour))
}

/// 域名与 pattern 的匹配规则，照抄 `WebsiteCategoryService.DomainMatches`：
/// `*.x` 走后缀，其余要求完全相等或以 `.pattern` 结尾。
fn domain_matches(domain: &str, pattern: &str) -> bool {
    let pat = pattern.to_lowercase();
    if let Some(suffix) = pat.strip_prefix("*.") {
        domain.ends_with(&format!(".{suffix}")) || domain == suffix
    } else {
        domain == pat || domain.ends_with(&format!(".{pat}"))
    }
}

/// 内置兜底关键词，照抄 `WebsiteCategoryService.GetDefaultCategory`。
fn default_web_category(domain: &str) -> &'static str {
    if domain.is_empty() {
        return "浏览";
    }
    let d = domain.to_lowercase();
    let hit = |keys: &[&str]| keys.iter().any(|k| d.contains(k));
    if hit(&["google", "baidu", "bing", "sogou"]) {
        return "搜索";
    }
    if hit(&["github", "gitlab", "stackoverflow", "csdn", "juejin", "segmentfault"]) {
        return "开发";
    }
    if hit(&["youtube", "bilibili", "netflix", "youku", "iqiyi", "douyin"]) {
        return "视频";
    }
    if hit(&[
        "twitter", "weibo", "facebook", "instagram", "linkedin", "zhihu", "xiaohongshu", "douban",
    ]) {
        return "社交";
    }
    if hit(&["amazon", "taobao", "jd", "tmall", "pinduoduo"]) {
        return "购物";
    }
    if hit(&["mail", "outlook", "gmail"]) {
        return "邮件";
    }
    if hit(&["notion", "docs.qq", "yuque", "confluence", "feishu", "dingtalk"]) {
        return "办公";
    }
    if hit(&["news", "bbc", "cnn", "sina", "sohu", "163"]) {
        return "新闻";
    }
    "浏览"
}

/// 启用分类下的域名映射，按 pattern 长度倒序（长的先匹配）。
fn load_web_category_map(conn: &Connection) -> Vec<(String, String)> {
    let mut out = Vec::new();
    let sql = "SELECT m.DomainPattern, c.Name FROM WebsiteDomainMappings m \
               JOIN WebsiteCategories c ON c.Id = m.CategoryId WHERE c.IsActive = 1";
    if let Ok(mut stmt) = conn.prepare(sql) {
        if let Ok(rows) = stmt.query_map([], |r| {
            Ok((
                r.get::<_, Option<String>>(0)?.unwrap_or_default(),
                r.get::<_, Option<String>>(1)?.unwrap_or_default(),
            ))
        }) {
            for row in rows.filter_map(|r| r.ok()) {
                if !row.0.trim().is_empty() {
                    out.push(row);
                }
            }
        }
    }
    out.sort_by(|a, b| b.0.len().cmp(&a.0.len()));
    out
}

/// 会话行上的物化分类优先，其次映射，最后内置关键词。
fn resolve_web_category(
    row: &WebRow,
    map: &[(String, String)],
) -> String {
    if !row.category_name.trim().is_empty() {
        return row.category_name.clone();
    }
    let lower = row.domain.to_lowercase();
    for (pattern, name) in map {
        if domain_matches(&lower, pattern) {
            return name.clone();
        }
    }
    default_web_category(&lower).to_string()
}

fn bytes_text(bytes: f64) -> String {
    const KB: f64 = 1024.0;
    const MB: f64 = KB * 1024.0;
    const GB: f64 = MB * 1024.0;
    if bytes >= GB {
        format!("{:.2} GB", bytes / GB)
    } else if bytes >= MB {
        format!("{:.1} MB", bytes / MB)
    } else if bytes >= KB {
        format!("{:.0} KB", bytes / KB)
    } else {
        format!("{:.0} B", bytes)
    }
}

/// 网页访问详情：近 N 天会话聚合 + 域名排行 + 小时/日分布 + 最近记录 + 流量归因。
/// 网页记录最早一天（`YYYY-MM-DD`）：网页页「全部历史」的起点。
pub fn web_earliest_date(db: &Path) -> Option<String> {
    if !db.exists() {
        return None;
    }
    let conn = open_readonly(db).ok()?;
    let mut stmt = conn
        .prepare("SELECT MIN(StartTime) FROM WebSessions")
        .ok()?;
    let mut rows = stmt.query([]).ok()?;
    let row = rows.next().ok()??;
    let min: Option<String> = row.get(0).ok()?;
    min.filter(|s| s.len() >= 10).map(|s| s[..10].to_string())
}

pub fn web_json(
    db: &Path,
    start_date: &str,
    end_date: &str,
) -> Result<serde_json::Value, String> {
    if !db.exists() {
        return Err(format!("数据库不存在：{}", db.display()));
    }
    let conn = open_readonly(db)?;
    let t0 = Instant::now();

    let start_stamp = format!("{start_date} 00:00:00.0000000");
    let end_next = date_shift(end_date, 1);
    let end_stamp = format!("{end_next} 00:00:00.0000000");

    let mut rows: Vec<WebRow> = Vec::new();
    {
        let mut stmt = conn
            .prepare(
                "SELECT Domain, Title, Url, StartTime, Duration, ActiveDuration, \
                 CategoryName, ClickCount FROM WebSessions \
                 WHERE StartTime >= ?1 AND StartTime < ?2 ORDER BY StartTime DESC",
            )
            .map_err(|e| e.to_string())?;
        let mapped = stmt
            .query_map([&start_stamp, &end_stamp], |r| {
                Ok((
                    r.get::<_, Option<String>>(0).unwrap_or(None).unwrap_or_default(),
                    r.get::<_, Option<String>>(1).unwrap_or(None).unwrap_or_default(),
                    r.get::<_, Option<String>>(2).unwrap_or(None).unwrap_or_default(),
                    r.get::<_, Option<String>>(3).unwrap_or(None).unwrap_or_default(),
                    r.get::<_, Option<i64>>(4).unwrap_or(None).unwrap_or(0),
                    r.get::<_, Option<i64>>(5).unwrap_or(None).unwrap_or(0),
                    r.get::<_, Option<String>>(6).unwrap_or(None).unwrap_or_default(),
                    r.get::<_, Option<i64>>(7).unwrap_or(None).unwrap_or(0),
                ))
            })
            .map_err(|e| e.to_string())?;
        for (domain, title, url, start_time, dur, active, category_name, clicks) in
            mapped.filter_map(|r| r.ok())
        {
            let Some((date, hour)) = parse_web_stamp(&start_time) else {
                continue;
            };
            rows.push(WebRow {
                domain,
                title,
                url,
                start_time,
                date,
                hour,
                duration_ticks: dur,
                active_ticks: active,
                category_name,
                clicks,
            });
        }
    }

    let cat_map = load_web_category_map(&conn);

    // ---------- 汇总 ----------
    let total_visits = rows.len() as i64;
    let total_minutes: f64 = rows.iter().map(|r| r.eff_seconds() / 60.0).sum();
    let wall_minutes: f64 = rows.iter().map(|r| r.duration_minutes()).sum();
    let mut domains_set: HashSet<String> = HashSet::new();
    let mut hour_count = [0i64; 24];
    let mut hour_minutes = [0f64; 24];
    for r in &rows {
        domains_set.insert(r.domain.clone());
        hour_count[r.hour as usize] += 1;
        hour_minutes[r.hour as usize] += r.duration_minutes();
    }
    let avg_seconds = if total_visits > 0 {
        rows.iter().map(|r| r.eff_seconds()).sum::<f64>() / total_visits as f64
    } else {
        0.0
    };
    let focus_ratio = if wall_minutes > 0.0 {
        total_minutes / wall_minutes
    } else {
        1.0
    };
    let peak_hour = hour_count
        .iter()
        .enumerate()
        .max_by_key(|(_, c)| **c)
        .map(|(h, c)| if *c > 0 { h as i64 } else { -1 })
        .unwrap_or(-1);

    // ---------- 域名聚合 ----------
    struct DomainAgg {
        visits: i64,
        eff_seconds: f64,
        minutes: f64,
        last_visit: String,
        category: String,
    }
    let mut domain_aggs: HashMap<String, DomainAgg> = HashMap::new();
    for r in &rows {
        let category = resolve_web_category(r, &cat_map);
        let e = domain_aggs.entry(r.domain.clone()).or_insert(DomainAgg {
            visits: 0,
            eff_seconds: 0.0,
            minutes: 0.0,
            last_visit: String::new(),
            category,
        });
        e.visits += 1;
        e.eff_seconds += r.eff_seconds();
        e.minutes += r.eff_seconds() / 60.0;
        if r.start_time > e.last_visit {
            e.last_visit = r.start_time.clone();
        }
    }

    // ---------- 流量归因 ----------
    // 1) 浏览器下行按 (date, hour) 汇总
    let mut down_by_hour: HashMap<(String, i64), f64> = HashMap::new();
    {
        let mut stmt = conn
            .prepare(
                "SELECT Date, Hour, ProcessName, BytesDown FROM ProcessNetworkUsages \
                 WHERE Date >= ?1 AND Date <= ?2",
            )
            .map_err(|e| e.to_string())?;
        let mapped = stmt
            .query_map([start_date, end_date], |r| {
                Ok((
                    r.get::<_, Option<String>>(0).unwrap_or(None).unwrap_or_default(),
                    r.get::<_, Option<i64>>(1).unwrap_or(None).unwrap_or(-1),
                    r.get::<_, Option<String>>(2).unwrap_or(None).unwrap_or_default(),
                    r.get::<_, Option<i64>>(3).unwrap_or(None).unwrap_or(0),
                ))
            })
            .map_err(|e| e.to_string())?;
        for (date, hour, process, down) in mapped.filter_map(|r| r.ok()) {
            if down <= 0 || !(0..=23).contains(&hour) {
                continue;
            }
            let norm = normalize_process_name(&process);
            if !matches!(norm.as_str(), "chrome" | "msedge" | "firefox") {
                continue;
            }
            *down_by_hour.entry((date, hour)).or_insert(0.0) += down as f64;
        }
    }

    // 2) 各域名在各 (date, hour) 的有效秒数
    let mut sec_by_domain_hour: HashMap<String, HashMap<(String, i64), f64>> = HashMap::new();
    for r in &rows {
        let sec = r.eff_seconds();
        if sec <= 0.0 {
            continue;
        }
        sec_by_domain_hour
            .entry(r.domain.clone())
            .or_default()
            .entry((r.date.clone(), r.hour))
            .and_modify(|v| *v += sec)
            .or_insert(sec);
    }

    // 3) 每小时总有效秒（只统计该小时有浏览器流量的）
    let mut total_sec_by_hour: HashMap<(String, i64), f64> = HashMap::new();
    for by_hour in sec_by_domain_hour.values() {
        for (key, sec) in by_hour {
            if !down_by_hour.contains_key(key) {
                continue;
            }
            *total_sec_by_hour.entry(key.clone()).or_insert(0.0) += *sec;
        }
    }

    // 4) 按权重拆分下行字节
    let mut down_by_domain: HashMap<String, f64> = HashMap::new();
    for (domain, by_hour) in &sec_by_domain_hour {
        for (key, sec) in by_hour {
            let Some(down) = down_by_hour.get(key) else {
                continue;
            };
            let Some(total_sec) = total_sec_by_hour.get(key) else {
                continue;
            };
            if *total_sec <= 0.0 {
                continue;
            }
            *down_by_domain.entry(domain.clone()).or_insert(0.0) += down * (sec / total_sec);
        }
    }

    // ---------- 域名排行（按有效分钟 Top 20） ----------
    let total_minutes_all = domain_aggs.values().map(|d| d.minutes).sum::<f64>();
    let mut ranked: Vec<(&String, &DomainAgg)> = domain_aggs.iter().collect();
    ranked.sort_by(|a, b| b.1.minutes.partial_cmp(&a.1.minutes).unwrap_or(std::cmp::Ordering::Equal));
    let domains_json: Vec<serde_json::Value> = ranked
        .iter()
        .take(20)
        .map(|(domain, agg)| {
            let down = down_by_domain.get(*domain).copied().unwrap_or(0.0);
            let share = if total_minutes_all > 0.0 {
                round1(agg.minutes * 100.0 / total_minutes_all)
            } else {
                0.0
            };
            serde_json::json!({
                "domain": domain,
                "visits": agg.visits,
                "minutes": round1(agg.minutes),
                "avgSeconds": round1(agg.eff_seconds / agg.visits as f64),
                "category": agg.category,
                "lastVisit": agg.last_visit,
                "sharePercent": share,
                "downBytes": down,
                "downText": bytes_text(down),
            })
        })
        .collect();

    // ---------- 小时 / 日分布 ----------
    let hourly_json: Vec<serde_json::Value> = (0..24)
        .map(|h| {
            serde_json::json!({
                "hour": h,
                "label": format!("{h:02}:00"),
                "visits": hour_count[h],
                "minutes": round1(hour_minutes[h]),
            })
        })
        .collect();

    let mut daily: BTreeMap<String, (i64, f64, HashSet<String>)> = BTreeMap::new();
    for r in &rows {
        let e = daily.entry(r.date.clone()).or_insert((0, 0.0, HashSet::new()));
        e.0 += 1;
        e.1 += r.duration_minutes();
        e.2.insert(r.domain.clone());
    }
    let daily_json: Vec<serde_json::Value> = daily
        .iter()
        .map(|(date, (visits, minutes, set))| {
            let label = if date.len() >= 10 {
                format!("{}/{}", &date[5..7], &date[8..10])
            } else {
                date.clone()
            };
            serde_json::json!({
                "date": date,
                "label": label,
                "visits": visits,
                "minutes": round1(*minutes),
                "domains": set.len(),
            })
        })
        .collect();

    // ---------- 最近访问 ----------
    let recent_json: Vec<serde_json::Value> = rows
        .iter()
        .take(300)
        .map(|r| {
            let mins = (r.eff_seconds() / 60.0).floor();
            let secs = (r.eff_seconds() % 60.0).floor();
            let duration_text = if mins > 0.0 || secs > 0.0 {
                format!("{}分{}秒", mins as i64, secs as i64)
            } else {
                "-".to_string()
            };
            let clock = if r.start_time.len() >= 19 {
                r.start_time[11..19].to_string()
            } else {
                r.start_time.clone()
            };
            serde_json::json!({
                "domain": r.domain,
                "title": r.title,
                "url": r.url,
                "date": r.date,
                "clock": clock,
                "durationText": duration_text,
                "category": resolve_web_category(r, &cat_map),
                "clicks": r.clicks,
            })
        })
        .collect();

    let top_domain = ranked
        .iter()
        .max_by_key(|(_, agg)| agg.visits)
        .map(|(d, _)| (*d).clone())
        .unwrap_or_default();

    Ok(serde_json::json!({
        "ok": true,
        "range": { "start": start_date, "end": end_date },
        "summary": {
            "totalVisits": total_visits,
            "totalMinutes": round1(total_minutes),
            "uniqueDomains": domains_set.len(),
            "avgSeconds": round1(avg_seconds),
            "focusRatioPercent": (focus_ratio * 100.0) as i64,
            "peakHour": peak_hour,
            "peakHourLabel": if peak_hour >= 0 { format!("{peak_hour}:00") } else { "-".to_string() },
            "topDomain": top_domain,
        },
        "domains": domains_json,
        "hourly": hourly_json,
        "daily": daily_json,
        "recent": recent_json,
        "trafficBrowserOnly": true,
        "ms": t0.elapsed().as_secs_f64() * 1000.0,
    }))
}
