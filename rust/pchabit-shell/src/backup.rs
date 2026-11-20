//! 备份与云同步（`AutoBackup*` / `BackupPath` / `WebDAV*` / `MaxCloudBackupCount`）。
//!
//! 壳里起一个低频线程（每 5 分钟醒一次）：
//!  1. 到了间隔（`AutoBackupIntervalHours`）就做一次本地备份（`VACUUM INTO`，
//!     由 `pchabit_datad::backup_now()` 完成），再按 `BackupPath` 复制一份；
//!  2. 本地 `Backups` 目录只保留最新的 `MaxBackupCount` 份；
//!  3. `WebDAVEnabled` 时把刚做的那份传到 `WebDAVUrl`，远端只留 `MaxCloudBackupCount` 份，
//!     成功后把 `WebDAVLastSync` 写回设置。

use crate::webdav::Dav;
use std::path::{Path, PathBuf};
use std::time::{Duration, SystemTime};

/// 读设置里的字符串（缺省用 `default`）。
fn s(key: &str, default: &str) -> String {
    crate::setting_str(key, default)
}

/// 读设置里的整数（缺省用 `default`）。
fn i(key: &str, default: i64) -> i64 {
    crate::setting_i64(key, default)
}

/// 读设置里的布尔（缺省用 `default`）。
fn b(key: &str, default: bool) -> bool {
    crate::setting_bool_default(key, default)
}

/// 备份目录：`BackupPath` 为空时用库旁边的 `Backups`。
fn backup_dir() -> PathBuf {
    let configured = s("BackupPath", "");
    if configured.trim().is_empty() {
        pchabit_datad::default_db()
            .parent()
            .map(|p| p.join("Backups"))
            .unwrap_or_else(|| PathBuf::from("."))
    } else {
        PathBuf::from(configured)
    }
}

/// 目录里最新的 N 个 `pchabit_backup_*.db`。
fn local_backups(dir: &Path) -> Vec<PathBuf> {
    let Ok(rd) = std::fs::read_dir(dir) else {
        return Vec::new();
    };
    let mut files: Vec<(SystemTime, PathBuf)> = rd
        .filter_map(|e| e.ok())
        .filter(|e| {
            let name = e.file_name().to_string_lossy().to_string();
            name.starts_with("pchabit_backup_") && name.ends_with(".db")
        })
        .map(|e| {
            let t = e
                .metadata()
                .and_then(|m| m.modified())
                .unwrap_or(SystemTime::UNIX_EPOCH);
            (t, e.path())
        })
        .collect();
    files.sort_by(|a, b| b.0.cmp(&a.0));
    files.into_iter().map(|(_, p)| p).collect()
}

/// 本地只留最新的 `keep` 份（`keep <= 0` 不删）。
pub fn prune_local(keep: i64) -> (usize, usize) {
    if keep <= 0 {
        return (0, 0);
    }
    let dir = backup_dir();
    let files = local_backups(&dir);
    let mut removed = 0usize;
    let mut bytes = 0usize;
    for p in files.into_iter().skip(keep as usize) {
        let sz = std::fs::metadata(&p).map(|m| m.len() as usize).unwrap_or(0);
        if std::fs::remove_file(&p).is_ok() {
            removed += 1;
            bytes += sz;
        }
    }
    (removed, bytes)
}

/// 做一次本地备份，返回 (路径, 字节数)。
pub fn local_backup() -> Result<(PathBuf, u64), String> {
    let (path, size) = pchabit_datad::backup_now()?;
    let src = PathBuf::from(&path);
    let dir = backup_dir();
    std::fs::create_dir_all(&dir).map_err(|e| format!("建备份目录失败：{e}"))?;
    let dst = dir.join(src.file_name().unwrap_or_default());
    if src != dst {
        let bytes = std::fs::read(&src).map_err(|e| format!("读备份失败：{e}"))?;
        std::fs::write(&dst, bytes).map_err(|e| format!("写 {} 失败：{e}", dst.display()))?;
    }
    Ok((dst, size))
}

/// 便签同步：远端 `pchabit_notes.json` 与本地互并（按 Id 去重、`updatedAt` 新的赢），
/// 合并结果再传回远端。返回 `(新增/更新条数, 本地总条数)`。
/// 需要 `WebDAV*` 配好且 `StickyNotesSyncEnabled` 为真（调用方判断）。
pub fn notes_sync() -> Result<(usize, usize), String> {
    let d = dav()?;
    let db = pchabit_datad::default_db();
    let local = pchabit_datad::notes_json(&db)?;
    let local_items: Vec<serde_json::Value> =
        local["items"].as_array().cloned().unwrap_or_default();

    let mut merged = local_items.clone();
    if let Some(bytes) = d.get("pchabit_notes.json")? {
        if let Ok(v) = serde_json::from_slice::<serde_json::Value>(&bytes) {
            if let Some(arr) = v["items"].as_array() {
                let known: std::collections::HashSet<String> = local_items
                    .iter()
                    .filter_map(|n| n["id"].as_str().map(|s| s.to_string()))
                    .collect();
                for it in arr {
                    if let Some(id) = it["id"].as_str() {
                        if !known.contains(id) {
                            merged.push(it.clone());
                        }
                    }
                }
            }
        }
    }

    let imported = pchabit_datad::notes_import(&merged)?;
    // 导入后再读一遍（本地已是合并后的全集），整份上传
    let now = pchabit_datad::notes_json(&db)?;
    let bytes = serde_json::to_vec(&now).map_err(|e| format!("序列化便签失败：{e}"))?;
    let code = d.put("pchabit_notes.json", &bytes)?;
    if !(200..=299).contains(&code) {
        return Err(format!("便签上传返回 {code}"));
    }
    let total = now["items"].as_array().map(|a| a.len()).unwrap_or(0);
    let stamp = pchabit_datad::today_local() + " " + &stamp_hm();
    crate::update_setting_file("StickyNotesLastSync", serde_json::Value::String(stamp));
    Ok((imported, total))
}

/// 按设置连一次 WebDAV；`WebDAVUrl` 为空直接报错。
pub(crate) fn dav() -> Result<Dav, String> {
    let url = s("WebDAVUrl", "");
    if url.trim().is_empty() {
        return Err("WebDAVUrl 为空".to_string());
    }
    Dav::new(
        url.trim(),
        &s("WebDAVUsername", ""),
        &s("WebDAVPassword", ""),
    )
}

/// 远端保留的备份文件名（按名排序，形如 `pchabit_backup_YYYYMMDD_HHMMSS.db`，字典序即时间序）。
fn remote_backups(d: &Dav) -> Vec<String> {
    let mut names: Vec<String> = d
        .list("")
        .unwrap_or_default()
        .into_iter()
        .filter(|n| n.starts_with("pchabit_backup_") && n.ends_with(".db"))
        .collect();
    names.sort();
    names
}

/// 远端全部备份名（按时间序）。给「查看云端状态」显示份数用。
pub fn cloud_names() -> Result<Vec<String>, String> {
    let d = dav()?;
    d.list("").map(|all| {
        let mut names: Vec<String> = all
            .into_iter()
            .filter(|n| n.starts_with("pchabit_backup_") && n.ends_with(".db"))
            .collect();
        names.sort();
        names
    })
}

/// 上传最新一份本地备份，并清理远端多余份数。返回上传的文件名。
pub fn cloud_backup() -> Result<String, String> {
    let d = dav()?;
    let (local, _) = local_backup()?;
    let name = local
        .file_name()
        .map(|n| n.to_string_lossy().to_string())
        .ok_or_else(|| "取备份文件名失败".to_string())?;
    let bytes = std::fs::read(&local).map_err(|e| format!("读备份文件失败：{e}"))?;
    let code = d.put(&name, &bytes)?;
    if !(200..=299).contains(&code) {
        return Err(format!("上传返回 {code}"));
    }
    let keep = i("MaxCloudBackupCount", 5);
    if keep > 0 {
        let names = remote_backups(&d);
        for old in names
            .into_iter()
            .rev()
            .skip(keep as usize)
            .collect::<Vec<_>>()
        {
            let _ = d.delete(&old);
        }
    }
    let stamp = pchabit_datad::today_local() + " " + &stamp_hm();
    crate::update_setting_file("WebDAVLastSync", serde_json::Value::String(stamp));
    Ok(name)
}

/// 远端最新一份备份的名字（`None` = 远端没有）。
pub fn cloud_latest() -> Result<Option<String>, String> {
    let d = dav()?;
    Ok(remote_backups(&d).into_iter().last())
}

/// 从远端拉最新一份到本地临时目录（恢复由用户手动确认，这里只负责下载）。
pub fn cloud_fetch_latest() -> Result<Option<PathBuf>, String> {
    let d = dav()?;
    let Some(name) = cloud_latest()? else {
        return Ok(None);
    };
    let Some(bytes) = d.get(&name)? else {
        return Ok(None);
    };
    let dir = std::env::temp_dir().join("PChabitRestore");
    std::fs::create_dir_all(&dir).map_err(|e| format!("建临时目录失败：{e}"))?;
    let path = dir.join(&name);
    std::fs::write(&path, bytes).map_err(|e| format!("写下载文件失败：{e}"))?;
    Ok(Some(path))
}

fn stamp_hm() -> String {
    let secs = SystemTime::now()
        .duration_since(SystemTime::UNIX_EPOCH)
        .map(|d| d.as_secs())
        .unwrap_or(0) as i64;
    let local = secs + 8 * 3600;
    format!(
        "{:02}:{:02}",
        (local % 86_400) / 3600,
        (local % 3600) / 60
    )
}

/// 低频线程：本地自动备份 + 云上传。
pub fn start() {
    std::thread::spawn(|| {
        // 启动后先等 2 分钟，别和采集/窗口初始化抢 IO
        std::thread::sleep(Duration::from_secs(120));
        let mut last_local = SystemTime::UNIX_EPOCH;
        let mut last_cloud = SystemTime::UNIX_EPOCH;
        loop {
            let auto = b("AutoBackupEnabled", true);
            let hours = i("AutoBackupIntervalHours", 4).clamp(1, 168) as u64;
            let keep = i("MaxBackupCount", 5);
            let due = |last: SystemTime| {
                SystemTime::now()
                    .duration_since(last)
                    .map(|d| d.as_secs() >= hours * 3600)
                    .unwrap_or(true)
            };
            if auto && due(last_local) {
                match local_backup() {
                    Ok((path, size)) => {
                        crate::log_line(&format!(
                            "[备份] 本地备份完成：{}（{} MB）",
                            path.display(),
                            (size as f64 / 1048576.0).round()
                        ));
                        last_local = SystemTime::now();
                        let (n, bytes) = prune_local(keep);
                        if n > 0 {
                            crate::log_line(&format!(
                                "[备份] 清理旧备份 {n} 份（{} MB），保留 {keep} 份",
                                (bytes as f64 / 1048576.0).round()
                            ));
                        }
                    }
                    Err(e) => crate::log_line(&format!("[备份] 本地备份失败：{e}")),
                }
            }
            if b("WebDAVEnabled", false) && due(last_cloud) {
                match cloud_backup() {
                    Ok(name) => {
                        crate::log_line(&format!("[云备份] 已上传 {name}"));
                        crate::notify("PChabit 云备份", &format!("已上传 {name}"));
                        last_cloud = SystemTime::now();
                    }
                    Err(e) => {
                        crate::log_line(&format!("[云备份] 上传失败：{e}"));
                        crate::notify("PChabit 云备份失败", &e);
                    }
                }
            }
            std::thread::sleep(Duration::from_secs(300));
        }
    });
}
