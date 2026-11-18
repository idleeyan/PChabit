//! 本地时间格式化：口径与 C# `DateTime.Now` 一致（yyyy-MM-dd hh:mm:ss.fffffff）。
//!
//! 为什么不用 chrono：整条采集链只需要「本年本地时间」的格式化，
//! 系统调用 GetLocalTime 一次就够，省掉一个依赖与时区库的体积。

use crate::win::{GetLocalTime, SystemTime};

pub fn now() -> SystemTime {
    let mut t = SystemTime::default();
    unsafe { GetLocalTime(&mut t) };
    t
}

/// `2026-11-18`
pub fn date(t: &SystemTime) -> String {
    format!("{:04}-{:02}-{:02}", t.wYear, t.wMonth, t.wDay)
}

/// `2026-11-18 23:50`
pub fn minute_key(t: &SystemTime) -> String {
    format!(
        "{:04}-{:02}-{:02} {:02}:{:02}",
        t.wYear, t.wMonth, t.wDay, t.wHour, t.wMinute
    )
}

/// `2026-11-18 23:50:08.4403103`（7 位小数，毫秒后补 4 个 0）
pub fn stamp7(t: &SystemTime) -> String {
    format!(
        "{:04}-{:02}-{:02} {:02}:{:02}:{:02}.{:07}",
        t.wYear,
        t.wMonth,
        t.wDay,
        t.wHour,
        t.wMinute,
        t.wSecond,
        (t.wMilliseconds as u64) * 10000
    )
}

/// `HH:MM:SS.fffffff`（用于 TypingBursts 的 Duration 字段）
pub fn duration_text(ms: u64) -> String {
    let h = ms / 3_600_000;
    let m = (ms / 60_000) % 60;
    let s = (ms / 1000) % 60;
    let frac7 = (ms % 1000) * 10000;
    format!("{h:02}:{m:02}:{s:02}.{frac7:07}")
}

/// 分钟以内的「现在」对应的小时
pub fn hour(t: &SystemTime) -> u32 {
    t.wHour as u32
}

/// 以 0 点起始的当日分钟索引
pub fn minute_of_day(t: &SystemTime) -> u32 {
    t.wHour as u32 * 60 + t.wMinute as u32
}
