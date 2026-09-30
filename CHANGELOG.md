# 更新日志

所有重要的更改都将记录在此文件中。

## [Unreleased]

### 文档
- `README.md` 全面重写：补充硬件监控、分析复盘驾驶舱、深度周报与 AI 解读、浏览历史与网站分类、
  任务栏小窗、全局操作日志等模块；删除已移除的「目标管理」章节并加升级说明；
  开发环境更新为 .NET 10 SDK + Windows App SDK 2.5.1；项目结构补充 `PChabit.HardwareMonitor`
- `AI_MAINTENANCE.md` 更新项目概览技术栈（.NET 10 / WinAppSDK 2.5.1 / EF Core 10.0.0 / Serilog 4.x）、
  项目结构与关键依赖版本表；`PublishReadyToRun` 禁令的版本说明同步（历史修复记录保持原样未改）
- `docs/发布产物语言资源说明.md`：构建输出路径示例由 net9.0 更正为 net10.0
- `docs/AI深度解读升级计划.md`：双轨升级计划（项目习惯资产 + AI 深度解读）

## [3.22.7] - 2026-09-30

### 修复：配置重启被刷回智谱默认 + 测试结果不可复制
- **根因**：加载设置时 `AiProviderBox.SelectedItem = …` 触发 SelectionChanged，把 Base URL/模型覆盖成预设（智谱/glm-4-flash）
- 加载期间全程 `_isLoading` 保护；预设填充仅在用户手动切换 Provider 时
- `SaveSetting` 改为**同步 Save()**，不再异步 fire-and-forget（退出竞态丢配置）
- 测试结果改为可选中 TextBox +「复制测试结果」按钮

## [3.22.6] - 2026-09-30

### 优化：API Key 明文显示（个人软件不做遮掩）
- API Key 改为明文 TextBox，测试结果直接打印完整 Key，便于和服务商控制台核对
- 请求日志输出端点/模型/Key 长度（不写 Key 明文到日志）

## [3.22.5] - 2026-09-30

### 修复：设置存不上 / 401 — 端点配置收敛
- **删掉「主配置 + 云端配置」双轨**：云端 = Base URL / API Key / 模型 **一组**；本地 = LM Studio 一组
- **输入即保存**（TextChanged/PasswordChanged），不再依赖失焦点按钮才落盘
- **「保存并测试连接」**：一键落盘并探测，显示 URL/模型/Key 是否已填 + 模型回复或错误
- Provider 预设改为一键填充，**不再自动覆盖**用户已填地址

## [3.22.4] - 2026-09-30

### 修复：端点配置混搭 401 + 解读英文空话
- **云端槽整组生效**：独立云端 URL 时必须单独填模型，禁止与主配置 URL/模型串用；缺模型视为未配置并回退（不再对错误 Key 发 401）
- 显式指定本地/云端槽时不串槽
- **输出强制简体中文**：system + user 前缀双重锁定；confidence/effort 仍用英文枚举
- 解析失败时区分「疑似英文输出 / 未结构化」，原文可复制，不再把英文堆当结论

## [3.22.3] - 2026-09-30

### 优化：端点模式更醒目
- 「端点模式」置顶为高亮卡片（Accent 描边 + 标题提示「必选」）
- ComboBox 改为三大单选：云端 / 本地 / 双端点（推荐），文案说明用途
- 底部实时显示「当前：…」摘要；云端槽 / 本地槽分组标题区分

## [3.22.2] - 2026-09-30

### 修复：本地 LM Studio 跑完但「AI 返回为空」
- **本地解读改走非流式**：避免 LM Studio 流式解析差异导致空正文
- 解析兼容：`message.content` / `reasoning_content` / `delta.content` / `text`
- 正文为空时抛错并附原始片段，不再静默显示「返回为空」

## [3.22.1] - 2026-09-30

### 修复：本地 AI 返回 400
- 本地端点（LM Studio/Ollama）**不再发送** `response_format` / `max_tokens`（常见 400 诱因）
- 云端 400/404/422 时自动降级重试一次（去掉可选字段）
- 错误信息带上响应体摘要，便于定位「模型名不对 / 路径不对」等问题

## [3.22.0] - 2026-09-30

### 新增：本地 + 云端双 AI 端点
- **端点模式**：`云端` / `本地 LM Studio` / `双端点`
  - **双端点**：深度解读走云端，追问走本地（缺一则自动回退另一端）
- **本地槽**：Base URL（默认 `http://127.0.0.1:1234/v1`）+ 模型（Key 可空）
- **云端槽**：Base URL / API Key / 模型（可独立于主配置，空则回退主配置）
- 设置页「分析 AI」增加端点模式与本地/云端分组配置；两套配置可同时保留
- 解析：`AiEndpointSlot` + `OpenAiCompatibleChatClient.Resolve`

### 测试
- `AiEndpointResolveTests`（6）；分析套件 **100 通过**

## [3.21.1] - 2026-09-30

### 修复：分析周期对比永远以「今天」为终点
- **问题**：「本周」按自然周（周一～周日）切，周三只有 3 天有效数据却对比完整上周 7 天（如 09-28~10-04 vs 09-21~09-27）
- **修复**：`ThisWeek`/`LastWeek`/`Last7Days`/`Last30Days`/`ThisMonth` 改为**滚动窗**：
  - 近 7 天 = [今天-6, 今天]，对比 **前 7 天** = [今天-13, 今天-7]
  - 近 30 天 = [今天-29, 今天]，对比前 30 天
- 分析页默认周期改为「近 7 天」；选项：近 7 天 / 前 7 天 / 近 30 天 / 近 3 天 / 今天
- 自然周 `CalendarWeek` 仅保留给明确需要周历的场景
- 单测覆盖 09-30（周三）场景：必须 7 天且含今天

## [3.21.0] - 2026-09-30

### 新增：收尾项
- **多模型档位**：设置「快捷模型（追问）」`AiModelFast`，追问用快模型、解读用准模型；留空则共用
- **洞察去重**：`AiInsightDeduper` 过滤与规则洞察重叠的 AI 发现（规则优先）
- **习惯养成轨迹卡片**：分析页展示近 28 天前后半对比（变好/变差/稳定 + 连续周数）
- 追问改走 `ChatFastAsync`

### 测试
- `AiInsightDeduperTests`（3）；分析套件 **92 通过**

## [3.20.0] - 2026-09-30

### 新增：自动化、隐私与习惯轨迹（P3/P4）
- **严格隐私模式**（设置）：出域指标包去掉应用/分类显示名
- **每周自动 AI 解读**（设置开关）：启动延迟 2 分钟检测本周是否已跑，生成后落库并通知
- **复制解读**：AI 结果导出 Markdown（结论/发现/归因/计划/风险/画像）
- **反馈学习**：历史计划 done/skipped 统计注入 `goals.planFeedback`，降低无效建议
- **`HabitTrajectoryBuilder`**：夜间/专注/标签占比的 better/worse/stable 轨迹入 AI 包（长期叙事）
- 设置页新增「严格隐私」「每周自动 AI 解读」开关

### 测试
- `HabitTrajectoryAndFeedbackTests`（5）；分析套件 **89 通过**

## [3.19.0] - 2026-09-30

### 新增：AI 闭环（P2）
- **`AiInsightSnapshot` 实体 + 表**（EF + `MigrateAnalysisTablesAsync` 双路径，陷阱 #20）
- **`AiInsightHistoryService`**：同周期覆盖保存、最新/按周期查询、计划状态 `pending/done/skipped` 落库、清空
- 解读成功后自动落库；下次解读自动带上 **lastAiPlan 与达成状态**
- 计划「采纳 / 忽略」持久化
- **追问对话**：输入框 + 回答卡片；上下文 = 精简指标包 + 上次结论 + 当前计划 + 用户问题（`FollowUpSystemPrompt`）

### 测试
- `AiInsightHistoryTests`（3）；分析套件 **84 通过**

## [3.18.0] - 2026-09-30

### 新增：习惯画像与个人基线（P1-A）
- **`HabitProfile` / `HabitProfileBuilder`**：夜型/日型、典型起止、专注中位数、夜间中位数、切换密度、生产力标签占比
- **`PersonalBaseline` / `PersonalBaselineBuilder`**：工作日/周末 P50/P75/P90；`DeviationPct` / `IsElevated` / `IsLow`
- **`HabitDayLoader`**：从 `DailySummaries` 近 28 天装载画像原料（无窗口标题/URL）
- 基线样本不足时禁止「异常」类结论（`Mature=false`）

### 新增：AI 包升级 + 计划闭环 UI（P1-B）
- `AiContextPack` 注入 `profile` / `baseline` / `deviations` / `goals`（每日目标）/ `lastAiPlan`
- 解读卡片：**可能原因**（diagnosis+置信度）、**周计划**（采纳/忽略）、**可追问**（followUps）
- 计划状态 `pending/done/skipped` 会带入下次解读上下文

### 测试
- `HabitProfileBuilderTests` + `PersonalBaselineBuilderTests`（11）；分析套件 **81 通过**

## [3.17.0] - 2026-09-30

### 新增：行为语义层（P0-A）
- **`ActivityLabels` / `ActivityLabeler`**：会话 → 活动标签（work-code / work-doc / meeting / learn /
  browse-info / browse-fun / game / comms / admin / idle / other）；纯函数可单测
- 窗口标题/域名仅本地推断，**不进入出域 payload**
- **`DailySummary`** 扩展：`LabelMinutesJson` / `NightMinutes` / `FirstActiveTime` / `LastActiveTime`；
  `MetricsVersion=3`；`DatabaseInitializer` 同步补列（陷阱 #20 双路径）
- 每日聚合写入活动标签分钟、夜间分钟（23:00–06:00）、首次/最后活跃时刻

### 新增：AI 指标包与协议（P0-B）
- **`AiContextPack` v2**：数值 KPI、日序列、24h 热力、TopApp 含环比、标签分钟、metricDictionary；
  camelCase JSON；严格隐私可去掉应用名
- **`OpenAiCompatibleChatClient`**：统一 OpenAI 兼容客户端（URL 补全、超时、max_tokens、
  response_format 尝试、流式 SSE + 非流式回退）；`AnalyticsAiService` / `AiChatService` 共用
- **`AnalyticsAiResponseParser` v2**：契约 v1/v2、evidence、diagnosis、plan、截断 JSON 括号修复
- **SystemPrompt v2**：禁止复述构成；强制归因 + 量化计划；evidence.metricId 必须来自指标包
- 分析页 AI：流式输出、「取消」按钮、失败可复制原文

### 修复/工程
- 测试项目 `CopyLocalLockFileAssemblies`（xunit host 需要包程序集落盘）
- 单测：`ActivityLabelerTests`（15）、解析截断/契约 v2/指标包用例；分析套件 **70 通过**

## [3.16.1] - 2026-09-28

### 移除：书签模块彻底下线
- 删除书签同步全链路代码：
  - Core：`BrowserBookmark` / `PendingBookmarkChange` 实体、`IBrowserBookmarkRepository`、`Core/Sync`（`BookmarkMergePlanner` / `BrowserSyncModels`）
  - Infrastructure：`BookmarkHubService` / `BookmarkLibraryService` / `BookmarkSyncService` / `BookmarkTidyService` / `BrowserBookmarkRepository`
  - App：`DataManagementViewModel.BookmarkSync.cs`；Tests：`BookmarkFolderSyncTests` / `BookmarkMergePlannerTests` / `BrowserDeletionDetectionTests`
- `App.xaml.cs` 移除 `StartBookmarkAutoSync` 与书签同步定时器；`BrowserSyncWebSocketHandler` 大幅精简
- `HistorySyncService` 由 `IBrowserBookmarkRepository` 改为直接注入 `IDbContextFactory<PChabitDbContext>`
- 浏览器书签改由各浏览器自带账号同步维护，PChabit 不再介入；**浏览历史 `BrowserHistoryItems` 保留**

### 优化：任务栏小窗视觉重构
- 主行数值放大加粗（标签 11px / 数值 15px）作为视觉焦点，次行统一 13px 弱化；标签、数值、分隔符分级配色，仅偏高/过高项着色
- 标签缩短以避免与主指标撞名：`磁盘`→`盘`、`CPU`→`C`、`GPU`→`G`、`今日`→`今`，网速项去掉标签
- 次行数值去掉冗余单位：网速 `K/s`→`K`、温度 `°C`→`°`（来源由 `C`/`G` 标签消歧）
- 条目间距放宽（主行 14px / 次行 10px），避免挤成一团

### 修复：任务栏小窗在 Explorer 重启后丢失
- `TaskbarWidget.Start()` 改为幂等：句柄失效时销毁重建；父窗口变化（`Shell_TrayWnd` 换代）时自动重新挂载

### 优化：开机自启更可靠
- `SettingsService` 除 Startup 快捷方式外，**新增写入 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`**
- 自启目标以 `Environment.ProcessPath` 为准（缺省回退 `AppContext.BaseDirectory`）；目标 exe 不存在时清理残留自启项，避免留下失效路径
- 新增依赖 `Microsoft.Win32.Registry`

### 其他
- 清理构建脚本残留：`publish.bat`、`scripts/fix_gcs.py`、`scripts/gen_gcs.py`、`scripts/test_xamlcompiler.bat`
- 新增项目索引文档 `PROJECT.md`

## [3.16.0] - 2026-09-25

### 新增：网络流量监控
- 新增「网络流量」页面（`Views/NetworkTrafficPage.xaml` + `ViewModels/NetworkTrafficViewModel.cs`）：实时展示进程级网络流量（上传/下载速度、会话流量、IP）
- 新增 `NetworkTrafficPersistenceService`：流量数据持久化服务（DI 注册），随应用启动/关闭管理
- 新增 `Core/Entities/NetworkTrafficEntities.cs` 网络流量实体；`PChabitDbContext` 增加对应表；`DatabaseInitializer` 同步初始化
- 硬件监控模块新增 `ProcessNetworkMonitor` + `ProcessNetworkNative`（进程网络流量采集核心，依具体进程/网卡枚举）

## [3.15.15] - 2026-09-20

### 修复：启动时窗口被自动移到屏幕左上角
- 原因：`CorrectWindowBoundsIfOffscreen` 用 **「75% 屏幕」** 当可见区，靠右/最大化窗口右缘必超 75% → 每次启动都被 `SetWindowPos` 拽到左上附近；还 2 秒一次连校 10 次
- 修复：仅当窗口与主屏 **几乎无交集（&lt;48px）** 或尺寸异常时才校正；**最大化窗口不干预**；取消启动后 10 次轮询

## [3.15.14] - 2026-09-20

### 优化：移除数据管理页重复的「全局操作日志」
- 侧栏已有常驻全局日志，数据管理页内同名列表已删除，避免重复
- 数据管理操作仍会写入侧栏/磁盘全局日志（`Logs/ops-*.log`）

## [3.15.13] - 2026-09-20

### 新增：侧栏全局操作日志面板
- 左侧导航菜单下方（设置按钮上方）常驻 **全局日志** 区
- 可滚动、跨页面不丢失；实时插入最新条目（上限约 120 条显示）
- 支持 **收起/展开**、**清空显示**（磁盘 `Logs/ops-*.log` 仍保留）
- 页面导航、监控启停等关键操作写入 GlobalOpLog，便于排查

## [3.15.12] - 2026-09-20

### 清理：书签模块残留代码与历史数据
- 删除 `BookmarkLibraryPage` / `BookmarkLibraryViewModel` 及相关测试
- `BookmarkHubService` / `BookmarkTidyAiService` 改为停用桩（不再推送/整理）
- 数据管理书签相关命令改为提示「模块已停用」
- **清空本地书签历史表**：BrowserBookmarks、PendingBookmarkChanges、BookmarkSyncBaselines、书签相关 BrowserSyncMetas
- **保留**：浏览历史 `BrowserHistoryItems`（约 2.5 万条，历史分析页仍可用）
- 浏览器侧书签由各浏览器自带同步维护，PChabit 不再介入

## [3.15.11] - 2026-09-20

### 变更：下线书签库 / 书签同步模块
- 原因：书签中枢同步多次出现数据丢失/结构错乱，且浏览器自带书签同步已够用
- **移除导航入口**「书签库」；书签同步命令改为提示已停用
- **默认关闭** 浏览器书签同步与自动推送（settings 已写入）
- **本地 BrowserBookmarks 等数据保留**，不自动删除；需要时可再恢复入口
- 建议：日常使用 Chrome / Edge / 豆包各自的账号书签同步

## [3.15.10] - 2026-09-20

### 修复：书签库书签/文件夹「凭空消失」
- 现象：界面里文件夹变少、子目录对不上
- 根因：文件夹 **Id 正确**（如 `f:书签栏/工具`），但 **Title/PathJson 被写歪**（Title 变成「书签栏」、Path 为空）→ UI 按 Title 拼路径 → 多项显示成同一层并被去重，看起来像丢了
- **修复**
  - UI/服务 `ToNode`：**以 Id 为准**解析目录名与路径
  - 归一化写库时 Title 与 Id 最后一段强制一致
  - 库内已按 Id 修复 9 个文件夹；并从备份合并缺失目录
- 数据仍在库内（约 141 书签），并非浏览器侧删除

## [3.15.9] - 2026-09-20

### 修复：书签库文件夹再次重复（书签栏 vs 收藏夹栏）
- 根因：浏览器导出入库时未规范化路径，Chrome `书签栏/*` 与 Edge `收藏夹栏/*` 并存，UI 显示两套同名文件夹
- **入库规范化**：`ToEntity` 统一为书签栏/其他书签/移动设备书签，并使用规范 Id
- **展示去重**：库加载按规范路径合并同一文件夹
- **检查浏览器变更后自动归一化**，降低再次漂移概率
- 库内已清理：重复文件夹已合并（约 16 个目录）

## [3.15.8] - 2026-09-20

### 修复：书签库推送失败 + 全局操作日志
- **推送失败原因**
  1. 扩展写回时对每条书签做整树查找，234 条极慢，90s 内回不完 → 状态误显示失败
  2. 桌面端把「扩展未回结果」统一报成「无浏览器」，无法区分
- **修复**
  - 扩展 **2.2.4**：一次遍历建立 `url→id` 索引，写回 O(n) 完成
  - 推送状态改为**人话**：已连接浏览器名、载荷条数、各端 +/−/~、超时/异常分开提示
  - 推送等待 90s，并把各浏览器写回明细写入日志
- **全局操作日志**
  - 新增 `GlobalOpLog`：内存环形 + `Logs/ops-日期.log`
  - 书签推送、数据管理操作等写入全局日志
  - 数据管理页「全局操作日志」：刷新 / 复制 / 清空（磁盘仍保留）
- **请在 Chrome / Edge / 豆包重新加载扩展 2.2.4 后再点「推送库到浏览器」**

## [3.15.7] - 2026-09-20

### 修复：书签库点击文件夹无响应
- 原因：`ListView` 未开启 `IsItemClickEnabled`，`ItemClick` 不触发；`SelectionChanged` 又被清空 → 点击无任何反应
- 修复：左导航/右侧内容列表启用 `ItemClick`；选中变化只做高亮，不再重复触发展开逻辑（避免点一次立刻又收起）
- 右侧：单击文件夹进入；双击书签在浏览器打开

## [3.15.6] - 2026-09-20

### 修复：书签库左侧文件夹导航层级与展开/收起
- **缩进**：改为像素级左边距（每级 18px），子文件夹与父级视觉层级清晰
- **箭头**：有子文件夹时显示 ▸/▾
- **交互**：点击加载右侧内容并展开；**再次点击同一文件夹 = 展开/收起切换**（可收回）
- 顶栏提示交互方式

## [3.15.5] - 2026-09-20

### 修复：书签库文件夹数量混乱（27 vs 浏览器约 8）
- 根因：Edge「收藏夹栏」与库内「书签栏」「书签栏 / 工具」（带空格整串）「工具」等并存，同一逻辑目录被存成多份
- **路径归一化**：收藏夹栏/Bookmarks bar → **书签栏**；拆分「书签栏 / 工具」伪路径；合并同名目录
- **库内已执行整理**：文件夹 27 → 约 11–14（书签栏 + 子目录 + 其他书签/移动设备书签）
- **展示**：右侧列表不再重复显示完整路径（仅左侧树 + 面包屑）；数量为彩色徽标
- **AI 整理**：分类「工具」自动落到「书签栏/工具」；「书签栏/工具」会拆成路径
- 工具栏新增 **「整理库结构」**，可随时再归一化

## [3.15.4] - 2026-09-20

### 修复：AI 整理结果弹窗无法滚动
- 提案列表、过程日志、对话框内容均包入 `ScrollViewer`（MaxHeight 限制 + 自动滚动条）
- 结果页提示「列表可上下滚动查看全部提案」
- 进行中日志自动滚到底部

## [3.15.3] - 2026-09-20

### 优化：书签库改为资源管理器式布局
- **左侧**：仅文件夹导航（可展开，带数量徽标）
- **中间**：当前文件夹内的子文件夹 + 书签（文件夹在前）
- **右侧**：当前路径与回收站
- 数量提示改为**强调色**（如 `2夹 · 5签`），不再与标题混在一起难分辨
- 双击右侧文件夹进入；右键支持删除/重命名/移动/新建等

## [3.15.2] - 2026-09-20

### 新增：书签库右键操作
- 树状列表中文件夹/书签 **右键菜单**：展开/收起、重命名、移动到、在此新建、复制链接、在浏览器打开
- **删除（进回收站）**、**彻底删除…**（确认后物理删）
- 回收站列表右键：恢复全部 / 彻底删除单条 / 清空回收站

## [3.15.1] - 2026-09-20

### 优化：书签库树状结构 + AI 整理过程/结果可见
- **树状列表**：文件夹可展开/收起，子文件夹与书签嵌套显示；行尾显示路径归属；文件夹标注子项数量；右侧面板显示「当前位置」
- **AI 智能整理**：
  - 进行中弹窗：ProgressRing + 进度文案 + **过程日志**（批次 n/m、模型回复长度、提案累计）
  - 结束后打开**提案列表**：原名→新名、目录、URL、依据，可勾选后「应用所选」
  - 未配置 AI / 空范围 / 失败时给出明确提示，不再静默结束
- 搜索时展开相关路径，便于看清书签属于哪个文件夹

## [3.15.0] - 2026-09-20

### 新增：书签库中枢（M1–M4 核心）
- 侧栏新增 **「书签库」**：目录树 + 列表 + 新建/重命名/移动/软删 + **回收站**（30 天）+ 搜索
- **中枢 Push**：「推送库到浏览器」把库内有效书签整树下发；「检查浏览器变更」生成**待确认**（新/改名/删除），确认后才写库
- **自动推送**开关（书签库页）；浏览器侧删除不会自动清空库
- **AI Provider 设置**：智谱 / DeepSeek / LM Studio / 自定义；选中自动填 Base URL 与模型；与分析页共用 `AiChatService`
- **AI 智能整理**（提案制）：对库内书签批量建议命名/分类并应用
- 单测：`BookmarkLibraryServiceTests`（CRUD、级联软删/恢复、改名级联路径）
- 安全：旧双向合并删除语义保持关闭；云端空不删库（3.14.4/3.14.5）

## [3.14.5] - 2026-09-20

### 修复：删除无法同步 + 云端残缺再次误删
- 现象：恢复书签后，在浏览器删除无法传到其它端；且日志出现 `-45`（云端仅约 30 条 vs 基线 168 条时，合并仍按「云端没有=删除」砍掉条目）
- **合并改为「云端缺失不删除」**：删除只来自浏览器导出对比（DetectBrowserDeletions / FolderDeletions）显式路径
- 基线有、云端/本地都无的条目也会保进合并集，防丢
- **last_exports**：写回成功的浏览器，以同步后权威集合更新快照，否则删除检测永远对不上「上次还在」的书签
- 版本 3.14.5

## [3.14.4] - 2026-09-20

### 严重修复：云端快照为空时禁止批量删除书签（2026-09-20 02:24 事故）
- **事故**：02:24 自动同步「云端无快照」，但本地/基线仍有 168 条；三路合并把「基线有、云端无」当成他端删除，向 Edge+豆包各删 142 条 URL，浏览器书签几乎清空
- **根因**：WebDAV 下载失败/文件为空时，合并引擎仍启用删除语义，无「云端不可用」保护
- **修复**（`BookmarkMergePlanner.PlanThreeWayMerge`）：
  - 若 **云端为空** 且 **基线非空** 且 **本地非空** → 禁止删除：以基线充当云端合并源，并关闭历史删除语义（union，不 ToRemoveLocal）
  - 同步日志在云端空+有基线时输出「安全合并」警告
- **恢复**：请用 `AppData\Local\PChabit\Backups\pchabit_backup_20260919_222435.zip` 中的书签数据恢复（见会话说明）；**恢复前请先关闭书签自动同步**

## [3.14.3] - 2026-09-19

### 修复：书签同步误把「文件夹非空跳过删除」当错误，且每次同步都尝试删文件夹
- 现象：同步报告 5 条 `[Edge] 文件夹非空，跳过删除 工具/其他/…`
- 根因 1：文件夹 key 含路径，Chrome「书签栏/工具」与 Edge「收藏夹栏/工具」不同 → 本地文件夹被误判为「合并集中不存在」→ **每次同步都下发删除**
- 根因 2：扩展端「非空跳过」本是保护逻辑，却写入 `errors`，导致界面显示同步错误
- 修复：
  - **路径规范化**：书签栏/收藏夹栏/Bookmarks Bar 等根别名折叠为同一逻辑根，参与文件夹归属与删除判定
  - **删除条件收紧**：合并集中仍有同逻辑文件夹或书签挂在该路径下 → **不下发删除**
  - **错误分级**：扩展 `warnings` 字段；非空跳过记 warning，桌面端仅进度日志，**不计入错误**
  - 扩展 **2.2.3**
- **请重新加载三个浏览器扩展 2.2.3 后再同步**

## [3.14.2] - 2026-09-19

### 优化：书签同步错误可见与可复制
- 此前状态只显示「错误 N 条」，无明细、无查看入口
- 数据管理 → 书签同步：错误时显示红色汇总 + **错误列表** + **「查看错误」弹窗** + **「复制错误」**
- 扩展写回错误带上浏览器名（如 `[Edge] 文件夹非空，跳过删除 …`）
- 每条错误同步写入页面「操作日志」
- 常见处理提示：重新加载扩展 2.2.2 → 确认已连接 → 再同步

## [3.14.1] - 2026-09-19

### 修复：Edge 删除书签文件夹后同步又恢复
- 根因：合并规则「文件夹永不自动删除」+ 写回把合并集中的**文件夹类型**一并下发，`ensureFolder` 会在 Edge 把刚删的文件夹建回来
- 修复：
  - **写回不再下发文件夹**；文件夹仅在新增书签时按路径按需创建
  - **文件夹删除检测**：导出可信时，上次有、本次无且该路径下已无书签/子文件夹 → 判定删除
  - **孤儿文件夹清理**：合并后无任何书签引用的文件夹从权威集合剔除，并删除本地/云端
  - 扩展 **2.2.2**：`toRemoveFolders` 只删除**空文件夹**，非空则跳过并记错误
- **请在 Chrome / Edge / 豆包重新加载扩展（2.2.2）后再同步**
- 注意：若其它浏览器（如 Chrome）仍保留该文件夹下的书签，同步会按「书签仍在」恢复内容——这是并集语义；只有各端都删掉内容后空文件夹才会消失

## [3.14.0] - 2026-09-19

### 新增：应用统计 · 单应用详情卡片
- 应用排行支持**行点击 /「详情」**打开 ContentDialog 详情卡片
- 卡片内容：KPI（时长/会话/均长/占比/专注/环比/最长）· 小时分布 · 日趋势 · Top 窗口标题 · 可选输入与硬件摘要
- 新增 `AppDetailEngine`（纯函数可单测）+ `AppDetailViewModel` + `AppDetailDialog`
- 数据：AppSessions + AppDailyStats 日趋势 + KeyboardSessions 按键 + HardwareSamples 对齐（有数据才显示）
- 操作：复制 Markdown 摘要；「改分类」关闭卡片后复用排行分类选择
- 版本 3.14.0（计划书默认方案：Dialog · 标题 Top5 · 多源可降级）

## [3.13.3] - 2026-09-18

### 修复：书签改名未同步，反而被还原
- 根因 1：书签 key 仅基于 URL；多浏览器交错 `UpsertMany` 时**后到的旧导出覆盖新标题**，DB/云端被写回旧名
- 根因 2：合并仅在「云端 DateModified 更大」时改名；本机更新后的权威标题未可靠写回其它浏览器
- 根因 3：扩展写回遇已存在 URL 直接 skip，**从不 update 标题** → 改名无法传播
- 修复：
  - `UpsertMany`：批内同 key 取 DateModified 最新；库内已有更新标题时拒绝被旧导出覆盖
  - `BookmarkMergePlanner.IndexByKey`：同 key 保留 DateModified 较新项；标题冲突以较新 DateModified 为权威
  - 扩展 **2.2.1**：`bookmarks_apply` 对已存在 URL 执行 `bookmarks.update` 同步标题
- **请在 Chrome / Edge / 豆包重新加载扩展（2.2.1）后再同步书签**；改名应传播到各端且不再被还原

## [3.13.2] - 2026-09-18

### 修复：Edge 书签明显少于 Chrome / 豆包
- **导出混批**：多浏览器共用同一 `requestId` 的 `ExportBatchCollector`，批次互相覆盖/提前 Complete，Edge 导出丢失且快照错误。现按 `requestId+browser` 分桶，各浏览器独立完成事件
- **删除误判防护**：本次导出条数 &lt; 上次 50%（且上次≥20）时不判删除，避免半截导出把 Edge/全局书签删掉
- **写回未对齐**：旧逻辑只下发全局 `ToAddLocal`，DB/云端对齐后 Edge 若仍缺书签，后续同步显示「无需写回」，Edge 永远补不齐。现**每次同步把合并集书签/文件夹下发给所有已连接浏览器**；扩展创建前按 URL 查重跳过已存在项
- **Apply 汇总**：等待各浏览器 `bookmarks_apply_result`（不再只认第一个）；日志按浏览器输出 +N/-N
- **WebSocket SendAsync**：按 clientId 真正发送（原先误发给第一个连接）
- 扩展 **tai-browser-extension 2.2.0**（写回查重）
- **请在 Chrome / Edge / 豆包 中重新加载扩展**后再点「同步书签」，Edge 会补齐缺失书签

## [3.13.1] - 2026-09-18

### 修复：分析页「历史分析」/「打开历史分析」/ 硬件页跳转无响应
- 根因：`ShellPage` 里 `new NavigationService()` 后只对 **该实例** `Initialize(ContentFrame)`；分析页/硬件页通过 `App.GetService<NavigationService>()` 拿到的是 **DI 单例**，其 `_frame` 一直为 null，`NavigateTo` 全部静默返回 false
- 修复：ShellPage 改为从 DI 取单例并 `Initialize(ContentFrame)`，全应用共用同一导航服务
- 打开历史分析 / AI actionKey=HistoryReport / 硬件页→分析 增加导航结果日志，失败时给出可读提示

## [3.13.0] - 2026-09-18

### 分析升级收尾：时序图 / AI 结构化 / 历史联动 / Excel / 硬件入口
- **硬件时序图**：分析 → 硬件页签接入 WebView2 曲线（`Assets/Analytics/hardware-trend.html`，CPU/GPU/温度），替代仅色块
- **AI 深度解读结构化**：解析模型 JSON 为摘要 / 发现 / 建议 / 风险卡片；建议带 `actionKey` 时可跳转构成/节奏/硬件/历史分析；解析失败仍显示原文
- **浏览历史联动**：分析总览「网页与浏览历史」卡片 + 工具栏「历史分析」→ 打开 HistoryReport
- **分析 Excel 多表导出**：工具栏「导出 Excel」→ 概览 / KPI / 分类构成 / 应用变化 / 硬件 / 洞察（`AnalysisExcelExporter`）
- **硬件监控页入口**：标题旁「历史分析 →」跳转分析页硬件页签
- 单测：AI 解析、Excel 导出字节流

## [3.12.1] - 2026-09-18

### 修复：AI 深度解读超时过短（本地模型不够用）
- 原因：`AnalyticsAiService` 使用静态 `HttpClient.Timeout = 60s`，本地大模型推理常超过 60 秒
- 修复：超时改为设置项 **AiTimeoutSeconds**（默认 **300** 秒，可设 30–900）；HttpClient 不再写死总超时，按请求 `CancellationTokenSource.CancelAfter` 控制
- 超时错误提示可读：引导到「设置 → 分析 AI」调大超时
- 设置页 AI 卡片增加「请求超时（秒）」NumberBox；文案提示本地模型建议 ≥300s

## [3.12.0] - 2026-09-18

### 分析升级 P3：深度周报 + 数据质量 + 可选 AI 深度解读
- **深度周报**：`AnalysisReportBuilder` 生成完整 Markdown（数据质量 / KPI / 多源 / 构成 / 应用变化 / 硬件画像 / 洞察 / 建议）；分析页「复制周报」一键复制
- **数据质量**：总览显示有效天数与多源覆盖（应用/键鼠/网页/硬件样本天与分钟数）
- **AI 深度解读（默认关闭）**
  - 设置 →「分析 AI 深度解读」：开关 + OpenAI 兼容 Base URL + API Key + 模型
  - 分析页「AI 深度解读」：仅上传聚合 JSON（KPI/构成/硬件摘要/规则洞察），不含窗口标题、URL、完整会话
  - `AnalyticsAiService` 调 `.../v1/chat/completions`；失败/未配置时给出可读提示，不影响本地规则洞察
  - 密钥仅存本机 settings.json，不写日志
- 单测：`AnalysisReportBuilderTests`（周报章节、质量、AI payload JSON）

## [3.11.0] - 2026-09-18

### 分析升级 P2：应用×硬件融合画像
- 新增 `HardwareAnalyticsEngine`（纯函数可单测）：`HardwareSamples` 分钟样本 × `AppSessions` 对齐
  - 每分钟归属「重叠最长」的前台进程；输出 Top 应用 CPU/GPU/峰温/重叠时长
  - 小时桶 CPU/GPU 负载；周期汇总（均值/P95/峰温/样本天数）
- `AnalyticsEngine.BuildAsync` 自动加载周期内硬件样本（表缺失时降级为空）
- 分析页新增 **「硬件」页签**（Tag=hardware）：周期汇总、应用负载 Top、小时 CPU/GPU、对齐说明
- 洞察：高 GPU 应用、高 CPU 应用、样本内 GPU 高温（≥85°C）
- 复制摘要追加「硬件画像」小节
- 单测 `HardwareAnalyticsEngineTests`：解析/对齐/小时桶/Compute 融合

## [3.10.1] - 2026-09-18

### 修复：分析页「构成/节奏」内容右偏、显示不完整
- 根因：WinUI Pivot 内容区按子项横向测量，`PivotItem` 内直接放 `ScrollViewer + StackPanel(MaxWidth)` 时，选中页内容可能整体右偏，右侧卡片被裁切
- 修复：各 PivotItem 内容包一层 `Grid`，宽度锁定 `RootPivot.ActualWidth`；`ScrollViewer` 禁用水平滚动并 Stretch；`StackPanel` 左对齐 + MaxWidth=1100
- 构成页分类/应用变化行改为 `Auto + *` 列，避免固定宽列把行撑出可视区
- 与 AppStatsPage Pivot 高度锁定方案同一思路（宽度侧）

## [3.10.0] - 2026-09-18

### 分析升级 P1：多源 KPI + 分析页页签重构
- **AnalyticsEngine 多源**：`BuildAsync` 同时查询 `AppSessions` + `DailySummaries`（键鼠/网页/硬件日汇总）；`Compute` 增加可选 daily 参数，旧签名兼容
- **KPI 扩展**（有数据才显示）：键盘按键、输入密度（按键/活跃小时）、网页时长、网页占比、CPU/GPU 日均、GPU 峰值温度；原 4 项行为 KPI 保留
- **分类构成**：按 Category 聚合本周期时长占比（Top 8，≥5 分钟）
- **洞察升级**：网页占比偏高、输入密度过低（挂机嫌疑）/过高、GPU 峰温≥85°C、CPU 高负载、GPU 高负载、分类过度集中；夜间/生产力等 ActionKey 指向 rhythm/compose
- **分析页重构**：Pivot 为「总览 | 节奏 | 构成 | 应用流向」
  - 总览：KPI（含 Hint）+ 多源摘要卡 + 可点击洞察
  - 节奏：近 14 天热力 + 24h 分布（P0 的 ActionKey 导航可直接切页签）
  - 构成：分类占比条 + 应用变化 Top
- **复制摘要**：追加多源指标与分类构成，便于粘贴给 AI
- 单测：AnalyticsEngine 多源 KPI/AggregateDaily/空日汇总等用例

## [3.9.6] - 2026-09-18

### 优化：硬件监控卡片副指标视觉表现
- 问题：同一硬件卡片内，使用率有色条，温度/显存/磁盘读写/网速等副指标几乎只有文字，阈值色不直观
- CPU：使用率大字+条；温度独立色条（0–100°C）
- GPU：使用率 / 温度 / 显存 三行各自「标签 + 阈值色数值 + 细进度条」
- 内存：占用百分比条 + 容量文案
- 磁盘：活动率 + 读取/写入速度条（约 50/150 MB/s 黄/红阈值，满刻度随峰值自适应）
- 网络：下行/上行速度条 + 会话流量/IP
- 无传感器读数时中性灰、进度条为空（不再假绿）
- ViewModel 补齐副指标数值绑定（CpuTemp/GpuTemp/Disk*Mbps/Net*Mbps）及计算属性通知

## [3.9.5] - 2026-09-18

### 优化：任务栏小窗字体改微软雅黑 + 网速加标签
- 任务栏小窗字体由 `Segoe UI`（中文靠 font linking 回退雅黑、字形不协调偏细丑）改为 **微软雅黑 `Microsoft YaHei UI`**，字号 12→13px、字重 400→600（SemiBold）：中英文统一样式、笔画饱满清晰，消除小字号发虚感；文本垂直基线随字号微调居中
- 网速项加"网速"标签：第二行由 `↓5K/s ↑1K/s` 改为 `网速 ↓5K/s ↑1K/s`，上下行数值合并为一项并保持绿色

## [3.9.4] - 2026-09-18

### 变更：任务栏小窗移到最左侧 + 删除托盘悬停提示、显示项独立自定义
- 任务栏小窗位置由"托盘图标左侧（任务栏右侧）"改为**固定最左侧**：与任务栏左边缘对齐（8px 边距），不再贴托盘区
  - 定位细节：小窗 SetParent 到 Shell_TrayWnd 后按 Z 序从底枚举排第一，说明会被任务栏 XAML 层（开始按钮等）盖住；`SetWindowPos` 改为 `hWndInsertAfter=HWND_TOP` 置顶，左侧显示正常
- **删除托盘悬停提示功能模块**（用户反馈不需要）：
  - 移除 `TrayService.UpdateDisplay` / `TrayDisplaySnapshot` / 悬停文本拼装（原 `BuildTrayDisplaySnapshot`/`BuildTrayTooltip`）
  - 移除设置项 `TrayTipEnabled`；设置页删除「托盘悬停提示」卡片
  - 托盘图标本体（悬停标语「PChabit - 电脑使用习惯追踪」+菜单+最小化到托盘+退出）保留
- 任务栏小窗显示项改由**用户勾选自定义**：`TrayTipShow*` → `TaskbarShow*`（CPU/内存/GPU/网速/磁盘/温度/今日使用统计，7 项生成类齐全），勾选随 5s 刷新实时作用于小窗；设置页勾选网格移入「任务栏显示」卡片并注明"左侧显示、随勾选更新"，每日目标输入框随卡片
- 设置持久化键随之改名（旧 `TrayTipShow*` 配置丢失回默认全开，可接受）
- 验证：全屏抓像素确认小窗在 X=8 左侧显示两行，中文（内存/显存/磁盘/今日）正常；关闭「温度」勾选后第二行 `CPU/GPU °C` 立即消失，重新开启即恢复

## [3.9.3] - 2026-09-18

### 新增：分析升级 P0 — 硬件分钟样本数据底座 + DailySummaries 扩展
- 新增 `HardwareSamples` 表与 `HardwareSample` 实体：1s 硬件采集按本地时间分钟桶聚合（均值/峰值，13 个传感器列 + `SensorFlags` 有效传感器位图），`Timestamp`（yyyy-MM-dd HH:mm）唯一索引；休眠/关机缺分钟无行、不补 0
- 新增 `HardwareSampleWriter`（单例，随硬件监控启停）：订阅 `HardwareMonitorService.ValuesUpdated` → 线程安全内存分钟桶（聚合逻辑在无 IO 的 `MinuteBucket`，可单测）→ Channel 串行消费 → 按 Timestamp 原子 Upsert；停止时提交残余分钟桶（普通事务，不做 WAL checkpoint）；单行写库失败不杀采集链路
- 迁移双路径同改（防新老库结构分叉）：EF 模型（EnsureCreated 覆盖全新库）+ `DatabaseInitializer` 手动建表脚本（老库升级），列名/类型/索引逐项对齐，脚本可重复执行
- `DailySummaries` 扩展 13 个可空列（`MetricsVersion` 口径版本标记）：`AggregateDailySummaryAsync` 聚合昨日分钟样本产出 CPU 日均/P95、GPU 日均、GPU 峰值温度、内存日均，并填 `WebMinutes`；新增最近 30 天幂等回填（升级前日期无样本时硬件列保持 null 仍标 v2，避免反复重扫）
- 修复自动清理硬编码 90 天：现读取设置 `DataRetentionDays`（钳制 1–3650），`HardwareSamples` 跟随该保留口径，`DailySummaries` 长期保留；顺带补齐 5 处 `ExecuteDeleteAsync()` 漏 await
- 洞察 ActionKey 真导航：分析页洞察卡片可点击（仅有 ActionKey 的卡片显示「查看相关分析 →」），compose/rhythm 经 ViewModel 事件切页签；P0 对应页签 P1 才建，暂时落总览并滚动+高亮 24h 分布/应用变化卡片；新增 `AnalyticsNavArgs(Pivot)` 支持外部 `NavigateTo("Analytics", args)` 带参跳入，P1 新增同 Tag 页签即自动生效
- 本阶段不改 v1 分析 KPI 口径；硬件样本自本版发布起开始累积（历史不可回填）

## [3.9.2] - 2026-09-18

### 修复：任务栏小窗文字全部乱码、肉眼几乎不可见（3.9.1 交接遗留问题）
- 根因：TaskbarWidget 的三个 GDI「W 后缀」函数 `TextOutW` / `GetTextExtentPoint32W` / `CreateFontW` 的 DllImport 未指定 `CharSet.Unicode`，.NET 默认按 ANSI（本机 GBK）封送字符串，而 W 版函数按 UTF-16 解读 → "CPU 10%" 被逐双字节拼成汉字乱码；字体名 "Segoe UI" 同样变乱码，GDI 回退为默认位图字体（部分符号显示 □）。窗口本身一直存在、可见、Z 序与位置均正确，乱码笔画混在托盘图标旁被误认为花屏
- 修复：三处 DllImport 补 `CharSet = CharSet.Unicode`；中文标签经 GDI font linking 正常显示雅黑
- 文字抗锯齿由 ClearType 改为灰度（ANTIALIASED_QUALITY）：色键透明表面上 ClearType 子像素边缘会产生彩色毛边，灰度边缘在 Mica 任意底色上均干净
- 定位手段：EnumChildWindows(Shell_TrayWnd) 枚举 + PrintWindow/屏幕抓像素 + 外部进程 SetLayeredWindowAttributes 切换 LWA_ALPHA 对照实验，无需用户介入即定位

## [3.9.1] - 2026-09-18

### 变更：任务栏显示改为 LiteMonitor 风格的两行实时文本（移除任务栏进度条）
- 任务栏右下角（托盘图标左侧）新增两行文本小窗，持续显示实时硬件信息（照抄 LiteMonitor 任务栏显示模式：无边框窗口 SetParent 挂载到任务栏 + 透明键 + GDI 绘制）
- 第一行：CPU / 内存 / GPU / 显存占用；第二行：上下行网速 / 磁盘活动 / CPU、GPU 温度 / 今日使用进度（已用/目标小时）
- 数值随系统深浅主题自动变色：负载 ≥70% 黄、≥90% 红（温度阈值 70/85°C），其余绿色
- 移除 3.9.0 引入的任务栏按钮进度条（用户反馈无实际作用）；托盘悬停提示保留并与此前一致
- 设置页「任务栏与托盘」卡片：任务栏显示开关 + 托盘悬停开关 + 信息项勾选（CPU/内存/GPU/网速/磁盘/温度/今日使用，同时作用于任务栏小窗与托盘提示）+ 每日目标小时数

## [3.9.0] - 2026-09-18

### 新增：任务栏进度条 + 可自定义托盘悬停信息
- 任务栏按钮底部进度条（ITaskbarList3）：默认以「今日使用进度」为主指标（今日活跃时长 ÷ 每日目标），可在设置页切换为 CPU 占用 / 内存占用 / GPU 占用 / 磁盘活动 / 下行网速 / CPU 温度；进度颜色：负载类 ≥70% 黄、≥90% 红，今日目标达成后转黄提示
- 托盘悬停提示动态化：CPU / 内存 / GPU 占用、上下行网速、磁盘活动、CPU/GPU 温度、今日使用统计（已用时长 / 每日目标 / 百分比），逐项可在设置页勾选开关；每 5 秒刷新，文本变化才更新（兼容 Windows 128 字符提示上限）
- 新增设置页「任务栏与托盘」卡片：进度条开关、主指标下拉、每日目标小时数（0.5~24）、悬停提示开关与 7 项信息勾选
- 今日活跃时长实时查询 AppSessions（DailySummary 仅聚合昨日），60 秒缓存减少 DB 开销



## [3.8.0] - 2026-09-18

### 新增：硬件监控模块（合并 LiteMonitor 核心）
- 新增 src/PChabit.HardwareMonitor 项目：移植 LiteMonitor（MIT，https://github.com/Diorser/LiteMonitor）硬件采集核心（LibreHardwareMonitor + 性能计数器），仅保留数据采集层，移除 UI/插件/驱动安装/流量历史等外围（见 NOTICE.md 差异说明）
- 新增「硬件监控」页面：CPU / GPU（负载·温度·显存）/ 内存 / 磁盘（活动率·读写速度）/ 网络（上下行速度·会话流量·IP）实时卡片，绿/黄/红三色阈值（负载 70%/90%，温度 70~75/85~90°C），每秒刷新
- 采集服务随应用启动/关闭（StartMonitoring / PerformShutdownAsync 步骤 2），DI 注册为单例
- 注意：传感器读数取决于主板/驱动支持；无管理员权限时部分温度读数可能不可用（显示 --）

## [3.7.5] - 2026-09-18

### 构建环境升级：.NET 9 → .NET 10
- 项目已全面升级 net10.0（global.json 要求 SDK 10.0.401，各 csproj TargetFramework 为 net10.0 / net10.0-windows10.0.22621.0）
- 本机构建环境同步升级：安装 .NET 10 SDK 10.0.401，卸载 .NET 9 SDK 9.0.315
- ⚠️ .NET 10 SDK 要求 MSBuild ≥ 18.0，VS 2022 BuildTools 的 MSBuild 17.14 无法解析 SDK 10（报错 "requires at least version 18.0.0 of MSBuild"），构建改用 `dotnet build`（SDK 自带 MSBuild 18）
- 发布目标：D:\Tool\PChabit（清空旧版本后全量复制 bin\x64\Release\net10.0-windows10.0.22621.0\win-x64）

## [3.7.4] - 2026-09-17

### 修复：分类管理页操作按钮文字被裁剪（"删除分类""添加程序"显示不完整）
- 根因：右侧映射区标题行（标题 + 编辑/删除按钮）在同一 Grid 单元格内左右并排，窗口较窄时按钮组溢出被右侧边缘裁剪（"删除分类"显示为"删除分"）
- 修复 1：标题行改为明确的 * + Auto 两列布局，按钮列固定 Auto 宽度永不被挤压，窗口变窄时标题列压缩并省略号截断
- 修复 2：窗口设置最小尺寸 1000x700，防止窗口被拖到过窄导致按钮/布局被裁剪

## [3.7.3] - 2026-09-17

### 修复：分类管理页右侧程序列表仍显示不全（3.7.1 修复未生效，重新修复）
- 3.7.1 移除了 AppStatsPage 中包裹 CategoryManagementTab 的外层 ScrollViewer，但实测右侧列表仍只显示前 12 项、无滚动条
- 真正根因：Pivot 内容区按内容高度测量，UserControl 默认不拉伸，内部 `*` 行 ListView 无受限高度 → 按内容高度渲染 → 超出部分被窗口裁剪且无滚动
- 修复：将「分类管理」页签内容用 Grid 包裹，高度绑定到 Pivot 的 ActualHeight，恢复 ListView 自身滚动（24 项程序可全部滚动查看）

## [3.7.2] - 2026-09-17

### 升级 WindowsAppSDK 2.2.0 → 2.5.1
- 尝试修复 D:\Tool\PChabit 部署位置启动崩溃（0xc000027b / Microsoft.UI.Xaml.dll）
- 升级后崩溃偏移变化（0x3ad79d → 0x3a9dbd），确认崩溃与应用代码/目录环境相关，非组件版本缺陷

## [3.7.1] - 2026-09-17

### 修复：分类管理页界面显示不全（底部被截断）
- 根因：AppStatsPage 的「分类管理」页签用 ScrollViewer 包裹 CategoryManagementTab，给内部两个 ListView 无限高度（禁用其自身滚动），内容超出窗口时底部条目被裁剪
- 修复：移除外层 ScrollViewer，左右两个 ListView 各自滚动，布局随窗口高度自适应

## [3.7.0] - 2026-09-17

### 「应用统计」页升级：真周期 + 环比 + 预聚合 + 视觉现代化
- **修复伪「本周」**：原按钮只把日期设为 7 天前那一天；现改为自然周窗口（周一起算，与分析页一致）
- **真时间范围**：今天 / 昨天 / 本周 / 上周 / 近 7 天 / 近 30 天 / 本月 / 自定义；周期与环比复用 AnalyticsEngine（新增 Today / Yesterday / Custom 枚举）
- **KPI 4 张**：总使用时长、活跃应用、Top 应用、专注段（≥25 分钟且属生产力分类），卡片化新样式
- **工具栏**：周期下拉 + 分类筛选 + 搜索（防抖）+ 刷新 + 复制 Markdown 摘要
- **排行行升级**：两行紧凑布局（图标/分类 Chip/时长/会话/均长/占比细条/环比/后台开关），按占比排序
- **计算层抽离**：新增 AppStatsEngine（纯函数可单测），ViewModel 只做绑定；分类映射、展示名解析、默认色板统一
- **预聚合 AppDailyStats**：EF 模型 + 手动迁移双注册；每日随 DailySummaries 聚合昨日；首次升级自动回填最近 30 天；查询异常回退实时；与 90 天清理联动
- **图表主题化**：piechart.html 修复写死白底（深色主题可读），WebView2 PreferredColorScheme 跟随主题；图表实例复用（不再每次 dispose 重建）
- **宿主页改版**：AppStatsPage 顶层 NavigationView 改为 Pivot 页签（应用排行 / 分类管理）
- **测试**：新增 AppStatsEngineTests 23 项（周期、环比、筛选、专注段、饼图、构成、小时分布、空态）
## [3.6.2] - 2026-09-16

### 浏览历史：与「网页访问」统一为实时记录
- 扩展 pageView / tabActivate / **navigation** 事件均写入 `BrowserHistoryItems`（与 WebSessions 同源）
- 改为**队列 + 每 15 秒批量入库**，替代每条页面一个 Task.Run
- 「同步历史」定位改为 **History API 补录 / 云合并**；实时记录不再依赖该按钮
- 数据管理页文案与按钮：启用实时浏览历史 / 刷新统计 / 补录近30天·近1年

## [3.6.1] - 2026-09-16

### 修复：键鼠计数虚高 / 不准
- 根因：周期落库把**整段累计值**反复 `+=` 到同小时行；同一批次两个快照会把同一段按键/点击算两次
- 改为**增量落库**：Flush 只提交「相对上次已写库的 delta」，Upsert 对增量行做 `+=`
- 整点切换仍写全量替换；每次 Upsert 后立即 `SaveChanges`，避免同 DbContext 批次跟踪冲突
- 键盘详情「平均点击间隔」按有数据小时数估算

## [3.6.0] - 2026-09-16

### 重做「分析」页：从周统计堆砌到复盘驾驶舱
- **时间范围**：本周 / 上周 / 近 7 天 / 近 30 天 / 本月，全局联动
- **KPI 环比**：活跃时长、专注段、生产力占比、夜间使用（▲▼%）
- **近 14 天日历热力** + **24 小时活跃分布**
- **应用变化 Top**：对比上一等长周期
- **洞察规则**：生产力环比、活跃骤降、夜间使用、专注段、超长连续、高效时段等；数据不足时提示
- **复制摘要**：一键复制 Markdown，可粘贴给 AI
- 计算抽离 `AnalyticsEngine`（可单测），新增 `AnalyticsEngineTests` 7 项
- 应用流向（Sankey）保留为第二页签

## [3.5.1] - 2026-09-16

### 优化：网页访问 — 右键设置网站分类
- 「网站访问排行」列表支持**右键**直接把域名指到任意分类（动态列出当前分类，当前项打 ✓）
- 支持「清除分类映射」；改完自动刷新统计，分类标签即时更新
- 不必再切换到「网站分类」页手动加映射

## [3.5.0] - 2026-09-16

### 新增：浏览器自定义识别标签（用户指定优先）
- 扩展消息统一附加 `isUserOverride` / `browserSource` / `autoDetected`
- 桌面解析优先级改为：**用户显式标签 > 进程识别 > 扩展自动检测 > Unknown**（`BrowserNameResolver` 纯函数，可单测）
- `history_export` 与书签链路对齐，同样走 Resolve（修复历史侧只信自报的问题）
- clientId → 显示名映射，改标签后替换旧 ready 键，避免双份
- 扩展 2.1.0：popup「清除自定义」、标签校验（≤20 字）、状态文案区分自定义/自动
- 单测 `BrowserNameResolverTests` 12 项

### 说明
- 3.4.x 中间版本含多轮加固（进程识别、删除传播、历史超时等），条目见各 3.3.x；本版将自定义标签能力正式对齐两端
- 旧扩展（2.0.x）连新桌面：无 override 字段时仍按进程优先，行为兼容

## [3.3.6] - 2026-09-16

### 修复：历史同步无限挂起导致界面一直「运行中」
- 根因：WebDAV HttpClient.Timeout 原为 30 分钟，且流式下载（ReadAsync 循环）完全不受该超时约束——服务器网络异常时同步可无限挂起，锁被占用，后续所有同步均显示「进行中」
- HttpClient.Timeout 收窄至 30 秒；下载/列表接口全面支持 CancellationToken（IWebDAVSyncService 接口同步更新）
- HistorySyncService.SyncAsync 增加整体 150 秒超时兜底（linked CancellationTokenSource），超时即中止并释放锁，界面恢复可用

## [3.3.5] - 2026-09-16

### 修复：历史入库主键冲突（Browser history sync 旧数据合并）
- 旧扩展按天分段导出时同一访问可能跨天重复（同 Url|VisitTime），同一批次 AddRange 因主键重复触发 EF Core 跟踪冲突，导致整个历史同步失败
- HistoryIngestService.IngestAsync 增加列表内去重：同 Id 合并 VisitCount 后入库

## [3.3.4] - 2026-09-16

### 修复：浮点时间戳解析（合并旧 Browser history sync 明文数据）
- Edge 扩展关闭加密后全量同步的 browser-history-total.json 时间戳为浮点毫秒（如 1763916397158.548），TryGetInt64 对非整数 Number 返回 false，导致时间戳全部解析为 0
- 历史解析（Handler + HistorySyncService 云端解析）增加浮点回退：Number 类型 TryGetInt64 失败后 TryGetDouble 转 long

## [3.3.3] - 2026-09-16

### 修复：浏览历史同步（合并自 Browser history sync 项目）
- 根因：历史导出入库时 GetInt64 直接解析 visitTime，豆包等国产 Chromium 的 history API 返回字符串时间戳，抛 FormatException 导致整批历史丢弃（BrowserHistoryItems 长期为 0）
- 桌面端历史解析改为容错：数字/字符串时间戳均可解析（Handler + HistorySyncService 云端解析双处修复）
- 扩展端导出规范化：Number() 转换 lastVisitTime / visitCount
- 云端历史合并增强：除 pchabit/browser-history-v1.json 与旧全量 browser-history-total.json 外，新增自动枚举读取 Browser history sync 增量文件 browser-history-increment-*.json（需明文；AES 加密文件需先在旧扩展关闭加密重新同步）
- 扩展 tai-browser-extension 升至 **2.0.2**

## [3.3.2] - 2026-09-16

### 修复：书签删除跨浏览器同步（A 浏览器删除 → 全端删除）
- 根因：同步只传播「云端相对基线的缺失」（跨设备删除）；浏览器本地删除（如 Edge 删书签）被多浏览器导出并集「补齐」，删除被忽略
- 新增每浏览器上次导出快照（BrowserSyncMetas 表 browser_last_exports），对比本次导出，缺失的 URL 书签判定为该浏览器删除，从云端快照剔除后经三路合并全局传播（写回所有浏览器 + 删本地库 + 云端剔除）
- 安全规则：文件夹永不删除、内部页 URL 跳过、首次同步（无快照）不判删除、本次离线浏览器不参与
- RequestExportAsync 改为返回按浏览器分组的导出（每浏览器内按 key 去重）
- 新增 9 项删除检测单元测试

## [3.3.1] - 2026-09-16

### 修复：浏览器精准识别（Chrome / Edge / 豆包浏览器）
- 根因：Chromium 系浏览器对 ws://localhost 走 IPv6 回环（[::1]），进程识别只查 IPv4 TCP 表导致反查不到进程；扩展自报兜底又硬编码 Chrome，豆包浏览器因此被误判为 Chrome
- 进程识别升级：同时查询 IPv4 + IPv6 TCP 表（GetExtendedTcpTable，AF_INET6）
- 识别证据升级为两级：可执行文件完整路径（安装目录品牌，如 \Doubao\、\Microsoft\Edge\、\Google\Chrome\）→ 进程名兜底；不依赖 UA / 内核版本号
- 扩展自报兜底改为中性的 Chromium，不再冒充 Chrome；browser_sync_ready / connection 消息附带 UA 供诊断
- 扩展 tai-browser-extension 升至 2.0.1

## [3.3.0] - 2026-02-12

### 新增：浏览器书签智能同步
- 从 Chrome/Edge 等浏览器扩展采集书签，经 WebSocket 推送到 PChabit
- SQLite 做唯一合并权威（`BrowserBookmarks` 表），绕开 `chrome.storage` 配额限制
- 三路合并算法（本机 × 云端 × 上次基线）：新增 / 删除 / 改名跨设备传播
- 首次同步只做并集，**不会传播删除**；文件夹永不自动删除
- 复用已有 WebDAV 配置，云文件 `pchabit/browser-bookmarks-v3.json`
- 扩展 `tai-browser-extension` 升至 **2.0.0**：新增 `bookmarks` / `history` 权限、`browser-sync.js` 导出/写回模块
- 数据管理页新增「浏览器书签智能同步」区块：同步书签 / 重置基线 / 开关 / 自动同步间隔
- 单元测试覆盖合并决策表（31 项）

### 技术
- 新表：`BrowserBookmarks`、`BookmarkSyncBaselines`、`BrowserSyncMetas`
- 新服务：`BookmarkSyncService`、`BrowserSyncWebSocketHandler`、`BrowserBookmarkRepository`
- 纯函数合并器 `BookmarkMergePlanner`（可单测）
- WebSocket 消息分流：`browser_sync_*` / `bookmarks_*` 走同步通道，不进活动统计

## [3.2.2] - 2026-09-16

### 应用统计
- 新增分类「AI 助手」「媒体创作」「云盘下载」「系统工具」
- 内置程序预设目录 `DefaultAppCatalog`：覆盖豆包、小米 MiMo、WorkBuddy、Trae、Cherry Studio、LM Studio、Copilot、ChatGPT、Claude、微信/QQ、WPS、剪映、百度网盘、迅雷、AdGuard、Logitech G HUB 等本机常见程序
- 启动时增量补齐缺失的系统分类与映射（不覆盖用户自定义）
- 实时解析优先命中预设目录，避免 AI 客户端被误归为「生产力/其他」
- 图标查找增强：Uninstall 注册表 DisplayIcon、含空格进程名、`D:\Tool` / `LocalAppData\Programs` 等安装路径扫描

## [3.2.1] - 2026-09-16

### 修复
- **开机自启动无效**：设置页开关只改了 ViewModel 内存值，从未调用 `SaveSetting` 落盘，因此不会写 Startup 快捷方式；现已在每次设置变更后保存
- 自启动快捷方式由旧名 `Tai.lnk` 改为 `PChabit.lnk`，并在每次应用设置时按当前 exe 路径重建（避免发布路径变更后失效）
- 应用启动 `Load()` 后会自动对齐自启动快捷方式

## [3.2.0] - 2026-09-16

### 升级
- **目标框架**：.NET 9 → **.NET 10**（`net10.0` / `net10.0-windows10.0.22621.0`）
- **SDK**：`global.json` 固定 `10.0.401`
- **依赖**：EF Core / Microsoft.Extensions.DependencyInjection / System.Drawing.Common → `10.0.0`

### 性能修复（鼠标周期卡顿）
- 低级输入钩子（`WH_MOUSE_LL` / `WH_KEYBOARD_LL` / WinEvent）迁移到专用消息泵线程 `InputHookThread`，与 UI 线程隔离
- 键盘钩子改用 `QueryFullProcessImageName` + 短 TTL 缓存，移除钩子路径上的 `Process.GetProcessById`
- 鼠标钩子移动包先 `TickCount` 节流再解包
- 键盘/鼠标统计改为内存累计、每 15s 落库，避免钩子路径高频写 SQLite
- SQLite 连接串：`Cache=Private`；WAL 仅通过 PRAGMA 设置（连接串不支持 `Journal Mode`）

### 修复
- 移除连接串中的 `Journal Mode` 关键字（会导致打开键鼠详情时未处理异常崩溃）
- `DbSafeViewModel.LoadDataAsync` 捕获加载异常，避免页面数据失败拖垮进程

### 设置页
- 「关于」区技术栈 / 版本 / 构建信息 / 运行时改为从程序集与运行时动态读取
- 新增「最新更新」摘要，实时读取 `CHANGELOG.md` 首个正式版本小节
- 进入设置页时刷新关于信息

## [3.1.10] - 2026-09-12

### 新增（网页访问统计增强 P0–P2）

#### 修复（P0 会话正确性）
- 扩展在 `tabClose`/`pageClose` 时携带最后已知 URL；主程序不再因无 URL 丢弃关闭事件
- 30s 周期保存改为同一 Session Id 的 upsert，不再切片新建行、不再重置 StartTime/计数
- content 修复 scroll direction 计算顺序；扩展增加离线队列（上限 200 条）

#### 功能（P1 活跃时长与分类物化）
- 新增 visibility / idle / heartbeat 事件，会话引擎按 ActiveDuration 累计有效浏览
- WebSession 增加 CategoryId / CategoryName / CategorySource / IdleDuration / IsLegacy
- 落库时物化网站分类；WebSocketPort 从设置注入，扩展 popup 可改端口

#### 功能（P2 分析与呈现）
- 时间线合并网页会话（按分类着色）；仪表盘读 DailySummary.WebPages
- 网页统计页使用有效浏览时长、有效浏览率；分类筛选走物化字段
- 每日聚合写入 WebPages / WebDuration / WebActiveDuration；导出 Top 站点按有效时长
- 搜索引擎白名单扩展；旧短切片会话标记 IsLegacy

### 改进（发布产物精简）
- 发布输出只保留简体中文与英语两套 WinUI 内置控件语言资源，不再输出 `en-GB`、`zh-TW` 等多余语言目录
  - 根因：`Microsoft.WindowsAppSDK.WinUI` 包在 `runtimes-framework\win-x64\native\<语言代码>\` 下携带 30+ 种语言的
    `Microsoft.ui.xaml.dll.mui` 与 `Microsoft.UI.Xaml.Phone.dll.mui`；自包含部署时由
    `Microsoft.WindowsAppSDK.SelfContained.targets` 全量 glob 复制，而 `<SatelliteResourceLanguages>`
    只过滤 NuGet 托管附属程序集，对这类原生 `.mui` 无效
  - 修复：`RemoveUnusedLanguageFolders*` 两个 Target 重写为「白名单 + 标记文件判定」——
    只有目录内存在 `Microsoft.ui.xaml.dll.mui` 时才视为语言目录，且仅当不在白名单内才删除
  - 新增属性 `KeepLanguageFolders`（默认 `zh-CN;en-us`），需要英式英语时改为 `zh-CN;en-us;en-GB`
- 安全性修复：旧实现按「目录名不以 `en-`/`zh-` 开头即删除」做反向过滤，
  会误删 `extensions` 等业务目录；新实现不再触碰任何非语言目录

### 修改文件
- `src/PChabit.App/PChabit.App.csproj`

## [3.1.3] - 2026-06-20

### 修复
- 构建系统：修复 XAML 编译器失败导致无法生成 `.g.i.cs` 文件的问题（需先构建依赖项目）

### 清理（代码质量）
- 删除冗余页面：
  - `MainPage.xaml` / `.xaml.cs`：测试页面，未使用
  - `CategoryManagementPage.xaml` / `.xaml.cs`：未注册到 NavigationService，功能已被 `CategoryManagementTab` 替代
  - `HeatmapTab.xaml` / `.xaml.cs`：未使用，`HeatmapPage` 已提供完整功能
  - `InsightsTab.xaml` / `.xaml.cs`：未使用，`InsightsPage` 已提供完整功能

## [3.0.1] - 2026-06-17

### 修复（性能）
- **周期卡顿问题**：修复程序每隔几分钟卡顿导致鼠标移动困难的问题
  - **根因 1**：Serilog 最低日志级别为 Debug，钩子回调（MouseMonitor/KeyboardMonitor）和事件处理器（DataCollectionService）中每次鼠标/键盘事件触发多次同步文件 I/O，当磁盘繁忙时阻塞钩子线程，导致全局输入消息排队
  - **修复 1**：最低日志级别从 `Debug` 提升到 `Information`，移除钩子回调和事件处理器中的 Debug 日志调用
  - **修复 2**：Serilog File Sink 改为异步写入（`WriteTo.Async()`），添加 `Serilog.Sinks.Async` 依赖
  - **根因 2**：`wal_autocheckpoint=200` 阈值过低（~800KB 即触发），频繁 checkpoint 产生密集磁盘 I/O，与同步日志写入叠加放大阻塞效应
  - **修复 3**：`wal_autocheckpoint` 从 200 提升到 10000（~40MB），大幅降低 checkpoint 频率

### 修改文件
- `src/PChabit.App/App.xaml.cs`：日志级别 Information + Async Sink
- `src/PChabit.App/PChabit.App.csproj`：添加 `Serilog.Sinks.Async` 依赖
- `src/PChabit.App/Services/ServiceConfiguration.cs`：提升 WAL checkpoint 阈值
- `src/PChabit.Infrastructure/Monitoring/MouseMonitor.cs`：移除钩子回调中的 Debug 日志
- `src/PChabit.Infrastructure/Monitoring/KeyboardMonitor.cs`：移除钩子回调中的 Debug 日志
- `src/PChabit.App/Services/DataCollectionService.cs`：移除事件处理器中的 Debug 日志

## [3.0.0] - 2026-06-17

### 🚨 技术栈全面升级
- **.NET SDK**: 8.0 → 9.0.315
- **Microsoft.WindowsAppSDK**: 1.4.231115000 → 2.2.1
- **EF Core**: 8.0.28 → 9.x
- **Csproj Version**: 2.x → 3.0.0
- **global.json**: 8.0 → 9.0.315

### 新增
- **DbSafeViewModel 基类**（`src/PChabit.App/ViewModels/DbSafeViewModel.cs`）：
  - 封装两阶段数据加载模式（Phase 1 线程池 DB 查询 + Phase 2 UI 线程 ObservableCollection 更新）
  - 8 个 ViewModel 迁移：Dashboard/KeyboardDetails/Timeline/Insights/AppStats/WebDetails/Heatmap/Sankey
- **DailySummary 实体**：预聚合日汇总数据，DbContext 已注册
- **SankeyViewModel 重构**：从单选日期改为日期范围（StartDate/EndDate, DateTimeOffset?），新增 TopN 属性

### 修复（关键）
- **陷阱 11：SQLitePCLRaw 原生库 `e_sqlite3.dll` 缺失**：
  - 根因：`dotnet publish` 未指定 `-r win-x64`，导致 NuGet 包中的原生运行时资源未被包含
  - 修复：在 csproj 中添加 `<RuntimeIdentifier>win-x64</RuntimeIdentifier>`
- **陷阱 12：DashboardViewModel 缺少 DailySummaries 表创建**：
  - 根因：`DatabaseInitializer.MigrateAnalysisTablesAsync()` 手动创建表时遗漏了 `DailySummaries` 表
  - 修复：在迁移方法中补建 DailySummaries 表，并对缓存查询加 try-catch 保护
  - 修复：WebDetailsViewModel 分类筛选改为通过 WebsiteDomainMappings 表查询域名模式
- **陷阱 13：SankeyView 数据丢失 — WebView2 初始化阻塞数据加载**：
  - 根因：数据加载仅在 `WebView2.NavigationCompleted` 事件中调用
  - 修复：数据加载与 WebView2 初始化并发进行
- **陷阱 14：退出程序时 SQLite WAL checkpoint 导致系统卡顿**：
  - 根因：`ServiceProvider.Dispose()` 触发 WAL checkpoint 产生大量磁盘 I/O
  - 修复：跳过 `ServiceProvider.Dispose()` 调用，直接 `ForceTerminate()`

### 修复（WMC9999 编译错误）
- **WMC9999 根因与修复**：
  - 根因链：SankeyView.xaml 中 XAML 绑定错误 → XAML 编译器尝试本地化错误消息 → 缺少中文卫星程序集资源 → 错误报告机制自身崩溃
  - 修复：为 SankeyViewModel 添加 StartDate（DateTimeOffset?）、EndDate（DateTimeOffset?）、TopN（int）属性

### 文档
- **AI_MAINTENANCE.md**：新增 14 条已知陷阱清单，供后续 AI 智能体维护时参考
- **MEMORY.md**：更新技术栈信息、14 条陷阱清单、两阶段数据加载架构、构建注意事项

### 教训
- **AI_MAINTENANCE.md 是项目唯一的跨智能体经验传递机制**，任何 AI 智能体在修改代码前必须完整阅读
- **14 条陷阱中超过半数是架构级认知陷阱**，无法通过静态分析发现，只能通过文档记录传递
- **WMC9999 本身不是根因**，而是错误报告链的崩溃，真正的 XAML 绑定错误被掩藏
- **退出路径属于热路径**，禁止昂贵操作（如 SQLite WAL checkpoint）

## [2.29.4] - 2026-06-16

### 修复（关键）
- **`.xbf` 缓存导致 `DataManagementPage` 启动崩溃**（`XamlParseException: Cannot create instance of type 'TextBox'`）:
  - 根因：MSBuild 增量构建**没有重新生成 `.xbf` 二进制文件**（`obj/.../Views/DataManagementPage.xbf` 时间戳是 1 天前的旧版本，与新 `.xaml` 源码不匹配）。`InitializeComponent()` 加载旧 `.xbf` 时反序列化失败
  - 修复：每次发版前**强制 `Rebuild`**（删除 `obj/` 和 `bin/`，执行 `MSBuild /t:Rebuild`）确保所有 `.xbf` 重新生成

## [2.29.3] - 2026-06-16

### 修复（关键）
- **`SettingsPage` 卡死（COMException 0x8001010E）**:
  - 根因（来自实际日志）：`SettingsViewModel.OnSelectedThemeKeyChanged` 直接同步调用 `ApplyTheme`，访问 `Window.Content`（COM 对象），从 Task.Run 线程触发崩溃
  - 根因（来自实际日志）：`OnTrackKeyboardChanged` / `OnTrackMouseChanged` / `OnTrackWebBrowsingChanged` 直接同步调用 `ApplyMonitorSettings`，在 Task.Run 线程安装 Win32 钩子（`SetWindowsHookEx` 需要消息循环）
  - 根因：`SettingsViewModel.InitializeAsync` 在 `LoadInBackgroundAsync` 包装下整体在 Task.Run 线程，但 `LoadUiFromSettings()` 同步触发上述 setter
  - 修复：
    1. `OnSelectedThemeKeyChanged` → `RunOnUIThread(() => ApplyTheme(value))`
    2. `OnTrackKeyboardChanged/Mouse/WebBrowsingChanged` → `RunOnUIThread(ApplyMonitorSettings)`
    3. `InitializeAsync` 中 `LoadUiFromSettings()` → `await RunOnUIThreadAsync(() => { LoadUiFromSettings(); return Task.CompletedTask; })`

## [2.29.2] - 2026-06-16

### 修复
- **页面卡死（陷阱 #5.5 / #10）**:
  - 修复 `AppStatsViewModel.OnSelectedDateChanged` 裸调用 `LoadDataAsync` —— 用 `LoadInBackgroundAsync` 包装，避免 UI 线程同步等待 DB 查询
  - 修复 `InsightsViewModel.LoadDataAsync` 中 `Insights` / `WeeklyScores` ObservableCollection 在线程池更新 —— 改用 `RunOnUIThreadAsync` 包装
  - 修复 `TimelineViewModel.LoadDataAsync` 中 `HourGroups` / `Activities` / `BarSegments` 集合在 Task.Run 线程更新 —— 先在线程池构造数据，再用 `RunOnUIThreadAsync` 调度到 UI 线程
  - 修复 `InsightsViewModel.RefreshAsync` / `PreviousDayAsync` / `NextDayAsync` 裸调用 `LoadDataAsync`
- **`ViewModelBase.RunOnUIThreadAsync` 死锁风险**:
  - 改用 `TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)` + `ConfigureAwait(false)`，避免在异常路径上死锁
  - 增加 `HasThreadAccess` 短路：UI 线程直接执行，避免不必要的 TryEnqueue
  - 增加 `TryEnqueue` 失败回退

## [2.26.0] - 2026-06-14

### 重构
- **数据管理界面重设计**: 解决三种数据出口语义混乱问题（云端 126M / 本地导出 1.56M / 本地备份 23M 大小不一致）
  - 取消「备份管理 / 数据导出」顶部 Tab 结构，改为单页垂直滚动的 5 个语义清晰卡片：
    1. **数据概览** — 一眼看清数据库大小、总记录数、数据范围、本地/云端备份状态、最近同步时间
    2. **本地数据库备份（灾难恢复）** — ZIP 压缩整个 .db 文件，保留 7 份，可一键还原
    3. **数据导出（分析 / 转移）** — 复用 IExportService 支持 4 种格式（json/markdown/csv/ai-prompt）
    4. **云端同步（异地容灾）** — 上传本地 ZIP 到 WebDAV，云端保留 5 份
    5. **操作日志** — 统一显示所有操作记录（备份/导出/同步/恢复/删除/清理）

### 改进
- **云端同步格式统一**: 不再上传 JSON（旧的 132-162M/文件），改为上传本地 ZIP（约 23M/文件，与本地备份同格式）
  - 旧 .json 文件继续支持列表展示和删除
  - 旧 .json 文件不再支持恢复（提示"已弃用"），避免不同格式数据冲突
- **云端自动清理**: 上传新备份后自动清理云端旧文件（默认保留最新 5 个，可配置 1-20）
- **数据概览**: 页面顶部新增 6 个统计卡片（数据库大小 / 总记录数 / 数据范围 / 本地备份数 / 云端备份数 / 最近云同步时间）
- **导出日期控件**: 替换 DatePicker 为 CalendarDatePicker，修复 DateTime ↔ DateTimeOffset 类型不匹配导致的 XAML 编译器静默失败
- **格式化显示**: BackupInfo 和 WebDAVFileInfo 新增 FormattedSize/FormattedModified/IsAutomaticText 计算属性，XAML 不再直接绑定原始 long/bool

### 删除
- `BackupTabContent.xaml/cs` 和 `ExportTabContent.xaml/cs`（合并到单页）
- 旧的 `SyncToWebDAVAsync` 中的 JSON 序列化逻辑（4 个 132-162M 的旧 .json 文件留在云端，可手动删除）

### 修复
- **XAML 编译器静默失败**: DatePicker.SelectedDate 绑定到 DateTime 时 XamlCompiler.exe 返回 exit 1 但无任何错误输出
  - 解决：改用 CalendarDatePicker（接受 DateTimeOffset?，可与 DateTime 互转）
- **类型不匹配**: `<Run Text="{Binding Size}" />` 中 Size 是 long，XAML 编译器无法处理 → 新增 FormattedSize/FormattedModified 字符串属性

## [2.25.2] - 2026-06-13


### 改进
- **图标系统重设计（方向 A · 显示器 + 进度环）**: 替换 LOGO 风格的简单显示器图标为"显示器 + 进度环"组合
  - 主色 `#1B3A6F`，运行态绿 `#22C55E`，暂停橙 `#F59E0B`
  - 替换 `src/PChabit.App/Assets/` 下所有 WinUI 资源（StoreLogo / Square44x44 / Square150x150 / LockScreenLogo / SplashScreen / Wide310x150 / Logo）
  - 替换 `extensions/tai-browser-extension/icons/` 下 PNG + SVG（废弃旧的紫渐变 T 字占位）
  - 新增 `scripts/icons-tray/tray-{running,paused,disabled}.ico` 多尺寸 ICO（16/24/32/48/64/128/256）
  - 提供 `scripts/generate_icons.py` 批量生成脚本，可重复运行
- **托盘动态进度环**: 托盘图标实时反映"今日已用时长 / 每日总目标"
  - 新增 `Services/IconRenderer.cs`：内存绘制 HICON（System.Drawing），无文件 IO
  - 新增 `Services/TrayProgressRefresher.cs`：60s 节流定时器，计算综合目标进度
  - 改造 `Services/TrayService.cs`：新增 `UpdateProgress(progress, status)` + `ForceRefresh()` + `NIM_MODIFY` 增量刷新
  - 改造 `App.xaml.cs`：托盘初始化后启动进度刷新器
  - 进度计算优先级：TotalTime 目标总和 → 各目标 DailyLimit 最小值 → 各目标 DailyTarget 最大值 → 无目标显示纯显示器
  - HICON 句柄管理：每次更新前 `DestroyIcon` 旧句柄，防止 GDI 泄漏
  - 节流策略：30s 内重复调用 + 进度变化 < 1% 跳过；状态切换不受节流限制

## [2.23.1] - 2026-06-13

### 修复
- **钩子安装在无消息循环的后台线程导致回调永不触发（根因修复）**: StartMonitoring() 在 Task.Run 中调用，导致 SetWindowsHookEx 在线程池线程上执行。Win32 低级钩子要求安装线程必须有消息循环，否则回调永远不会被调用
  - 改回仓库老代码的方式：在 UI 线程上通过 DispatcherQueue.TryEnqueue 同步启动监控器
  - StartMonitoring() 改为 _monitorManager.StartAllAsync().Wait() 同步等待，确保钩子在 UI 线程安装
- **钩子健康检查无自动恢复**: MonitorManager 的健康检查只记录警告不重启钩子，导致钩子被 Windows 静默卸载后永久失效
  - 添加自动恢复逻辑：连续3次检测到钩子5分钟无活动，自动 Stop+Start 重启钩子
  - 添加钩子未运行检测：IsRunning=false 但 MonitorManager 还在运行时自动重启
- **进程同步缺失**: 键鼠 Monitor 的 SetCurrentProcess 从未被调用，ActiveProcess 永远为 null
  - IAppMonitor 添加 GetCurrentProcess() 接口方法
  - IKeyboardMonitor/IMouseMonitor 添加 SetCurrentProcess() 接口方法
  - MonitorManager 添加 _processSyncTimer 每秒将 AppMonitor 的当前进程同步到键鼠 Monitor
  - AppMonitor 实现 GetCurrentProcess() 返回当前前台应用进程名
  - MouseMonitor 实现 SetCurrentProcess() 存储当前进程名

## [2.23.0] - 2026-06-13

### 修复
- **键鼠统计数据完全不写入数据库（根因修复）**: DataCollectionService 从 accumulator+FlushTimer 批量模式回退到 v2.15 的 EnqueueOperation 逐事件写入模式
  - 旧 accumulator 模式在5秒 flush 间隔内累积数据，但 flush 可能因异常/竞态静默丢失所有累积数据
  - 新模式通过 Channel 实现生产者-消费者模式，每批50个操作写入一次，保证数据不丢失
  - 删除了 KeyboardSessionAccumulator / MouseSessionAccumulator / FlushAccumulatorsAsync / _flushTimer 等遗留代码
- **EF Core SQLite DateTime 格式匹配**: 为 KeyboardSession/MouseSession/DailyPattern/EfficiencyScore/WorkPattern 添加 Date 值转换器，强制 `yyyy-MM-dd HH:mm:ss` 格式，防止 DateTimeKind.Local 参数带 `+08:00` 后缀导致查询失败
- **EF Core 并发查询**: DashboardViewModel / KeyboardDetailsViewModel 改用 IDbContextFactory，避免同一 DbContext 上的并行操作抛 InvalidOperationException
- **Win32 低级钩子异常防护**: KeyboardMonitor / MouseMonitor 的 HookCallback 添加 try-catch，防止未捕获异常导致 Windows 静默卸载钩子
- **钩子健康检查**: MonitorManager 添加60秒间隔健康检查，基于 LastActivityTime 检测钩子失效

## [2.22.5] - 2026-06-13

### 修复
- **键鼠钩子回调 GC 回收 / 重启失效**: Win32 低级钩子回调委托 `_proc` 在 Stop/Start 重启周期中可能被 GC 回收
  - 添加诊断日志 `[KB-Start]` 和 `[MS-Start]`，输出 ModuleHandle 和 HookHandle
  - 添加诊断日志 `[KB-Hook]` 每次按键时记录，验证回调是否真的被调用
  - 修复 KeyboardMonitor.Stop() 中错误的 `_idleCheckTimer.Dispose()` —— Dispose 后无法再 Start()
  - Stop() 改为只 Stop()，不 Dispose()，确保 Start() 时 timer 仍可用
- **SetCurrentProcess 从未被调用**: 键鼠 Monitor 永远拿不到当前激活进程
  - AppMonitor 添加 `GetCurrentProcess()` 公开方法
  - MonitorManager 添加 `_processSyncTimer` 每秒将 AppMonitor 的当前进程同步到 KeyboardMonitor/MouseMonitor
  - MouseMonitor 添加 `SetCurrentProcess()` 接口（占位实现）

## [2.22.4] - 2026-06-13

### 修复
- **EF Core 并发查询导致数据丢失**: DashboardViewModel 和 KeyboardDetailsViewModel 使用 Task.WhenAll 在同一个 DbContext 上并发查询
  - EF Core 不支持同一 DbContext 实例上的并发操作，会抛出 InvalidOperationException
  - DashboardViewModel: 改用 IDbContextFactory + 顺序 await
  - KeyboardDetailsViewModel: 为键盘和鼠标查询分别创建独立 DbContext
- **钩子健康检查无法检测"僵尸钩子"**: 钩子被 Windows 静默卸载后 IsRunning 仍为 true
  - 添加 LastActivityTime 属性到 IMonitor 接口和所有 Monitor 实现
  - 健康检查改为：5 分钟无活动视为钩子失效，Stop + Start 重启
  - 检查间隔从 30 秒调整为 60 秒，连续 3 次检测失败才重启

### 改进
- **添加查询诊断日志**: DashboardViewModel 和 KeyboardDetailsViewModel 在数据加载后记录会话数量和统计值
  - 便于排查"数据在库但页面显示为空"的问题

## [2.22.3] - 2026-06-13

### 修复
- **键鼠统计查询返回空结果（根本原因）**: EF Core SQLite 的 DateTime 参数格式与数据库存储格式不匹配
  - 数据库存储 Date 为 `2026-06-13 00:00:00`（无时区后缀）
  - EF Core 对 DateTimeKind.Local 的 DateTime 参数添加 `+08:00` 时区后缀
  - 导致 `s.Date == today` 和 `s.Date >= today` 的 SQL 字符串比较全部失败
  - **修复方案**: 为 Date 属性添加值转换器，强制使用 `yyyy-MM-dd HH:mm:ss` 格式（无时区）
  - 影响范围：KeyboardSession、MouseSession、DailyPattern、EfficiencyScore、WorkPattern
- **15+ 个文件中的 `s.Date ==` 和 `s.StartTime.Date ==` 查询全部改为范围查询**
  - `s.Date == today` → `s.Date >= today && s.Date < tomorrow`
  - `s.StartTime.Date == date.Date` → `s.StartTime >= date.Date && s.StartTime < date.Date.AddDays(1)`
  - EF Core SQLite 不支持 DateTime.Date 属性翻译，范围查询更健壮
- 涉及文件：DashboardViewModel、KeyboardDetailsViewModel、MouseDetailsViewModel、DetailDialogViewModel、
  KeyboardSessionRepository、MouseSessionRepository、DailyPatternRepository、PatternAnalyzer、
  InsightService、EfficiencyCalculator、GoalService、BehaviorAnalyzer、SessionAggregator、
  PatternDetector、DailyAggregator

## [2.22.2] - 2026-06-13

### 修复
- **键鼠统计数据中断**: Win32 低级钩子回调缺少异常保护，异常导致系统静默卸载钩子，键鼠数据停止采集
  - KeyboardMonitor/MouseMonitor HookCallback 添加 try-catch 保护
  - 添加 MonitorManager 钩子健康检查（每30秒检测，连续3次失效自动恢复）
- **FlushAccumulatorsAsync 并发重入**: 定时器回调可能在上一次未完成时再次触发，添加 _isFlushing 互斥标志
- **Infrastructure 连接字符串**: 修复 ServiceCollectionExtensions 中 SQLite 不支持的 Pooling/Max Pool Size 参数

### 改进
- **打字速度统计激活**: AverageTypingSpeed/PeakTypingSpeed 原为死代码，从未计算
  - 在 OnKeyboardDataCollected 中集成打字突发检测（2秒无按键视为突发结束）
  - 累加器新增 TypingBursts/PeakTypingSpeed/AverageTypingSpeed 字段
  - Flush 时正确合并打字速度数据到 KeyboardSession

## [2.22.1] - 2026-06-13

### 修复
- **SettingsPage 卡死**: App.xaml 精简时误删 Gray50/Gray700 资源，导致设置页面 XAML 运行时解析失败卡死
  - Gray50 → CardBackgroundFillColorDefaultBrush (ThemeResource)
  - Gray700 → TextFillColorSecondaryBrush (ThemeResource)
  - 硬编码 #D1FAE5 → SuccessSoftBrush
  - 硬编码 #EDE9FE → InsightSoftBrush
  - 硬编码 #DBEAFE → PrimarySoftBrush

## [2.22.0] - 2026-06-13

### 新增
- **深色模式支持**: 全应用支持浅色/深色/系统默认三主题切换
  - 设计系统全面升级为 WinUI 3 ThemeResource 体系
  - 卡片背景/边框/文本色自动适配深浅主题
  - 软色图标背景（蓝/绿/黄/紫）深浅主题自动调暗

### 改进
- **设计系统 v2.22**: 从硬编码色彩迁移到 WinUI ThemeResource 体系
  - 卡片样式 CardStyle 使用 CardBackgroundFillColorDefaultBrush
  - 页面背景使用 SolidBackgroundFillColorBaseBrush
  - 新增 SectionTitleStyle、PageSubtitleStyle 统一样式
- **UI 统一化**: 所有页面卡片统一使用 CardStyle
  - DashboardPage/GoalsPage/SettingsPage/AnalyticsPage 消除硬编码背景色
  - 统计卡片图标背景改用 SuccessSoft/WarningSoft/InsightSoft 画刷

## [2.21.3] - 2026-06-13

### 改进
- **分析页面统一重构**: 取消「分析」导航的子页面展开结构，将周统计、热力图、智能洞察、应用流向合并为单一页面的顶部标签页
- 新建 HeatmapTab、InsightsTab UserControl，保持原有功能逻辑不变
- 新建 AnalyticsPage 顶部 NavigationView 4 标签页布局（周统计/热力图/智能洞察/应用流向）
- ShellPage 简化「分析」为直接导航项，移除三名子项

## [2.21.2] - 2026-06-13

### 修复
- **启动卡顿深度优化**: 将托盘初始化、监控启动、备份服务从 `OnLaunched` 移到 `Window.Activated` 一次性事件中，100ms 延迟后异步初始化，确保窗口 UI 先渲染
- **仪表盘延迟加载**: `OnNavigatedTo` 中 DB 查询改用 `DispatcherQueuePriority.Low` 延迟执行，先渲染 UI 框架再加载数据
- **更新日志卡死修复**: 从硬编码超长字符串改为异步读取 CHANGELOG.md + 分段 TextBlock 渲染，消除 WinUI 3 单 TextBlock Wrap 布局计算卡死

### 改进
- CHANGELOG.md 纳入 csproj Content 项，自动复制到输出目录
- 更新日志对话框添加 ProgressRing 加载动画和防重复点击保护

## [2.21.1] - 2026-06-13

### 设置页面升级
- **分组卡片设计**: 每个设置分组带有彩色图标头（蓝/绿/紫）
  - 基本设置：蓝色 (#1E40AF) + 齿轮图标
  - 监控设置：绿色 (#10B981) + 键盘图标
  - 外观设置：紫色 (#8B5CF6) + 调色板图标
  - 关于：蓝色 + 信息图标
- **设置项容器化**: Toggle 开关放入浅灰背景容器内 (#F8FAFC)，更易识别分组
- **监控设置两列布局**: 监控间隔和空闲阈值并排显示
- **外观两列布局**: 主题和语言并排显示
- **数据采集分组**: 三个 Toggle 开关放入浅灰容器中

### 关于卡片重设计
- **应用信息头部**: LOGO 占位 + 应用名 + 副标题 + 版本徽章
- **版本徽章**: 胶囊式蓝色背景徽章显示当前版本
- **发布日期**: 顶部显示更新时间
- **信息行**: 版本/项目地址/技术栈 三行 grid 布局
- **使用说明卡片**: 蓝色提示框样式 (#DBEAFE) + 信息图标
- **自动从程序集读取版本号**: LoadVersionInfo 从 AssemblyVersion 读取

### 目标管理页面升级
- **页面标题区**: 标题 + 副标题 + 右上角"添加目标"按钮
- **目标卡片**: 圆形图标 + 名称 + 标签徽章 + 详情 + Toggle + 删除按钮
- **目标类型徽章**: 紫色背景胶囊显示类型
- **每日限制显示**: 时钟图标 + "每日限制" + 黄色高亮数值
- **空状态重设计**: 圆形背景图标 + 标题 + 详细说明
- **添加目标覆盖层**: 半透明黑色背景 + 白色对话框
- **添加对话框**: 彩色图标头 + 表单字段 + 双按钮布局

## [2.21.0] - 2026-06-13

### 视觉设计
- **品牌色重构**: 从紫色 (#512BD4) 改为蓝色系 (#1E40AF)，与 LOGO 蓝色品牌色一致
- **设计系统升级**: 在 App.xaml 中建立完整的 PChabit 设计系统 v2.21.0
  - 新增色彩系统：Primary (#1E40AF)、Primary Light (#3B82F6)、Accent (#60A5FA)、Soft (#DBEAFE)
  - 新增语义色：Success (#10B981)、Warning (#F59E0B)、Danger (#EF4444)、Insight (#8B5CF6)
  - 新增中性色系统：Gray50/100/200/500/700/900
  - 圆角规范：small 4px、medium 8px、card 12px、pill 20px
- **页面背景**: 新增 PageBackgroundBrush (#F8FAFC) 浅灰背景，提升层次感

### 仪表盘优化
- **统计卡片彩色图标**: 4 张统计卡片各配有语义化彩色图标背景
  - 时间卡片：浅蓝背景 + 蓝色时钟图标
  - 键盘卡片：浅绿背景 + 绿色键盘图标
  - 鼠标卡片：浅黄背景 + 橙色鼠标图标
  - 网页卡片：浅紫背景 + 紫色网页图标
- **卡片内边距**: 统一调整为 20px，符合设计规范
- **应用排行列表**: 保持原有的图标 + 名称 + 时长布局
- **网站访问**: 统一使用 Insight 色 (#8B5CF6) 圆角图标 + 域名首字母

### 导航优化
- **ShellPage 头部**: 添加 PChabit LOGO + 应用名称 + 版本号
- **状态栏样式**: 暂停按钮使用 Warning 语义色 (#F59E0B)

## [2.20.1] - 2026-06-13

### 问题修复
- **页面不随窗口大小拉伸**: 修复应用统计和网页访问页面不随窗口大小变化填充的问题
  - ContentControl 添加 HorizontalContentAlignment="Stretch" 和 VerticalContentAlignment="Stretch"
  - 所有子 UserControl 添加 HorizontalAlignment="Stretch" 和 VerticalAlignment="Stretch"
- **启动卡顿优化**: 消除程序启动时的卡顿
  - 数据库初始化改为纯异步执行（移除 GetAwaiter().GetResult() 同步阻塞）
  - 监控器启动从 StartAllAsync().Wait() 改为 Task.Run 异步启动
- **关闭卡顿优化**: 消除程序关闭时的卡顿
  - DataCollectionService.Stop() 中 FlushAccumulators 从同步阻塞改为带超时的异步执行
  - 处理任务和后台定时器等待超时从 2 秒缩短为 1 秒
  - 关闭流程移除不必要的 Task.Delay(100)，窗口关闭等待从 500ms 缩短为 200ms
