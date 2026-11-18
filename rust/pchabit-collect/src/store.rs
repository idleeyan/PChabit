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

/// 键鼠增量（相对上次落库的差值）
#[derive(Default, Clone)]
pub struct KbDelta {
    pub total: i64,
    pub repeat: i64,
    pub backspace: i64,
    pub delete: i64,
    pub undo: i64,
    pub freq: BTreeMap<i32, i64>,
    pub cats: BTreeMap<String, i64>,
    pub avg_speed: f64,
    pub peak_speed: f64,
    pub bursts: Vec<(u64, u64, i64)>, // (起始毫秒偏移, 时长 ms, 键数)
}

#[derive(Default, Clone)]
pub struct MsDelta {
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

impl Store {
    pub fn open(path: &Path) -> Result<Store, String> {
        let conn = Connection::open(path).map_err(|e| format!("打开数据库失败: {e}"))?;
        conn.busy_timeout(Duration::from_millis(5000))
            .map_err(|e| format!("设置 busy_timeout 失败: {e}"))?;
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
            VALUES (?1,?2,?3,?4,?5,?6,?7,?8,?9,?10,?11,?12,?13,NULL,NULL,NULL,?14,?15,?16,?17) \
            ON CONFLICT(Timestamp) DO UPDATE SET \
             CpuLoadAvg=excluded.CpuLoadAvg, CpuLoadMax=excluded.CpuLoadMax, GpuLoadAvg=excluded.GpuLoadAvg, \
             GpuLoadMax=excluded.GpuLoadMax, GpuTempMax=excluded.GpuTempMax, VramUsedMax=excluded.VramUsedMax, \
             VramTotal=excluded.VramTotal, MemLoadAvg=excluded.MemLoadAvg, MemLoadMax=excluded.MemLoadMax, \
             MemUsedMax=excluded.MemUsedMax, NetUpAvg=excluded.NetUpAvg, NetDownAvg=excluded.NetDownAvg, \
             SampleCount=excluded.SampleCount, SensorFlags=excluded.SensorFlags";
        let cpu_avg = HwBucket::avg(b.cpu_sum, b.cpu_n);
        let gpu_avg = HwBucket::avg(b.gpu_sum, b.gpu_n);
        let mem_avg = HwBucket::avg(b.mem_sum, b.mem_n);
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
                    r.title,
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

    pub fn kb_add(
        &self,
        date_key: &str,
        hour: u32,
        process: &str,
        d: &KbDelta,
    ) -> Result<(), String> {
        if d.total == 0 && d.repeat == 0 && d.freq.is_empty() {
            return Ok(());
        }
        let (mut freq_new, mut cats_new, mut bursts_new) = self.kb_existing(date_key, hour)?;
        for (k, v) in &d.freq {
            *freq_new.entry(*k).or_insert(0) += v;
        }
        for (k, v) in &d.cats {
            *cats_new.entry(k.clone()).or_insert(0) += v;
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

        self.conn
            .execute(
                "INSERT INTO KeyboardSessions \
                 (Id, Date, Hour, ProcessName, TotalKeyPresses, KeyFrequency, KeyCategoryFrequency, \
                  AverageTypingSpeed, PeakTypingSpeed, UndoCount, DeleteCount, BackspaceCount, \
                  TypingBursts, Shortcuts, RepeatKeyPresses) \
                 VALUES (?1,?2,?3,?4,?5,?6,?7,?8,?9,?10,?11,?12,?13,?14,?15) \
                 ON CONFLICT(Date, Hour) DO UPDATE SET \
                  TotalKeyPresses=TotalKeyPresses+excluded.TotalKeyPresses, \
                  RepeatKeyPresses=RepeatKeyPresses+excluded.RepeatKeyPresses, \
                  BackspaceCount=BackspaceCount+excluded.BackspaceCount, \
                  DeleteCount=DeleteCount+excluded.DeleteCount, \
                  UndoCount=UndoCount+excluded.UndoCount, \
                  KeyFrequency=excluded.KeyFrequency, KeyCategoryFrequency=excluded.KeyCategoryFrequency, \
                  TypingBursts=excluded.TypingBursts, \
                  AverageTypingSpeed=MAX(AverageTypingSpeed, excluded.AverageTypingSpeed), \
                  PeakTypingSpeed=MAX(PeakTypingSpeed, excluded.PeakTypingSpeed), \
                  ProcessName=CASE WHEN excluded.ProcessName IS NULL OR excluded.ProcessName='' \
                    THEN ProcessName ELSE excluded.ProcessName END",
                params![
                    uuid_like(),
                    format!("{date_key} 00:00:00"),
                    hour as i64,
                    if process.is_empty() { None } else { Some(process) },
                    d.total,
                    freq_json,
                    cats_json,
                    d.avg_speed,
                    d.peak_speed,
                    d.undo,
                    d.delete,
                    d.backspace,
                    Value::Array(bursts_new).to_string(),
                    "[]",
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
    ) -> Result<(BTreeMap<i32, i64>, BTreeMap<String, i64>, Vec<Value>), String> {
        let existing: Option<(String, String, String)> = self
            .conn
            .query_row(
                "SELECT KeyFrequency, KeyCategoryFrequency, TypingBursts FROM KeyboardSessions \
                 WHERE Date = ?1 AND Hour = ?2",
                params![format!("{date_key} 00:00:00"), hour as i64],
                |r| Ok((r.get(0)?, r.get(1)?, r.get(2)?)),
            )
            .optional()
            .map_err(|e| format!("读 KeyboardSessions 失败: {e}"))?;

        match existing {
            None => Ok((BTreeMap::new(), BTreeMap::new(), vec![])),
            Some((f, c, b)) => {
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
                Ok((freq, cats, bursts))
            }
        }
    }

    pub fn mouse_add(
        &self,
        date_key: &str,
        hour: u32,
        process: &str,
        d: &MsDelta,
    ) -> Result<(), String> {
        if d.left == 0 && d.right == 0 && d.middle == 0 && d.scroll == 0 && d.dist < 0.5 {
            return Ok(());
        }
        self.conn
            .execute(
                "INSERT INTO MouseSessions \
                 (Id, Date, Hour, ProcessName, LeftClickCount, RightClickCount, MiddleClickCount, \
                  TotalMoveDistance, MoveEventCount, ScrollCount, ScrollLines, AverageClickInterval) \
                 VALUES (?1,?2,?3,?4,?5,?6,?7,?8,?9,?10,0,0.0) \
                 ON CONFLICT(Date, Hour) DO UPDATE SET \
                  LeftClickCount=LeftClickCount+excluded.LeftClickCount, \
                  RightClickCount=RightClickCount+excluded.RightClickCount, \
                  MiddleClickCount=MiddleClickCount+excluded.MiddleClickCount, \
                  ScrollCount=ScrollCount+excluded.ScrollCount, \
                  TotalMoveDistance=TotalMoveDistance+excluded.TotalMoveDistance, \
                  MoveEventCount=MoveEventCount+excluded.MoveEventCount, \
                  ProcessName=CASE WHEN excluded.ProcessName IS NULL OR excluded.ProcessName='' \
                    THEN ProcessName ELSE excluded.ProcessName END",
                params![
                    uuid_like(),
                    format!("{date_key} 00:00:00"),
                    hour as i64,
                    if process.is_empty() { None } else { Some(process) },
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

