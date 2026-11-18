//! 分析页计算层（P3 收口块）：`AppSessions` + `DailySummaries` + `HardwareSamples`
//! + `WebSessions` + `ProcessNetworkUsages` → KPI / 日历 / 小时热力 / Top 变化 /
//! 分类构成 / 洞察 / 多源指标 / 硬件画像。
//!
//! 口径必须与 `AnalyticsEngine.Compute` + `HardwareAnalyticsEngine.Build` 逐条一致。
//! 只搬计算，不搬文案格式以外的任何东西；所有展示字符串（`▲ 12%`、`2小时30分`）
//! 也在这里生成，宿主只做 DTO 映射。

use std::collections::{BTreeMap, HashMap};

use rusqlite::Connection;
use serde_json::{json, Value};

use crate::{civil_from_days, parse_dt, is_productive, normalize_process_name, round1};

/// 一条已解析的应用会话（只保留计算需要的字段）。
struct S {
    proc_name: String,
    app_name: Option<String>,
    category: String,
    start: f64,
    end: f64,
    hour: u32,
    start_day: i64,
}

impl S {
    fn minutes(&self) -> f64 {
        if self.end > self.start {
            (self.end - self.start) / 60.0
        } else {
            0.0
        }
    }
}

/// 一条日汇总（只有 KPI 需要的列）。
struct Daily {
    total_keys: f64,
    total_clicks: f64,
    active_minutes: f64,
    web_minutes: f64,
    web_pages: f64,
    cpu_avg: Option<f64>,
    cpu_p95: Option<f64>,
    gpu_avg: Option<f64>,
    gpu_temp_max: Option<f64>,
    mem_avg: Option<f64>,
    net_up: i64,
    net_down: i64,
    search_count: i64,
}

/// 一条硬件分钟样本。
struct HwSample {
    start: f64,
    start_day: i64,
    hour: u32,
    cpu: Option<f64>,
    gpu: Option<f64>,
    temp: Option<f64>,
    mem: Option<f64>,
}

/// 一条网页会话（只保留计算需要的字段）。
struct WebS {
    start: f64,
    hour: u32,
    minutes: f64,
    category: Option<String>,
}

pub fn run(conn: &Connection, req: &Value) -> Result<Value, String> {
    let get = |k: &str| -> Result<String, String> {
        req.get(k)
            .and_then(|v| v.as_str())
            .map(|s| s.to_string())
            .ok_or_else(|| format!("缺少字段 {k}"))
    };

    let cur_start = secs_of(&get("curStart")?)?;
    let cur_end = secs_of(&get("curEnd")?)?;
    let prev_start = secs_of(&get("prevStart")?)?;
    let prev_end = secs_of(&get("prevEnd")?)?;
    let label = req
        .get("label")
        .and_then(|v| v.as_str())
        .unwrap_or("")
        .to_string();
    let prev_label = req
        .get("prevLabel")
        .and_then(|v| v.as_str())
        .unwrap_or("上一周期")
        .to_string();

    let cat_map = crate::load_category_map(conn)?;

    let mut cur = load_sessions(conn, &get("curStart")?, &get("curEnd")?, &cat_map)?;
    let mut prev = load_sessions(conn, &get("prevStart")?, &get("prevEnd")?, &cat_map)?;

    let daily = load_daily(conn, &date_of(cur_start), &date_of(cur_end))?;
    let prev_daily = load_daily(conn, &date_of(prev_start), &date_of(prev_end))?;
    let hardware = load_hardware(conn, &get("curStart")?, &get("curEnd")?)?;
    let web = load_web(conn, cur_start, cur_end)?;
    let net = load_net(conn, &date_of(cur_start), &date_of(cur_end))?;

    let report = compute(
        cur_start, cur_end, prev_start, prev_end, &label, &prev_label, &mut cur, &mut prev, &daily,
        &prev_daily, &hardware, &web, &net,
    );

    Ok(report)
}

fn secs_of(s: &str) -> Result<f64, String> {
    parse_dt(s)
        .map(|d| d.secs)
        .ok_or_else(|| format!("时间格式无法解析: {s}"))
}

/// 秒 → 本地日期 "YYYY-MM-DD"（用整日天数换算，避免时区参与）。
fn date_of(secs: f64) -> String {
    civil_from_days((secs / 86_400.0).floor() as i64)
}

fn load_sessions(
    conn: &Connection,
    start: &str,
    end: &str,
    cat_map: &HashMap<String, (String, String)>,
) -> Result<Vec<S>, String> {
    let mut stmt = conn
        .prepare(
            "SELECT ProcessName, AppName, Category, StartTime, EndTime FROM AppSessions \
             WHERE StartTime >= ?1 AND StartTime < ?2 AND EndTime IS NOT NULL ORDER BY StartTime",
        )
        .map_err(|e| e.to_string())?;

    let rows = stmt
        .query_map([start, end], |r| {
            Ok((
                r.get::<_, Option<String>>(0)?.unwrap_or_default(),
                r.get::<_, Option<String>>(1)?,
                r.get::<_, Option<String>>(2)?,
                r.get::<_, Option<String>>(3)?.unwrap_or_default(),
                r.get::<_, Option<String>>(4)?,
            ))
        })
        .map_err(|e| e.to_string())?;

    let mut out = Vec::new();
    for row in rows.filter_map(|r| r.ok()) {
        let (proc_name, app_name, category, start_ts, end_ts) = row;
        let Some(sc) = parse_dt(&start_ts) else { continue };
        let Some(ec) = end_ts.as_deref().and_then(parse_dt) else { continue };

        // 分类口径与应用统计页统一：映射表优先，会话快照回落
        let proxy = crate::Session {
            proc_name: proc_name.clone(),
            app_name: app_name.clone(),
            category: category.clone(),
            start: sc.secs,
            end: Some(ec.secs),
            hour: sc.hour,
        };
        let (name, _) = crate::resolve_category(&proxy, cat_map);

        out.push(S {
            proc_name,
            app_name,
            category: name,
            start: sc.secs,
            end: ec.secs,
            hour: sc.hour,
            start_day: (sc.secs / 86_400.0).floor() as i64,
        });
    }
    Ok(out)
}

fn load_daily(conn: &Connection, start_day: &str, end_day: &str) -> Result<Vec<Daily>, String> {
    let mut stmt = conn
        .prepare(
            "SELECT TotalKeys, TotalMouseClicks, ActiveMinutes, WebPages, WebDurationTicks, \
                    WebActiveDurationTicks, WebMinutes, CpuLoadAvg, CpuLoadP95, GpuLoadAvg, \
                    GpuTempMax, MemLoadAvg, NetBytesUp, NetBytesDown, SearchCount \
             FROM DailySummaries WHERE Date >= ?1 AND Date < ?2",
        )
        .map_err(|e| e.to_string())?;

    let rows = stmt
        .query_map([start_day, end_day], |r| {
            let web_minutes: Option<f64> = r.get(6)?;
            let web_active_ticks: i64 = r.get::<_, Option<i64>>(5)?.unwrap_or(0);
            Ok(Daily {
                total_keys: r.get::<_, Option<i64>>(0)?.unwrap_or(0) as f64,
                total_clicks: r.get::<_, Option<i64>>(1)?.unwrap_or(0) as f64,
                active_minutes: r.get::<_, Option<f64>>(2)?.unwrap_or(0.0),
                web_pages: r.get::<_, Option<i64>>(3)?.unwrap_or(0) as f64,
                web_minutes: web_minutes
                    .unwrap_or_else(|| web_active_ticks as f64 / 600_000_000.0),
                cpu_avg: r.get(7)?,
                cpu_p95: r.get(8)?,
                gpu_avg: r.get(9)?,
                gpu_temp_max: r.get(10)?,
                mem_avg: r.get(11)?,
                net_up: r.get::<_, Option<i64>>(12)?.unwrap_or(0),
                net_down: r.get::<_, Option<i64>>(13)?.unwrap_or(0),
                search_count: r.get::<_, Option<i64>>(14)?.unwrap_or(0),
            })
        })
        .map_err(|e| e.to_string())?;

    Ok(rows.filter_map(|r| r.ok()).collect())
}

fn load_hardware(conn: &Connection, start: &str, end: &str) -> Result<Vec<HwSample>, String> {
    let hw_start = format!("{}", &start[..16.min(start.len())]);
    let hw_end = format!("{}", &end[..16.min(end.len())]);

    let mut stmt = conn
        .prepare(
            "SELECT Timestamp, CpuLoadAvg, GpuLoadAvg, GpuTempMax, MemLoadAvg FROM HardwareSamples \
             WHERE Timestamp >= ?1 AND Timestamp < ?2",
        )
        .map_err(|e| e.to_string())?;

    let rows = stmt
        .query_map([hw_start.as_str(), hw_end.as_str()], |r| {
            Ok((
                r.get::<_, Option<String>>(0)?.unwrap_or_default(),
                r.get::<_, Option<f64>>(1)?,
                r.get::<_, Option<f64>>(2)?,
                r.get::<_, Option<f64>>(3)?,
                r.get::<_, Option<f64>>(4)?,
            ))
        })
        .map_err(|e| e.to_string())?;

    let mut out = Vec::new();
    for (ts, cpu, gpu, temp, mem) in rows.filter_map(|r| r.ok()) {
        let Some(d) = parse_dt(&ts) else { continue };
        out.push(HwSample {
            start: d.secs,
            start_day: (d.secs / 86_400.0).floor() as i64,
            hour: d.hour,
            cpu,
            gpu,
            temp,
            mem,
        });
    }
    Ok(out)
}

fn load_web(conn: &Connection, start: f64, end: f64) -> Result<Vec<WebS>, String> {
    let mut stmt = conn
        .prepare(
            "SELECT StartTime, Duration, ActiveDuration, CategoryName FROM WebSessions \
             WHERE IsLegacy = 0",
        )
        .map_err(|e| e.to_string())?;

    let rows = stmt
        .query_map([], |r| {
            Ok((
                r.get::<_, Option<String>>(0)?.unwrap_or_default(),
                r.get::<_, Option<i64>>(1)?.unwrap_or(0),
                r.get::<_, Option<i64>>(2)?.unwrap_or(0),
                r.get::<_, Option<String>>(3)?,
            ))
        })
        .map_err(|e| e.to_string())?;

    let mut out = Vec::new();
    for (ts, dur, active, cat) in rows.filter_map(|r| r.ok()) {
        let Some(d) = parse_dt(&ts) else { continue };
        if d.secs < start || d.secs >= end {
            continue;
        }
        // 有效浏览优先，缺失时退回 Duration；列是 TimeSpan(TEXT? INTEGER 100ns ticks)，1 分钟 = 6e8 ticks
        let minutes = if active > 0 {
            active as f64 / 600_000_000.0
        } else {
            dur as f64 / 600_000_000.0
        };
        if minutes <= 0.0 {
            continue;
        }
        out.push(WebS {
            start: d.secs,
            hour: d.hour,
            minutes,
            category: cat,
        });
    }
    Ok(out)
}

fn load_net(conn: &Connection, start_day: &str, end_day: &str) -> Result<[f64; 24], String> {
    let mut stmt = conn
        .prepare("SELECT Hour, BytesDown FROM ProcessNetworkUsages WHERE Date >= ?1 AND Date < ?2")
        .map_err(|e| e.to_string())?;

    let rows = stmt
        .query_map([start_day, end_day], |r| {
            Ok((
                r.get::<_, Option<i64>>(0)?.unwrap_or(-1),
                r.get::<_, Option<i64>>(1)?.unwrap_or(0),
            ))
        })
        .map_err(|e| e.to_string())?;

    let mut buckets = [0.0f64; 24];
    for (hour, down) in rows.filter_map(|r| r.ok()) {
        if down <= 0 || !(0..24).contains(&hour) {
            continue;
        }
        buckets[hour as usize] += down as f64 / 1024.0 / 1024.0;
    }
    Ok(buckets)
}

#[allow(clippy::too_many_arguments)]
fn compute(
    cur_start: f64,
    cur_end: f64,
    prev_start: f64,
    prev_end: f64,
    label: &str,
    prev_label: &str,
    cur: &mut [S],
    prev: &mut [S],
    daily: &[Daily],
    prev_daily: &[Daily],
    hardware: &[HwSample],
    web: &[WebS],
    net: &[f64; 24],
) -> Value {
    let cur_min: f64 = cur.iter().map(|s| s.minutes()).sum();
    let prev_min: f64 = prev.iter().map(|s| s.minutes()).sum();
    let cur_prod: f64 = cur
        .iter()
        .filter(|s| is_productive(&s.category))
        .map(|s| s.minutes())
        .sum();
    let prev_prod: f64 = prev
        .iter()
        .filter(|s| is_productive(&s.category))
        .map(|s| s.minutes())
        .sum();
    let cur_focus = cur
        .iter()
        .filter(|s| s.minutes() >= 25.0 && is_productive(&s.category))
        .count() as f64;
    let prev_focus = prev
        .iter()
        .filter(|s| s.minutes() >= 25.0 && is_productive(&s.category))
        .count() as f64;
    let cur_night: f64 = cur
        .iter()
        .filter(|s| s.hour >= 23 || s.hour < 6)
        .map(|s| s.minutes())
        .sum();
    let prev_night: f64 = prev
        .iter()
        .filter(|s| s.hour >= 23 || s.hour < 6)
        .map(|s| s.minutes())
        .sum();
    let cur_prod_pct = if cur_min > 0.0 { cur_prod / cur_min * 100.0 } else { 0.0 };
    let prev_prod_pct = if prev_min > 0.0 { prev_prod / prev_min * 100.0 } else { 0.0 };

    let mut hour_web_minutes = [0.0f64; 24];
    for w in web {
        hour_web_minutes[w.hour as usize] += w.minutes;
    }

    let web_category_shares = build_web_category_shares(web);

    let days_with_data = {
        let mut set: Vec<i64> = cur.iter().map(|s| s.start_day).collect();
        set.sort_unstable();
        set.dedup();
        set.len()
    };

    let cur_extra = aggregate_daily(daily);
    let prev_extra = aggregate_daily(prev_daily);
    let active_for_input = if cur_min > 0.0 { cur_min } else { cur_extra.web_minutes };

    let mut kpis: Vec<Value> = Vec::new();
    kpis.push(make_kpi("active", "活跃时长", format_hours(cur_min), cur_min, prev_min, "应用会话总时长"));
    kpis.push(make_kpi("focus", "专注段", format!("{} 次", f0(cur_focus)), cur_focus, prev_focus, "≥25 分钟且属生产力分类"));
    kpis.push(make_kpi("prod", "生产力占比", format!("{}%", f0(cur_prod_pct)), cur_prod_pct, prev_prod_pct, "生产力时长 / 总时长"));
    kpis.push(make_kpi("night", "夜间使用", format_hours(cur_night), cur_night, prev_night, "23:00–06:00"));

    if cur_extra.has_input {
        kpis.push(make_kpi("keys", "键盘按键", format!("{} 次", group_thousands(cur_extra.total_keys)), cur_extra.total_keys, prev_extra.total_keys, "DailySummaries 合计"));
        kpis.push(make_kpi("input", "输入密度", format!("{}/时", f0(cur_extra.keys_per_active_hour)), cur_extra.keys_per_active_hour, prev_extra.keys_per_active_hour, "按键 ÷ 活跃小时（可辅助判断挂机）"));
    }
    if cur_extra.has_web {
        kpis.push(make_kpi("web", "网页时长", format_hours(cur_extra.web_minutes), cur_extra.web_minutes, prev_extra.web_minutes, "有效浏览分钟（日汇总）"));
        kpis.push(make_kpi("web-share", "网页占比", format!("{}%", f0(cur_extra.web_share_pct)), cur_extra.web_share_pct, prev_extra.web_share_pct, "网页分钟 ÷（应用活跃+网页）"));
    }
    if cur_extra.has_hardware {
        if let Some(cpu) = cur_extra.cpu_avg {
            kpis.push(make_kpi("cpu", "CPU 日均", format!("{}%", f0(cpu)), cpu, prev_extra.cpu_avg.unwrap_or(0.0), "HardwareSamples 日均（有样本天）"));
        }
        if let Some(gpu) = cur_extra.gpu_avg {
            kpis.push(make_kpi("gpu", "GPU 日均", format!("{}%", f0(gpu)), gpu, prev_extra.gpu_avg.unwrap_or(0.0), "HardwareSamples 日均"));
        }
        if let Some(t) = cur_extra.gpu_temp_max {
            kpis.push(make_kpi("gpu-temp", "GPU 峰值温度", format!("{}°C", f0(t)), t, prev_extra.gpu_temp_max.unwrap_or(0.0), "周期内日峰值最高值"));
        }
    }

    // 日历：近 14 天（含期外，便于对比）
    let heat_end_day = (cur_end / 86_400.0).floor() as i64 - 1;
    let heat_start_day = heat_end_day - 13;
    let mut by_day: BTreeMap<i64, f64> = BTreeMap::new();
    for s in cur.iter() {
        *by_day.entry(s.start_day).or_insert(0.0) += s.minutes();
    }
    let mut max_day_hours = by_day.values().cloned().fold(0.0f64, f64::max) / 60.0;
    if max_day_hours <= 0.0 {
        max_day_hours = 1.0;
    }

    let period_start_day = (cur_start / 86_400.0).floor() as i64;
    let period_end_day = (cur_end / 86_400.0).floor() as i64;

    let mut calendar: Vec<Value> = Vec::new();
    let mut d = heat_start_day;
    while d <= heat_end_day {
        let mins = *by_day.get(&d).unwrap_or(&0.0);
        let hours = mins / 60.0;
        let level = if mins <= 0.0 {
            0
        } else if hours < 1.0 {
            1
        } else if hours < 3.0 {
            2
        } else if hours < 6.0 {
            3
        } else {
            4
        };
        calendar.push(json!({
            "date": civil_from_days(d),
            "dayLabel": day_name(d),
            "dateLabel": short_date(d),
            "hours": hours,
            "intensityLevel": level,
            "inPeriod": d >= period_start_day && d < period_end_day,
        }));
        d += 1;
    }

    // 24h 热力
    let mut by_hour = [0.0f64; 24];
    for s in cur.iter() {
        by_hour[s.hour as usize] += s.minutes();
    }
    let max_hour = by_hour.iter().cloned().fold(0.0f64, f64::max).max(1.0);
    let hour_heat: Vec<Value> = (0..24)
        .map(|h| {
            let mins = by_hour[h];
            let level = if mins <= 0.0 { 0 } else { (mins / max_hour * 4.0).ceil() as i32 };
            json!({
                "hour": h,
                "label": format!("{:02}", h),
                "minutes": mins,
                "intensityLevel": level.clamp(0, 4),
            })
        })
        .collect();

    // Top 变化应用（口径同 C#：两期字典各自大小写敏感，键的并集大小写不敏感）
    let cur_app = group_minutes(cur, false);
    let prev_app = group_minutes(prev, false);
    let mut all_apps: Vec<String> = Vec::new();
    let mut seen: Vec<String> = Vec::new();
    for k in cur_app.keys().chain(prev_app.keys()) {
        let lower = k.to_lowercase();
        if seen.contains(&lower) {
            continue;
        }
        seen.push(lower);
        all_apps.push(k.clone());
    }

    let mut top_changes: Vec<Value> = Vec::new();
    for name in all_apps {
        let c = *cur_app.get(&name).unwrap_or(&0.0);
        let p = *prev_app.get(&name).unwrap_or(&0.0);
        let delta = c - p;
        let dir = if delta.abs() < 5.0 { "flat" } else if delta > 0.0 { "up" } else { "down" };
        let pct = if p > 0.0 { delta / p * 100.0 } else if c > 0.0 { 100.0 } else { 0.0 };
        let delta_text = format!("{}{}%", if pct >= 0.0 { "+" } else { "" }, f0(pct));
        if (c - p).abs() < 15.0 {
            continue;
        }
        top_changes.push(json!({
            "name": name, "kind": "app",
            "currentMinutes": c, "previousMinutes": p,
            "deltaText": delta_text, "direction": dir,
        }));
    }
    top_changes.sort_by(|a, b| {
        let av = (a["currentMinutes"].as_f64().unwrap_or(0.0) - a["previousMinutes"].as_f64().unwrap_or(0.0)).abs();
        let bv = (b["currentMinutes"].as_f64().unwrap_or(0.0) - b["previousMinutes"].as_f64().unwrap_or(0.0)).abs();
        bv.partial_cmp(&av).unwrap_or(std::cmp::Ordering::Equal)
    });
    top_changes.truncate(5);

    // 分类构成
    let total_for_share: f64 = cur.iter().map(|s| s.minutes()).sum();
    let mut category_shares: Vec<Value> = Vec::new();
    let mut cat_minutes: Vec<(String, f64)> = Vec::new();
    {
        let mut agg: BTreeMap<String, f64> = BTreeMap::new();
        for s in cur.iter() {
            let key = if s.category.trim().is_empty() { "未分类".to_string() } else { s.category.clone() };
            *agg.entry(key).or_insert(0.0) += s.minutes();
        }
        for (k, v) in agg {
            if v >= 5.0 {
                cat_minutes.push((k, v));
            }
        }
        cat_minutes.sort_by(|a, b| b.1.partial_cmp(&a.1).unwrap_or(std::cmp::Ordering::Equal));
        cat_minutes.truncate(8);
        for (k, mins) in cat_minutes.iter() {
            let pct = if total_for_share > 0.0 { mins / total_for_share * 100.0 } else { 0.0 };
            category_shares.push(json!({ "category": k, "minutes": mins, "percentage": pct }));
        }
    }

    // 多源指标合并（活跃时长以会话为准）
    let mut extra = merge_extra(&cur_extra, active_for_input);
    let extra_value = if !extra.has_input && !extra.has_web && !extra.has_hardware {
        Value::Null
    } else {
        extra_to_json(&extra)
    };

    let mut insights = build_insights(
        cur_min, prev_min, cur_prod_pct, prev_prod_pct, cur_focus as i64, prev_focus as i64,
        cur_night, cur, days_with_data, &extra, &category_shares,
    );

    // 硬件画像
    let hardware_report = if hardware.is_empty() {
        Value::Null
    } else {
        let hw = build_hardware(cur, hardware);
        if hw.get("hasData").and_then(|v| v.as_bool()).unwrap_or(false) {
            insights.extend(build_hardware_insights(&hw));
        }
        hw
    };

    insights.sort_by_key(|i| match i["severity"].as_str().unwrap_or("") {
        "warning" => 0,
        "success" => 2,
        _ => 1,
    });

    let has_enough = days_with_data >= 2 && cur_min >= 30.0;

    json!({
        "ok": true,
        "period": { "start": date_of(cur_start), "endExclusive": date_of(cur_end), "label": label },
        "previous": { "start": date_of(prev_start), "endExclusive": date_of(prev_end), "label": prev_label },
        "kpis": kpis,
        "calendarDays": calendar,
        "hourHeat": hour_heat,
        "topChanges": top_changes,
        "categoryShares": category_shares,
        "insights": insights,
        "extra": extra_value,
        "hardware": hardware_report,
        "dayCountWithData": days_with_data,
        "hasEnoughData": has_enough,
        "hourWebMinutes": hour_web_minutes.to_vec(),
        "hourNetDownMb": net.to_vec(),
        "webCategoryShares": web_category_shares,
    })
}

fn group_minutes(sessions: &[S], _unused: bool) -> HashMap<String, f64> {
    let mut map: HashMap<String, f64> = HashMap::new();
    for s in sessions {
        *map.entry(s.proc_name.clone()).or_insert(0.0) += s.minutes();
    }
    map
}

fn build_web_category_shares(web: &[WebS]) -> Vec<Value> {
    let mut agg: BTreeMap<String, f64> = BTreeMap::new();
    for w in web {
        if let Some(c) = w.category.as_deref() {
            if c.trim().is_empty() {
                continue;
            }
            *agg.entry(c.to_string()).or_insert(0.0) += w.minutes;
        }
    }
    if agg.is_empty() {
        return Vec::new();
    }
    let total: f64 = agg.values().sum();
    let mut list: Vec<(String, f64)> = agg.into_iter().filter(|(_, v)| *v > 0.0).collect();
    list.sort_by(|a, b| b.1.partial_cmp(&a.1).unwrap_or(std::cmp::Ordering::Equal));
    list.into_iter()
        .map(|(c, mins)| {
            json!({
                "category": c,
                "minutes": round1(mins),
                "percentage": if total > 0.0 { round1(mins / total * 100.0) } else { 0.0 },
            })
        })
        .collect()
}

struct Extra {
    total_keys: f64,
    total_clicks: f64,
    keys_per_active_hour: f64,
    web_minutes: f64,
    web_pages: f64,
    web_share_pct: f64,
    cpu_avg: Option<f64>,
    cpu_p95: Option<f64>,
    gpu_avg: Option<f64>,
    gpu_temp_max: Option<f64>,
    mem_avg: Option<f64>,
    has_input: bool,
    has_web: bool,
    has_hardware: bool,
    net_up: Option<i64>,
    net_down: Option<i64>,
    has_net: bool,
    search_count: i64,
}

fn aggregate_daily(daily: &[Daily]) -> Extra {
    if daily.is_empty() {
        return Extra {
            total_keys: 0.0, total_clicks: 0.0, keys_per_active_hour: 0.0,
            web_minutes: 0.0, web_pages: 0.0, web_share_pct: 0.0,
            cpu_avg: None, cpu_p95: None, gpu_avg: None, gpu_temp_max: None, mem_avg: None,
            has_input: false, has_web: false, has_hardware: false,
            net_up: None, net_down: None, has_net: false, search_count: 0,
        };
    }

    let keys: f64 = daily.iter().map(|d| d.total_keys).sum();
    let clicks: f64 = daily.iter().map(|d| d.total_clicks).sum();
    let web_min: f64 = daily.iter().map(|d| d.web_minutes).sum();
    let web_pages: f64 = daily.iter().map(|d| d.web_pages).sum();

    let cpu_avgs: Vec<f64> = daily.iter().filter_map(|d| d.cpu_avg).collect();
    let cpu_p95s: Vec<f64> = daily.iter().filter_map(|d| d.cpu_p95).collect();
    let gpu_avgs: Vec<f64> = daily.iter().filter_map(|d| d.gpu_avg).collect();
    let gpu_temps: Vec<f64> = daily.iter().filter_map(|d| d.gpu_temp_max).collect();
    let mem_avgs: Vec<f64> = daily.iter().filter_map(|d| d.mem_avg).collect();

    let net_up: i64 = daily.iter().map(|d| d.net_up).sum();
    let net_down: i64 = daily.iter().map(|d| d.net_down).sum();
    let search_count: i64 = daily.iter().map(|d| d.search_count).sum();

    let has_input = keys > 0.0 || clicks > 0.0;
    let has_web = web_min > 0.0 || web_pages > 0.0;
    let has_hw = !cpu_avgs.is_empty() || !gpu_avgs.is_empty() || !gpu_temps.is_empty();
    let has_net = net_up > 0 || net_down > 0;

    let active_minutes: f64 = daily.iter().map(|d| d.active_minutes).sum();
    let keys_per_hour = if active_minutes > 0.0 { keys / (active_minutes / 60.0) } else { 0.0 };
    let web_share = if active_minutes + web_min > 0.0 { web_min / (active_minutes + web_min) * 100.0 } else { 0.0 };

    Extra {
        total_keys: keys,
        total_clicks: clicks,
        keys_per_active_hour: keys_per_hour,
        web_minutes: web_min,
        web_pages,
        web_share_pct: web_share,
        cpu_avg: mean(&cpu_avgs),
        cpu_p95: cpu_p95s.iter().cloned().fold(None, |acc: Option<f64>, v| Some(acc.map_or(v, |a| a.max(v)))),
        gpu_avg: mean(&gpu_avgs),
        gpu_temp_max: gpu_temps.iter().cloned().fold(None, |acc: Option<f64>, v| Some(acc.map_or(v, |a| a.max(v)))),
        mem_avg: mean(&mem_avgs),
        has_input,
        has_web,
        has_hardware: has_hw,
        net_up: if has_net { Some(net_up) } else { None },
        net_down: if has_net { Some(net_down) } else { None },
        has_net,
        search_count,
    }
}

fn mean(v: &[f64]) -> Option<f64> {
    if v.is_empty() {
        None
    } else {
        Some(v.iter().sum::<f64>() / v.len() as f64)
    }
}

fn merge_extra(extra: &Extra, session_active_minutes: f64) -> Extra {
    if !extra.has_input && !extra.has_web {
        return Extra {
            total_keys: extra.total_keys,
            total_clicks: extra.total_clicks,
            keys_per_active_hour: extra.keys_per_active_hour,
            web_minutes: extra.web_minutes,
            web_pages: extra.web_pages,
            web_share_pct: extra.web_share_pct,
            cpu_avg: extra.cpu_avg,
            cpu_p95: extra.cpu_p95,
            gpu_avg: extra.gpu_avg,
            gpu_temp_max: extra.gpu_temp_max,
            mem_avg: extra.mem_avg,
            has_input: extra.has_input,
            has_web: extra.has_web,
            has_hardware: extra.has_hardware,
            net_up: extra.net_up,
            net_down: extra.net_down,
            has_net: extra.has_net,
            search_count: extra.search_count,
        };
    }

    let active = session_active_minutes.max(0.0);
    let keys_per_hour = if active > 0.0 {
        extra.total_keys / (active / 60.0)
    } else {
        extra.keys_per_active_hour
    };
    let web_share = if active + extra.web_minutes > 0.0 {
        extra.web_minutes / (active + extra.web_minutes) * 100.0
    } else {
        extra.web_share_pct
    };

    Extra {
        total_keys: extra.total_keys,
        total_clicks: extra.total_clicks,
        keys_per_active_hour: keys_per_hour,
        web_minutes: extra.web_minutes,
        web_pages: extra.web_pages,
        web_share_pct: web_share,
        cpu_avg: extra.cpu_avg,
        cpu_p95: extra.cpu_p95,
        gpu_avg: extra.gpu_avg,
        gpu_temp_max: extra.gpu_temp_max,
        mem_avg: extra.mem_avg,
        has_input: extra.has_input,
        has_web: extra.has_web,
        has_hardware: extra.has_hardware,
        net_up: extra.net_up,
        net_down: extra.net_down,
        has_net: extra.has_net,
        search_count: extra.search_count,
    }
}

fn extra_to_json(e: &Extra) -> Value {
    json!({
        "totalKeys": e.total_keys,
        "totalClicks": e.total_clicks,
        "keysPerActiveHour": e.keys_per_active_hour,
        "webMinutes": e.web_minutes,
        "webPages": e.web_pages,
        "webSharePct": e.web_share_pct,
        "cpuLoadAvg": e.cpu_avg,
        "cpuLoadP95": e.cpu_p95,
        "gpuLoadAvg": e.gpu_avg,
        "gpuTempMax": e.gpu_temp_max,
        "memLoadAvg": e.mem_avg,
        "hasInputData": e.has_input,
        "hasWebData": e.has_web,
        "hasHardwareData": e.has_hardware,
        "netBytesUp": e.net_up,
        "netBytesDown": e.net_down,
        "hasNetData": e.has_net,
        "searchCount": e.search_count,
    })
}

#[allow(clippy::too_many_arguments)]
fn build_insights(
    cur_min: f64,
    prev_min: f64,
    cur_prod_pct: f64,
    prev_prod_pct: f64,
    cur_focus: i64,
    prev_focus: i64,
    cur_night: f64,
    sessions: &[S],
    days_with_data: usize,
    extra: &Extra,
    category_shares: &[Value],
) -> Vec<Value> {
    let mut list: Vec<Value> = Vec::new();

    if days_with_data < 2 || cur_min < 30.0 {
        list.push(json!({
            "id": "low-data", "severity": "info", "title": "数据不足",
            "message": format!("本周期仅有 {} 天、{} 活跃数据，洞察可能不准确。请确认监控已开启。", days_with_data, format_hours(cur_min)),
            "actionKey": null,
        }));
        return list;
    }

    if prev_prod_pct > 0.0 {
        let drop = prev_prod_pct - cur_prod_pct;
        if drop >= 15.0 {
            list.push(json!({
                "id": "prod-drop", "severity": "warning", "title": "生产力下降",
                "message": format!("生产力占比从 {}% 降至 {}%（-{} 个百分点）。可到「构成」查看是哪些应用拖累。", f0(prev_prod_pct), f0(cur_prod_pct), f0(drop)),
                "actionKey": "compose",
            }));
        } else if cur_prod_pct - prev_prod_pct >= 15.0 {
            list.push(json!({
                "id": "prod-up", "severity": "success", "title": "生产力提升",
                "message": format!("生产力占比提升至 {}%（+{} 个百分点），继续保持。", f0(cur_prod_pct), f0(cur_prod_pct - prev_prod_pct)),
                "actionKey": null,
            }));
        }
    }

    if prev_min > 60.0 {
        let change = (cur_min - prev_min) / prev_min * 100.0;
        if change <= -30.0 {
            list.push(json!({
                "id": "active-drop", "severity": "warning", "title": "活跃时间明显减少",
                "message": format!("总活跃 {}，较上一周期下降 {}%。是否漏采或休息较多？", format_hours(cur_min), f0(change.abs())),
                "actionKey": null,
            }));
        } else if change >= 50.0 {
            list.push(json!({
                "id": "active-up", "severity": "info", "title": "活跃时间大幅增加",
                "message": format!("总活跃 {}，较上一周期增加 {}%。", format_hours(cur_min), f0(change)),
                "actionKey": null,
            }));
        }
    }

    if cur_night >= 90.0 {
        list.push(json!({
            "id": "night", "severity": "warning", "title": "夜间使用偏多",
            "message": format!("23:00–06:00 使用 {}，建议关注作息。", format_hours(cur_night)),
            "actionKey": "rhythm",
        }));
    }

    if prev_focus > 0 && (cur_focus as f64) < prev_focus as f64 * 0.5 {
        list.push(json!({
            "id": "focus-drop", "severity": "warning", "title": "专注段减少",
            "message": format!("≥25 分钟专注段 {} 次（上期 {} 次）。可尝试固定深度工作时段。", cur_focus, prev_focus),
            "actionKey": "rhythm",
        }));
    } else if cur_focus >= 10 {
        list.push(json!({
            "id": "focus-good", "severity": "success", "title": "专注状态良好",
            "message": format!("本周期完成 {} 次专注段。", cur_focus),
            "actionKey": null,
        }));
    }

    let long_run = sessions
        .iter()
        .filter(|s| (s.end - s.start) / 3600.0 >= 3.0)
        .count();
    if long_run > 0 {
        list.push(json!({
            "id": "long-run", "severity": "warning", "title": "注意休息",
            "message": format!("有 {} 次连续使用超过 3 小时。", long_run),
            "actionKey": null,
        }));
    }

    // 高效小时
    let mut by_hour: BTreeMap<u32, f64> = BTreeMap::new();
    for s in sessions {
        *by_hour.entry(s.hour).or_insert(0.0) += s.minutes();
    }
    let top_hour = by_hour
        .iter()
        .max_by(|a, b| a.1.partial_cmp(b.1).unwrap_or(std::cmp::Ordering::Equal))
        .map(|(h, m)| (*h, *m));
    // C# 是 OrderByDescending().First()：并列时取先出现（键升序），BTreeMap 的 max_by 在并列返回后者，此处按「分钟最大且键最小」修正
    let top_hour = top_hour.map(|(h, m)| {
        let same: Vec<(u32, f64)> = by_hour.iter().filter(|(_, v)| (**v - m).abs() < f64::EPSILON).map(|(k, v)| (*k, *v)).collect();
        (same.first().map(|x| x.0).unwrap_or(h), m)
    });
    if let Some((h, m)) = top_hour {
        if m >= 60.0 {
            list.push(json!({
                "id": "peak-hour", "severity": "info", "title": "高效时段",
                "message": format!("{}:00–{}:00 累计 {}，适合安排深度工作。", h, h + 1, format_hours(m)),
                "actionKey": "rhythm",
            }));
        }
    }

    // Top 应用
    let mut app_min: HashMap<String, f64> = HashMap::new();
    for s in sessions {
        *app_min.entry(s.proc_name.clone()).or_insert(0.0) += s.minutes();
    }
    let top_app = app_min
        .iter()
        .max_by(|a, b| a.1.partial_cmp(b.1).unwrap_or(std::cmp::Ordering::Equal))
        .map(|(k, v)| (k.clone(), *v));
    if let Some((name, min)) = top_app {
        if min >= 60.0 {
            list.push(json!({
                "id": "top-app", "severity": "info", "title": "最常用应用",
                "message": format!("{} 占用 {}。", name, format_hours(min)),
                "actionKey": "compose",
            }));
        }
    }

    // 构成
    if let Some(top) = category_shares.first() {
        let pct = top["percentage"].as_f64().unwrap_or(0.0);
        if pct >= 45.0 {
            list.push(json!({
                "id": "cat-concentrate", "severity": "info", "title": "时间高度集中",
                "message": format!("「{}」约占 {}%（{}）。若非目标分类，可到构成查看明细。", top["category"].as_str().unwrap_or(""), f0(pct), format_hours(top["minutes"].as_f64().unwrap_or(0.0))),
                "actionKey": "compose",
            }));
        }
    }

    // 多源：网页
    if extra.has_web && extra.web_share_pct >= 40.0 && extra.web_minutes >= 60.0 {
        list.push(json!({
            "id": "web-heavy", "severity": "info", "title": "网页占比较高",
            "message": format!("有效网页浏览 {}，约占行为时间 {}%。可在网页访问页查看域名分布。", format_hours(extra.web_minutes), f0(extra.web_share_pct)),
            "actionKey": null,
        }));
    }

    // 多源：输入密度
    if extra.has_input && cur_min >= 120.0 && extra.keys_per_active_hour > 0.0 && extra.keys_per_active_hour < 80.0 {
        list.push(json!({
            "id": "low-input", "severity": "warning", "title": "输入密度偏低",
            "message": format!("活跃 {}，但按键密度仅 {}/小时，会话可能含挂机/只读。", format_hours(cur_min), f0(extra.keys_per_active_hour)),
            "actionKey": null,
        }));
    } else if extra.has_input && extra.keys_per_active_hour >= 400.0 {
        list.push(json!({
            "id": "high-input", "severity": "info", "title": "输入强度较高",
            "message": format!("按键密度约 {}/小时，节奏偏紧，注意穿插休息。", f0(extra.keys_per_active_hour)),
            "actionKey": null,
        }));
    }

    // 多源：硬件
    if extra.has_hardware {
        if let Some(t) = extra.gpu_temp_max {
            if t >= 85.0 {
                list.push(json!({
                    "id": "gpu-hot", "severity": "warning", "title": "GPU 温度偏高",
                    "message": format!("周期内 GPU 日峰值最高 {}°C，长期高温可能影响稳定性与噪音。", f0(t)),
                    "actionKey": null,
                }));
            }
        }
        let cpu_high = extra.cpu_p95.map(|p| p >= 90.0).unwrap_or(false)
            || extra.cpu_avg.map(|a| a >= 75.0).unwrap_or(false);
        if cpu_high {
            list.push(json!({
                "id": "cpu-high", "severity": "info", "title": "CPU 负载较高",
                "message": format!("CPU 日均 {}%、P95 {}%，可在硬件监控页对照运行中的应用。", f0(extra.cpu_avg.unwrap_or(0.0)), f0(extra.cpu_p95.unwrap_or(0.0))),
                "actionKey": null,
            }));
        }
        if let Some(g) = extra.gpu_avg {
            if g >= 70.0 {
                list.push(json!({
                    "id": "gpu-high", "severity": "info", "title": "GPU 持续高负载",
                    "message": format!("GPU 日均负载约 {}%，创作/游戏/AI 推理会拉高此项。", f0(g)),
                    "actionKey": null,
                }));
            }
        }
    }

    list
}

fn build_hardware(sessions: &[S], samples: &[HwSample]) -> Value {
    let days = {
        let mut set: Vec<i64> = samples.iter().map(|s| s.start_day).collect();
        set.sort_unstable();
        set.dedup();
        set.len()
    };

    let cpu_vals: Vec<f64> = samples.iter().filter_map(|s| s.cpu).collect();
    let gpu_vals: Vec<f64> = samples.iter().filter_map(|s| s.gpu).collect();
    let temps: Vec<f64> = samples.iter().filter_map(|s| s.temp).collect();
    let mems: Vec<f64> = samples.iter().filter_map(|s| s.mem).collect();

    let app_loads = align_app_loads(sessions, samples);
    let hours = build_hour_buckets(samples);

    json!({
        "appLoads": app_loads,
        "hours": hours,
        "cpuLoadAvg": mean(&cpu_vals).map(round1),
        "cpuLoadP95": if cpu_vals.is_empty() { None } else { Some(percentile95(&cpu_vals)) },
        "gpuLoadAvg": mean(&gpu_vals).map(round1),
        "gpuTempMax": temps.iter().cloned().fold(None::<f64>, |acc, v| Some(acc.map_or(v, |a| a.max(v)))).map(round1),
        "memLoadAvg": mean(&mems).map(round1),
        "sampleMinutes": samples.len(),
        "sampleDays": days,
        "hasData": true,
    })
}

/// 应用 × 分钟样本对齐：同一分钟归属重叠时长最长的会话（平手取先出现者）。
fn align_app_loads(sessions: &[S], samples: &[HwSample]) -> Vec<Value> {
    if sessions.is_empty() || samples.is_empty() {
        return Vec::new();
    }

    let closed: Vec<&S> = sessions.iter().filter(|s| s.end > s.start).collect();
    if closed.is_empty() {
        return Vec::new();
    }

    // 展示名：首个非空 AppName
    let mut display: HashMap<String, String> = HashMap::new();
    for s in closed.iter() {
        let key = s.proc_name.to_lowercase();
        let entry = display.entry(key).or_insert_with(|| s.proc_name.clone());
        if entry.as_str() == s.proc_name {
            if let Some(n) = s.app_name.as_deref() {
                if !n.trim().is_empty() {
                    *display.get_mut(&s.proc_name.to_lowercase()).unwrap() = n.to_string();
                }
            }
        }
    }

    struct Bucket {
        proc_name: String,
        minutes: f64,
        cpu: Vec<f64>,
        gpu: Vec<f64>,
        temp: Vec<f64>,
        valid: usize,
    }
    let mut buckets: Vec<Bucket> = Vec::new();
    let mut index: HashMap<String, usize> = HashMap::new();

    for sample in samples {
        let sample_start = sample.start;
        let sample_end = sample.start + 60.0;

        let mut best: Option<&S> = None;
        let mut best_overlap = 0.0f64;
        for s in closed.iter() {
            let o_start = s.start.max(sample_start);
            let o_end = s.end.min(sample_end);
            let overlap = (o_end - o_start) / 60.0;
            if overlap <= 0.0 {
                continue;
            }
            if s.proc_name.is_empty() {
                continue;
            }
            if overlap > best_overlap {
                best_overlap = overlap;
                best = Some(s);
            }
        }

        let Some(sess) = best else { continue };
        let key = sess.proc_name.to_lowercase();

        let idx = *index.entry(key).or_insert_with(|| {
            buckets.push(Bucket {
                proc_name: sess.proc_name.clone(),
                minutes: 0.0,
                cpu: Vec::new(),
                gpu: Vec::new(),
                temp: Vec::new(),
                valid: 0,
            });
            buckets.len() - 1
        });

        let b = &mut buckets[idx];
        b.minutes += best_overlap;

        let has_reading = sample.cpu.is_some() || sample.gpu.is_some() || sample.temp.is_some();
        if has_reading {
            let cpu = sample.cpu.unwrap_or(0.0);
            let gpu = sample.gpu.unwrap_or(0.0);
            // 只统计真正有读数的样本（全 0 且无温度视为无传感器）
            if sample.temp.is_some() || cpu > 0.0 || gpu > 0.0 {
                b.cpu.push(cpu);
                b.gpu.push(gpu);
                if let Some(t) = sample.temp {
                    b.temp.push(t);
                }
                b.valid += 1;
            }
        }
    }

    let mut out: Vec<Value> = buckets
        .into_iter()
        .map(|b| {
            let name = display
                .get(&b.proc_name.to_lowercase())
                .cloned()
                .unwrap_or_else(|| b.proc_name.clone());
            json!({
                "processName": b.proc_name,
                "displayName": name,
                "overlapMinutes": round1(b.minutes),
                "cpuLoadAvg": mean(&b.cpu).map(round1),
                "cpuLoadP95": if b.cpu.is_empty() { None } else { Some(percentile95(&b.cpu)) },
                "gpuLoadAvg": mean(&b.gpu).map(round1),
                "gpuTempMax": b.temp.iter().cloned().fold(None::<f64>, |acc, v| Some(acc.map_or(v, |a| a.max(v)))).map(round1),
                "sampleCount": b.valid,
            })
        })
        .collect();

    out.sort_by(|a, b| {
        let ag = a["gpuLoadAvg"].as_f64().unwrap_or(0.0);
        let bg = b["gpuLoadAvg"].as_f64().unwrap_or(0.0);
        let ac = a["cpuLoadAvg"].as_f64().unwrap_or(0.0);
        let bc = b["cpuLoadAvg"].as_f64().unwrap_or(0.0);
        let am = a["overlapMinutes"].as_f64().unwrap_or(0.0);
        let bm = b["overlapMinutes"].as_f64().unwrap_or(0.0);
        bg.partial_cmp(&ag)
            .unwrap_or(std::cmp::Ordering::Equal)
            .then(bc.partial_cmp(&ac).unwrap_or(std::cmp::Ordering::Equal))
            .then(bm.partial_cmp(&am).unwrap_or(std::cmp::Ordering::Equal))
    });
    out.truncate(12);
    out
}

fn build_hour_buckets(samples: &[HwSample]) -> Vec<Value> {
    (0..24)
        .map(|h| {
            let hs: Vec<&HwSample> = samples.iter().filter(|s| s.hour == h).collect();
            if hs.is_empty() {
                return json!({ "hour": h, "label": format!("{:02}", h), "cpuLoadAvg": 0.0, "gpuLoadAvg": 0.0, "gpuTempAvg": null, "sampleCount": 0 });
            }
            let cpu: Vec<f64> = hs.iter().filter_map(|s| s.cpu).collect();
            let gpu: Vec<f64> = hs.iter().filter_map(|s| s.gpu).collect();
            let temp: Vec<f64> = hs.iter().filter_map(|s| s.temp).collect();
            json!({
                "hour": h,
                "label": format!("{:02}", h),
                "cpuLoadAvg": mean(&cpu).map(round1).unwrap_or(0.0),
                "gpuLoadAvg": mean(&gpu).map(round1).unwrap_or(0.0),
                "gpuTempAvg": mean(&temp).map(round1),
                "sampleCount": hs.len(),
            })
        })
        .collect()
}

fn build_hardware_insights(hw: &Value) -> Vec<Value> {
    let mut list = Vec::new();
    let loads = hw["appLoads"].as_array().cloned().unwrap_or_default();

    if let Some(app) = loads
        .iter()
        .find(|a| a["gpuLoadAvg"].as_f64().map(|g| g >= 45.0).unwrap_or(false))
    {
        list.push(json!({
            "id": "gpu-app", "severity": "info", "title": "高 GPU 应用",
            "message": format!("{} 前台重叠时段 GPU 均约 {}%（{}，样本 {}）。",
                app["displayName"].as_str().unwrap_or(""),
                f0(app["gpuLoadAvg"].as_f64().unwrap_or(0.0)),
                format_hours(app["overlapMinutes"].as_f64().unwrap_or(0.0)),
                app["sampleCount"].as_i64().unwrap_or(0)),
            "actionKey": null,
        }));
    }

    let mut best_cpu: Option<&Value> = None;
    for a in loads.iter() {
        if let Some(c) = a["cpuLoadAvg"].as_f64() {
            if best_cpu.map_or(true, |b| b["cpuLoadAvg"].as_f64().unwrap_or(0.0) < c) {
                best_cpu = Some(a);
            }
        }
    }
    if let Some(app) = best_cpu {
        if app["cpuLoadAvg"].as_f64().unwrap_or(0.0) >= 70.0 {
            list.push(json!({
                "id": "cpu-app", "severity": "info", "title": "高 CPU 应用",
                "message": format!("{} 重叠时段 CPU 均约 {}%。",
                    app["displayName"].as_str().unwrap_or(""),
                    f0(app["cpuLoadAvg"].as_f64().unwrap_or(0.0))),
                "actionKey": null,
            }));
        }
    }

    if let Some(t) = hw["gpuTempMax"].as_f64() {
        if t >= 85.0 {
            list.push(json!({
                "id": "gpu-hot-sample", "severity": "warning", "title": "样本内 GPU 高温",
                "message": format!("周期分钟样本 GPU 峰值 {}°C，建议关注散热与连续高负载时长。", f0(t)),
                "actionKey": null,
            }));
        }
    }

    list
}

fn percentile95(values: &[f64]) -> f64 {
    if values.is_empty() {
        return 0.0;
    }
    let mut ordered: Vec<f64> = values.to_vec();
    ordered.sort_by(|a, b| a.partial_cmp(b).unwrap_or(std::cmp::Ordering::Equal));
    let mut index = (0.95 * ordered.len() as f64).ceil() as i64 - 1;
    if index < 0 {
        index = 0;
    }
    round1(ordered[index as usize])
}

fn make_kpi(id: &str, name: &str, display: String, cur: f64, prev: f64, hint: &str) -> Value {
    if prev <= 0.0 && cur <= 0.0 {
        return json!({ "id": id, "name": name, "value": display, "deltaText": "—", "deltaDirection": "none", "hint": hint });
    }
    if prev <= 0.0 {
        return json!({ "id": id, "name": name, "value": display, "deltaText": "新增", "deltaDirection": "up", "hint": hint });
    }
    let pct = (cur - prev) / prev * 100.0;
    let dir = if pct.abs() < 1.0 { "flat" } else if pct > 0.0 { "up" } else { "down" };
    let arrow = if dir == "flat" { "→" } else if pct > 0.0 { "▲" } else { "▼" };
    json!({
        "id": id, "name": name, "value": display,
        "deltaText": format!("{} {}%", arrow, f0(pct.abs())),
        "deltaDirection": dir, "hint": hint,
    })
}

/// .NET `"F0"` 口径：银行家舍入（.NET Core 3.0 起浮点格式化为 round-half-to-even）。
/// 不能用 `f64::round()`（远离零），否则 42.5 会得到 43 而 C# 给 42。
fn f0(v: f64) -> i64 {
    v.round_ties_even() as i64
}

/// 与 `AnalyticsEngine.FormatHours` 逐字一致。
fn format_hours(minutes: f64) -> String {
    if minutes < 1.0 {
        return "0分钟".to_string();
    }
    let h = (minutes / 60.0) as i64;
    let m = (minutes % 60.0) as i64;
    if h <= 0 {
        format!("{}分钟", m)
    } else if m == 0 {
        format!("{}小时", h)
    } else {
        format!("{}小时{}分", h, m)
    }
}

/// .NET `N0` 千分位。
fn group_thousands(v: f64) -> String {
    let n = f0(v);
    let s = n.abs().to_string();
    let mut out = String::new();
    for (i, ch) in s.chars().enumerate() {
        if i > 0 && (s.len() - i) % 3 == 0 {
            out.push(',');
        }
        out.push(ch);
    }
    if n < 0 {
        format!("-{out}")
    } else {
        out
    }
}

fn day_name(day: i64) -> &'static str {
    // 1970-01-01 是周四
    match (day + 4).rem_euclid(7) {
        0 => "日",
        1 => "一",
        2 => "二",
        3 => "三",
        4 => "四",
        5 => "五",
        _ => "六",
    }
}

fn short_date(day: i64) -> String {
    let iso = civil_from_days(day);
    let m: i64 = iso[5..7].parse().unwrap_or(0);
    let d: i64 = iso[8..10].parse().unwrap_or(0);
    format!("{}/{}", m, d)
}
