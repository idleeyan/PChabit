//! PChabit 采集宿主（P5 第一块）：原本分散在 WPF 的采集职责搬到这里。
//!
//! 与 WPF 版的关系：**单写者**。`PChabit.exe` 在跑时宿主拒绝启动，
//! 反过来宿主在跑时 WPF 版也不该再开（后续在壳里做互斥提示）。
//!
//! 还没搬完的部分（`docs/P5-collect-migration.md` 里有清单）：
//! - 托盘、开机自启、默认入口切换。

pub mod daily;
pub mod dt;
pub mod host;
pub mod input;
pub mod netd;
pub mod netif;
pub mod sampler;
pub mod sha1;
pub mod store;
pub mod timefmt;
pub mod websync;
pub mod win;

pub use host::{Config, Host};
pub use store::Store;

/// 默认库路径（与 WPF 版一致，可用 `PCHABIT_DB` 覆盖，自测时指向副本）。
pub fn default_db() -> std::path::PathBuf {
    if let Ok(p) = std::env::var("PCHABIT_DB") {
        if !p.trim().is_empty() {
            return std::path::PathBuf::from(p);
        }
    }
    let base = std::env::var("LOCALAPPDATA").unwrap_or_default();
    std::path::PathBuf::from(base)
        .join("PChabit")
        .join("Data")
        .join("pchabit.db")
}

