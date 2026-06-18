# PChabit 项目长期记忆

## 🚨 项目核心规则（必须遵守）

### 1. 发布流程
- **每次重新编译后**，必须使用 `dotnet publish` 发布到 `E:\SYNC\My VS\PChabit\publish`
- **发布前先清理旧的发布目录**：删除 `E:\SYNC\My VS\PChabit\publish` 下的所有内容
- **发布命令**：`dotnet publish "src/PChabit.App/PChabit.App.csproj" -c Release -r win-x64 --self-contained -p:Platform=x64 -o "E:\SYNC\My VS\PChabit\publish"`
- **不要使用 `-o bin\publish` 等临时目录**，永远输出到项目根的 `publish/` 文件夹

### 2. 版本号与更新日志
- **每次代码更新都必须修改版本号**：编辑 `src/PChabit.App/PChabit.App.csproj` 中的 `<Version>` 标签
- **每次代码更新都必须在 CHANGELOG.md 顶部追加更新日志**（如果 CHANGELOG.md 不存在则创建）
- **版本号规则**：遵循 SemVer（MAJOR.MINOR.PATCH），例如 `2.30.0`
- **更新日志格式**：
  ```
  ## [版本号] - YYYY-MM-DD
  ### 新增
  - 功能描述
  ### 改进
  - 改进描述
  ### 修复
  - 修复描述
  ```

### 3. 上述规则不需用户再次提醒
- 每次完成代码修改 → 自动执行：清理 + 发布 + 更新版本号 + 更新 CHANGELOG
- 不要等用户说"发布"或"更新版本号"才执行

### 4. 设置-关于页面同步
- 每次版本号更新时，**必须同步更新 `SettingsPage.xaml.cs` 中 `OnViewChangelogClick` 方法的更新日志内容**
- 更新日志内容应包含本次版本的完整变更说明（与 CHANGELOG.md 顶部条目保持一致）
- 关于卡片显示的版本号会自动从 AssemblyVersion 读取，无需手动更新

## 项目技术栈（2026-06-17 更新）
- **运行时**：.NET 9.0 (net9.0-windows10.0.22621.0)
- **UI 框架**：WinUI 3 (Windows App SDK 2.2.1)
- **架构模式**：Clean Architecture (Core → Infrastructure → Application → App)
- **MVVM 框架**：CommunityToolkit.Mvvm 8.4.2 (源生成器)
- **ORM**：EF Core 9.x + SQLite (WAL 模式)
- **日志**：Serilog → `%LocalAppData%\PChabit\Logs\`
- **目标平台**：win-x64，非打包 (Unpackaged) 自包含部署
- **C# 语言**：latest，Nullable 启用，ImplicitUsings 启用
- **关键依赖版本**：
  - `Microsoft.WindowsAppSDK`：2.2.1（不可随意升级）
  - `CommunityToolkit.Mvvm`：8.*（当前锁定 8.4.2）
  - `Microsoft.EntityFrameworkCore.Sqlite`：9.*
  - `Serilog`：3.*
  - `EPPlus`：7.7.0

## 🔴 已知陷阱清单（共 14 条，违反必出 BUG）
> **强制规则**：修改任何代码前，必须读完所有陷阱条目。不要以为"这个问题很简单，不需要读文档"。

### 陷阱 1：PublishReadyToRun 导致启动崩溃
- **禁止**在 `.csproj` 中设置 `<PublishReadyToRun>true</PublishReadyToRun>`
- ReadyToRun AOT 与 Windows App SDK 2.2.1 存在 WinRT 互操作兼容性缺陷
- 修复：在 csproj 中强制设为 `<PublishReadyToRun>false</PublishReadyToRun>`

### 陷阱 2：ContentDialog.XamlRoot 未设置
- **禁止**通过 DI 创建 `ContentDialog` 后不设置 `XamlRoot` 就直接 `ShowAsync()`
- 修复：在 `ShowAsync()` 之前显式赋值 `dialog.XamlRoot = XamlRoot;`

### 陷阱 3：DI 注册遗漏
- **禁止**在 `ServiceConfiguration.cs` 中遗漏 ViewModel 或 Dialog 的 DI 注册
- 检查清单：ViewModel 已 `AddTransient<T>()` 注册 + Dialog 已 `AddTransient<T>()` 注册

### 陷阱 4：UI 线程同步阻塞
- **禁止**在 UI 线程上同步调用 async 方法（`.Wait()` / `.Result`）
- 修复：改为 `await _monitorManager.StartAllAsync();`

### 陷阱 5：SQLite WAL 锁冲突
- **禁止**在 UI 线程同步执行数据库查询
- 修复原则：UI 层永远异步读取数据库 + ContentDialog 使用 `Closing` 事件替代 `PrimaryButtonClick`

### 陷阱 5.5：await 实际同步阻塞 UI 线程（看似异步实则同步）
- **根因**：WinUI 3 默认安装 `SynchronizationContext`，`await` 会捕获当前上下文并在后续代码上回到 UI 线程
- **修复**：使用 `LoadInBackgroundAsync` 包装，或 ViewModel 内部使用 `Task.Run` 强制丢到线程池

### 陷阱 5.6：ObservableCollection 跨线程 COMException
- **症状**：`COMException (0x8001010E, RPC_E_WRONG_THREAD)`
- **根因**：`LoadInBackgroundAsync` 内部 `Task.Run` 把整个 `LoadDataAsync` 丢到线程池，但某些实现在线程池上操作 `ObservableCollection`
- **修复**：所有 `ObservableCollection` 的 `.Clear()` / `.Add()` / `.Remove()` 调用必须通过 `RunOnUIThreadAsync` 调度回 UI 线程

### 陷阱 6：跨天数据泄露
- **禁止**在数据统计字典中使用单维度键（如只用 `Hour` 不用 `Date+Hour`）
- 修复：使用 `(Date, Hour)` 组合键，每天独立

### 陷阱 7：双层 Task.Run 破坏 SynchronizationContext
- **禁止**在 ViewModel 内部已用 `Task.Run` 管理线程的 Page 上再套 `LoadInBackgroundAsync`
- 修复：移除 Page 层的 `LoadInBackgroundAsync` 包装，改为直接 `await ViewModel.LoadDataAsync();`

### 陷阱 8：WinRT DependencyObject 非 UI 线程创建
- **禁止**在 Phase 1（线程池）数据模型类中使用 WinRT `DependencyObject` 类型（如 `SolidColorBrush`）作为属性默认值
- 修复：移除默认值，改为可空属性，延迟到 UI 线程赋值

### 陷阱 10：WMC9999 编译错误
- **症状**：在 zh-CN 系统上使用 WinAppSDK 2.x 编译时，XAML 绑定错误会触发 WMC9999 吞没真正错误
- **根因**：XAML 编译器在检测到绑定错误后，调用 `ResourceManager.GetString()` 尝试本地化错误消息，但缺少中文卫星程序集资源
- **诊断方法**：output.json 中 WMC9999 条目的前一个事件（perfXC_PageCodeGenStart）指示出问题的 XAML 文件名
- **修复**：修复根因的 XAML 绑定错误，不要试图通过创建卫星程序集绕过

### 陷阱 11：e_sqlite3.dll 缺失
- **症状**：程序可以启动，但所有页面数据为空，键鼠详情页点击后卡死
- **根因**：`dotnet publish` 未指定 `-r win-x64`，导致 NuGet 包中的原生运行时资源未被包含
- **修复**：在 csproj 中添加 `<RuntimeIdentifier>win-x64</RuntimeIdentifier>`

### 陷阱 12：DailySummaries 表未创建
- **症状**：仪表盘数据全部为空，日志出现 `SqliteException: no such table: DailySummaries`
- **根因**：`DatabaseInitializer.MigrateAnalysisTablesAsync()` 手动创建表时遗漏了 `DailySummaries` 表
- **修复**：在迁移方法中补建 DailySummaries 表，并对缓存查询加 try-catch 保护

### 陷阱 13：WebView2 阻塞数据加载
- **禁止**数据加载依赖 UI 渲染组件的生命周期事件
- **修复**：数据加载与 UI 渲染应该是两条并行管道，数据立即加载，WebView2 就绪后推送渲染

### 陷阱 14：退出时 WAL checkpoint 卡顿
- **症状**：退出程序时卡顿数秒，期间鼠标无法移动
- **根因**：`ServiceProvider.Dispose()` 会递归释放所有单例服务，SQLite WAL checkpoint 产生大量磁盘 I/O
- **修复**：跳过 `_serviceProvider?.Dispose()` 调用，直接 `ForceTerminate()`

## 架构模式：两阶段数据加载（强制使用）
> **适用场景**：任何需要在后台加载数据并更新多个 `ObservableCollection` 的 ViewModel。
> **强制使用**：当 ViewModel 同时满足以下两条时必须采用此架构：
> 1. 有数据库查询（EF Core / SQLite）
> 2. 有 `ObservableCollection<T>` 需要更新

### 架构设计
```
Phase 1: await Task.Run(() => LoadStatsAsync(date))
  └─ 线程池：EF Core 查询 + LINQ 分组/排序/聚合 + 存入纯 POCO 中间对象

Phase 2: await RunOnUIThreadAsync(() => { ... })
  └─ UI 线程：ObservableCollection.Clear() / Add() + WinRT 对象创建
```

### Page 层调用规范
```csharp
// ✅ 正确：直接 await ViewModel.LoadDataAsync()
// ViewModel 内部已通过 Task.Run + RunOnUIThreadAsync 管理线程
protected override async void OnNavigatedTo(NavigationEventArgs e)
{
    await ViewModel.LoadDataAsync();
}

// ❌ 错误：再套一层 LoadInBackgroundAsync → 双层 Task.Run 陷阱
protected override async void OnNavigatedTo(NavigationEventArgs e)
{
    await ViewModel.LoadInBackgroundAsync(async () =>
    {
        await ViewModel.LoadDataAsync();
    });
}
```

### DbSafeViewModel 基类
- 封装两阶段数据加载模式
- 已迁移 8 个 ViewModel：Dashboard/KeyboardDetails/Timeline/Insights/AppStats/WebDetails/Heatmap/Sankey

## WinUI 3 发布注意事项
- `dotnet publish` 不会自动复制 XAML 资源文件（.xbf, .pri, Views/, Assets/）到发布目录
- 发布后必须对比 `bin/Release/.../win-x64` 和 publish 目录，手动补全缺失文件
- csproj 中已添加 `CopyWinUIAssetsToPublish` Target 自动补全（但需验证是否生效）
- 不要使用 `TrimMode=partial`，会导致 WinUI 资源被裁剪

## SQLite 重要注意事项
- **SQLite (Microsoft.Data.Sqlite) 不支持连接池关键字**：`Pooling=true`、`Max Pool Size=N` 会导致 `ArgumentException`
- 使用 `Cache=Shared` 即可实现多连接共享同一个数据库文件
- 连接字符串有效格式：`Data Source=path;Cache=Shared;Mode=ReadWriteCreate;`

## EF Core SQLite DateTime 格式问题 🔴 极重要
- **EF Core 对 DateTimeKind.Local 的 DateTime 参数会附加时区后缀**（如 `+08:00`）
- 数据库中存储的 DateTime 如果没有时区后缀，精确匹配 `==` 和范围比较 `>=` 都会失败
- **解决方案**：在 DbContext 中为 Date 属性添加值转换器，强制使用 `yyyy-MM-dd HH:mm:ss` 格式
  ```csharp
  entity.Property(e => e.Date).HasConversion(
      v => v.ToString("yyyy-MM-dd HH:mm:ss"),
      v => DateTime.SpecifyKind(DateTime.Parse(v), DateTimeKind.Unspecified));
  ```
- **所有 Date 比较必须用范围查询**：`s.Date >= today && s.Date < tomorrow`
- **禁止使用**：`s.Date == date`（精确匹配会因格式差异失败）
- **禁止使用**：`s.StartTime.Date == date.Date`（EF Core SQLite 不支持 DateTime.Date 翻译）

## EF Core 并发查询注意事项 🔴 极重要
- **EF Core 不支持同一 DbContext 实例上的并发操作**，会抛 InvalidOperationException
- **禁止使用** Task.WhenAll 在同一个 DbContext 上并行执行多个 ToListAsync()
- **正确做法**：
  - 顺序 await（简单但慢）
  - 或使用 IDbContextFactory 为每个查询创建独立 DbContext（可并行）

## 构建注意事项（2026-06-17 更新）
- **global.json SDK 版本必须匹配已安装的 .NET SDK**：当前要求的是 9.0.315
- 如果 global.json 要求不存在的 SDK 版本，dotnet build 会直接报错
- XamlCompiler.exe 静默失败（exit code 1，无输出）时，首先检查 global.json 版本
- **清理 obj/bin 后重新构建**可解决 XamlCompiler 缓存问题

## XAML 编译器静默失败 🔴 极重要
XamlCompiler.exe 失败时**不会输出任何错误信息**，只返回 exit 1。已知的触发条件：
1. **DatePicker.SelectedDate 绑定 DateTime 类型**（期望 DateTimeOffset?）— 改用 `CalendarDatePicker`
2. **`<Run Text="{Binding LongProperty}" />`** — long/bool/int? 不能直接给 string — 加 FormattedXxx 计算属性
3. **代码-behind 引用 XAML 中尚未声明的 x:Name 字段** — 必须保证代码和 XAML 同步
4. **Run + 父 TextBlock.Text 同时存在** — XAML 解析歧义
5. **XAML 绑定错误在 zh-CN 系统上触发 WMC9999** — 见陷阱 10

**诊断步骤**：
1. 先用 `minimal XAML` 替换测试，定位问题段
2. 逐步添加 XAML 节点 + 同步添加代码-behind
3. 如果是 C# 错误先于 XAML 错误，会被 XamlCompiler 屏蔽（exit 1 静默失败）

**XAML 数据类型最佳实践**：
- long/bool/DateTime 字段在 XAML 中显示时，**一律**用 `FormattedXxx` 字符串属性桥接
- DateTime 字段需要绑定到 CalendarDatePicker 时，提供 `DateTimeOffset?` 桥接属性
- 永远不要 `<Run Text="{Binding LongXxx}" />`

## 数据管理架构 (v2.26.0)
- **三种数据出口语义分离**：
  - 本地备份（灾难恢复）— ZIP 压缩 .db，保留 7 份
  - 数据导出（分析/转移）— IExportService，4 种格式
  - 云端同步（异地容灾）— 上传本地 ZIP 到 WebDAV，保留 5 份
- 旧 WebDAV .json 上传已弃用（v2.22.x 时代实现，文件 132-162M/份）
- DataManagementPage 单页垂直布局，无 Tab
- WebDAV 同步后自动清理云端旧文件（保留最新 5 份）

## 性能优化记录 (2026-06-13)
- DataCollectionService 批量处理（50操作/Scope）
- DailyAggregator N+1 消除
- AppIconService LockBits 替代 GetPixel
- PatternDetector 合并查询
- Repository Take(50000) 限制
- SQLite PRAGMA cache_size/temp_store 优化

## 日常维护注意事项
1. **NuGet 版本**：`Microsoft.WindowsAppSDK` 和 `CommunityToolkit.Mvvm` 使用浮动版本（`*`），升级前需验证兼容性
2. **SQLite WAL**：数据库路径硬编码在 `App.xaml.cs` 中，默认 `%LocalAppData%\PChabit\pchabit.db`
3. **Serilog 配置**：日志按天滚动，7 天保留，路径 `%LocalAppData%\PChabit\Logs\app-.log`
4. **WinUI unpkg 部署**：`dotnet publish` 后可能需要手动复制 XBF/PRI 文件（csproj 中有 `CopyWinUIAssetsToPublish` Target 处理此问题）
5. **DI 容器**：`ServiceConfiguration.ConfigureServices()` 是唯一注册入口，不要在其他地方分散注册
6. **Entity DateTime Kind**：项目中使用 `DateTime.SpecifyKind(DateTime.Parse(v), DateTimeKind.Unspecified)` 值转换器，新 Entity 的 `DateTime` 属性需注意 Kind 一致性
7. **编译后 DLL 时间戳检查**：当怀疑修改未生效时，确认编译产物已更新：`(Get-Item "E:\SYNC\My VS\PChabit\publish\PChabit.dll").LastWriteTime`
