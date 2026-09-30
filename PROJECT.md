# PChabit 项目记录

> **最后更新**：2026-09-28
> **文档定位**：**索引层**，不重复造轮子。深度内容一律查 `AI_MAINTENANCE.md`（1775 行，21 条陷阱）与 `CHANGELOG.md`。
> **同步说明**：本目录位于 Verysync 同步盘 `E:\SYNC` 内，会自动同步到其他终端。**本地文件即权威源，云端文档非必需。**

---

## 1. 项目概览

| 维度 | 详情 |
|---|---|
| 项目定位 | Windows 键鼠统计 + 习惯追踪桌面应用 |
| 源码目录 | `E:\SYNC\My VS\PChabit` |
| 发布输出 | `E:\SYNC\My VS\PChabit\publish` |
| 安装运行目录 | `D:\Tool\PChabit`（用户实际运行的程序在此，非 publish 目录） |
| 当前版本 | **3.21.0**（`src\PChabit.App\PChabit.App.csproj` 第 13 行 `<Version>`） |
| 技术栈 | .NET 10.0 / WinUI 3 / Windows App SDK 2.5.1 / EF Core 10.0.0 / Serilog 4.x |
| 代码仓库 | `github.com/idlee/PChabit` |

### 关键约束（硬性）

1. **只 build 不算完成**：必须 `dotnet publish -c Release -p:Platform=x64 -o "E:\SYNC\My VS\PChabit\publish"`。只 build 的 DLL 在 `bin\` 下，用户运行的是 `publish\`，修复不会生效。
2. **版本号必须递增且 UI 可见**：改代码即改 `<Version>`，且版本号要显示在界面上，否则用户无法确认加载的是哪个版本。
3. **动手前必读陷阱表**：`AI_MAINTENANCE.md` 第 281 行起的 21 条陷阱，禁止以"这个陷阱是老版本"为由跳过。
4. **GitHub 与本地保持一致**：本地改动需同步 push。

---

## 2. 标准流程 SOP

| # | 步骤 | 完成判断（做到什么算成功） |
|---|---|---|
| 1 | 读 `AI_MAINTENANCE.md` 陷阱全表 + 禁止事项（第 108 行） | 21 条陷阱已逐条过目，能说出本次改动可能触及哪几条 |
| 2 | 定位并修改代码 | 改动未触犯禁止事项表；未引入新的 `.Wait()` / 双层 `Task.Run` |
| 3 | 递增版本号 `src\PChabit.App\PChabit.App.csproj` | `<Version>` 大于上一版，且 UI 上能看到该版本号 |
| 4 | 发布 | `dotnet publish -c Release -p:Platform=x64 -o "E:\SYNC\My VS\PChabit\publish"` 无编译错误退出 |
| 5 | 验证产物时间戳 | `(Get-Item publish\PChabit.dll).LastWriteTime` 为刚刚的时间 |
| 6 | 起进程验证 | 双击 `publish\PChabit.exe` 能正常启动，改动点可复现验证 |
| 7 | 提交 GitHub | `git status` 干净，本地改动已 push |
| 8 | 补文档 | 新坑写入 `AI_MAINTENANCE.md` 陷阱表并编号递增；本次改动写入 `CHANGELOG.md` 与本文档「历史记录」 |

**关键提示**：第 5 步是整个流程最容易漏的。只验证"编译通过"会导致用户运行旧 DLL，BUG 依旧——这是信任崩塌的直接原因。

---

## 3. 常见坑与解法

按踩坑频率排序。完整 21 条见 `AI_MAINTENANCE.md` 第 281 行起。

| 坑 | 现象 | 正确做法 |
|---|---|---|
| 只 build 不 publish（#0 禁止事项） | 用户运行后 BUG 依旧 | 必须 publish + 验证时间戳；不推给用户"请自行验证" |
| 新表迁移双路径分叉（#20） | 老库缺表，新装正常 | 改 EF 模型**同时**补 `MigrateAnalysisTablesAsync` 手动迁移路径 |
| C# 编译失败级联 XAML 假错误（#21） | 一堆 `WMC9999` /「Unknown type 本地类型」 | **先修 C# 再看 XAML**，XAML 报错多为 `MarkupCompilePass2` 级联产物 |
| e_sqlite3.dll 缺失（#11） | `DllNotFoundException` / `TypeInitializationException` | csproj 需同时设 `RuntimeIdentifier`（单数）与 `SelfContained` |
| DI 注册遗漏（#3） | `InvalidOperationException`、闪退 | 新增服务必须在 DI 容器注册，新增页面/VM 一并登记 |
| ObservableCollection 跨线程（#5.6） | `COMException 0x8001010E` | 集合操作回 UI 线程；`await` 不等于不阻塞，需查 `SynchronizationContext` |
| SQLite WAL 锁冲突（#5） | 卡顿、并发异常 | 启用 WAL；退出时 checkpoint 需异步，避免阻塞关闭 |
| 版本号遗漏 | 用户装了旧版找不到新功能 | 改代码即改 `<Version>`，且版本号显示在 UI 上 |

---

## 4. 历史记录（时间倒序）

| 日期 | 版本 | 关键操作与结果 |
|---|---|---|
| 2026-09-30 | 3.21.0 | 收尾：多模型档位、AI/规则洞察去重、习惯轨迹卡片；分析测试 92 通过；publish 3.21.0.0 |
| 2026-09-30 | 3.20.0 | P3/P4：严格隐私、周自动解读、复制解读 Markdown、计划反馈、习惯轨迹入包；分析测试 89 通过；publish 3.20.0.0 |
| 2026-09-30 | 3.19.0 | P2 闭环：AiInsightSnapshot 落库（#20）+ lastAiPlan 闭环 + 计划状态持久化 + 追问对话；分析测试 84 通过；publish 3.19.0.0 |
| 2026-09-30 | 3.18.0 | P1：HabitProfile / PersonalBaseline / HabitDayLoader；AI 包注入画像+基线+偏离；周计划采纳/忽略 UI；分析测试 81 通过；publish 3.18.0.0 |
| 2026-09-30 | 3.17.0 | AI 习惯理解双轨 P0：ActivityLabeler 行为语义 + DailySummary 标签/夜间/起止 + AiContextPack v2 + 统一 OpenAI 客户端（流式/取消）+ 解析 v2；分析测试 70 通过；`dotnet publish` 完成（PChabit.dll 3.17.0.0） |
| 2026-09-28 | 3.16.1 | 书签模块彻底移除（Core/Infrastructure/App/Tests 全链路约 4500 行）+ 任务栏小窗视觉重构与 Explorer 重启自愈 + 开机自启改注册表 Run 键；`dotnet build` 通过，0 错误；本机（备份终端）已构建部署至 `D:\Tool\PChabit`（510 文件，版本 3.16.1.0） |
| 2026-09-27 | — | 建立项目记录文档（索引层），确认源码位于 `E:\SYNC\My VS\PChabit`，处于 Verysync 同步盘内 |
| 2026-09-25 | 3.16.0 | 新增网络流量监控：`NetworkTrafficPage` + `ProcessNetworkMonitor` + 持久化服务 |
| 2026-09-20 | 3.15.15 | 修复启动时窗口被拽到左上角：`CorrectWindowBoundsIfOffscreen` 误用「75% 屏幕」当可见区 |
| 2026-09-19 | — | 修复键鼠统计数据未写入数据库；通过对比 GitHub 旧代码定位回归 |
| 2026-09-18 | — | 技术栈升级至 .NET 10，更新 `AI_MAINTENANCE.md` |
| 2026-06-17 | — | .NET 9 + WinAppSDK 2.2.1 升级 + WMC9999 编译错误修复（`AI_MAINTENANCE.md` 第 1003 行） |

---

## 5. 待办与维护

**未完成事项**

- [ ] 确认 GitHub 仓库与本地改动完全同步（`.workbuddy` 日志显示 9/11、9/18、9/20 均有 PChabit 相关会话）
- [ ] 确认 `D:\Tool\PChabit` 安装目录是从 `publish` 复制的最新版
- [ ] 确认 Verysync 在另一台终端处于运行状态（`.verysync` 目录存在但当前为空，需核对）

**更新规则**

- 每次修复 BUG → 先写 `AI_MAINTENANCE.md` 陷阱表（递增编号），再更新 `CHANGELOG.md`，最后追加本文件「历史记录」
- 每次发布 → 更新本文件「当前版本」
- 本文件只做索引，技术细节一律沉淀到 `AI_MAINTENANCE.md`，避免两处维护同一内容
