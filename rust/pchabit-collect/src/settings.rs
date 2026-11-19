//! 采集侧的设置读取：`%LOCALAPPDATA%\PChabit\settings.json`。
//!
//! 采集进程（`pchabit-collect`）每秒都在跑，设置文件又可能被壳随时改写，
//! 所以这里按「文件修改时间」缓存：mtime 没变就直接用上次解析的结果，
//! 变了才重读。路径可用 `PCHABIT_SETTINGS` 覆盖（自测用，不去碰生产设置）。

use std::path::PathBuf;
use std::sync::{Mutex, OnceLock};
use std::time::SystemTime;

struct Cache {
    stamp: Option<SystemTime>,
    value: serde_json::Value,
}

static CACHE: OnceLock<Mutex<Cache>> = OnceLock::new();

fn path() -> Option<PathBuf> {
    if let Ok(p) = std::env::var("PCHABIT_SETTINGS") {
        if !p.trim().is_empty() {
            return Some(PathBuf::from(p));
        }
    }
    let base = std::env::var("LOCALAPPDATA").ok().map(PathBuf::from)?;
    Some(base.join("PChabit").join("settings.json"))
}

fn snapshot() -> serde_json::Value {
    let cell = CACHE.get_or_init(|| {
        Mutex::new(Cache {
            stamp: None,
            value: serde_json::Value::Null,
        })
    });
    let stamp = path().and_then(|p| std::fs::metadata(&p).ok().and_then(|m| m.modified().ok()));
    {
        let cached = cell.lock().unwrap();
        if cached.stamp == stamp && cached.stamp.is_some() {
            return cached.value.clone();
        }
    }
    let value = path()
        .and_then(|p| std::fs::read_to_string(&p).ok())
        .and_then(|t| serde_json::from_str::<serde_json::Value>(&t).ok())
        .unwrap_or(serde_json::Value::Null);
    let mut cached = cell.lock().unwrap();
    cached.stamp = stamp;
    cached.value = value.clone();
    value
}

/// 读一个布尔键，缺省用 `default`。
pub fn bool_of(key: &str, default: bool) -> bool {
    snapshot().get(key).and_then(|v| v.as_bool()).unwrap_or(default)
}

/// 读一个整数键，缺省用 `default`。
pub fn i64_of(key: &str, default: i64) -> i64 {
    snapshot().get(key).and_then(|v| v.as_i64()).unwrap_or(default)
}

/// 读一个字符串键，缺省用 `default`。
pub fn str_of(key: &str, default: &str) -> String {
    snapshot()
        .get(key)
        .and_then(|v| v.as_str())
        .unwrap_or(default)
        .to_string()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn missing_file_falls_back_to_defaults() {
        std::env::set_var("PCHABIT_SETTINGS", r"C:\definitely\not\here.json");
        assert!(bool_of("TrackKeyboard", true));
        assert!(!bool_of("Nope", false));
        assert_eq!(i64_of("RetentionDays", 90), 90);
    }
}
