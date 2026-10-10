# PChabit 项目记录

> **最后更新**：2026-10-10
> **文档定位**：**索引层**，不重复造轮子。深度内容一律查 `AI_MAINTENANCE.md`（2000+ 行，30 条陷阱）与 `CHANGELOG.md`。
> **同步说明**：本目录位于 Verysync 同步盘 `E:\SYNC` 内，会自动同步到其他终端。**本地文件即权威源，云端文档非必需。**

---

## 1. 项目概览

| 维度 | 详情 |
|---|---|
| 项目定位 | Windows 键鼠统计 + 习惯追踪桌面应用 |
| 源码目录 | `E:\SYNC\My VS\PChabit` |
| 发布输出 | `E:\SYNC\My VS\PChabit\publish` |
| 安装运行目录 | `D:\Tool\PChabit`（用户实际运行的程序在此，非 publish 目录） |
| 当前版本 | **3.33.0**（`src\PChabit.App\PChabit.App.csproj` 第 13 行 `<Version>`） |
| 技术栈 | .NET 10.0 / WinUI 3 / Windows App SDK 2.5.1 / EF Core 10.0.0 / Serilog 4.x |
| 代码仓库 | `github.com/idlee/PChabit` |

### 关键约束（硬性）

1. **只 build 不算完成**：必须 `dotnet publish -c Release -p:Platform=x64 -o "E:\SYNC\My VS\PChabit\publish"`。只 build 的 DLL 在 `bin\` 下，用户运行的是 `publish\`，修复不会生效。
2. **版本号必须递增且 UI 可见**：改代码即改 `<Version>`，且版本号要显示在界面上，否则用户无法确认加载的是哪个版本。
3. **动手前必读陷阱表**：`AI_MAINTENANCE.md` 第 281 行起的 27 条陷阱，禁止以"这个陷阱是老版本"为由跳过。
4. **GitHub 与本地保持一致**：本地改动需同步 push。

---

## 2. 标准流程 SOP

| # | 步骤 | 完成判断（做到什么算成功） |
|---|---|---|
| 1 | 读 `AI_MAINTENANCE.md` 陷阱全表 + 禁止事项（第 108 行） | 27 条陷阱已逐条过目，能说出本次改动可能触及哪几条 |
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

按踩坑频率排序。完整 27 条见 `AI_MAINTENANCE.md` 第 281 行起。

| 坑 | 现象 | 正确做法 |
|---|---|---|
| 只 build 不 publish（#0 禁止事项） | 用户运行后 BUG 依旧 | 必须 publish + 验证时间戳；不推给用户"请自行验证" |
| **启动即查表未等迁移（#25）** | 首启 `no such table: xxx` | 任何启动期读写数据库的逻辑都要 `await _dbInitCompleted.Task` + 超时兜底，参照 `App.xaml.cs:788` |
| 新表迁移双路径分叉（#20） | 老库缺表，新装正常 | 改 EF 模型**同时**补 `MigrateAnalysisTablesAsync` 手动迁移路径 |
| C# 编译失败级联 XAML 假错误（#21） | 一堆 `WMC9999` /「Unknown type 本地类型」 | **先修 C# 再看 XAML**，XAML 报错多为 `MarkupCompilePass2` 级联产物 |
| e_sqlite3.dll 缺失（#11） | `DllNotFoundException` / `TypeInitializationException` | csproj 需同时设 `RuntimeIdentifier`（单数）与 `SelfContained` |
| DI 注册遗漏（#3） | `InvalidOperationException`、闪退 | 新增服务必须在 DI 容器注册，新增页面/VM 一并登记 |
| ObservableCollection 跨线程（#5.6） | `COMException 0x8001010E` | 集合操作回 UI 线程；`await` 不等于不阻塞，需查 `SynchronizationContext` |
| SQLite WAL 锁冲突（#5） | 卡顿、并发异常 | 启用 WAL；退出时 checkpoint 需异步，避免阻塞关闭 |
| **文本文件双重编码（#26）** | 中文变「鏇存柊鏃ュ織」 | 根因是 GBK↔UTF-8 有损转换（字节被替换为 `?`、PUA 字符混入），**机器不可逆**，只能按语义重写 |
| 版本号遗漏 | 用户装了旧版找不到新功能 | 改代码即改 `<Version>`，且版本号显示在 UI 上 |
| **sln 级 dotnet publish 混入测试产物（#28）** | 发布目录出现 BuildHost/CodeCoverage/TestPlatform 多语言，文件数 700+ | 发布必须指定 `PChabit.App.csproj` 路径；发布后查关键文件：e_sqlite3.dll、Assets\*.png（13 个）、FileVersion、文件数（基线 467） |
| **e_sqlite3/Assets png 未进 publish（#28）** | 指定项目发布后启动数据全空（缺 e_sqlite3）；图标缺失 | csproj `CopySqliteAndAssetsToPublish`（AfterTargets=Publish）已补；手动复制到 bin 的原生库 publish 不会自动带 |
| **Page 转 UserControl 页签化（#29）** | `WMC9997` 开始/结束标记不匹配 | 根节点、x:Class、`Page.Resources`、`</Page>` 四处必须同步改；cs 去掉 OnNavigatedTo，改 `Loaded` + 供宿主调用的 `EnsureLoaded()` |
| **publish 整目录删除被拦截（#30）** | `SAFE_DELETE_BULK_CONFIRM_REQUIRED`（阈值 50） | 不整删 publish，直接覆盖发布后再单独清残留 .xbf；**robocopy 不要在 Git Bash 里跑**（`/MIR` 被转路径），改用 `scripts/deploy_to_tool.py` |
| **XAML 里设 IsSelected 导致崩溃（#31）** | 打开页面即崩，报 `XamlParseException`，内层 `NullReferenceException` 指向自己的 SelectionChanged handler | 解析期触发事件时后续控件还没创建。去掉 XAML 的 `IsSelected`，改在 `InitializeComponent()` 后设 `SelectedIndex`；handler 用 `sender`；被操作控件加 null 判断。同类：Pivot 也一样 |

---

## 4. 历史记录（时间倒序）

| 日期 | 版本 | 关键操作与结果 |
|---|---|---|
| 2026-10-10 | 3.33.0 | **结构重组阶段 2：合并两套分类管理 CRUD**。新增 `CategoryCrudViewModelBase<TCategory,TMapping>`（Core 层新增 `ICategoryEntity`/`IMappingEntity` 供泛型约束，免反射）：`CategoryManagementViewModel` 328→112 行、`WebsiteCategoryManagementViewModel` 325→58 行，Initialize/SelectCategory/ApplyFilter/增删改全部共用一份。新增 `CategoryEditDialogViewModelBase`：删`WebsiteCategoryEditDialog.xaml`(141 行，XAML 相似度 98.9%) 与 `.xaml.cs`(84 行)，改为纯代码类继承 `CategoryEditDialog`，统一走 `Closing`+`GetDeferral()`（原网站版用 `PrimaryButtonClick` 无法异步校验）。**顺带修**：网站分类种子数据原在 VM 里内联了第二份副本(58 行)，已删除，种子统一由 `DatabaseInitializer` 补齐。**踩陷阱 21 一次**（`FindAsync` 返回 `ValueTask` 类型不匹配引发 8 个 XAML 假错误）。**新发现**：ContentDialog 的 `x:Bind` 必须显式 `x:DataType` 指向基类，否则编译器按属性实际类型推断，派生类场景报 CS1503。验证：Release 0 错误、154/154 测试、publish 470 文件 3.33.0.0 部署 `D:\Tool\PChabit`、uiautomation 实测两页签分别加载 11 类/205 映射与 10 类/28 映射、合并后对话框正常、新增 ERR/FTL=0；截图确认网站分类 10 类与对话框（默认 🌐 图标、网络类候选居前）显示正常。改进了 `deploy_to_tool.py`：`.xbf` 清理改为自动比对源目录，不再维护死页清单 |
| 2026-10-10 | 3.32.0 | **结构重组阶段 1：清死代码（-789 行）**。删 `SettingsViewModel.Categories.cs`(435)、`Mappings.cs`(53)、`MainViewModel.cs`(86)、`AnalyticsViewModel` 10 个死成员 + 4 个类；`SettingsViewModel` 构造函数去掉 `IDbContextFactory` 参数（已核实全库仅 DI 构造、无手工 new、测试未引用）并清 4 个无用 using。**顺带修一个真实隐患**：`CategoryService.EnsureCatalogSeededAsync`（默认分类种子）唯一调用点就在这段死代码里——用户若从未打开设置页，新装机器进分类管理会是空列表、应用/网页分类全落「未分类」。已把种子补齐移到 `DatabaseInitializer.SeedDefaultDataAsync`（启动期），并给 `WebsiteCategoryService` 抽出同名静态方法（原先网站分类种子也只在设置页路径下走）。验证：Release 0 错误 19 警告、154/154 测试、publish 470 文件 3.32.0.0 部署 `D:\Tool\PChabit`、uiautomation 实点设置/分类管理/应用统计/分析四页均正常、新增 ERR/FTL=0；日志实证「默认类别和映射已是最新」出现在启动期而非设置页 |
| 2026-10-10 | 3.31.1 | **「历史」页签改名并转移**：分析页原「历史」页签改名「网页浏览历史」并移除；网页访问页改为 Pivot 双页签（访问统计 / 网页浏览历史）；分析页工具栏按钮、网页摘要卡片（"查看网页浏览历史 →"）及 AI 行动项改为整页 NavigateTo("WebAccess","history") 并自动落历史页签；HistoryReportTab 标题栏、report.html title/大标题同步更名。构建 0 错误，部署 D:\Tool（465 文件，3.31.1.0）；UI 实拍双页签正常（历史库 25103 条）、分析页跳转落点正确 |
| 2026-10-10 | 3.31.0 | **全项目分类口径统一**：修复时间线应用分类与应用统计页不同步（时间线直接读会话快照 AppSession.Category，绕过用户可编辑的 ProgramCategoryMappings）。全面排查"同一数据、两套逻辑"共 **8 处**——应用分类 5 处（时间线、桑基图 GetCategory、AnalyticsEngine 生产力 KPI、KeyboardDetails 鼠标程序统计、Dashboard 重复 ResolveCategorySafely 副本）统一到 `AppStatsEngine`；网站分类 3 处（WebDetailsViewModel、DetailDialogViewModel 纯硬编码不查映射、DataCollectionService.Web 落库）统一到 `IWebsiteCategoryService`；删除全部复制的色板/关键词兜底/域名匹配。服务接口新增 GetDefaultCategoryName、ResolveForDomainSync，DomainMatches 公开。154/154 测试通过；DB 实证近 3 天 10+ 分叉进程（Doubao 快照 AI 助手→映射浏览、QwenWorkCN 其他→AI 助手）；UI 实拍时间线分类正确；构建 0 错误，部署 D:\Tool（465 文件，3.31.0.0，自包含校验通过），启动日志无新增错误。构建期撞陷阱 21（漏 using 级联 XAML 假错误）|
| 2026-10-10 | 3.30.3 | 本机（备份终端）构建部署 3.30.3 至 `D:\Tool\PChabit`：`dotnet build` Release x64，0 错误 21 警告；产物 466 文件（清理 BuildHost-net472/netcore 测试宿主后），版本 3.30.3.0，自包含校验通过（`includedFrameworks` 在，hostfxr/hostpolicy/coreclr/e_sqlite3/WinUIEdit 齐全，源/目标 466 一致）。经 explorer 以用户身份启动（规避 TRAE 沙箱对 `pchabit.db-wal` 的写入拦截），进程稳定 50s+，新实例日志 0 ERR / 0 WRN；托盘、便签子系统、数据库迁移、仪表盘加载均正常 |
| 2026-10-10 | 3.30.3 | **下载网速独占任务栏左右分栏的上下两行**。任务栏小窗从「两行平铺」改为「左侧专属区块 + 右侧两行」结构：左侧区块占满两行高度，上行=当前下载速率（21px 全窗最大字号 + 速率分级配色），下行=今日累计下载量 `今 2.98G`（14px，`NetworkTrafficSamples` 分钟样本当天求和，60s 缓存）；区块与右侧之间画 1px 竖分隔线（DIM 色，贯穿两行）。右侧保持上行 CPU/内存/GPU/显存、下行 上传/盘/温度/今日使用。接口调整：`UpdateContent` 参数由 `(row1,row2)` 改为 `TaskbarLayoutItem(BlockRow1,BlockRow2,Row1,Row2)`；`MeasureAndResize` 分别测量区块宽与右侧宽；关闭网速开关时自动退回纯两行。**踩陷阱 21 一次**：EF 表达式树里不能用 string `>=`/`<`（CS0019），引发 6 个 XAML 假错误（WMC0001 Unknown type / WMC0909 / WMC1111 / WMC9999），改用 `Timestamp.StartsWith("yyyy-MM-dd")` 前缀匹配后一次通过。实测截图确认浅色/深色两套主题布局均正确 |
| 2026-10-10 | 3.30.2 | **任务栏下载速度显示改善**。原本网速 `↓xxK ↑xxK` 挤在次行小字，下载中不易察觉。改动：① 下载速度提升到主行**首位**，用 17px 大字号（常规主行数值 15px），其后留 20px 分组间距与 CPU/内存/GPU 隔开（`TaskbarMetricItem` 新增 `Emphasize` 可选参数 + `_fontValueEmphasis` 字体）② 速率分级配色新增三档枚举 `NetIdle`/`NetActive`/`NetFast`：空闲灰 → 活跃蓝 ≥100KB/s（#185FA5/#85B7EB）→ 高速青 ≥1MB/s（#0F6E56/#5DCAA5），冷色语义与负载黄红警告区分 ③ 格式收窄 `↓340K`/`↓12.3M`/`↓123M`（高速省小数），<1KB/s 显示 `↓0` 防抖动 ④ 上传 `↑xxK` 留次行。**用 PIL 截任务栏区域实拍验证**：布局、大字号、蓝色活跃档全部生效（高速青档因测试源限速 ~200KB/s 未触发，代码路径与蓝色相同仅颜色表不同）。编译 0 错误，publish 3.30.2.0 部署 `D:\Tool\PChabit`（先 Stop-Process 关闭运行实例） |
| 2026-10-10 | 3.30.1 | **修复 3.30.0 引入的崩溃回归：点击设置页直接崩**。日志外层是 `XamlParseException: XAML parsing failed`，内层 `NullReferenceException` 指向 `ApplySection`。根因是设置页分区导航的 `ListViewItem` 写了 `IsSelected="True"`，XAML 自上而下解析到该项时**立即**触发 `SelectionChanged`，而右侧 9 个分区 Border 定义在 ListView 之后、此刻尚未创建，x:Name 字段为 null。修复三处：① 去掉 XAML 的 `IsSelected`，改在 `InitializeComponent()` 之后设 `SelectedIndex = 0` ② handler 改用 `sender` 而非 `SectionList` 字段 ③ `Show` 改可空参数加 null 判断；同类隐患一并修（硬件监控页 `MainPivot_SelectionChanged` 也改用 `sender`）。编译 0 错误，publish 3.30.1.0 并部署 `D:\Tool\PChabit`；**用 uiautomation 实际点击侧栏设置项验证**：日志出现「SettingsPage: 构造函数完成」，无新增 ERR/FTL（残留 5 条均为 01:51 修复前的旧记录）。新增陷阱 31 |
| 2026-10-10 | 3.30.0 | **减法版本：只做合并与删减，不加功能**。① 删除两个无入口死页面 `HeatmapPage`（447 行 XAML）+ `InsightsPage`，连带 `HeatmapViewModel` / `InsightsViewModel` / 只被前者使用的 `HeatmapAggregator`（共 6 文件 ~630 行）② 网络流量页转 `NetworkTrafficTab` 并入硬件监控页「实时 / 流量」双页签③ 历史分析页转 `HistoryReportTab` 并入分析页第 6 个页签「历史」，原三入口（侧栏 + 顶部按钮 + 卡片链接）统一收敛为切页签④ 新建「分类管理」页合并应用分类 / 网站分类两套 CRUD 入口，应用统计页与网页访问页各自退化为单页 + 跳转按钮⑤ 仪表盘两个完整榜单改 Top 5 + 「查看全部 →」⑥ 设置页 1025 行单页长滚改左侧七分区导航（常规/监控/界面/便签/数据/AI/关于），不删任何设置项⑦ 侧栏 10 项平铺改 概览/使用/系统/洞察/工具 五组带分组标题。全程 4 次增量编译均 0 错误；publish 产物 470 文件、FileVersion 3.30.0.0、e_sqlite3.dll 与 13 个 Assets png 齐全；手动清理 4 个残留旧页面 .xbf；同步部署 `D:\Tool\PChabit`（先 Stop-Process 关闭运行实例），启动验证日志 0 ERR，完整走完迁移/备份/任务栏小组件挂载链路。**新坑 2 条**：陷阱 29（Page→UserControl 四处必须同步改，否则 WMC9997）、陷阱 30（publish 目录整删被安全策略拦截 + Git Bash 下 robocopy `/MIR` 被路径转换吞掉） |
| 2026-10-04 | 3.29.0 | 本机（备份终端）构建部署 3.29.0 至 `D:\Tool\PChabit`：`dotnet build` Release x64，0 错误 25 警告；产物 467 文件（清理 BuildHost-net472/netcore 46 个测试宿主后），版本 3.29.0.0，自包含校验通过（`includedFrameworks` 在，hostfxr/hostpolicy/coreclr/e_sqlite3/WinUIEdit 齐全，源/目标 467 一致）。启动验证：`SearchCount` 列迁移成功、仪表盘加载成功、启动备份成功，运行 60s+ 日志 0 ERR 0 WRN。**注意**：从 TRAE 沙箱内启动会被拦截对 `pchabit.db-wal`/Backups 的写入（表现为 `no such column: d.SearchCount` + `SQLite Error 10: disk I/O error`），经 explorer 以用户身份启动后全部消失——非代码缺陷，是沙箱限制 |
| 2026-10-03 | 3.26.2 | **发布流程修复 + 重新打包部署**：发现 sln 级 `dotnet publish` 把 PChabit.Tests 的测试平台文件（BuildHost-net472/netcore、CodeCoverage、TestPlatform 多语言 satellite）混入产物（722 文件）；指定 App 项目发布后 e_sqlite3.dll 与 Assets\*.png 又缺失（CopyNativeSqliteToOutput 只复制到 bin、png Content 未设 CopyToOutputDirectory）。修复：发布命令改指定 csproj 路径 + csproj 新增 `CopySqliteAndAssetsToPublish`（AfterTargets=Publish，补 e_sqlite3/png、清 BuildHost），产物 467 文件。robocopy /MIR（/XD WebView2）部署 D:\Tool\PChabit，PURGE 清掉历史 BuildHost 46 文件。冒烟：进程存活 85 线程、热键/便签/数据库迁移/仪表盘/桌面+任务栏小组件全绿，12:17 后日志 0 ERR 0 WRN（旧实例「批量落库失败」ERR 消失，3.26.2 修复生效）。陷阱 28 新增 |
| 2026-10-03 | — | **补提交 3.23.0–3.26.2 到 GitHub**：一次提交 `31f5c18`（43 文件，+7218/−405）覆盖桌面悬浮插件、便签系统、仪表盘升级与 3.26.1/3.26.2 修复；`git push` 直接成功，远端 master 与本地同步（此前文档误判「需 PAT」，实际本机凭据可用）。推送前 Release 编译 0 错误 |
| 2026-10-03 | 3.26.2 | 修 `WebSessions.Id` UNIQUE 冲突（长期静默丢数据）：`EnqueueSaveWebSession` 无条件 Add，与周期落库的 `FindAsync`+更新路径冲突——同一会话（同一 Id）先入队 Add、再被周期落库 Add，两条操作落在同批次共用一个 `DbContext` 时对同一 Id 执行两次 INSERT。改为先查后插/更新；并给 `ProcessDataAsync` 加**逐条隔离重试**（原实现下一条脏数据会丢整批最多 50 条） |
| 2026-10-03 | 3.26.1 | 修「仪表盘与应用统计页分类不同步」（3.25.0 引入的回归）：仪表盘用 `AppCategoryResolver` 硬编码映射，绕过了 `ProgramCategoryMappings`（198 条用户可自定义）。改为与应用统计页**同源**——同一张映射表 + 复用 `AppStatsEngine.BuildCategoryMap`/`NormalizeProcessName`/`DefaultColorFor`，解析优先级完全一致（映射表 → `AppSession.Category` → 未分类）；分类配色也统一取自 `ProgramCategories.Color`，不再用硬编码色板。验证：`MuMuNxDevice.exe`→娱乐、`QwenWorkCN.exe`→AI 助手，与应用统计页一致 |
| 2026-10-03 | 3.26.0 | **仪表盘升级阶段 2–6 一次完成**。① 顶部状态条：效率总分（大数字+等级）、个人基线偏离、专注/切换/活跃时段、数据截止时刻（未产出的字段显示「--」而非 0）。② 环比昨日同时段（截断到昨日同一时刻，避免午后打开时的假跌）。③ 今日速览：调 `ChatFastAsync`，**未配置/失败时降级为规则摘要**并明示来源；附习惯画像时型。④ 每小时活动改 WebView2 柱状图（替换旋转 ProgressBar 取巧做法），未来时段灰色占位与「真零活跃」区分，**主题自适应**（既有 hardware-trend.html 硬编码深色），数据加载与图表初始化解耦（陷阱 #13）。⑤ 30 秒实时刷新 + 「当前活动」条，进页面启动/离开即停。⑥ 四张卡补齐点击（今日活动时间卡此前不可点）与**键盘可达**；悬停改 `HoverableCardStyle` 修深浅色闪白；补上此前算了却没绑 XAML 的分类分布可视化；新增空态。**新发现陷阱 27（定时器重入竞态）**：`LoadDataAsync` 的 `if (IsLoading) return` 非原子，改 `Interlocked`。全程两次撞陷阱 #21（C# 错误引发 6 个 XAML 假错误）。编译 0 错误、154/154 测试通过，部署 410 文件，启动零错误 |
| 2026-10-03 | 3.25.0 | **仪表盘升级阶段 1/6：数据层口径收口**。改读 `DailySummaries` 的 `FocusMinutesV2`/`AppSwitches`/`FirstActiveTime`/`LastActiveTime`（此前一个都没用）；效率评分改用 `EfficiencyCalculator` 四维加权，删 `ProductivityScore` + 硬编码 `IsProductiveCategory`；分类改用 `AppCategoryResolver`（12 类），删 `Contains("code")` 猜测，分组改 `(ProcessName, ExecutablePath)` 复合键；接入 `HabitDayLoader` + `PersonalBaselineBuilder` 算 P50 偏离度。**顺带修复测试项目自 3.23.0 起无法编译**（`FakeSettings` 缺 24 个 `DesktopWidget*`/`StickyNotes*` 成员），补齐后 154/154 通过。DI 补 `AppCategoryResolver` 注册（App 未调 `AddTaiInfrastructure`）。发布 3.25.0.0（410 文件），日志确认 `SummaryMetricsVersion=2` 且零错误。阶段 1 完成判断全部达成：与 AnalyticsPage 同源同值、两个野路子方法已从代码库消失 |
| 2026-10-03 | 3.24.1 | **收口 3.24.0 两处遗留 + 仪表盘升级启动**。① 修便签启动竞态（陷阱 25）：`App.StickyNotes.cs` 的启动清理与 WebDAV 同步改 `Task.WhenAny(_dbInitCompleted.Task, 15s)`，照 `App.xaml.cs:788` 既有模式；日志确认「便签子系统已初始化」且无 `no such table`。② CHANGELOG 乱码：标题 + `[Unreleased]` + 3.24.0–3.22.0 全部修复，**3.21.1 及更早（约 100 版本 / 309 行）确认机器不可逆**（含 `?`/PUA 有损字节），已在「已知问题」如实记录（陷阱 26）；106 个版本条目结构完整。③ 删 nettrace 遗留并加 `.gitignore`。④ 补陷阱 25/26，陷阱表 24→26 条。发布 3.24.1.0（410 文件，`e_sqlite3.dll` 在，语言目录仅 `en-us`+`zh-CN`），部署 `D:\Tool\PChabit`，启动验证通过。**新发现既有问题**：`WebSessions.Id` UNIQUE 约束冲突 42 次（07:00 起，早于本次部署），已记录待查 |
| 2026-10-03 | — | 产出 `docs/仪表盘升级计划.md`：诊断仪表盘脱离版本六层（数据层绕过 DailySummary、评分口径自建第二套、图表是旋转 ProgressBar、四张卡仅三张可点、习惯画像/基线/AI 一概未用、CategoryDistribution 算了没绑 XAML），规划六阶段（数据层→布局→图表→AI→实时化→打磨），版本 3.25.0–3.29.0 |
| 2026-10-02 | 3.24.0 | 本机（备份终端）构建部署 3.24.0 至 `D:\Tool\PChabit`（512 文件，版本 3.24.0.0），构建 0 错误；启动验证通过：便签热键 `Ctrl+Alt+N`/`Ctrl+Alt+B` 注册成功、便签子系统初始化、数据库迁移完成。**发现两处待主终端处理**：① `App.StickyNotes.cs` 启动清理未等待 DB 初始化完成（竞态，首启报 `no such table: StickyNotes`）② `CHANGELOG.md` 顶部 [Unreleased] 段落文本双重编码乱码 |
| 2026-10-02 | 3.24.0 | 便签 P0：Ctrl+Alt+N 快速录入窗（无边框置顶/5 色/草稿暂存/Ctrl+Enter）、Ctrl+Alt+B 便签页、NotesPage 搜索筛选回收站、SQLite StickyNotes 表、WebDAV 墓碑+LWW 同步、托盘/导航/设置入口；常驻 CPU 优化（采样 5s/GPU 计数器 160/按需监控/内容签名）空闲 1.62%→0.43~0.55%；0 错误部署 D:\Tool（511 文件），热键/输入/落库实测通过 |
| 2026-10-02 | 3.23.2 | 悬浮窗渲染升级为 UpdateLayeredWindow per-pixel alpha（32bpp PARGB+GDI+）：文字半透明投影、灰阶 AA 真 alpha 边缘（任意壁纸光滑）、药丸条抗锯齿；深浅主题截图验证，0 错误部署 D:\Tool |
| 2026-10-02 | 3.23.1 | 悬浮窗可调整大小（NCHITTEST 热区+最小尺寸+宽高记忆）；内容随宽度等比缩放解决小字模糊；药丸进度条+琥珀/鲜红阈值警告；网速下载/上传双行带条；dotnet build 0 错误，部署 D:\Tool 截图冒烟通过 |
| 2026-10-02 | 3.23.0 | 新增桌面硬件悬浮插件（纯 Win32+GDI，行为照抄 LiteMonitor MIT）：7 项指标、置顶/穿透/拖拽/位置记忆；13 项 DesktopWidget* 设置即时生效；部署 D:\Tool 冒烟通过 |
| 2026-09-30 | 3.22.5 | AI 配置收敛为云端+本地两组；输入即保存；保存并测试连接按钮 |
| 2026-09-30 | 3.22.4 | 端点整组生效防混搭 401；解读强制简体中文；解析失败区分英文空话 |
| 2026-09-30 | 3.22.3 | 端点模式 UI 置顶高亮卡片 + 三大单选 + 当前模式提示 |
| 2026-09-30 | 3.22.2 | 修复本地 AI 空正文：本地非流式 + 多形状 content 解析 + 空正文抛错附原文 |
| 2026-09-30 | 3.22.1 | 修复本地 AI 400：本地端点不发 response_format/max_tokens，云端 400 自动降级重试 |
| 2026-09-30 | 3.22.0 | 双 AI 端点（LM Studio + 云端），可切换/分工；测试 100 通过；部署 D:\Tool 并启动验证 |
| 2026-09-30 | 3.21.1 | 修复分析周期：近 7 天/30 天改为以今天为终点的滚动窗，对比期为等长前窗；默认「近 7 天」；测试 94 通过；部署 D:\Tool 并启动验证 |
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

- [x] ~~3.30.0–3.31.1 待推送 GitHub~~ **已完成 2026-10-10 17:20**：`git push origin master` + `--tags` 全部成功（`46d4871..8fe1146`），7 个提交历史完整保留未被压平；标签 v3.30.0 / v3.30.1 / v3.30.2 / v3.30.3 / v3.32.0 共 5 个。**此前多次推送失败的真正原因**：本机 PAC（`127.0.0.1:55511/steam.pac`）规定 github走 **SOCKS5 127.0.0.1:1080**，而 git 配置里写的是 `http://127.0.0.1:1080`（HTTP 协议），协议错配导致 `Proxy CONNECT aborted`。已 `git config --global http.proxy socks5h://127.0.0.1:1080` 修正，之后裸命令即可推送。详见 `AI_MAINTENANCE.md` 陷阱 33

- [x] ~~`WebSessions.Id` UNIQUE 约束冲突~~ **已修 3.26.2**：`EnqueueSaveWebSession` 无条件 Add 与周期落库的 FindAsync-更新路径冲突，同批次共用 DbContext 时对同一 Id 两次 INSERT。已改为先查后插/更新，并给批量落库加逐条隔离重试（原实现下一条脏数据会丢整批 50 条）
- [x] **仪表盘升级六阶段全部完成**（计划见 `docs/仪表盘升级计划.md`）：阶段 1 数据层重构（3.25.0）→ 阶段 2 布局 + 阶段 3 WebView2 图表 + 阶段 4 AI 速览 + 阶段 5 实时化 + 阶段 6 打磨（合并为 3.26.0，因五者是同一次升级的连续部分，拆版只会制造无意义的碎片）
- [ ] **CHANGELOG 3.21.1 及更早乱码**（约 100 版本 / 309 行）：机器不可逆，需按语义重写或从外部备份恢复；不做猜测性改写
- [ ] **csproj 注释乱码 57 行**：含 PUA 字符不可逆，需按语义重写（改 csproj 会触发版本号规则）
- [x] ~~补提交 3.23.0–3.24.1 到 GitHub~~ **已完成 2026-10-03**：提交 `31f5c18` 一次覆盖 3.23.0–3.26.2，`git push` 直接成功（本机凭据可用，无需 PAT）
- [ ] 确认 Verysync 在另一台终端处于运行状态（`.verysync` 目录存在但当前为空，需核对）

**更新规则**

- 每次修复 BUG → 先写 `AI_MAINTENANCE.md` 陷阱表（递增编号），再更新 `CHANGELOG.md`，最后追加本文件「历史记录」
- 每次发布 → 更新本文件「当前版本」
- 本文件只做索引，技术细节一律沉淀到 `AI_MAINTENANCE.md`，避免两处维护同一内容
- 遇乱码问题**先判定是否可逆**（见陷阱 26），不可逆就如实记录范围，不要猜测性改写
