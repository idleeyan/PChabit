# pchabit-dbcore（P3 可行性探针）

只读打开宿主正在写的同一个 SQLite（`%LOCALAPPDATA%\PChabit\Data\pchabit.db`），
验证 Rust 数据面的两个前提：能不能读到一致的数据、读得快不快。

**这个 crate 不进发布产物**，也不参与 `pchabit-netd` 的构建——内核保持零 crate 依赖，
`dotnet publish` 的 473 文件链路不受影响。它只是一个决策工具。

## 用法

```powershell
cd rust\pchabit-dbcore
cargo build --release
.\target\release\pchabit-dbcore.exe                 # 默认库路径 + 本地今天
.\target\release\pchabit-dbcore.exe --date 2026-11-17
```

## 已知约束

- 只读优先直接 `SQLITE_OPEN_READ_ONLY`；WAL 模式下若 `-shm` 不可用会退化为
  「复制 db/-wal/-shm 到临时目录再读」的快照模式（快照可能落后于宿主最后几秒的写入）。
- 依赖 `rusqlite` 的 `bundled` 特性，需要 C 工具链（本机已验证可用，首次编译约 22 秒）。
  这也是它**不并入 netd** 的原因：一旦内核依赖 C 编译，发布机的构建前置就变了。

## 2026-11-18 实测（本机真实库）

| 项 | 结果 |
|---|---|
| 库规模 | AppSessions 199,335 行 / HardwareSamples 45,784 行 / KeyboardSessions 1,038 行 |
| 当日聚合（24 进程分组 + 排序） | **2.52 ms**（Python 独立实现同口径 3.14 ms，结果逐行一致） |
| 最近 5 条硬件样本 | 0.08 ms |
| 首次 release 构建 | 22.58 s（含 libsqlite3-sys 编译） |
