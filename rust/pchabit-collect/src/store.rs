//! 写库层：采集结果直接落 SQLite，口径与 WPF 版逐列对齐。
//!
//! 关键口径（照抄 EF 实现，改动会破坏跨版本可读性）：
//! - 时间列都是字符串：`HardwareSamples.Timestamp` = `yyyy-MM-dd HH:mm`，
//!   `AppSessions.StartTime` = `yyyy-MM-dd HH:mm:ss.fffffff`，
//!   键鼠 `Date` = `yyyy-MM-dd 00:00:00`（EF 把 DateTime 写成带时分秒的形式）；
//! - `AppSessions.Duration` 是 100ns ticks（TimeSpan），不是秒；
//! - `KeyboardSessions.KeyFrequency` / `KeyCategoryFrequency` / `TypingBursts` 都是 JSON 字符串；
//! - 硬件样本按 `Timestamp` 唯一键 upsert，网络分钟样本按 `Timestamp` 累加。

use std::collections::BTreeMap;
use std::path::Path;
use std::time::Duration;

use rusqlite::{params, Connection, OptionalExtension};
use serde_json::{json, Map, Value};

use crate::timefmt;

pub struct Store {
    conn: Connection,
}

/// 硬件分钟桶（与 C# `MinuteBucket` 同构：均值维护 sum/count，峰值维护 max）
#[derive(Default)]
pub struct HwBucket {
    pub key: String,
    pub cpu_sum: f64,
    pub cpu_n: i64,
    pub cpu_max: f64,
    pub gpu_sum: f64,
    pub gpu_n: i64,
    pub gpu_max: f64,
    pub gpu_temp: f64,
    pub vram_used: f64,
    pub vram_total: f64,
    pub mem_sum: f64,
    pub mem_n: i64,
    pub mem_max: f64,
    pub mem_used_max: f64,
    pub disk_act_sum: f64,
    pub disk_act_n: i64,
    pub disk_read_max: f64,
    pub disk_write_max: f64,
    pub net_up_sum: f64,
    pub net_down_sum: f64,
    pub net_n: i64,
    pub samples: i64,
    pub flags: i64,
}

pub const F_CPU_LOAD: i64 = 1 << 0;
pub const F_GPU_LOAD: i64 = 1 << 2;
pub const F_GPU_TEMP: i64 = 1 << 3;
pub const F_VRAM_USED: i64 = 1 << 4;
pub const F_VRAM_TOTAL: i64 = 1 << 5;
pub const F_MEM_LOAD: i64 = 1 << 6;
pub const F_MEM_USED: i64 = 1 << 7;
pub const F_DISK_ACT: i64 = 1 << 8;
pub const F_DISK_READ: i64 = 1 << 9;
pub const F_DISK_WRITE: i64 = 1 << 10;
pub const F_NET_UP: i64 = 1 << 11;
pub const F_NET_DOWN: i64 = 1 << 12;

impl HwBucket {
    fn avg(sum: f64, n: i64) -> Option<f64> {
        if n > 0 {
            Some(sum / n as f64)
        } else {
            None
        }
    }
}

/// 键鼠增量（按 (日期, 小时, 进程) 分桶后的增量）
#[derive(Default, Clone)]
pub struct KbDelta {
    pub date: String,
    pub hour: u32,
    pub process: String,
    pub total: i64,
    pub repeat: i64,
    pub backspace: i64,
    pub delete: i64,
    pub undo: i64,
    pub freq: BTreeMap<i32, i64>,
    pub cats: BTreeMap<String, i64>,
    /// 组合键 → 次数（如 `Ctrl+C`），4.12.20 补回 WPF 时代的 `Shortcuts`
    pub combos: BTreeMap<String, i64>,
    pub avg_speed: f64,
    pub peak_speed: f64,
    pub bursts: Vec<(u64, u64, i64)>, // (起始毫秒偏移, 时长 ms, 键数)
}

#[derive(Default, Clone)]
pub struct MsDelta {
    pub date: String,
    pub hour: u32,
    pub process: String,
    pub left: i64,
    pub right: i64,
    pub middle: i64,
    pub scroll: i64,
    pub dist: f64,
    pub move_events: i64,
}

#[derive(Default, Clone)]
pub struct AppRow {
    pub start: String,
    pub end: String,
    pub duration_ticks: i64,
    pub process: String,
    pub path: String,
    pub app_name: String,
    pub title: String,
    pub class: String,
    pub x: f64,
    pub y: f64,
    pub width: f64,
    pub height: f64,
    pub maximized: bool,
    pub category: Option<String>,
}

/// `WebSessions` 一行。时长列都是 100ns ticks（与 `AppSessions.Duration` 同一单位）。
#[derive(Default, Clone)]
pub struct WebRow {
    pub id: String,
    pub start: String,
    pub end: Option<String>,
    pub duration_ticks: i64,
    pub url: String,
    pub title: String,
    pub domain: String,
    pub favicon: Option<String>,
    pub browser: String,
    pub tab_id: i64,
    pub scroll_depth: i64,
    pub click_count: i64,
    pub has_form: bool,
    pub is_active_tab: bool,
    pub active_ticks: i64,
    pub idle_ticks: i64,
    pub category_id: Option<i64>,
    pub category_name: Option<String>,
    pub category_source: Option<String>,
}

impl Store {
    pub fn open(path: &Path) -> Result<Store, String> {
        let conn = Connection::open(path).map_err(|e| format!("打开数据库失败: {e}"))?;
        conn.busy_timeout(Duration::from_millis(5000))
            .map_err(|e| format!("设置 busy_timeout 失败: {e}"))?;
        ensure_schema(&conn);
        Ok(Store { conn })
    }

    pub fn today_proc_totals(&self, date: &str) -> Vec<(String, i64, i64)> {
        let mut stmt = match self.conn.prepare(
            "SELECT ProcessName, COALESCE(SUM(BytesUp),0), COALESCE(SUM(BytesDown),0) \
             FROM ProcessNetworkUsages WHERE Date = ?1 GROUP BY ProcessName",
        ) {
            Ok(s) => s,
            Err(_) => return vec![],
        };
        let rows = stmt.query_map([date], |r| {
            Ok((
                r.get::<_, String>(0)?,
                r.get::<_, i64>(1)?,
                r.get::<_, i64>(2)?,
            ))
        });
        match rows {
            Ok(it) => it.filter_map(|x| x.ok()).collect(),
            Err(_) => vec![],
        }
    }

    pub fn hw_upsert(&self, b: &HwBucket) -> Result<(), String> {
        let sql = "INSERT INTO HardwareSamples \
            (Id, Timestamp, CpuLoadAvg, CpuLoadMax, CpuTempMax, GpuLoadAvg, GpuLoadMax, GpuTempMax, \
             VramUsedMax, VramTotal, MemLoadAvg, MemLoadMax, MemUsedMax, \
             DiskActivityAvg, DiskReadMax, DiskWriteMax, NetUpAvg, NetDownAvg, SampleCount, SensorFlags) \
            VALUES (?1,?2,?3,?4,?5,?6,?7,?8,?9,?10,?11,?12,?13,?14,?15,?16,?17,?18,?19,?20) \
            ON CONFLICT(Timestamp) DO UPDATE SET \
             CpuLoadAvg=excluded.CpuLoadAvg, CpuLoadMax=excluded.CpuLoadMax, GpuLoadAvg=excluded.GpuLoadAvg, \
             GpuLoadMax=excluded.GpuLoadMax, GpuTempMax=excluded.GpuTempMax, VramUsedMax=excluded.VramUsedMax, \
             VramTotal=excluded.VramTotal, MemLoadAvg=excluded.MemLoadAvg, MemLoadMax=excluded.MemLoadMax, \
             MemUsedMax=excluded.MemUsedMax, DiskActivityAvg=excluded.DiskActivityAvg, \
             DiskReadMax=excluded.DiskReadMax, DiskWriteMax=excluded.DiskWriteMax, \
             NetUpAvg=excluded.NetUpAvg, NetDownAvg=excluded.NetDownAvg, \
             SampleCount=excluded.SampleCount, SensorFlags=excluded.SensorFlags";
        let cpu_avg = HwBucket::avg(b.cpu_sum, b.cpu_n);
        let gpu_avg = HwBucket::avg(b.gpu_sum, b.gpu_n);
        let mem_avg = HwBucket::avg(b.mem_sum, b.mem_n);
        let disk_act = HwBucket::avg(b.disk_act_sum, b.disk_act_n);
        let net_up = if b.net_n > 0 {
            Some(b.net_up_sum / b.net_n as f64)
        } else {
            None
        };
        let net_down = if b.net_n > 0 {
            Some(b.net_down_sum / b.net_n as f64)
        } else {
            None
        };
        self.conn
            .execute(
                sql,
                params![
                    uuid_like(),
                    b.key,
                    cpu_avg,
                    opt(b.cpu_max, b.cpu_n),
                    None::<f64>,
                    gpu_avg,
                    opt(b.gpu_max, b.gpu_n),
                    opt(b.gpu_temp, b.gpu_n),
                    opt(b.vram_used, b.gpu_n),
                    opt(b.vram_total, b.gpu_n),
                    mem_avg,
                    opt(b.mem_max, b.mem_n),
                    opt(b.mem_used_max, b.mem_n),
                    disk_act,
                    opt(b.disk_read_max, b.disk_act_n),
                    opt(b.disk_write_max, b.disk_act_n),
                    net_up,
                    net_down,
                    b.samples,
                    b.flags,
                ],
            )
            .map_err(|e| format!("写 HardwareSamples 失败: {e}"))?;
        Ok(())
    }

    pub fn net_minute_add(
        &self,
        key: &str,
        up: i64,
        down: i64,
        peak_up: f64,
        peak_down: f64,
    ) -> Result<(), String> {
        let now = timefmt::stamp7(&timefmt::now());
        self.conn
            .execute(
                "INSERT INTO NetworkTrafficSamples \
                 (Id, Timestamp, BytesUp, BytesDown, PeakUpBps, PeakDownBps, SampleCount, LastUpdated) \
                 VALUES (?1,?2,?3,?4,?5,?6,1,?7) \
                 ON CONFLICT(Timestamp) DO UPDATE SET \
                  BytesUp=BytesUp+excluded.BytesUp, BytesDown=BytesDown+excluded.BytesDown, \
                  PeakUpBps=MAX(PeakUpBps, excluded.PeakUpBps), \
                  PeakDownBps=MAX(PeakDownBps, excluded.PeakDownBps), \
                  SampleCount=SampleCount+1, LastUpdated=excluded.LastUpdated",
                params![uuid_like(), key, up, down, peak_up, peak_down, now],
            )
            .map_err(|e| format!("写 NetworkTrafficSamples 失败: {e}"))?;
        Ok(())
    }

    pub fn proc_hour_add(
        &self,
        date: &str,
        hour: u32,
        name: &str,
        dup: i64,
        ddown: i64,
        peak: i64,
    ) -> Result<(), String> {
        if dup == 0 && ddown == 0 {
            return Ok(());
        }
        let now = timefmt::stamp7(&timefmt::now());
        self.conn
            .execute(
                "INSERT INTO ProcessNetworkUsages \
                 (Date, Hour, ProcessName, BytesUp, BytesDown, PeakBytesPerSec, LastUpdated) \
                 VALUES (?1,?2,?3,?4,?5,?6,?7) \
                 ON CONFLICT(Date, Hour, ProcessName) DO UPDATE SET \
                  BytesUp=BytesUp+excluded.BytesUp, BytesDown=BytesDown+excluded.BytesDown, \
                  PeakBytesPerSec=MAX(PeakBytesPerSec, excluded.PeakBytesPerSec), \
                  LastUpdated=excluded.LastUpdated",
                params![date, hour as i64, name, dup, ddown, peak, now],
            )
            .map_err(|e| format!("写 ProcessNetworkUsages 失败: {e}"))?;
        Ok(())
    }

    pub fn app_insert(&self, r: &AppRow) -> Result<(), String> {
        // 「数据匿名化」：窗口标题不入库（对已写入的旧行不追溯）
        let title = if crate::settings::bool_of("AnonymizeData", false) {
            ""
        } else {
            r.title.as_str()
        };
        self.conn
            .execute(
                "INSERT INTO AppSessions \
                 (Id, StartTime, EndTime, Duration, ProcessName, ExecutablePath, AppName, AppVersion, \
                  Publisher, WindowTitle, WindowClass, WindowX, WindowY, WindowWidth, WindowHeight, \
                  IsMaximized, InputEventCount, ActiveDuration, ProjectContext, Category) \
                 VALUES (?1,?2,?3,?4,?5,?6,?7,NULL,NULL,?8,?9,?10,?11,?12,?13,?14,0,0,NULL,?15)",
                params![
                    uuid_like(),
                    r.start,
                    r.end,
                    r.duration_ticks,
                    r.process,
                    r.path,
                    r.app_name,
                    title,
                    r.cls(),
                    r.x,
                    r.y,
                    r.width,
                    r.height,
                    if r.maximized { 1 } else { 0 },
                    r.category,
                ],
            )
            .map_err(|e| format!("写 AppSessions 失败: {e}"))?;
        Ok(())
    }

    /// 便签永久保存：这里**不再**提供按保留期清理的入口。
    ///
    /// 历史：4.12.25 曾按 `UpdatedAt < 今天-N天` 软删，把用户两张月前的笔记静默删掉（4.12.28 已恢复）；
    /// 4.12.31 起彻底移除，`StickyNotesRetentionDays` 在设置页标「已废弃」。
    pub fn vacuum_into(&self, path: &str) -> Result<u64, String> {
        let escaped = path.replace('\'', "''");
        self.conn
            .execute_batch(&format!("VACUUM INTO '{escaped}'"))
            .map_err(|e| format!("归档数据库失败: {e}"))?;
        Ok(std::fs::metadata(path).map(|m| m.len()).unwrap_or(0))
    }

    pub fn kb_add(&self, d: &KbDelta) -> Result<(), String> {
        if d.total == 0 && d.repeat == 0 && d.freq.is_empty() && d.combos.is_empty() {
            return Ok(());
        }
        let date_key = d.date.as_str();
        let hour = d.hour;
        let process = d.process.as_str();
        let (mut freq_new, mut cats_new, mut bursts_new, mut combos_new) =
            self.kb_existing(date_key, hour, process)?;
        for (k, v) in &d.freq {
            *freq_new.entry(*k).or_insert(0) += v;
        }
        for (k, v) in &d.cats {
            *cats_new.entry(k.clone()).or_insert(0) += v;
        }
        for (k, v) in &d.combos {
            *combos_new.entry(k.clone()).or_insert(0) += v;
        }
        for (off_ms, dur_ms, count) in &d.bursts {
            bursts_new.push(json!({
                "StartTime": burst_time(date_key, *off_ms),
                "Duration": timefmt::duration_text(*dur_ms),
                "KeyCount": count,
                "Context": Value::Null,
            }));
        }
        let freq_json = {
            let mut m = Map::new();
            for (k, v) in &freq_new {
                m.insert(k.to_string(), json!(v));
            }
            Value::Object(m).to_string()
        };
        let cats_json = {
            let mut m = Map::new();
            for (k, v) in &cats_new {
                m.insert(k.clone(), json!(v));
            }
            Value::Object(m).to_string()
        };
        let combos_json = {
            let mut m = Map::new();
            for (k, v) in &combos_new {
                m.insert(k.clone(), json!(v));
            }
            Value::Object(m).to_string()
        };

        self.conn
            .execute(
                "INSERT INTO KeyboardSessions \
                 (Id, Date, Hour, ProcessName, TotalKeyPresses, KeyFrequency, KeyCategoryFrequency, \
                  AverageTypingSpeed, PeakTypingSpeed, UndoCount, DeleteCount, BackspaceCount, \
                  TypingBursts, Shortcuts, RepeatKeyPresses) \
                 VALUES (?1,?2,?3,?4,?5,?6,?7,?8,?9,?10,?11,?12,?13,?14,?15) \
                 ON CONFLICT(Date, Hour, ProcessName) DO UPDATE SET \
                  TotalKeyPresses=TotalKeyPresses+excluded.TotalKeyPresses, \
                  RepeatKeyPresses=RepeatKeyPresses+excluded.RepeatKeyPresses, \
                  BackspaceCount=BackspaceCount+excluded.BackspaceCount, \
                  DeleteCount=DeleteCount+excluded.DeleteCount, \
                  UndoCount=UndoCount+excluded.UndoCount, \
                  KeyFrequency=excluded.KeyFrequency, KeyCategoryFrequency=excluded.KeyCategoryFrequency, \
                  TypingBursts=excluded.TypingBursts, \
                  AverageTypingSpeed=MAX(AverageTypingSpeed, excluded.AverageTypingSpeed), \
                  PeakTypingSpeed=MAX(PeakTypingSpeed, excluded.PeakTypingSpeed)",
                params![
                    uuid_like(),
                    format!("{date_key} 00:00:00"),
                    hour as i64,
                    process,
                    d.total,
                    freq_json,
                    cats_json,
                    d.avg_speed,
                    d.peak_speed,
                    d.undo,
                    d.delete,
                    d.backspace,
                    Value::Array(bursts_new).to_string(),
                    combos_json,
                    d.repeat,
                ],
            )
            .map_err(|e| format!("写 KeyboardSessions 失败: {e}"))?;
        Ok(())
    }

    fn kb_existing(
        &self,
        date_key: &str,
        hour: u32,
        process: &str,
    ) -> Result<
        (
            BTreeMap<i32, i64>,
            BTreeMap<String, i64>,
            Vec<Value>,
            BTreeMap<String, i64>,
        ),
        String,
    > {
        let existing: Option<(String, String, String, String)> = self
            .conn
            .query_row(
                "SELECT KeyFrequency, KeyCategoryFrequency, TypingBursts, Shortcuts \
                 FROM KeyboardSessions WHERE Date = ?1 AND Hour = ?2 AND ifnull(ProcessName,'') = ?3",
                params![format!("{date_key} 00:00:00"), hour as i64, process],
                |r| Ok((r.get(0)?, r.get(1)?, r.get(2)?, r.get(3)?)),
            )
            .optional()
            .map_err(|e| format!("读 KeyboardSessions 失败: {e}"))?;

        match existing {
            None => Ok((
                BTreeMap::new(),
                BTreeMap::new(),
                vec![],
                BTreeMap::new(),
            )),
            Some((f, c, b, s)) => {
                let mut freq = BTreeMap::new();
                if let Ok(Value::Object(m)) = serde_json::from_str::<Value>(&f) {
                    for (k, v) in m {
                        if let (Ok(kk), Some(vv)) = (k.parse::<i32>(), v.as_i64()) {
                            freq.insert(kk, vv);
                        }
                    }
                }
                let mut cats = BTreeMap::new();
                if let Ok(Value::Object(m)) = serde_json::from_str::<Value>(&c) {
                    for (k, v) in m {
                        if let Some(vv) = v.as_i64() {
                            cats.insert(k, vv);
                        }
                    }
                }
                let bursts = serde_json::from_str::<Value>(&b)
                    .ok()
                    .and_then(|v| v.as_array().cloned())
                    .unwrap_or_default();
                // Shortcuts 兼容两代格式：老 WPF 写数组，这里写对象，读的时候都认
                let mut combos = BTreeMap::new();
                match serde_json::from_str::<Value>(&s) {
                    Ok(Value::Object(m)) => {
                        for (k, v) in m {
                            if let Some(vv) = v.as_i64() {
                                combos.insert(k, vv);
                            }
                        }
                    }
                    Ok(Value::Array(a)) => {
                        for item in a {
                            let name = item["name"]
                                .as_str()
                                .or_else(|| item["Name"].as_str())
                                .unwrap_or("")
                                .to_string();
                            let n = item["count"]
                                .as_i64()
                                .or_else(|| item["Count"].as_i64())
                                .unwrap_or(0);
                            if !name.is_empty() {
                                *combos.entry(name).or_insert(0) += n;
                            }
                        }
                    }
                    _ => {}
                }
                Ok((freq, cats, bursts, combos))
            }
        }
    }

    pub fn mouse_add(&self, d: &MsDelta) -> Result<(), String> {
        if d.left == 0 && d.right == 0 && d.middle == 0 && d.scroll == 0 && d.dist < 0.5 {
            return Ok(());
        }
        self.conn
            .execute(
                "INSERT INTO MouseSessions \
                 (Id, Date, Hour, ProcessName, LeftClickCount, RightClickCount, MiddleClickCount, \
                  TotalMoveDistance, MoveEventCount, ScrollCount, ScrollLines, AverageClickInterval) \
                 VALUES (?1,?2,?3,?4,?5,?6,?7,?8,?9,?10,0,0.0) \
                 ON CONFLICT(Date, Hour, ProcessName) DO UPDATE SET \
                  LeftClickCount=LeftClickCount+excluded.LeftClickCount, \
                  RightClickCount=RightClickCount+excluded.RightClickCount, \
                  MiddleClickCount=MiddleClickCount+excluded.MiddleClickCount, \
                  ScrollCount=ScrollCount+excluded.ScrollCount, \
                  TotalMoveDistance=TotalMoveDistance+excluded.TotalMoveDistance, \
                  MoveEventCount=MoveEventCount+excluded.MoveEventCount",
                params![
                    uuid_like(),
                    format!("{} 00:00:00", d.date),
                    d.hour as i64,
                    d.process.as_str(),
                    d.left,
                    d.right,
                    d.middle,
                    d.dist,
                    d.move_events,
                    d.scroll,
                ],
            )
            .map_err(|e| format!("写 MouseSessions 失败: {e}"))?;
        Ok(())
    }

    /// 类别：按进程名（小写，含 .exe）查映射，查不到给 None（页面按「未分类」处理）。
    pub fn category_of(&self, process: &str) -> Option<String> {
        let lower = process.to_ascii_lowercase();
        let key = if lower.ends_with(".exe") {
            lower.clone()
        } else {
            format!("{lower}.exe")
        };
        self.conn
            .query_row(
                "SELECT pc.Name FROM ProgramCategoryMappings m \
                 JOIN ProgramCategories pc ON pc.Id = m.CategoryId \
                 WHERE lower(m.ProcessName) = ?1 AND pc.IsActive = 1 LIMIT 1",
                [&key],
                |r| r.get::<_, String>(0),
            )
            .optional()
            .ok()
            .flatten()
    }

    pub fn app_name_of(&self, path: &str, fallback: &str) -> String {
        if !path.is_empty() {
            let desc = crate::win::file_description(path);
            if !desc.trim().is_empty() {
                return desc;
            }
        }
        fallback.to_string()
    }

    /// 网页会话一行（`WebSessions`）。就业中会话 `end` 为 None、`is_active_tab` 为 1。
    pub fn web_upsert(&self, r: &WebRow) -> Result<(), String> {
        self.conn
            .execute(
                "INSERT INTO WebSessions \
                 (Id, StartTime, EndTime, Duration, Url, Title, Domain, Favicon, Browser, TabId, \
                  ScrollDepth, ClickCount, HasFormInteraction, Referrer, Source, IsActiveTab, \
                  ActiveDuration, IdleDuration, CategoryId, CategoryName, CategorySource, IsLegacy) \
                 VALUES (?1,?2,?3,?4,?5,?6,?7,?8,?9,?10,?11,?12,?13,NULL,NULL,?14,?15,?16,?17,?18,?19,0) \
                 ON CONFLICT(Id) DO UPDATE SET \
                  EndTime=excluded.EndTime, Duration=excluded.Duration, Url=excluded.Url, \
                  Title=excluded.Title, Domain=excluded.Domain, Favicon=excluded.Favicon, \
                  Browser=excluded.Browser, TabId=excluded.TabId, ScrollDepth=excluded.ScrollDepth, \
                  ClickCount=excluded.ClickCount, HasFormInteraction=excluded.HasFormInteraction, \
                  IsActiveTab=excluded.IsActiveTab, ActiveDuration=excluded.ActiveDuration, \
                  IdleDuration=excluded.IdleDuration, CategoryId=excluded.CategoryId, \
                  CategoryName=excluded.CategoryName, CategorySource=excluded.CategorySource",
                params![
                    r.id,
                    r.start,
                    r.end,
                    r.duration_ticks,
                    r.url,
                    r.title,
                    r.domain,
                    r.favicon,
                    r.browser,
                    r.tab_id,
                    r.scroll_depth,
                    r.click_count,
                    if r.has_form { 1 } else { 0 },
                    if r.is_active_tab { 1 } else { 0 },
                    r.active_ticks,
                    r.idle_ticks,
                    r.category_id,
                    r.category_name,
                    r.category_source,
                ],
            )
            .map_err(|e| format!("写 WebSessions 失败: {e}"))?;
        Ok(())
    }

    /// 浏览历史一行。Id 用 `url|visitTime`（与 C# 侧去重键一致），重复访问直接忽略。
    pub fn history_add(
        &self,
        browser: &str,
        url: &str,
        title: &str,
        visit_ms: i64,
        ingested_ms: i64,
    ) -> Result<bool, String> {
        let id = format!("{url}|{visit_ms}");
        self.conn
            .execute(
                "INSERT OR IGNORE INTO BrowserHistoryItems \
                 (Id, Url, Title, VisitTime, VisitCount, SourceBrowser, IngestedAt) \
                 VALUES (?1,?2,?3,?4,1,?5,?6)",
                params![id, url, title, visit_ms, browser, ingested_ms],
            )
            .map_err(|e| format!("写 BrowserHistoryItems 失败: {e}"))
            .map(|n| n > 0)
    }

    /// 网站分类表（用于把域名物化成 CategoryId / CategoryName）。
    pub fn web_catalog(&self) -> (Vec<(i64, String, bool)>, Vec<(String, i64)>) {
        let mut cats = Vec::new();
        if let Ok(mut stmt) = self
            .conn
            .prepare("SELECT Id, Name, IsActive FROM WebsiteCategories")
        {
            if let Ok(rows) = stmt.query_map([], |r| {
                Ok((
                    r.get::<_, i64>(0)?,
                    r.get::<_, String>(1)?,
                    r.get::<_, i64>(2)? != 0,
                ))
            }) {
                cats = rows.filter_map(|x| x.ok()).collect();
            }
        }
        let mut maps = Vec::new();
        if let Ok(mut stmt) = self
            .conn
            .prepare("SELECT DomainPattern, CategoryId FROM WebsiteDomainMappings")
        {
            if let Ok(rows) = stmt.query_map([], |r| {
                Ok((r.get::<_, String>(0)?, r.get::<_, i64>(1)?))
            }) {
                maps = rows.filter_map(|x| x.ok()).collect();
            }
        }
        (cats, maps)
    }

    // ==================== 日汇总（DailySummaries / AppDailyStats，4.5.0）====================

    /// 程序分类映射：归一化进程名 → 分类名（只取 IsActive 的分类）。
    pub fn prog_cat_map(&self) -> Vec<(String, String)> {
        let mut maps = Vec::new();
        if let Ok(mut stmt) = self.conn.prepare(
            "SELECT m.ProcessName, c.Name FROM ProgramCategoryMappings m \
             JOIN ProgramCategories c ON m.CategoryId = c.Id WHERE c.IsActive = 1",
        ) {
            if let Ok(rows) = stmt.query_map([], |r| {
                Ok((r.get::<_, String>(0)?, r.get::<_, String>(1)?))
            }) {
                maps = rows.filter_map(|x| x.ok()).collect();
            }
        }
        maps
    }

    /// 键盘小时键量：`[(hour, keypresses)]`。`[from, to)` 为 `yyyy-MM-dd 00:00:00` 形态。
    pub fn keyboard_hours(&self, from: &str, to: &str) -> Vec<(i64, i64)> {
        let mut out = Vec::new();
        if let Ok(mut stmt) = self.conn.prepare(
            "SELECT Hour, TotalKeyPresses FROM KeyboardSessions WHERE Date >= ?1 AND Date < ?2",
        ) {
            if let Ok(rows) = stmt.query_map(params![from, to], |r| {
                Ok((r.get::<_, i64>(0)?, r.get::<_, i64>(1)?))
            }) {
                out = rows.filter_map(|x| x.ok()).collect();
            }
        }
        out
    }

    pub fn mouse_clicks(&self, from: &str, to: &str) -> i64 {
        self.conn
            .query_row(
                "SELECT COALESCE(SUM(LeftClickCount + RightClickCount + MiddleClickCount), 0) \
                 FROM MouseSessions WHERE Date >= ?1 AND Date < ?2",
                params![from, to],
                |r| r.get::<_, i64>(0),
            )
            .unwrap_or(0)
    }

    pub fn app_sessions_in(&self, from: &str, to: &str) -> Vec<AppSessRow> {
        let mut out = Vec::new();
        if let Ok(mut stmt) = self.conn.prepare(
            "SELECT ProcessName, Category, WindowTitle, StartTime, EndTime, ActiveDuration \
             FROM AppSessions WHERE StartTime >= ?1 AND StartTime < ?2 ORDER BY StartTime",
        ) {
            if let Ok(rows) = stmt.query_map(params![from, to], |r| {
                Ok(AppSessRow {
                    process: r.get::<_, String>(0)?,
                    category: r.get::<_, Option<String>>(1)?,
                    title: r.get::<_, Option<String>>(2)?.unwrap_or_default(),
                    start: r.get::<_, String>(3)?,
                    end: r.get::<_, Option<String>>(4)?,
                    active_ticks: r.get::<_, i64>(5)?,
                })
            }) {
                out = rows.filter_map(|x| x.ok()).collect();
            }
        }
        out
    }

    pub fn web_sessions_in(&self, from: &str, to: &str) -> Vec<WebSessRow> {
        let mut out = Vec::new();
        if let Ok(mut stmt) = self.conn.prepare(
            "SELECT Title, Domain, CategoryName, Browser, Duration, ActiveDuration \
             FROM WebSessions WHERE StartTime >= ?1 AND StartTime < ?2 AND IsLegacy = 0 \
             ORDER BY StartTime",
        ) {
            if let Ok(rows) = stmt.query_map(params![from, to], |r| {
                Ok(WebSessRow {
                    title: r.get::<_, Option<String>>(0)?.unwrap_or_default(),
                    domain: r.get::<_, Option<String>>(1)?.unwrap_or_default(),
                    category: r.get::<_, Option<String>>(2)?,
                    browser: r.get::<_, Option<String>>(3)?.unwrap_or_default(),
                    duration_ticks: r.get::<_, i64>(4)?,
                    active_ticks: r.get::<_, i64>(5)?,
                })
            }) {
                out = rows.filter_map(|x| x.ok()).collect();
            }
        }
        out
    }

    /// 某日硬件分钟样本：`[(cpu_avg, gpu_avg, gpu_temp_max, mem_avg)]`，空列跳过。
    pub fn hardware_day(&self, date: &str) -> Vec<(Option<f64>, Option<f64>, Option<f64>, Option<f64>)> {
        let mut out = Vec::new();
        if let Ok(mut stmt) = self.conn.prepare(
            "SELECT CpuLoadAvg, GpuLoadAvg, GpuTempMax, MemLoadAvg FROM HardwareSamples \
             WHERE Timestamp LIKE ?1",
        ) {
            if let Ok(rows) = stmt.query_map(params![format!("{date}%")], |r| {
                Ok((
                    r.get::<_, Option<f64>>(0)?,
                    r.get::<_, Option<f64>>(1)?,
                    r.get::<_, Option<f64>>(2)?,
                    r.get::<_, Option<f64>>(3)?,
                ))
            }) {
                out = rows.filter_map(|x| x.ok()).collect();
            }
        }
        out
    }

    /// 读某日已有汇总行，用于「是否已经聚合过」的判定。
    pub fn daily_existing(&self, date: &str) -> Option<DailyExisting> {
        self.conn
            .query_row(
                "SELECT Id, TotalKeys, TotalMouseClicks, ActiveMinutes, TopApps, FocusMinutesV2 \
                 FROM DailySummaries WHERE Date = ?1",
                params![date],
                |r| {
                    Ok(DailyExisting {
                        id: r.get::<_, String>(0)?,
                        total_keys: r.get::<_, i64>(1)?,
                        clicks: r.get::<_, i64>(2)?,
                        active_minutes: r.get::<_, f64>(3)?,
                        top_apps: r.get::<_, Option<String>>(4)?.unwrap_or_default(),
                        focus_minutes: r.get::<_, Option<f64>>(5)?,
                    })
                },
            )
            .optional()
            .ok()
            .flatten()
    }

    pub fn daily_upsert(&self, d: &DailyOut) -> Result<(), String> {
        self.conn
            .execute(
                "INSERT INTO DailySummaries \
                 (Id, Date, TotalKeys, TotalMouseClicks, ActiveMinutes, TopApps, HourlyKeyDistribution, \
                  LastUpdated, WebPages, WebDurationTicks, WebActiveDurationTicks, AppSwitches, \
                  FocusMinutesV2, WebMinutes, CpuLoadAvg, CpuLoadP95, GpuLoadAvg, GpuTempMax, MemLoadAvg, \
                  MetricsVersion, LabelMinutesJson, NightMinutes, FirstActiveTime, LastActiveTime) \
                 VALUES (?1,?2,?3,?4,?5,?6,?7,?8,?9,?10,?11,?12,?13,?14,?15,?16,?17,?18,?19,?20,?21,?22,?23,?24) \
                 ON CONFLICT(Date) DO UPDATE SET \
                  TotalKeys=excluded.TotalKeys, TotalMouseClicks=excluded.TotalMouseClicks, \
                  ActiveMinutes=excluded.ActiveMinutes, TopApps=excluded.TopApps, \
                  HourlyKeyDistribution=excluded.HourlyKeyDistribution, LastUpdated=excluded.LastUpdated, \
                  WebPages=excluded.WebPages, WebDurationTicks=excluded.WebDurationTicks, \
                  WebActiveDurationTicks=excluded.WebActiveDurationTicks, AppSwitches=excluded.AppSwitches, \
                  FocusMinutesV2=excluded.FocusMinutesV2, WebMinutes=excluded.WebMinutes, \
                  CpuLoadAvg=excluded.CpuLoadAvg, CpuLoadP95=excluded.CpuLoadP95, \
                  GpuLoadAvg=excluded.GpuLoadAvg, GpuTempMax=excluded.GpuTempMax, \
                  MemLoadAvg=excluded.MemLoadAvg, MetricsVersion=excluded.MetricsVersion, \
                  LabelMinutesJson=excluded.LabelMinutesJson, NightMinutes=excluded.NightMinutes, \
                  FirstActiveTime=excluded.FirstActiveTime, LastActiveTime=excluded.LastActiveTime",
                params![
                    uuid_like(),
                    d.date,
                    d.total_keys,
                    d.clicks,
                    d.active_minutes,
                    d.top_apps_json,
                    d.hourly_keys_json,
                    d.last_updated,
                    d.web_pages,
                    d.web_duration_ticks,
                    d.web_active_ticks,
                    d.app_switches,
                    d.focus_minutes,
                    d.web_minutes,
                    d.cpu_avg,
                    d.cpu_p95,
                    d.gpu_avg,
                    d.gpu_temp_max,
                    d.mem_avg,
                    d.metrics_version,
                    d.label_json,
                    d.night_minutes,
                    d.first_active,
                    d.last_active
                ],
            )
            .map(|_| ())
            .map_err(|e| format!("写 DailySummaries 失败: {e}"))
    }

    pub fn app_daily_upsert(&self, date: &str, a: &AppDailyOut, now: &str) -> Result<(), String> {
        self.conn
            .execute(
                "INSERT INTO AppDailyStats \
                 (Date, ProcessName, Minutes, Sessions, FocusMinutes, HourlyJson, LastUpdated) \
                 VALUES (?1,?2,?3,?4,?5,?6,?7) \
                 ON CONFLICT(Date, ProcessName) DO UPDATE SET \
                  Minutes=excluded.Minutes, Sessions=excluded.Sessions, \
                  FocusMinutes=excluded.FocusMinutes, HourlyJson=excluded.HourlyJson, \
                  LastUpdated=excluded.LastUpdated",
                params![
                    date,
                    a.process,
                    a.minutes,
                    a.sessions,
                    a.focus,
                    a.hourly_json,
                    now
                ],
            )
            .map(|_| ())
            .map_err(|e| format!("写 AppDailyStats 失败: {e}"))
    }

    /// 数据保留清理：逐条照抄 WPF `CleanupOldDataAsync`（DailySummaries 不删，只删原始表）。
    /// 返回删除行数；单条失败不影响其它表。
    pub fn cleanup_before(&self, cutoff_date: &str, sample_cutoff: &str) -> i64 {
        let mut deleted = 0i64;
        for (sql, arg) in [
            ("DELETE FROM KeyboardSessions WHERE Date < ?1", cutoff_date),
            ("DELETE FROM MouseSessions WHERE Date < ?1", cutoff_date),
            ("DELETE FROM WebSessions WHERE StartTime < ?1", cutoff_date),
            ("DELETE FROM AppSessions WHERE StartTime < ?1", cutoff_date),
            ("DELETE FROM AppDailyStats WHERE Date < ?1", cutoff_date),
            ("DELETE FROM HardwareSamples WHERE Timestamp < ?1", sample_cutoff),
        ] {
            if let Ok(n) = self.conn.execute(sql, params![arg]) {
                deleted += n as i64;
            }
        }
        deleted
    }

    /// 把当日搜索计数写进 `DailySummaries.SearchCount`（保留行内其它字段；没有行就建一条）。
    pub fn search_count_set(&self, date: &str, count: i64, now: &str) -> Result<(), String> {
        if count <= 0 {
            return Ok(());
        }
        self.conn
            .execute(
                "INSERT INTO DailySummaries (Id, Date, SearchCount, LastUpdated) \
                 VALUES (?1, ?2, ?3, ?4) \
                 ON CONFLICT(Date) DO UPDATE SET SearchCount=excluded.SearchCount, LastUpdated=excluded.LastUpdated",
                params![uuid_like(), date, count, now],
            )
            .map(|_| ())
            .map_err(|e| format!("写 SearchCount 失败: {e}"))
    }

    /// AppDailyStats 是否有任何行（首次迁移回填的判据）。
    pub fn app_daily_rows(&self) -> i64 {
        self.conn
            .query_row("SELECT COUNT(*) FROM AppDailyStats", [], |r| r.get::<_, i64>(0))
            .unwrap_or(0)
    }

    /// 有应用会话的日期（按日期升序，供回填用）。
    pub fn app_session_dates(&self, limit: i64) -> Vec<String> {
        let mut stmt = match self
            .conn
            .prepare("SELECT DISTINCT substr(StartTime, 1, 10) AS d FROM AppSessions ORDER BY d DESC LIMIT ?1")
        {
            Ok(s) => s,
            Err(_) => return Vec::new(),
        };
        stmt.query_map(params![limit], |r| r.get::<_, String>(0))
            .map(|rows| rows.filter_map(|r| r.ok()).collect())
            .unwrap_or_default()
    }

    /// 最近 N 天里 `DailySummaries` 缺硬件列的日期（硬件日指标回填用）。
    pub fn daily_missing_hardware(&self, limit: i64) -> Vec<String> {
        let mut stmt = match self.conn.prepare(
            "SELECT Date FROM DailySummaries \
             WHERE (CpuLoadAvg IS NULL OR CpuLoadAvg = 0) \
             ORDER BY Date DESC LIMIT ?1",
        ) {
            Ok(s) => s,
            Err(_) => return Vec::new(),
        };
        stmt.query_map(params![limit], |r| r.get::<_, String>(0))
            .map(|rows| rows.filter_map(|r| r.ok()).collect())
            .unwrap_or_default()
    }

    /// 只更新某天的硬件日指标（回填用；不动其它列，也不动 SearchCount）。
    pub fn daily_hardware_set(
        &self,
        date: &str,
        cpu_avg: f64,
        cpu_p95: f64,
        gpu_avg: f64,
        gpu_temp_max: f64,
        mem_avg: f64,
    ) -> Result<(), String> {
        self.conn
            .execute(
                "UPDATE DailySummaries SET CpuLoadAvg=?2, CpuLoadP95=?3, GpuLoadAvg=?4, \
                 GpuTempMax=?5, MemLoadAvg=?6, MetricsVersion=3 \
                 WHERE Date=?1",
                params![date, cpu_avg, cpu_p95, gpu_avg, gpu_temp_max, mem_avg],
            )
            .map(|_| ())
            .map_err(|e| format!("回填硬件日指标失败: {e}"))
    }
}

#[derive(Debug, Clone)]
pub struct AppSessRow {
    pub process: String,
    pub category: Option<String>,
    pub title: String,
    pub start: String,
    pub end: Option<String>,
    pub active_ticks: i64,
}

#[derive(Debug, Clone)]
pub struct WebSessRow {
    pub title: String,
    pub domain: String,
    pub category: Option<String>,
    pub browser: String,
    pub duration_ticks: i64,
    pub active_ticks: i64,
}

#[derive(Debug, Clone)]
pub struct DailyExisting {
    pub id: String,
    pub total_keys: i64,
    pub clicks: i64,
    pub active_minutes: f64,
    pub top_apps: String,
    pub focus_minutes: Option<f64>,
}

#[derive(Debug, Clone)]
pub struct AppDailyOut {
    pub process: String,
    pub minutes: f64,
    pub sessions: i64,
    pub focus: f64,
    pub hourly_json: String,
}

#[derive(Debug, Clone)]
pub struct DailyOut {
    pub date: String,
    pub total_keys: i64,
    pub clicks: i64,
    pub active_minutes: f64,
    pub top_apps_json: String,
    pub hourly_keys_json: String,
    pub last_updated: String,
    pub web_pages: i64,
    pub web_duration_ticks: i64,
    pub web_active_ticks: i64,
    pub app_switches: i64,
    pub focus_minutes: f64,
    pub web_minutes: f64,
    pub cpu_avg: Option<f64>,
    pub cpu_p95: Option<f64>,
    pub gpu_avg: Option<f64>,
    pub gpu_temp_max: Option<f64>,
    pub mem_avg: Option<f64>,
    pub metrics_version: i64,
    pub label_json: String,
    pub night_minutes: f64,
    pub first_active: Option<String>,
    pub last_active: Option<String>,
}

fn opt(v: f64, n: i64) -> Option<f64> {
    if n > 0 {
        Some(v)
    } else {
        None
    }
}

impl AppRow {
    fn cls(&self) -> Option<String> {
        if self.class.is_empty() {
            None
        } else {
            Some(self.class.clone())
        }
    }
}

/// burst 起始时间：把「当日 0 点 + 偏移毫秒」写成 C# 的带时区 ISO。
fn burst_time(date_key: &str, off_ms: u64) -> String {
    let total_s = off_ms / 1000;
    let h = total_s / 3600;
    let m = (total_s / 60) % 60;
    let s = total_s % 60;
    let frac7 = (off_ms % 1000) * 10000;
    // 与 C# `DateTime.ToString("o")` 的本地时区形态一致（+08:00）；不依赖时区库
    format!("{date_key}T{h:02}:{m:02}:{s:02}.{frac7:07}+08:00")
}

/// 32 位十六进制 ID（SQLite 里 Id 是 TEXT，EF 用 Guid 字符串；这里不要求符合 Guid 格式）
fn uuid_like() -> String {
    use std::time::{SystemTime, UNIX_EPOCH};
    let nanos = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_nanos() as u64)
        .unwrap_or(0);
    let pid = std::process::id() as u64;
    let seq = COUNTER.fetch_add(1, std::sync::atomic::Ordering::Relaxed) as u64;
    let a = (nanos ^ (pid << 32)) & 0xffff_ffff;
    let b = (nanos >> 32) ^ seq;
    format!(
        "{:08x}-{:04x}-{:04x}-{:04x}-{:012x}",
        a as u32,
        (b & 0xffff) as u16,
        ((b >> 16) & 0xffff) as u16,
        ((seq & 0xffff) as u16),
        (nanos & 0xffff_ffff_ffff) as u64
    )
}

static COUNTER: std::sync::atomic::AtomicUsize = std::sync::atomic::AtomicUsize::new(0);

/// 建表：整套 `schema.sql`（从生产库 `sqlite_master` 导出的 29 张表 + 索引）。
/// 逐句执行并忽略「已存在」错误，所以老库再跑一遍也安全。
fn ensure_schema(conn: &Connection) {
    const DDL: &str = include_str!("schema.sql");
    for stmt in DDL.split(";\n") {
        let s = stmt.trim();
        if s.is_empty() {
            continue;
        }
        let _ = conn.execute_batch(&format!("{s};"));
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::path::PathBuf;

    fn tmp_store(name: &str) -> (Store, PathBuf) {
        let dir = std::env::temp_dir().join(format!("pchabit_store_{name}_{}", uuid_like()));
        std::fs::create_dir_all(&dir).unwrap();
        let path = dir.join("t.db");
        let store = Store::open(&path).unwrap();
        // 库结构平时由 WPF 的 EF 迁移建；现在 `Store::open` 会跑完整 schema，
        // 测试要的是精简版，所以先删掉同名表再建（临时库，互不影响）
        store
            .conn
            .execute_batch(
                "DROP TABLE IF EXISTS KeyboardSessions;\
                 DROP TABLE IF EXISTS MouseSessions;\
                 DROP TABLE IF EXISTS WebSessions;\
                 DROP TABLE IF EXISTS AppSessions;\
                 DROP TABLE IF EXISTS AppDailyStats;\
                 DROP TABLE IF EXISTS HardwareSamples;\
                 DROP TABLE IF EXISTS DailySummaries;\
                 DROP TABLE IF EXISTS SearchQueries;",
            )
            .unwrap();
        store
            .conn
            .execute_batch(
                "CREATE TABLE IF NOT EXISTS KeyboardSessions (Id TEXT PRIMARY KEY, Date TEXT, Hour INTEGER, TotalKeyPresses INTEGER);\
                 CREATE TABLE IF NOT EXISTS MouseSessions (Id TEXT PRIMARY KEY, Date TEXT, Hour INTEGER);\
                 CREATE TABLE IF NOT EXISTS WebSessions (Id TEXT PRIMARY KEY, StartTime TEXT);\
                 CREATE TABLE IF NOT EXISTS AppSessions (Id TEXT PRIMARY KEY, StartTime TEXT);\
                 CREATE TABLE IF NOT EXISTS AppDailyStats (Date TEXT, ProcessName TEXT, Minutes REAL, Sessions INTEGER, \
                     FocusMinutes REAL, HourlyJson TEXT, LastUpdated TEXT, PRIMARY KEY (Date, ProcessName));\
                 CREATE TABLE IF NOT EXISTS HardwareSamples (Id INTEGER PRIMARY KEY AUTOINCREMENT, Timestamp TEXT, CpuLoad REAL, SampleCount INTEGER);\
                 CREATE TABLE IF NOT EXISTS DailySummaries (Id TEXT PRIMARY KEY, Date TEXT UNIQUE, TotalKeys INTEGER, SearchCount INTEGER, LastUpdated TEXT);\
                 CREATE UNIQUE INDEX IF NOT EXISTS IX_DailySummaries_Date ON DailySummaries(Date);",
            )
            .unwrap();
        (store, dir)
    }

    #[test]
    fn cleanup_deletes_old_raw_rows_and_keeps_summaries() {
        let (store, dir) = tmp_store("cleanup");
        // 造两条键盘会话（新/旧）、一条旧硬件样本、一条旧 AppDailyStats、一条旧日汇总
        store
            .conn
            .execute(
                "INSERT INTO KeyboardSessions (Id, Date, Hour, TotalKeyPresses) VALUES \
                 ('k-old','2020-01-01 00:00:00', 9, 100), ('k-new','2026-11-18 00:00:00', 9, 50)",
                params![],
            )
            .unwrap();
        store
            .conn
            .execute(
                "INSERT INTO HardwareSamples (Timestamp, CpuLoad, SampleCount) VALUES \
                 ('2020-01-01 09:00', 10.0, 60), ('2026-11-18 09:00', 10.0, 60)",
                params![],
            )
            .unwrap();
        store
            .conn
            .execute(
                "INSERT INTO AppDailyStats (Date, ProcessName, Minutes, Sessions, FocusMinutes, HourlyJson, LastUpdated) \
                 VALUES ('2020-01-01','a.exe',1.0,1,0.0,'[]','2020-01-01 00:00:00')",
                params![],
            )
            .unwrap();
        store
            .conn
            .execute(
                "INSERT INTO DailySummaries (Id, Date, TotalKeys) VALUES ('d-old','2020-01-01', 10)",
                params![],
            )
            .unwrap();

        let n = store.cleanup_before("2025-01-01", "2025-01-01 00:00");
        assert_eq!(n, 3, "键盘/硬件/AppDailyStats 各一条应被删");

        let left_keys: i64 = store
            .conn
            .query_row(
                "SELECT COUNT(*) FROM KeyboardSessions WHERE Id='k-old'",
                params![],
                |r| r.get(0),
            )
            .unwrap();
        assert_eq!(left_keys, 0);
        assert_eq!(store.app_daily_rows(), 0);
        let summaries: i64 = store
            .conn
            .query_row(
                "SELECT COUNT(*) FROM DailySummaries WHERE Id='d-old'",
                params![],
                |r| r.get(0),
            )
            .unwrap();
        assert_eq!(summaries, 1, "日汇总不删");
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn search_count_upsert_keeps_other_columns() {
        let (store, dir) = tmp_store("searchcount");
        store
            .conn
            .execute(
                "INSERT INTO DailySummaries (Id, Date, TotalKeys, SearchCount) VALUES ('x1','2026-11-19', 77, 0)",
                params![],
            )
            .unwrap();
        store.search_count_set("2026-11-19", 5, "2026-11-19 23:59:59").unwrap();
        let (keys, search): (i64, i64) = store
            .conn
            .query_row(
                "SELECT TotalKeys, SearchCount FROM DailySummaries WHERE Date='2026-11-19'",
                params![],
                |r| Ok((r.get(0)?, r.get(1)?)),
            )
            .unwrap();
        assert_eq!(keys, 77, "其它列不能被覆盖");
        assert_eq!(search, 5);

        // count<=0 不建行、不改行
        store.search_count_set("2026-11-20", 0, "2026-11-20 23:59:59").unwrap();
        let rows: i64 = store
            .conn
            .query_row("SELECT COUNT(*) FROM DailySummaries", params![], |r| r.get(0))
            .unwrap();
        assert_eq!(rows, 1);
        let _ = std::fs::remove_dir_all(&dir);
    }

    /// 4.12.22：同一小时里不同应用必须各占一行，且同一应用多次落库要累加。
    #[test]
    fn kb_mouse_rows_split_by_process() {
        let dir = std::env::temp_dir().join(format!("pchabit_store_kbsplit_{}", uuid_like()));
        std::fs::create_dir_all(&dir).unwrap();
        // 这里直接用 `Store::open` 建好的完整表结构（已有 (Date,Hour,ProcessName) 唯一索引）
        let store = Store::open(&dir.join("t.db")).unwrap();

        for (proc, n) in [("A", 3), ("B", 4), ("A", 5)] {
            let mut d: KbDelta = Default::default();
            d.date = "2026-11-20".into();
            d.hour = 10;
            d.process = proc.into();
            d.total = n;
            store.kb_add(&d).unwrap();
        }
        let (rows, ta, tb): (i64, i64, i64) = store
            .conn
            .query_row(
                "SELECT COUNT(*), \
                    (SELECT SUM(TotalKeyPresses) FROM KeyboardSessions WHERE ProcessName='A'), \
                    (SELECT SUM(TotalKeyPresses) FROM KeyboardSessions WHERE ProcessName='B') \
                 FROM KeyboardSessions",
                params![],
                |r| Ok((r.get(0)?, r.get(1)?, r.get(2)?)),
            )
            .unwrap();
        assert_eq!(rows, 2, "同一小时不同应用应各占一行");
        assert_eq!(ta, 8, "同应用的多次落库应累加");
        assert_eq!(tb, 4);

        for (proc, l) in [("A", 2), ("B", 1)] {
            let mut d: MsDelta = Default::default();
            d.date = "2026-11-20".into();
            d.hour = 10;
            d.process = proc.into();
            d.left = l;
            d.middle = 0;
            d.right = 0;
            store.mouse_add(&d).unwrap();
        }
        let ms_rows: i64 = store
            .conn
            .query_row("SELECT COUNT(*) FROM MouseSessions", params![], |r| r.get(0))
            .unwrap();
        assert_eq!(ms_rows, 2);
        let _ = std::fs::remove_dir_all(&dir);
    }
}

