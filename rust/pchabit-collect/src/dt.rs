//! 本地时间换算：扩展推的 `timestamp` 是 UTC ISO（`2026-11-19T00:33:42.594Z`），
//! 库里所有时间列都是本地时间字符串，所以要能做 UTC → 本地毫秒 → 字符串的双向换算。
//!
//! 时区偏移只取一次（本机固定 UTC+8、无夏令时；跨时区运行也只是开机时算错一次，
//! 不会在会话中途跳变）。

use std::sync::OnceLock;
use std::time::{SystemTime, UNIX_EPOCH};

#[repr(C)]
#[allow(non_snake_case)]
struct SysTime {
    wYear: u16,
    wMonth: u16,
    wDayOfWeek: u16,
    wDay: u16,
    wHour: u16,
    wMinute: u16,
    wSecond: u16,
    wMilliseconds: u16,
}

#[link(name = "kernel32")]
extern "system" {
    fn GetLocalTime(t: *mut SysTime);
}

fn utc_ms() -> i64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_millis() as i64)
        .unwrap_or(0)
}

/// 本地时区偏移（毫秒）。用「本地时间字段按 UTC 解释」与真实 UTC 之差算出来。
fn offset_ms() -> i64 {
    static OFF: OnceLock<i64> = OnceLock::new();
    *OFF.get_or_init(|| {
        let mut st = SysTime {
            wYear: 0,
            wMonth: 0,
            wDayOfWeek: 0,
            wDay: 0,
            wHour: 0,
            wMinute: 0,
            wSecond: 0,
            wMilliseconds: 0,
        };
        unsafe { GetLocalTime(&mut st) };
        let local_as_utc = days_from_civil(st.wYear as i64, st.wMonth as i64, st.wDay as i64)
            * 86_400_000
            + (st.wHour as i64) * 3_600_000
            + (st.wMinute as i64) * 60_000
            + (st.wSecond as i64) * 1000
            + st.wMilliseconds as i64;
        let now = utc_ms();
        // 对齐到分钟，避免秒级抖动被算进偏移（四舍五入到最近分钟，
        // 截断会在 diff 略小于整分钟时整整差一分钟）
        let diff = local_as_utc - now;
        ((diff as f64 / 60_000.0).round() as i64) * 60_000
    })
}

/// 当前本地时间（epoch 毫秒）
pub fn local_ms_now() -> i64 {
    utc_ms() + offset_ms()
}

/// UTC epoch 毫秒 → 本地 epoch 毫秒
pub fn to_local_ms(utc: i64) -> i64 {
    utc + offset_ms()
}

/// 解析扩展的 ISO 时间（`2026-11-19T00:33:42.594Z`，也接受无 `Z` 的本地形态），
/// 返回本地 epoch 毫秒。
pub fn iso_to_local_ms(s: &str) -> Option<i64> {
    let b = s.as_bytes();
    if b.len() < 19 {
        return None;
    }
    let num = |from: usize, to: usize| -> Option<i64> { s.get(from..to)?.parse::<i64>().ok() };
    let year = num(0, 4)?;
    let month = num(5, 7)?;
    let day = num(8, 10)?;
    let hour = num(11, 13)?;
    let minute = num(14, 16)?;
    let second = num(17, 19)?;
    let ms = if b.len() > 20 {
        let frac = &s[20..];
        let digits: String = frac.chars().take_while(|c| c.is_ascii_digit()).collect();
        let mut v: i64 = digits.parse().ok()?;
        while digits.len() < 3 {
            // 极端情况：".5" 视为 500ms
            v *= 10;
        }
        if digits.len() >= 3 {
            v /= 10i64.pow((digits.len() - 3) as u32);
        }
        v
    } else {
        0
    };

    let epoch = days_from_civil(year, month, day) * 86_400_000
        + hour * 3_600_000
        + minute * 60_000
        + second * 1000
        + ms;

    // 带 Z / +00:00 视为 UTC；否则按本地时间直接使用
    let is_utc = s.ends_with('Z') || s.contains("+00:00");
    Some(if is_utc { to_local_ms(epoch) } else { epoch })
}

/// 解析库里的本地时间串（`2026-11-18 13:27:23`、`2026-11-18 13:27:23.594`、
/// `2026-11-18 13:27:23.5941234` 都认），返回本地 epoch 毫秒。
pub fn parse_stamp(s: &str) -> Option<i64> {
    let b = s.as_bytes();
    if b.len() < 19 || b[10] != b' ' {
        return None;
    }
    let num = |from: usize, to: usize| -> Option<i64> { s.get(from..to)?.parse::<i64>().ok() };
    let year = num(0, 4)?;
    let month = num(5, 7)?;
    let day = num(8, 10)?;
    let hour = num(11, 13)?;
    let minute = num(14, 16)?;
    let second = num(17, 19)?;
    let ms = if b.len() > 20 && b[19] == b'.' {
        let digits: String = s[20..].chars().take_while(|c| c.is_ascii_digit()).collect();
        if digits.is_empty() {
            0
        } else {
            let mut v: i64 = digits.parse().ok()?;
            if digits.len() >= 3 {
                v /= 10i64.pow((digits.len() - 3) as u32);
            } else {
                for _ in digits.len()..3 {
                    v *= 10;
                }
            }
            v
        }
    } else {
        0
    };
    Some(
        days_from_civil(year, month, day) * 86_400_000
            + hour * 3_600_000
            + minute * 60_000
            + second * 1000
            + ms,
    )
}

/// 本地 epoch 毫秒 → `yyyy-MM-dd HH:mm:ss.fffffff`
pub fn stamp7_ms(local_ms: i64) -> String {    let (y, mo, d, h, mi, s, ms) = breakdown(local_ms);
    format!(
        "{y:04}-{mo:02}-{d:02} {h:02}:{mi:02}:{s:02}.{f:07}",
        f = ms * 10_000
    )
}

/// 本地 epoch 毫秒 → `yyyy-MM-dd`
pub fn date_ms(local_ms: i64) -> String {
    let (y, mo, d, _, _, _, _) = breakdown(local_ms);
    format!("{y:04}-{mo:02}-{d:02}")
}

fn breakdown(local_ms: i64) -> (i64, i64, i64, i64, i64, i64, i64) {
    let mut ms = local_ms;
    let mut days = ms / 86_400_000;
    ms %= 86_400_000;
    if ms < 0 {
        ms += 86_400_000;
        days -= 1;
    }
    let (y, m, d) = civil_from_days(days);
    let h = ms / 3_600_000;
    let mi = (ms / 60_000) % 60;
    let s = (ms / 1000) % 60;
    let milli = ms % 1000;
    (y, m, d, h, mi, s, milli)
}

/// Howard Hinnant 的 days_from_civil：1970-01-01 起的天数
fn days_from_civil(y: i64, m: i64, d: i64) -> i64 {
    let y = if m <= 2 { y - 1 } else { y };
    let era = if y >= 0 { y } else { y - 399 } / 400;
    let yoe = y - era * 400;
    let mp = (m + 9) % 12;
    let doy = (153 * mp + 2) / 5 + d - 1;
    let doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
    era * 146_097 + doe - 719_468
}

fn civil_from_days(z: i64) -> (i64, i64, i64) {
    let z = z + 719_468;
    let era = if z >= 0 { z } else { z - 146_096 } / 146_097;
    let doe = z - era * 146_097;
    let yoe = (doe - doe / 1460 + doe / 36_524 - doe / 146_096) / 365;
    let y = yoe + era * 400;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let d = doy - (153 * mp + 2) / 5 + 1;
    let m = if mp < 10 { mp + 3 } else { mp - 9 };
    (if m <= 2 { y + 1 } else { y }, m, d)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn roundtrip_civil() {
        for days in [-1000i64, 0, 19_000, 20_800] {
            let (y, m, d) = civil_from_days(days);
            assert_eq!(days_from_civil(y, m, d), days, "days={days}");
        }
    }

    #[test]
    fn iso_parses() {
        let ms = iso_to_local_ms("2026-11-19T00:33:42.594Z").unwrap();
        let s = stamp7_ms(ms);
        assert!(s.starts_with("2026-11-19 08:33:42"), "got {s}");
    }
}
