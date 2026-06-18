---
AIGC:
    Label: "1"
    ContentProducer: 001191440300708461136T1XGW3
    ProduceID: 42dfa2fcd38ff5bf05e1580992a0bf7f_728f7264687911f18805525400d9a7a1
    ReservedCode1: GtWFhqBZOzvJilkC+Ys3ZICfAq4dVrfziQL+UPi+EwRmck2aGE5Wv6LXdxpjlI3YCEYVUPcnGj2yx7/7DWCY3lY/d1D2Ivkgvjbwx6/Lz8rPKcU8HXlZ918TK1HQNMxdbRPlmKH+xjThdHr7CqNNKsp57OT9gpDAs3VOyaOEtCfvDopmoQQiCQArFhE=
    ContentPropagator: 001191440300708461136T1XGW3
    PropagateID: 42dfa2fcd38ff5bf05e1580992a0bf7f_728f7264687911f18805525400d9a7a1
    ReservedCode2: GtWFhqBZOzvJilkC+Ys3ZICfAq4dVrfziQL+UPi+EwRmck2aGE5Wv6LXdxpjlI3YCEYVUPcnGj2yx7/7DWCY3lY/d1D2Ivkgvjbwx6/Lz8rPKcU8HXlZ918TK1HQNMxdbRPlmKH+xjThdHr7CqNNKsp57OT9gpDAs3VOyaOEtCfvDopmoQQiCQArFhE=
---





# AI 维护文档 - PChabit

> **最后更新**: 2026-06-17
> **文档目的**: 供后续 AI 智能体维护本项目时参考，避免重复踩坑。

---

## ⚠️ AI 智能体必读声明（最高优先级）

**本文件是项目唯一的跨智能体经验传递机制。任何 AI 智能体在修改本项目代码之前，必须完整阅读本文档的陷阱章节（共 17 条陷阱），否则必然重复踩坑。**

### 强制规则

1. **动手前先读文档**：修改任何 `.cs` / `.xaml` / `.csproj` 前，先读完所有陷阱条目的症状+根因+修复三栏。不要以为"这个问题很简单，不需要读文档"——历史上 80% 的 AI 提交都在重犯已记录的陷阱。
2. **修复后写文档**：每次修复 BUG 后，必须在"陷阱"章节末尾新增一条记录（递增编号），格式为：症状 + 根因 + 修复代码 + 教训。写文档的时间和修复代码的时间同等重要。
3. **禁止绕过**：不得以"这个陷阱是老版本的"、"我已经用新方法了"、"这个 Mode 不会触发"等理由跳过阅读。未知的已知陷阱是最危险的一类 BUG。
4. **编译 + 发布 = 完成**：修复代码后必须 `dotnet publish -c Release -p:Platform=x64 -o "E:\SYNC\My VS\PChabit\publish"` 并验证时间戳，只 build 不 publish 不算完成。

### 当前已知陷阱（共 19 条）

| # | 陷阱 | 关键词 |
|---|---|---|
| 1 | XAML 资源路径 2.2.1 兼容性 | XAML、AppIcon、ms-appx |
| 2 | ContentDialog.XamlRoot 未设置 | WinUI、崩溃、XamlRoot |
| 3 | DI 注册遗漏 | InvalidOperationException、闪退 |
| 4 | UI 线程同步阻塞 | .Wait()、卡死 |
| 5 | SQLite WAL 锁冲突 | 卡顿、并发 |
| 5.5 | await 实际同步阻塞 UI 线程 | SynchronizationContext、假异步 |
| 5.6 | ObservableCollection 跨线程 COMException | 0x8001010E、线程池 |
| 6 | 跨天数据泄露 | Dictionary、Date |
| 7 | 双层 Task.Run 破坏 SynchronizationContext | LoadInBackgroundAsync、COMException |
| 8 | WinRT DependencyObject 非 UI 线程创建 | SolidColorBrush、线程亲和 |
| 10 | WMC9999 编译错误 | XAML、绑定、卫星资源 |
| 11 | e_sqlite3.dll 缺失 | DllNotFoundException、原生库 |
| 12 | DailySummaries 表未创建 | SqliteException、手动迁移 |
| 13 | WebView2 阻塞数据加载 | SankeyView、NavigationCompleted |
| 14 | 退出时 WAL checkpoint 卡顿 | ServiceProvider.Dispose、磁盘 IO |
| 15 | SelfContained 缺失提示下载 .NET | runtimeconfig.json、framework |
| 16 | 备份保留分数硬编码导致设置无效 | BackupService、MaxBackupCount、WebDAV |
| 17 | WebDAV ListFiles 返回绝对路径导致删除/下载失败 | WebDAVSyncService、PROPFIND、href |
| 18 | 网页统计分类不显示+时间不准 | WebDetailsViewModel、DataCollectionService |
| 19 | System.Timers.Timer 线程池重启钩子导致键盘统计丢数据 | WH_KEYBOARD_LL、消息泵、线程池 |

> **阅读前提**: 建议先阅读 `PChabit_架构分析报告.md` 了解项目全貌。

---

## 项目概览

| 维度 | 详情 |
|---|---|
| **运行时** | .NET 9.0 (net9.0-windows10.0.22621.0) |
| **UI 框架** | WinUI 3 (Windows App SDK 2.2.1) |
| **架构模式** | Clean Architecture (Core → Infrastructure → Application → App) |
| **MVVM 框架** | CommunityToolkit.Mvvm 8.4.2 (源生成器) |
| **ORM** | EF Core 9.x + SQLite (WAL 模式) |
| **日志** | Serilog → `%LocalAppData%\PChabit\Logs\` |
| **目标平台** | win-x64，非打包 (Unpackaged) 自包含部署 |
| **C# 语言** | latest，Nullable 启用，ImplicitUsings 启用 |
| **功能** | 键盘使用统计、应用使用统计、鼠标统计、网页统计、习惯追踪、效率分析、桑基图/热力图 |

### 项目结构

```
E:\SYNC\My VS\PChabit\
├── PChabit.sln
├── src\
│   ├── PChabit.Core\          # 领域核心：16 实体 + 17 接口（零依赖）
│   ├── PChabit.Infrastructure\ # 基础设施：EF Core / 监控器 / 20 服务
│   ├── PChabit.Application\    # 应用层：5 聚合器 + 行为分析
│   ├── PChabit.App\            # UI 层：WinUI 3 / 21 ViewModel / 28 页面
│   └── PChabit.Tests\          # 测试：xUnit + Moq
└── publish\                    # 发布输出目录
```

### 关键依赖版本（精确）

| 包 | 版本 | 注意 |
|---|---|---|
| `Microsoft.WindowsAppSDK` | 2.2.1 | **不可随意升级**，卫星程序集 zh-CN 本地化资源缺失问题需关注 |
| `CommunityToolkit.Mvvm` | 8.* (浮动) | 当前锁定 8.4.2 |
| `Microsoft.EntityFrameworkCore.Sqlite` | 9.* | |
| `Serilog` | 3.* | |
| `EPPlus` | 7.7.0 | |

---

## 禁止事项（违反必出 BUG）

以下操作已被验证会导致崩溃、卡死、数据异常或功能失效。**绝对禁止**。

| # | 禁止操作 | 原因 | 错误现象 / 异常码 |
|---|---------|------|------------------|
| **0** | **修复任何 BUG 后只 `dotnet build` 不 `dotnet publish`，或提醒用户"请自行验证"** | AI 必须自己完成全流程：`dotnet publish -c Release -p:Platform=x64 -o "E:\SYNC\My VS\PChabit\publish"` → `(Get-Item publish\PChabit.dll).LastWriteTime` 验证时间戳。只 build 的 DLL 在 `bin\` 下，用户运行的是 `publish\` 下的旧版本，修复不会生效。提醒用户验证意味着 AI 没有对自己的产出负责。 | 用户运行后 BUG 依旧存在 → 信任崩塌 |
| 1 | 在 `.csproj` 中设置 `<PublishReadyToRun>true</PublishReadyToRun>` | ReadyToRun AOT 与 Windows App SDK 2.2.1 `Microsoft.UI.Xaml.dll` 存在 WinRT 互操作兼容性缺陷，导致栈缓冲区溢出 | 启动立即崩溃，事件查看器显示 `0xc000027b`，故障模块 `Microsoft.UI.Xaml.dll` |
| 2 | 在 UI 线程上同步调用 async 方法（`.Wait()` / `.Result`） | 阻塞 UI 消息泵，Win32 低级钩子 (`WH_KEYBOARD_LL` / `WH_MOUSE_LL`) 必须在有消息泵的线程上安装，阻塞会导致钩子安装超时或死锁 | 监控器无法启动、应用卡死 |
| 3 | 通过 DI 创建 `ContentDialog` 后不设置 `XamlRoot` 就直接 `ShowAsync()` | DI 容器创建的控件没有视觉树上下文，`XamlRoot` 为 `null`。WinUI 3 要求 `ContentDialog` 的 `XamlRoot` 必须指向当前 UI 元素所在树 | 异常被 `try-catch` 静默吞掉（点击无反应），或 `ShowAsync()` 挂起（卡死） |
| 4 | 在 `ServiceConfiguration.cs` 中遗漏 ViewModel 或 Dialog 的 DI 注册 | `App.GetService<T>()` → `GetRequiredService<T>()` 抛出 `InvalidOperationException`，在 WinRT 互操作层表现为 `combase.dll` 崩溃 | 进程闪退，事件查看器异常码 `80131509` (0x80131509) |
| 5 | 在 SQLite WAL 模式下于 UI 线程同步执行数据库查询 | WAL 模式下写操作不阻塞读，但当后台 `DataCollectionService` 持续写入 `AppSession` 等表时，同步读可能长时间阻塞或触发 SQLite `BUSY` 锁冲突 | UI 线程卡顿数秒后闪退 |
| 6 | 在数据统计字典中使用单维度键（如只用 `Hour` 不用 `Date+Hour`） | 跨天后新旧数据混淆，统计结果泄露到相邻日期 | 键盘热力图跨天数据异常、打字爆发计数错误 |
| 7 | 快捷键检测中遗漏 `Win` 键 (`VK_LWIN` / `VK_RWIN`) | Win+E、Win+R 等系统快捷键不被识别为快捷键，统计缺失，`IsShortcut` 判断不完整 | 快捷键使用次数偏低，`GetShortcutString()` 缺少 "Win" 前缀 |
| **8** | **在 ViewModel 内部已用 `Task.Run` 管理线程的 Page 上再套 `LoadInBackgroundAsync`** | 双层 `Task.Run` 破坏 `SynchronizationContext` 传递：页面层 `LoadInBackgroundAsync(Task.Run)` + ViewModel 内部 `await Task.Run`，导致 DB 查询后的 ObservableCollection 操作在线程池执行 → COMException | 页面数据全部为空，日志无错误或仅有被吞掉的 Warning |
| **9** | **在 Phase 1（线程池）数据模型类中使用 WinRT `DependencyObject` 类型（如 `SolidColorBrush`）作为属性默认值** | C# 属性默认值初始化器先于对象初始化器执行，即使对象初始化器显式赋值了该属性，`new SolidColorBrush()` 仍在线程池上触发 `WinRT.BaseActivationFactory._ActivateInstance()` → COMException | 仅包含 WinRT 类型的模块数据为空，纯 POCO 模块正常（对比排查法） |
| **10** | **在线程池线程上调用 `ObservableCollection<T>.Clear()` / `.Add()` / `.Remove()`** | `ObservableCollection.CollectionChanged` 事件通知 WinUI 绑定时需要 UI 线程，线程池触发 `COMException (0x8001010E, RPC_E_WRONG_THREAD)`。`ViewModelBase.OnPropertyChanged` 覆写只拦截了 `INotifyPropertyChanged.PropertyChanged`，不拦截 `CollectionChanged` | 集合保持空状态，异常被 `catch (Exception)` 静默吞掉 |
| **11** | **在 zh-CN 系统上使用 WinAppSDK 2.x 编译时，XAML 绑定错误会触发 WMC9999 吞没真正错误** | XAML 编译器在检测到 `x:Bind` 属性不存在等绑定错误后，调用 `ResourceManager.GetString()` 尝试本地化错误消息为 zh-CN。WinAppSDK 2.x 的 XamlCompiler 在 zh-CN 系统上缺少中文卫星程序集资源 → `ResourceManager` 找不到 `ErrorMessages.resources` → 报告 WMC9999 崩溃，真正的绑定错误（如 WMC1121）无法显示 | 编译输出仅 WMC9999，无实际错误的 WMC 码；output.json 中 MSBuildLogEntries 无 Type=3 条目；WMC9999 前的 perfXC_PageCodeGenStart 事件指示问题文件 |

---

## 标准模板

### 新增 ViewModel

```csharp
// 文件位置: src/PChabit.App/ViewModels/NewFeatureViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace PChabit.App.ViewModels;

public partial class NewFeatureViewModel : ViewModelBase
{
    // 1. 必须继承 ViewModelBase（提供 UI 线程调度 + ObservableObject）
    // 2. 属性使用 [ObservableProperty] 源生成器
    // 3. 异步数据加载使用 DispatcherQueue.TryEnqueue 延迟执行
    // 4. 构造函数可注入 IServiceProvider 或具体服务

    private readonly IServiceProvider _serviceProvider;

    [ObservableProperty]
    private string _title = "新功能";

    [ObservableProperty]
    private bool _isLoading;

    public NewFeatureViewModel(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    // 异步初始化：不阻塞构造函数
    public async Task InitializeAsync()
    {
        try
        {
            IsLoading = true;
            // 数据加载逻辑...
        }
        catch (Exception ex)
        {
            Log.Error(ex, "NewFeatureViewModel 初始化失败");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await InitializeAsync();
    }
}
```

**关键约束**：
- 不要使用 Scoped 生命周期——ViewModel 通过 `App.GetService<T>()` 从根容器解析，Scoped 会导致俘定依赖。始终使用 `AddTransient<T>()`。
- ViewModel 不应直接持有 `PChabitDbContext`，应通过 `IServiceScopeFactory` 创建 Scoped 上下文或注入聚合服务。

### 新增 ContentDialog（含 XamlRoot 设置）

```csharp
// 文件位置: src/PChabit.App/ViewModels/NewFeatureDialogViewModel.cs
// ViewModel 也需注册
```

```xml
<!-- 文件位置: src/PChabit.App/Views/NewFeatureDialog.xaml -->
<ContentDialog
    x:Class="PChabit.App.Views.NewFeatureDialog"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Title="新功能"
    PrimaryButtonText="确定"
    SecondaryButtonText="取消"
    DefaultButton="Primary">
    <!-- 内容 -->
</ContentDialog>
```

```csharp
// 文件位置: src/PChabit.App/Views/NewFeatureDialog.xaml.cs
// ⚠️ 不要使用 PrimaryButtonClick 事件（无法异步验证）
// 使用 Closing 事件 + GetDeferral() 异步验证
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;

namespace PChabit.App.Views;

public sealed partial class NewFeatureDialog : ContentDialog
{
    public NewFeatureDialogViewModel ViewModel { get; }

    public NewFeatureDialog()
    {
        ViewModel = App.GetService<NewFeatureDialogViewModel>();
        InitializeComponent();
    }
}
```

**调用端（Page/Tab 中）**：

```csharp
// ⚠️ 两个关键步骤缺一不可：
private async void OnOpenDialogClick(object sender, RoutedEventArgs e)
{
    try
    {
        // 步骤 1: 从 DI 获取 Dialog 实例
        var dialog = App.GetService<NewFeatureDialog>();

        // 步骤 2: 必须设置 XamlRoot！（否则 ShowAsync 失败/卡死）
        dialog.XamlRoot = XamlRoot;

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            // 处理结果...
        }
    }
    catch (Exception ex)
    {
        Log.Error(ex, "打开 NewFeatureDialog 失败");
    }
}
```

### DI 注册模板（ServiceConfiguration.cs）

```csharp
// 文件位置: src/PChabit.App/Services/ServiceConfiguration.cs
// ViewModel: Transient（原因见上文）
services.AddTransient<NewFeatureViewModel>();

// Dialog: Transient（必须同时注册 Dialog 和 DialogViewModel）
services.AddTransient<NewFeatureDialogViewModel>();
services.AddTransient<NewFeatureDialog>();          // ← 容易遗漏！
```

**检查清单**（新增 UI 功能后逐项核对）：
- [ ] ViewModel 已 `AddTransient<T>()` 注册
- [ ] ContentDialog 已 `AddTransient<T>()` 注册（**不可遗漏**，遗漏会在运行时 `InvalidOperationException` 闪退）
- [ ] 调用端设置了 `dialog.XamlRoot = XamlRoot`
- [ ] 调用端包裹在 `try-catch` 中

---

## 已知陷阱

### 陷阱 1：PublishReadyToRun 导致启动崩溃

**症状**：
- 双击 `PChabit.exe` 后无窗口显示，进程立即退出
- Windows 事件查看器 → Windows 日志 → 应用程序 → 错误级别事件
- 异常码 `0xc000027b`，故障模块 `Microsoft.UI.Xaml.dll`

**根因**：
.NET 9 ReadyToRun (R2R) 预编译 (`PublishReadyToRun=true`) 与 Windows App SDK 2.2.1 的 WinRT 互操作层存在兼容性缺陷。R2R 跨程序集内联了部分 WinRT 投影代码，导致栈缓冲区溢出。

**修复**：
在 `src/PChabit.App/PChabit.App.csproj` 中强制设为 `false`：

```xml
<PublishReadyToRun>false</PublishReadyToRun>
```

**验证方法**：
`dotnet publish` 后直接运行 `publish\PChabit.exe`，确认启动正常。

---

### 陷阱 2：ContentDialog 弹出失败（XamlRoot 未设置）

**症状**（两种表现）：
- 表现 A：点击按钮后**毫无反应**，无对话框弹出，无日志输出。异常被 `try-catch` 静默吞掉。
- 表现 B：点击按钮后应用**卡死**（`ShowAsync()` 无限挂起，等待一个永远不会到来的消息泵响应）。

**根因**：
通过 `App.GetService<ContentDialog>()` 从 DI 容器创建的对话框没有视觉树上下文，`XamlRoot` 属性为 `null`。WinUI 3 要求 `ContentDialog.ShowAsync()` 必须已知其所属的 XAML 树。

**修复**：
在 `ShowAsync()` 之前显式赋值：

```csharp
dialog.XamlRoot = XamlRoot;  // this.XamlRoot（在 Page/UserControl 中）
```

**已有的正确示例**（`CategoryManagementTab.xaml.cs`）：

```csharp
private async void OnAddCategoryClick(object sender, RoutedEventArgs e)
{
    var dialog = App.GetService<CategoryEditDialog>();
    dialog.ViewModel.InitializeForAdd();
    dialog.XamlRoot = XamlRoot;          // ← 必须设置
    var result = await dialog.ShowAsync();
    // ...
}
```

---

### 陷阱 3：DI 注册遗漏导致 InvalidOperationException 闪退

**症状**：
- 点击按钮后**进程直接闪退**，无 .NET 异常对话框
- Windows 事件查看器：APPCRASH，异常码 `0x80131509`（即 `COR_E_INVALIDOPERATION` = `InvalidOperationException`），故障模块 `combase.dll`
- 应用日志中**没有**点击事件处理函数的任何日志输出——崩溃发生在进入事件处理函数之前

**根因**：
`App.GetService<T>()` 内部调用 `GetRequiredService<T>()`，当类型 `T` 未在 DI 容器中注册时抛出 `InvalidOperationException`。在 WinUI/WinRT 互操作层中，此异常不触发 .NET 异常对话框，而是通过 `combase.dll` 直接终止进程。

**诊断方法**：
当修复内部逻辑无效时，检查调用链入口是否已失败——对 `OnAddCategoryClick`，崩溃前无任何日志意味着 `App.GetService<CategoryEditDialog>()` 这行就已经抛异常。

**对比法快速定位**：
比较同类的已正常工作的代码。例如 `WebsiteCategoryEditDialog` 已注册：
```
services.AddTransient<WebsiteCategoryEditDialogViewModel>();
services.AddTransient<WebsiteCategoryEditDialog>();          // ✅ 有
```
但 `CategoryEditDialog` 缺少 Dialog 本身的注册：
```
services.AddTransient<CategoryEditDialogViewModel>();        // ✅ ViewModel 有
services.AddTransient<CategoryEditDialog>();                 // ❌ 缺失！
```

**修复**：
在 `ServiceConfiguration.cs` 中补充注册。

---

### 陷阱 4：UI 线程同步阻塞导致卡死

**症状**：
- 应用启动后监控器不工作（键盘/鼠标统计无数据）
- 或在启动阶段卡住不动

**根因**（`App.xaml.cs` 第 265 行）：

```csharp
// ❌ 这段代码在 UI 线程（DispatcherQueue.TryEnqueue 回调）中执行
_monitorManager.StartAllAsync().Wait();
```

`StartAllAsync` 内部安装 `WH_KEYBOARD_LL` / `WH_MOUSE_LL` 低级钩子，这些钩子**必须在有消息泵（message pump）的线程上安装**。`.Wait()` 同步阻塞了当前 UI 线程，消息泵暂停运转，钩子安装可能超时或死锁。

**为何不能简单地 `await`**：
Win32 低级钩子确实要在 UI 线程上启动（不能 `Task.Run` 到线程池，线程池线程无消息泵），但这并不意味着必须同步阻塞。正确的做法是将 `StartMonitoring()` 整个改为 async：

```csharp
// ✅ 正确做法
private async void StartMonitoring()
{
    await _monitorManager.StartAllAsync();
}
```

---

### 陷阱 5：SQLite WAL 锁冲突

**症状**：
- 某页面加载数据时卡顿数秒
- 高并发场景下偶尔闪退
- `CategoryEditDialog` 点击"确定"时卡顿后崩溃

**根因**：
PChabit 后台 `DataCollectionService` 持续高频写入 `AppSession` / `KeyboardSession` 等表（每秒多次），同时 UI 线程同步执行 `CategoryExists()` 类的数据库查询。WAL 模式下写操作会持有 `SHARED` 锁，同步读等待可能导致严重 UI 卡顿。

**修复原则**：

1. **UI 层永远异步读取数据库**：

```csharp
// CategoryEditDialogViewModel.cs
// ❌ 同步验证 (旧版)
public bool Validate()
{
    return _categoryService.CategoryExists(Name); // 同步 SQLite 查询
}

// ✅ 拆分为两步（新版）
public bool ValidateBasic()   // 同步快检（不碰 DB）
public async Task<bool> ValidateExistsAsync() // 异步 DB 查询
```

2. **ContentDialog 使用 `Closing` 事件替代 `PrimaryButtonClick`**：

```csharp
// ❌ PrimaryButtonClick 是同步事件，无法 await
// ✅ Closing 事件支持 GetDeferral() 异步验证
dialog.Closing += async (s, args) =>
{
    var deferral = args.GetDeferral();
    try
    {
        if (!await viewModel.ValidateExistsAsync())
            args.Cancel = true;
    }
    finally { deferral.Complete(); }
};
```

---

### 陷阱 5.5：WinUI 3 `await` 阻塞 UI 线程（看似异步实则同步）

**症状**：
- 打开任何包含数据库查询的页面（仪表盘/时间线/热力图/键盘详情/数据管理等）卡顿至死
- 用户的「ProgressRing 占位 + Loaded 事件」修复方案**不生效**——首次导航仍卡
- 真正卡的不是「数据多」而是「执行查询的线程不对」

**根因**（最隐蔽的反模式）：

WinUI 3 默认安装 `SynchronizationContext`。当你写：

```csharp
// DashboardPage.xaml.cs
protected override async void OnNavigatedTo(NavigationEventArgs e)
{
    await ViewModel.LoadDataAsync();   // ❌ 看似异步，实则 UI 线程同步阻塞
}
```

`await` 会捕获当前 `SynchronizationContext` 并在 `await` 后续代码上回到 UI 线程。EF Core 内部使用 `Task.GetAwaiter().GetResult()` 模式，会在 UI 线程上**同步等待 DB 查询完成**。即使代码「看起来」是 `async` 的，UI 线程仍然被堵住。

**修复**（v2.26.2+ 标准模式）：

1. **ViewModelBase 提供统一包装**：

```csharp
// ViewModelBase.cs
public async Task LoadInBackgroundAsync(Func<Task> loadAction)
{
    if (IsLoading) return;
    await RunOnUIThreadAsync(() => { IsLoading = true; return Task.CompletedTask; });
    try
    {
        // 关键：Task.Run 强制把 loadAction 丢到线程池
        await Task.Run(async () => await loadAction());
    }
    finally
    {
        await RunOnUIThreadAsync(() => { IsLoading = false; return Task.CompletedTask; });
    }
}
```

2. **Page 调用方式**：

```csharp
// ✅ 正确
protected override async void OnNavigatedTo(NavigationEventArgs e)
{
    await ViewModel.LoadInBackgroundAsync(async () =>
    {
        await ViewModel.LoadDataAsync();
    });
}
```

3. **多查询合并为单次 SQL（额外优化）**：

```csharp
// ❌ 6 次独立查询（DataManagementViewModel.LoadOverviewAsync 旧版）
var appCount = await dbContext.AppSessions.CountAsync();
var keyCount = await dbContext.KeyboardSessions.CountAsync();
var mouseCount = await dbContext.MouseSessions.CountAsync();
var webCount = await dbContext.WebSessions.CountAsync();
var earliest = await dbContext.AppSessions.MinAsync(...);
var latest = await dbContext.AppSessions.MaxAsync(...);

// ✅ 单次 SqlQueryRaw（新版）
var stats = await dbContext.Database
    .SqlQueryRaw<DbStats>(@"
        SELECT 
            (SELECT COUNT(*) FROM AppSessions) AS AppCount,
            (SELECT COUNT(*) FROM KeyboardSessions) AS KeyCount,
            ...
    ")
    .FirstAsync();
```

**诊断方法**：
当 `Loaded` 事件 / `OnNavigatedTo` 中使用 `await ViewModel.LoadXxxAsync()` 时，检查 ViewModel 内部是否有：
- 多个 `await` 串行执行（每 `await` 一次就回 UI 线程）
- EF Core 的 `ToListAsync` / `CountAsync` / `MinAsync` / `MaxAsync`
- 任何使用 `IDbContextFactory` 创建 DbContext 的查询

**如果命中上述 3 条之一，必须用 `LoadInBackgroundAsync` 包装**。

---

### 陷阱 5.6：ObservableCollection 在 Task.Run 线程更新导致 COMException（2026-06-16 发现）

**症状**：
- 打开时间线/智能洞察页面卡死（即使已用 `LoadInBackgroundAsync` 包装）
- 日志抛出 `COMException (0x8001010E RPC_E_WRONG_THREAD)`

**根因**：
`LoadInBackgroundAsync` 内部 `Task.Run(async () => await loadAction())` 把整个 `LoadDataAsync` 丢到线程池。但某些 `LoadDataAsync` 实现**在 `Task.Run` 内部又嵌套了一次 `Task.Run` 拉数据**（`TimelineViewModel` 旧版），拿回数据后**直接在当前线程（线程池）操作 `ObservableCollection`**，触发 `0x8001010E`。

**错误模式**（`TimelineViewModel.LoadDataAsync` 旧版）：
```csharp
var result = await Task.Run(async () => /* 查询 DB + LINQ 分组 */);
// ❌ result 已经回到 Task.Run 线程，下面直接 Add 到 ObservableCollection
HourGroups.Clear();
foreach (var hg in hourlyGroups)
{
    var hourGroup = new TimelineHourGroup { ... };
    hourGroup.Activities.Add(...);  // ❌ 线程池线程更新 UI 集合
    HourGroups.Add(hourGroup);      // ❌ 触发 COMException
}
```

**正确修复**：
```csharp
// ✅ 1. 在线程池构造数据模型（POCO 可在线程池创建）
var builtGroups = new List<(TimelineHourGroup Group, bool IsCurrent)>();
foreach (var hg in hourlyGroups)
{
    var hourGroup = new TimelineHourGroup { ... };
    builtGroups.Add((hourGroup, isCurrent));
}

// ✅ 2. 集合修改统一调度到 UI 线程
await RunOnUIThreadAsync(() =>
{
    HourGroups.Clear();
    foreach (var (hourGroup, _) in builtGroups)
        HourGroups.Add(hourGroup);
    return Task.CompletedTask;
});
```

**诊断方法**：
1. 搜索 `LoadDataAsync` 中所有 `ObservableCollection` 的 `.Clear()` / `.Add()` / `.Insert()` / `.RemoveAt()` 调用
2. 如果这些调用在 `Task.Run` lambda **外层**（即延续到 Task.Run 线程），必须包到 `RunOnUIThreadAsync` 中
3. `TimelineViewModel.LoadDataAsync` / `InsightsViewModel.LoadTodayInsightsAsync` / `LoadWeeklyScoresAsync` / `AppStatsViewModel.LoadDataAsync` 修复前均有此问题

---

### 陷阱 6：跨天数据泄露

**症状**：
- 键盘统计热力图在凌晨前后数据异常（前一天最后一个小时的数据混入次日）
- 打字爆发（Typing Burst）计数明显偏高

**根因**（`DataCollectionService.cs`）：

```csharp
// ❌ 旧版：键只使用 Hour，跨天不隔离
private readonly Dictionary<int, (DateTime startTime, int keyCount)> _typingBursts = new();

// ✅ 新版：使用 (Date, Hour) 组合键，每天独立
private readonly Dictionary<(DateTime Date, int Hour), (DateTime startTime, int keyCount)> _typingBursts = new();
```

当应用跨天运行时（如从 23:00 运行到次日 01:00），旧代码中 Hour=1 的条目在两天之间共享状态，导致：
- 前一天 01:00 的打字爆发数据泄露到当天
- `keyCount` 持续累加不被重置

**同类型检查项**：
所有 `Dictionary` / 缓存 / 内存状态使用时间维度作为键时，检查是否包含了 `Date` 分量。

---

### 陷阱 7：双层 Task.Run 破坏 SynchronizationContext（LoadInBackgroundAsync + ViewModel 自管理线程冲突）

**症状**：
- 页面所有数据模块全部为空
- 日志中无 Error 级别日志，或只有被 `catch (Exception)` 吞掉的 Warning
- 数据库查询本身成功执行（Dashboard 等页面的 AppSessions 计数正常）

**根因**（2026-06-15 发现的模式级陷阱）：

```csharp
// TimelinePage.xaml.cs（页面层）
protected override async void OnNavigatedTo(NavigationEventArgs e)
{
    await ViewModel.LoadInBackgroundAsync(async () =>   // Task.Run #1（页面层）
    {
        await ViewModel.LoadDataAsync();                // 线程池线程
    });
}

// TimelineViewModel.cs（ViewModel 层）
public async Task LoadDataAsync()
{
    // 此时已在线程池（来自 LoadInBackgroundAsync 的 Task.Run #1）
    var result = await Task.Run(async () =>             // Task.Run #2（ViewModel 层）
    {
        // EF Core 查询...
        return data;
    });
    // ⚠️ 继续在线程池！SynchronizationContext 为 null
    
    HourGroups.Clear();   // ❌ ObservableCollection.Clear() on thread pool → COMException
    HourGroups.Add(...);  // ❌ ObservableCollection.Add() on thread pool → COMException
}
```

**为什么 `await Task.Run(...)` 没有回到 UI 线程**：
- `await` 默认捕获 `SynchronizationContext.Current`
- 页面层 `LoadInBackgroundAsync` 通过 `Task.Run` 将整个回调移到线程池 → `SynchronizationContext.Current` = `null`
- ViewModel 内部的 `await Task.Run(...)` 在线程池上下文中执行 → 无 SynchronizationContext 可捕获 → 继续在线程池
- 所有后续 `ObservableCollection` 操作触发 `COMException 0x8001010E`

**修复（两种方案，视 ViewModel 架构选其一）**：

**方案 A**（推荐 — ViewModel 内部已自管理线程时）：
```csharp
// ❌ 旧版
protected override async void OnNavigatedTo(NavigationEventArgs e)
{
    await ViewModel.LoadInBackgroundAsync(async () =>
    {
        await ViewModel.LoadDataAsync();
    });
}

// ✅ 新版：直接 await，让 ViewModel 自己管理线程
protected override async void OnNavigatedTo(NavigationEventArgs e)
{
    await ViewModel.LoadDataAsync();
}
```
适用条件：ViewModel 内部已有 `await Task.Run(...)` 或两阶段架构来分离 DB 查询和 UI 更新。

**方案 B**（ViewModel 内部无自管理线程时）：
保持 `LoadInBackgroundAsync` 包装，但 ViewModel 内部所有 ObservableCollection 操作必须通过 `RunOnUIThreadAsync` 调度回 UI 线程。

**诊断方法**：
搜索 Warning 日志中是否有 COMException 相关堆栈，或在 ViewModel 中将 `catch (Exception)` 临时改为 `throw` 以暴露真实异常。

---

### 陷阱 8：WinRT DependencyObject 在非 UI 线程创建

**症状**：
- 同一页面中，纯 POCO 数据模块正常显示，包含 WinRT 类型的模块全部为空
- 日志堆栈中出现 `WinRT.BaseActivationFactory._ActivateInstance()` 调用

**根因**（2026-06-15 发现）：

```csharp
// KeyUsageItem（数据模型，在 Phase 1 线程池中构造）
public class KeyUsageItem
{
    // ❌ SolidColorBrush 是 DependencyObject，必须在 UI 线程创建
    public SolidColorBrush Color { get; init; } = new SolidColorBrush();
}
```

C# 属性默认值初始化器 `= new SolidColorBrush()` 在构造函数体执行之前运行，且**先于**对象初始化器的属性赋值。即使对象初始化器中显式写了 `Color = GetKeyColor(keyName)`，`new SolidColorBrush()` 仍然会在线程池上被调用。

这会触发：
```
WinRT.BaseActivationFactory._ActivateInstance[I]()
  → Microsoft.UI.Xaml.Media.SolidColorBrush..ctor()
    → KeyUsageItem..ctor()
```

**对比排查法快速定位**：
- 鼠标模块（`HourlyMouseStat`、`MouseClickDetail` 等）仅含 `int`/`double`/`string` → 线程池构造正常 → 数据可见
- 键盘模块（`KeyUsageItem`）含 `SolidColorBrush` → 线程池构造抛异常 → 数据为空
- 结论：问题不在数据库查询，而在数据模型的线程亲和性

**修复**：

```csharp
// ✅ 方案 A：移除默认值，改为可空属性，延迟到 UI 线程赋值
public class KeyUsageItem
{
    public SolidColorBrush? Color { get; set; }
}

// Phase 2（UI 线程）中赋值：
foreach (var item in s.KeyUsage)
{
    item.Color = GetKeyColor(item.KeyName);  // ✅ UI 线程安全
    KeyUsageList.Add(item);
}

// ✅ 方案 B：存储颜色为 string，在 XAML 绑定中使用 StringToColorConverter
public string ColorHex { get; init; } = "#0078D4";
```

**检查清单**（两阶段架构 Phase 1 中的所有数据模型）：
- [ ] 无 `SolidColorBrush`、`BitmapImage`、`ImageSource` 等 WinRT 类型
- [ ] 无 `UIElement`、`FrameworkElement` 等可视化树类型
- [ ] 无 `DispatcherQueue`、`CoreDispatcher` 等调度器类型
- [ ] 所有属性为纯数据：`int`、`string`、`double`、`DateTime`、`bool`、`List<T>` 等

---

### 杂项 BUG 清单

以下 BUG 也被修复过，但严重度较低或原因较直观：

| # | 问题 | 修复 |
|---|------|------|
| 1 | Ctrl+Z 撤销按键未被计入 `UndoCount` | `DataCollectionService.cs`: 添加 `e.KeyCode == 0x5A` (VK_Z) 检测 |
| 2 | 键盘统计 UI 显示"字/分钟" | `KeyboardDetailsViewModel.cs`: 改为"键/分钟"（实际存储的是 KPM = Keys Per Minute） |
| 3 | csproj 缺少 `<PlatformTarget>x64</PlatformTarget>` | SelfContained 构建时缺少平台目标会报错 |
| 4 | `CategoryEditDialog` XAML 中残留废弃的 `PrimaryButtonClick` 绑定 | 移除该属性 |

---

### 陷阱 11：SQLitePCLRaw 原生库 `e_sqlite3.dll` 缺失导致所有页面功能失效（2026-06-17 发现）

**症状**：
- 程序可以启动，但所有页面数据为空，键鼠详情页点击后**卡死**
- 日志充斥 `System.DllNotFoundException: Unable to load DLL 'e_sqlite3'`（错误码 0x8007007E）
- `SQLitePCLRaw.batteries_v2.dll` / `SQLitePCLRaw.core.dll` 等托管 DLL 存在，但原生 `e_sqlite3.dll` 不在 publish 目录

**根因**：
`dotnet publish` 未指定 `-r win-x64`（或 csproj 中未设置 `<RuntimeIdentifier>win-x64</RuntimeIdentifier>`），导致 NuGet 包 `SQLitePCLRaw.lib.e_sqlite3` 中的原生运行时资源（`runtimes/win-x64/native/e_sqlite3.dll`）未被包含到发布输出中。托管 DLL 正常复制，但 `Batteries_V2.Init()` 初始化时调用 `NativeMethods.sqlite3_libversion_number()` 找不到原生 DLL，触发 `TypeInitializationException`。

**诊断方法**：
检查 publish 目录下是否存在 `e_sqlite3.dll`；查看日志中是否有 `DllNotFoundException: e_sqlite3`。

**修复**：
在 `src/PChabit.App/PChabit.App.csproj` 中添加：

```xml
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
```

`RuntimeIdentifier`（单数）控制发布时的运行时资产解析；`RuntimeIdentifiers`（复数）仅用于构建时多 RID 规范，不影响发布时的原生资产拷贝。两者必须同时设置。

**关联**: 此陷阱仅在 .NET 8 → .NET 9 升级后暴露，因为旧版 publish 输出残留了 `batteries_v2` 托管 DLL 掩盖了问题。

---

### 陷阱 12：DashboardViewModel 缺少 DailySummaries 表创建 + 分类筛选硬编码中文匹配（2026-06-17 发现）

**症状**：
- 仪表盘数据全部为空（TodayActiveTime / TodayKeyPresses / TodayMouseClicks 均为 0 或占位符）
- 网页访问统计页选择"搜索"/"视频"/"社交"等非"全部分类"选项后，列表始终为空
- 分析-应用流向（桑基图）数据丢失
- 日志出现 `SqliteException: no such table: DailySummaries`

**根因（3 个关联问题）**：

1. **致命：DailySummaries 表未创建**
   `DatabaseInitializer.MigrateAnalysisTablesAsync()` 手动创建了 UserGoals / EfficiencyScores / WorkPatterns / InsightReports 等 7 组表，独缺 `DailySummaries` 表。`DashboardViewModel.LoadStatsOnBackgroundAsync()` 首行查询该表立即抛 `SqliteException`，外层无 try-catch 保护，异常直接传播，阻止了 fallback 到 `BuildStatsFromRawDataAsync()` 的逻辑。

2. **WebDetailsViewModel 分类筛选硬编码中文关键词**
   ```csharp
   // ❌ 错误：用中文分类名去匹配 Domain 字段
   if (SelectedCategory != "全部分类")
   {
       query = query.Where(s => s.Domain.Contains(SelectedCategory) || s.Url.Contains(SelectedCategory));
   }
   ```
   分类选项 "搜索"/"开发"/"视频" 等是中文名，但 Domain 字段存的是域名（如 `bing.com`），`Contains("搜索")` 永远匹配不上，非"全部分类"时结果必然为 0。

3. **BuildStatsFromSummary 硬编码 TodayWebPages = "0"**
   `DailySummary` 实体不存储网页访问数，`BuildStatsFromSummary()` 中 `TodayWebPages` 硬编码为 `"0"`，`ProductivityScore` 硬编码为 `0`，即使从预聚合表加载成功，网页统计也显示为空。

**修复**：

1. `DatabaseInitializer.MigrateAnalysisTablesAsync()` 末尾补建 DailySummaries 表：
```sql
CREATE TABLE DailySummaries (
    Id TEXT PRIMARY KEY,
    Date TEXT NOT NULL,
    TotalKeys INTEGER NOT NULL DEFAULT 0,
    TotalMouseClicks INTEGER NOT NULL DEFAULT 0,
    ActiveMinutes REAL NOT NULL DEFAULT 0,
    TopApps TEXT NOT NULL DEFAULT '[]',
    HourlyKeyDistribution TEXT NOT NULL DEFAULT '[]',
    LastUpdated TEXT NOT NULL
);
CREATE UNIQUE INDEX IX_DailySummaries_Date ON DailySummaries (Date);
```

2. `DashboardViewModel.LoadStatsOnBackgroundAsync()` 对 DailySummaries 查询包裹 try-catch，异常时 fallback 到实时查询：

```csharp
try
{
    var summary = await dbContext.DailySummaries
        .AsNoTracking()
        .FirstOrDefaultAsync(s => s.Date == dateKey);
    if (summary != null) { /* ... */ }
}
catch (Exception ex)
{
    Log.Warning(ex, "DailySummary 查询失败，回退到实时查询");
}
```

3. `WebDetailsViewModel.LoadSessionsAsync()` 分类筛选改为通过 WebsiteDomainMappings 表查询域名模式：

```csharp
if (SelectedCategory != "全部分类")
{
    var domainPatterns = await dbContext.WebsiteDomainMappings
        .AsNoTracking()
        .Include(m => m.Category)
        .Where(m => m.Category != null && m.Category.Name == SelectedCategory)
        .Select(m => m.DomainPattern)
        .Distinct()
        .ToListAsync();
    if (domainPatterns.Any())
    {
        var lowerPatterns = domainPatterns.Select(p => p.ToLower()).ToList();
        query = query.Where(s => lowerPatterns.Any(p => s.Domain.ToLower().Contains(p)));
    }
}
```

4. `BuildStatsFromSummary` 返回后在 `LoadStatsOnBackgroundAsync` 中补查 WebSessions 数量填充 `TodayWebPages`。

**教训**：
- 手动维护的架构迁移方法（`MigrateAnalysisTablesAsync`）是脆弱点。新增实体时必须在 DbContext（EF 模型）和手动迁移（SQL 回退）两处同步创建表，遗漏其一会导致 EF 查询失败。
- 对"优先查缓存/预聚合表，不存在则 fallback"的模式，**必须**对缓存查询加 try-catch 保护，因为表缺失/列不匹配/数据损坏等任意异常都会阻断 fallback 路径。

---

## 最佳实践：两阶段数据加载架构

> **适用场景**：任何需要在后台加载数据并更新多个 `ObservableCollection` 的 ViewModel。
> **强制使用**：当 ViewModel 同时满足以下两条时必须采用此架构：
> 1. 有数据库查询（EF Core / SQLite）
> 2. 有 `ObservableCollection<T>` 需要更新

### 架构设计

```
┌─────────────────────────────────────────────────────────┐
│  LoadDataAsync()                                        │
│                                                         │
│  Phase 1: await Task.Run(() => LoadStatsAsync(date))    │
│  ┌───────────────────────────────────────────────────┐  │
│  │  线程池                                            │  │
│  │  - EF Core 查询 (ToListAsync)                     │  │
│  │  - LINQ 分组/排序/聚合                             │  │
│  │  - 存入纯 POCO 中间对象 (List<T>, 非 Observable)   │  │
│  │  - 禁止: ObservableCollection, SolidColorBrush    │  │
│  └───────────────────────────────────────────────────┘  │
│                         │                                │
│  Phase 2: await RunOnUIThreadAsync(() => { ... })       │
│  ┌───────────────────────────────────────────────────┐  │
│  │  UI 线程                                          │  │
│  │  - ObservableCollection.Clear() / Add()           │  │
│  │  - WinRT 对象创建 (SolidColorBrush 等)            │  │
│  │  - [ObservableProperty] 赋值                      │  │
│  └───────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────┘
```

### 中间数据持有对象（Phase 1 产物）

```csharp
// ✅ 正确：纯 POCO，不含任何 WinRT 类型
private sealed class KeyboardStats
{
    public int TotalPresses;
    public double AvgSpeed;
    public int PeakSpeed;
    public List<KeyUsageItem> KeyUsage = new();      // List<T>，非 ObservableCollection
    public List<HourlyKeyItem> Hourly = new();
    public List<ShortcutItem> Shortcuts = new();
    public List<AppKeyItem> AppKey = new();
}

// ❌ 错误：包含 SolidColorBrush（WinRT DependencyObject）
public class KeyUsageItem
{
    public SolidColorBrush Color { get; init; } = new SolidColorBrush();  // 线程池崩溃
}
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

### 完整示例

参考 `KeyboardDetailsViewModel.cs`（2026-06-15 重构版）：

```csharp
public async Task LoadDataAsync()
{
    IsLoading = true;
    try
    {
        var selectedDate = SelectedDate;
        
        // Phase 1: 线程池 — DB 查询 + 纯数据计算
        var keyboardStats = await Task.Run(() => LoadKeyboardStatsAsync(selectedDate));
        var mouseStats = await Task.Run(() => LoadMouseStatsAsync(selectedDate));
        
        // Phase 2: UI 线程 — ObservableCollection 更新 + WinRT 对象创建
        await RunOnUIThreadAsync(() =>
        {
            ApplyKeyboardStats(keyboardStats);
            ApplyMouseStats(mouseStats);
            return Task.CompletedTask;
        });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "加载失败");
    }
    finally
    {
        IsLoading = false;
    }
}

private void ApplyKeyboardStats(KeyboardStats s)
{
    // 所有 ObservableCollection 操作 + WinRT 对象创建集中在此
    TotalKeyPresses = s.TotalPresses.ToString("N0");
    
    KeyUsageList.Clear();
    foreach (var item in s.KeyUsage)
    {
        item.Color = GetKeyColor(item.KeyName);  // SolidColorBrush 在 UI 线程创建
        KeyUsageList.Add(item);
    }
    // ...
}
```

### 架构判断决策树

```
ViewModel 是否有数据库查询？
├─ 否 → 直接在当前线程操作，无需两阶段
└─ 是 → 是否有 ObservableCollection 需要更新？
    ├─ 否 → 直接在 LoadInBackgroundAsync 包装中执行
    └─ 是 → 必须采用两阶段架构：
             Phase 1 (Task.Run): DB + 纯数据
             Phase 2 (RunOnUIThreadAsync): ObservableCollection + WinRT
```

### 常见违规模式识别

| 违规代码特征 | 问题 | 修复方向 |
|---|---|---|
| `ObservableCollection.Clear()` 出现在 `Task.Run` 回调之后、`RunOnUIThreadAsync` 之外 | 可能在非 UI 线程执行 | 移入 `RunOnUIThreadAsync` 回调 |
| 数据模型类包含 `= new SolidColorBrush()` | WinRT 对象在线程池创建 | 改为 `SolidColorBrush? Color { get; set; }` |
| Page 层 `LoadInBackgroundAsync(async () => { await VM.LoadDataAsync(); })` 同时 VM 内部也有 `Task.Run` | 双层 Task.Run 破坏 SynchronizationContext | 移除 Page 层包装，直接 `await VM.LoadDataAsync()` |
| `catch (Exception ex) { Log.Warning(ex, "..."); }` 覆盖了包含 ObservableCollection 操作的代码块 | 跨线程 COMException 被静默吞掉 | 修复根因后可以保留 catch，但建议增加 `COMException` 单独捕获以辅助诊断 |

---

## 本次修复记录 (2026-06-17)：.NET 9 + WinAppSDK 2.2.1 升级 + WMC9999 编译错误修复

### 升级内容

| 组件 | 旧版本 | 新版本 |
|---|---|---|
| .NET SDK | 8.0 | 9.0.315 |
| Microsoft.WindowsAppSDK | 1.4.231115000 | 2.2.1 |
| EF Core | 8.0.28 | 9.x |
| Csproj Version | 2.x | 3.0.0 |
| global.json | 8.0 | 9.0.315 |

### 架构改动

1. **DbSafeViewModel 基类**（`src/PChabit.App/ViewModels/DbSafeViewModel.cs`）：封装两阶段数据加载模式（Phase 1 线程池 DB 查询 + Phase 2 UI 线程 ObservableCollection 更新），8 个 ViewModel 迁移：Dashboard/KeyboardDetails/Timeline/Insights/AppStats/WebDetails/Heatmap/Sankey
2. **DailySummary 实体**：预聚合日汇总数据，DbContext 已注册
3. **SankeyViewModel 重构**：从单选日期改为日期范围（StartDate/EndDate, DateTimeOffset?），新增 TopN 属性

### WMC9999 根因与修复

**错误信息**：`WMC9999: 未能找到任何适合于指定的区域性或非特定区域性的资源。请确保在编译时已将"Microsoft.UI.Xaml.Markup.Compiler.ErrorMessages.resources"正确嵌入或链接到程序集"XamlCompiler"`

**根因链**：
1. SankeyView.xaml 中 `{x:Bind ViewModel.StartDate}` / `{x:Bind ViewModel.EndDate}` / `{x:Bind ViewModel.TopN}` 引用了 SankeyViewModel 中不存在的属性
2. XAML 编译器检测到绑定错误后，尝试调用 `ResourceManager.GetString()` 本地化错误消息为 zh-CN
3. WinAppSDK 2.2.1 的 XamlCompiler 在 zh-CN 系统上缺少中文卫星程序集资源，ResourceManager 找不到 ErrorMessages.resources
4. 错误报告机制自身崩溃 → WMC9999 吞没真正的绑定错误

**修复**：为 SankeyViewModel 添加 StartDate（DateTimeOffset?）、EndDate（DateTimeOffset?）、TopN（int）属性，匹配 XAML 绑定引用。同时更新类型为 DateTimeOffset? 以兼容 CalendarDatePicker.Date。

**关键教训**：
- WMC9999 本身不是根因，而是错误报告链的崩溃。真正的 XAML 绑定错误被掩藏。
- 诊断方法：output.json 中 WMC9999 条目的前一个事件（perfXC_PageCodeGenStart）指示出问题的 XAML 文件名
- 如果 output.json 中没有任何 WMC 类型错误（Type=3）只有 WMC9999，则问题一定在 WMC9999 之前已开始处理的那个文件
- **不要**试图通过创建卫星程序集或修改区域设置绕过 WMC9999——修复根因的 XAML 绑定错误才是正确做法

### 产物

- `publish/PChabit.dll` — 2026-06-17 05:59:49

---

## 本次修复记录 (2026-06-16)：五页面卡死批量修复

### 背景

五个页面打开时卡死：应用统计-分类管理 (`CategoryManagementTab`)、网页访问 (`WebStatsTab` / `WebDetailsViewModel`)、数据管理 (`DataManagementPage`)、目标管理 (`GoalsPage`)、设置 (`SettingsPage`)。每次使用 AI 智能体修改后都会重现此问题。

### 统一根因

所有五个页面的 **Page 层使用了 `LoadInBackgroundAsync` 包装 ViewModel 加载方法**，而 ViewModel 内部已有自管理线程机制（`Task.Run` + `RunOnUIThreadAsync`），造成**双层 Task.Run 破坏 SynchronizationContext**，导致 DB 查询后 ObservableCollection 操作在线程池执行 → `COMException 0x8001010E`，或被 `catch (Exception)` 静默吞掉。

这正是**陷阱 7**的典型案例，且五个页面同时命中。

### 修复方案

**统一策略**：移除 Page 层的 `LoadInBackgroundAsync` 包装，改为直接 `await ViewModel.LoadDataAsync()` / `await ViewModel.InitializeAsync()`。ViewModel 内部负责通过两阶段架构管理线程：

| 页面 | Page 层改动 | ViewModel 层改动 |
|---|---|---|
| **CategoryManagementTab** | `LoadInBackgroundAsync` → 直接 `await ViewModel.InitializeAsync()` | 删除已废弃的 `LoadCategoriesAsync`/`LoadMappingsAsync`，统一使用 `InitializeAsync` |
| **WebStatsTab** | `LoadInBackgroundAsync` → 直接 `await ViewModel.LoadDataAsync()` | `LoadDataAsync` 改为 Phase 1 `Task.Run` (DB+计算) + Phase 2 `RunOnUIThreadAsync` (OC 更新) |
| **DataManagementPage** | `LoadInBackgroundAsync` → 直接 `await ViewModel.LoadOverviewAsync()` 等 | `LoadBackupsAsync` 包裹 `Task.Run` |
| **GoalsPage** | `LoadInBackgroundAsync` → 直接 `await ViewModel.LoadGoalsAsync()` | 已有两阶段架构，无需改动 |
| **SettingsPage** | `LoadInBackgroundAsync` → 直接 `await ViewModel.InitializeAsync()` | `InitializeAsync` 将所有文件 I/O + DB 查询合并到单个 `Task.Run` |

### 关键教训

1. **不要在所有 Page 上无差别使用 `LoadInBackgroundAsync`**。该包装仅在 ViewModel **内部未自管理线程**时使用。若 ViewModel 已用 `Task.Run` + `RunOnUIThreadAsync`，Page 层直接 `await` 即可。
2. **AI 智能体容易犯的模式错误**：为"防止 UI 卡顿"而在 Page 层加 `LoadInBackgroundAsync` 包装，却不知道 ViewModel 内部已有线程管理，导致双层 Task.Run 破坏 SynchronizationContext。
3. **诊断此问题的标准流程**：
   - 搜索 Page `.xaml.cs` 中 `LoadInBackgroundAsync` 调用
   - 搜索对应 ViewModel 中 `await Task.Run` 或 `RunOnUIThreadAsync`
   - 若两者同时存在 → 移除 Page 层包装
   - 若 ViewModel 无自管理线程 → 按两阶段架构重构 ViewModel

### 验证结果

发布 `publish\PChabit.dll` 时间戳更新为 `2026-06-16 09:50:31`，五个页面均可正常打开，数据正常显示，无卡死。

---

### 陷阱 13：SankeyView 数据丢失 — WebView2 初始化阻塞数据加载（2026-06-17 发现）

**症状**：
- 分析-应用流向（桑基图）页面无数据，图表始终为空
- 日志显示 `SankeyAggregator` 查询 AppSessions 表正常，但数据从未加载到 ViewModel
- 页面偶尔正常（WebView2 初始化快且 NavigationCompleted 触发时）

**根因**：
`SankeyView.xaml.cs` 中数据加载（`RefreshDataAsync`）仅在 `WebView2.NavigationCompleted` 事件中调用。如果 WebView2 初始化慢或 `NavigationCompleted` 未触发（如导航失败、CoreWebView2 初始化异常），数据就一直不加载。`SankeyAggregator` 直接查询 AppSessions 表，本身无缺陷。

**修复**（`SankeyView.xaml.cs` > `SankeyView_Loaded`）：
在 WebView2 初始化代码之前增加 `_ = ViewModel.LoadDataAsync();`，让数据加载与 WebView2 初始化并发进行，确保数据不管 WebView2 状态如何都能加载。

**教训**：
- 数据加载不应依赖 UI 渲染组件的生命周期事件。UI 组件可能初始化失败/慢/被跳过，但数据应该是独立加载的。
- 对"WebView2 就绪 → 加载数据"这种耦合模式，应改为"数据立即加载 + WebView2 就绪后推送渲染"的松耦合。

---

### 陷阱 15：SelfContained 缺失导致启动提示下载 .NET（2026-06-17 发现）

**症状**：
- 双击 `publish\PChabit.exe` 弹出".NET 环境下载"提示窗口
- 在同一台机器上 `dotnet run` 正常，但 publish 版本无法启动

**根因**：
csproj 设置了 `<RuntimeIdentifier>win-x64</RuntimeIdentifier>` 但缺少 `<SelfContained>true</SelfContained>`。在 .NET 9 中，`dotnet publish` 不传 `--self-contained` 参数时，即使指定了 RID，也默认使用框架依赖部署（Framework-Dependent），运行时不会被打包。`runtimeconfig.json` 中出现 `"framework": { "name": "Microsoft.NETCore.App" }` 而非 `"includedFrameworks"`。

**诊断方法**：
检查 `publish\PChabit.runtimeconfig.json`：
- 框架依赖：`"framework": { "name": "Microsoft.NETCore.App", "version": "9.0.0" }`
- 自包含：`"includedFrameworks": [{ "name": "Microsoft.NETCore.App", "version": "9.0.17" }]`

**修复**：
在 csproj 中添加 `<SelfContained>true</SelfContained>`。

**关联**：`WindowsAppSDKSelfContained` 只控制 WinAppSDK 运行时，不影响 .NET 运行时。两者需分别设置。

---

### 陷阱 14：退出程序时 SQLite WAL Checkpoint 导致系统卡顿（2026-06-17 发现）

**症状**：
- 退出程序时卡顿数秒，期间鼠标无法移动
- 卡顿发生在退出流程的最后阶段

**根因**：
`App.PerformShutdownAsync` 步骤 5 执行 `_serviceProvider?.Dispose()`，会递归释放所有单例服务。由于 SQLite 使用 WAL 模式，释放 `PChabitDbContext` 时会做 checkpoint 操作（将 WAL 日志内容合并回主数据库文件 `.db`），数据量大时产生大量磁盘 I/O，导致系统级卡顿、鼠标无法移动。

**修复**（`App.xaml.cs` > `PerformShutdownAsync` 步骤 5）：
跳过 `_serviceProvider?.Dispose()` 调用，直接记录日志后 `ForceTerminate`。进程退出时操作系统会回收所有资源（包括文件句柄、内存），WAL 日志文件（`.db-wal`）保留在磁盘上，下次打开数据库时 SQLite 会自动恢复。无需手工 checkpoint。

```csharp
// ✅ 修复后
try
{
    // 跳过 Dispose：SQLite WAL checkpoint 可能产生大量磁盘 IO 导致系统级卡顿
    // 进程退出时操作系统会回收所有资源，无需显式释放
    Log.Information("步骤 5/5: 跳过服务提供者释放，避免 SQLite WAL checkpoint 阻塞...");
}
catch (Exception ex)
{
    Log.Error(ex, "关闭流程记录失败");
}
```

**教训**：
- `ServiceProvider.Dispose()` 在持有数据库上下文的场景下是昂贵操作，不应在退出路径上同步等待。
- SQLite WAL checkpoint 的 I/O 开销可能大到影响整个系统的响应性，而数据库完整性由 WAL 机制保证，不强制需要 shutdown checkpoint。

---

### 陷阱 16：备份保留分数硬编码导致设置无效（2026-06-17 发现）

**症状**：
- 用户在数据管理页面设置"保留分数"（如改为 3），但备份时仍保留 7 个旧备份
- WebDAV 云端同步同样不遵守用户设置的最大保留数量

**根因**：
两处硬编码导致了相同的问题模式：

1. **BackupService.CleanupOldBackupsAsync()**：`maxBackups` 硬编码为 `7`，从未读取 `ISettingsService.MaxBackupCount`。`BackupService` 构造函数仅注入 `IDbContextFactory`，缺少 `ISettingsService` 依赖。

2. **DataManagementViewModel.MaxCloudBackupCount**：ViewModel 中初始化为 `5`，但 `AppSettings` 类根本不存在 `MaxCloudBackupCount` 属性。用户修改云端保留数量后无法持久化，每次重启恢复为 5。

**修复**（涉及 4 个文件）：

(1) `AppSettings` — 新增字段：
```csharp
public int MaxCloudBackupCount { get; set; } = 5;
```

(2) `ISettingsService` — 接口新增：
```csharp
int MaxCloudBackupCount { get; set; }
```

(3) `BackupService` — 注入 `ISettingsService`，从设置读取：
```csharp
// 构造函数增加 ISettingsService 参数
public BackupService(IDbContextFactory<PChabitDbContext> dbContextFactory, ISettingsService settingsService)

// CleanupOldBackupsAsync 替换硬编码
var maxBackups = _settingsService.MaxBackupCount;
if (maxBackups <= 0) return;
```

(4) `DataManagementViewModel` — 从 settings 读写云备份数量：
```csharp
// 构造函数中
_maxCloudBackupCount = settingsService.MaxCloudBackupCount;

// SaveWebDAVSettingsAsync 中
_settingsService.MaxCloudBackupCount = MaxCloudBackupCount;
```

**教训**：
- 用户可配置的数值绝对不能硬编码。所有"XX上限"、"XX阈值"类数字必须从 SettingsService 读取。
- 新增用户配置项时，必须走完整链路：`AppSettings 字段 → ISettingsService 接口 → SettingsService 属性 → ViewModel 读写`，遗漏任何一环都会导致设置"无效"。
- `BackupService` 作为 Infrastructure 层服务，注入 `ISettingsService`（同层）不会产生循环依赖。

---

### 陷阱 17：WebDAV ListFiles 返回绝对路径导致删除/下载失败（2026-06-17 发现）

**症状**：
- WebDAV 云端备份列表能正常显示
- 手动删除云端备份或自动清理旧备份时，"删除失败"
- 恢复云端备份（下载）也会失败
- 但上传和连接测试正常

**根因**：

`ListFilesAsync` 从 PROPFIND 响应的 `<d:href>` 中提取 `itemPath`，该值是服务器返回的绝对路径（如 `/remote.php/dav/files/user/pchabit_backup.zip`）。旧代码直接将其赋值给 `FullPath`：

```csharp
var fullPath = itemPath;  // "/remote.php/dav/files/user/pchabit_backup.zip"
```

而 `DeleteFileAsync` / `DownloadFileWithProgressAsync` 将 `fileName` 拼接到用户提供的 URL 后：

```csharp
var fullUrl = url.TrimEnd('/') + "/" + fileName;
// 结果: https://server.com/remote.php/dav/files/user//remote.php/dav/files/user/...
```

形成路径重复，HTTP 404。

**修复**（仅 WebDAVSyncService.cs，`ListFilesAsync` 方法）：

核心思路：用 `new Uri(listUrl).AbsolutePath` 提取 URL 的路径部分，从服务器返回的 `itemPath` 中剥离该前缀，得到相对路径。

```csharp
// 新增：提取 URL 的路径部分
var urlPath = new Uri(listUrl).AbsolutePath.TrimEnd('/');

// 规范化 href：如果服务器返回完整 URL，提取路径
var serverPath = itemPath;
if (serverPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
    serverPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
{
    serverPath = new Uri(serverPath).AbsolutePath.TrimEnd('/');
}

// 用 URL 路径匹配替换原来的 basePath 字符串比较
if (serverPath.Equals(urlPath, StringComparison.OrdinalIgnoreCase)) continue;

// 计算相对路径：剥离 URL 路径前缀
var fullPath = name; // 默认回退到文件名
if (serverPath.StartsWith(urlPath + "/", StringComparison.OrdinalIgnoreCase))
{
    fullPath = serverPath[(urlPath.Length + 1)..];
}
```

**教训**：
- WebDAV PROPFIND 的 `href` 可能是绝对路径（`/path/to/file`）或完整 URL（`https://server/path/to/file`），客户端必须规范化处理
- "列表正常显示但操作失败"是典型的路径拼接错误信号——列表用的是服务器解析后的值（`LastModified`/`Size` 均在 XML 中独立返回），只有路径被用于后续请求
- 所有涉及远程文件列表的 WebDAV 操作（删除、下载、移动）都依赖 `FullPath` 字段，一处路径计算错误会影响所有下游操作
- 不同 WebDAV 服务器（Nextcloud / ownCloud / Apache mod_dav）返回的 href 格式可能不同，需要同时处理全 URL 和绝对路径两种形式

---

## 诊断工具清单

### 1. Windows 事件查看器（首选）

**路径**：`eventvwr.msc` → Windows 日志 → 应用程序

**查找**：
- 级别 = "错误"，来源 = "Application Error" / ".NET Runtime"
- 异常码对照：
  - `0xc000027b` → 栈缓冲区溢出 / XAML 资源解析失败（陷阱 1）
  - `0x80131509` → `InvalidOperationException`（陷阱 3: DI 注册遗漏）
  - `0xe0434352` → 通用 CLR 异常，查看详细事件获取具体异常类型

### 2. 应用日志文件

**路径**：`%LocalAppData%\PChabit\Logs\app-{date}.log`

**使用方式**：
- `Ctrl+F` 搜索 `Fatal` / `Error` / `Exception`
- 关注最后几行：崩溃前的日志是诊断入口
- 如果某事件处理函数（如 `OnAddCategoryClick`）**完全没有日志输出**，说明崩溃发生在该函数调用之前（如 DI 解析阶段）

### 3. Build vs Publish 二分法

当发布版本崩溃但 `dotnet run` 正常时，问题在发布配置：

```
dotnet build -c Release    →  运行 bin/Release/.../PChabit.exe  →  正常？
dotnet publish -c Release  →  运行 publish/PChabit.exe          →  崩溃？
                                                        ↓
                                  对比 csproj 中 Publish 专属配置
                                  重点检查: PublishReadyToRun, PublishTrimmed
```

### 4. 属性因子隔离法

在 csproj 中逐个注释/还原可疑属性，每次只改一个，重新发布测试：

```powershell
# 修改 csproj → dotnet publish → 测试 → 还原 → 下一个属性
```

### 5. 与正常工作的同类代码对比

当某个功能（如"网页分类管理"）正常工作但类似功能（如"应用分类管理"）有问题时：

```powershell
# 逐行对比两个文件的差异
code --diff src/PChabit.App/Views/WebsiteCategoryTab.xaml.cs `
          src/PChabit.App/Views/CategoryManagementTab.xaml.cs
```

常见遗漏点：
- `dialog.XamlRoot = XamlRoot` 是否缺失
- DI 注册是否遗漏 Dialog 本身
- 事件绑定方式是否一致

### 6. 编译后 DLL 时间戳检查

当怀疑修改未生效时，确认编译产物已更新：

```powershell
(Get-Item "E:\SYNC\My VS\PChabit\publish\PChabit.dll").LastWriteTime
```

### 7. dotnet build 输出分析

```powershell
dotnet build src/PChabit.App/PChabit.App.csproj -c Release 2>&1 | Select-String "error|warning"
```

当前已知的无害警告：
- `MVVMTK0045` — CommunityToolkit 源生成器预分配警告（不影响运行）
- `NETSDK1206` — 已通过 `<NoWarn>` 抑制

---

## 日常维护注意事项

1. **NuGet 版本**：`Microsoft.WindowsAppSDK` 和 `CommunityToolkit.Mvvm` 使用浮动版本（`*`），升级前需验证兼容性
2. **SQLite WAL**：数据库路径硬编码在 `App.xaml.cs` 中，默认 `%LocalAppData%\PChabit\pchabit.db`
3. **Serilog 配置**：日志按天滚动，7 天保留，路径 `%LocalAppData%\PChabit\Logs\app-.log`
4. **WinUI unpkg 部署**：`dotnet publish` 后可能需要手动复制 XBF/PRI 文件（csproj 中有 `CopyWinUIAssetsToPublish` Target 处理此问题）
5. **DI 容器**：`ServiceConfiguration.ConfigureServices()` 是唯一注册入口，不要在其他地方分散注册
6. **Entity DateTime Kind**：项目中使用 `DateTime.SpecifyKind(DateTime.Parse(v), DateTimeKind.Unspecified)` 值转换器，新 Entity 的 `DateTime` 属性需注意 Kind 一致性

---

## 本次修复方法论总结 (2026-06-17 双 BUG 一键修复)

### 事件背景

用户报告两个问题：(1) 分析-应用流向页面数据丢失，(2) 退出程序时卡顿到鼠标无法移动。此前 5 个页面卡死的批量修复已通过 `DbSafeViewModel` 两阶段架构解决。

### 诊断方法论

#### 问题1：数据加载不依赖 UI 渲染

**错误思维**：数据加载 → 等 WebView2 就绪 → 渲染。这是典型的 UI 耦合数据加载反模式。

**正确思维**：数据加载与 UI 渲染应该是**两条并行管道**：
```
数据管道: _ = ViewModel.LoadDataAsync()    // 立即启动，不等待任何 UI
渲染管道: await EnsureCoreWebView2Async() → NavigationCompleted → 拿数据渲染
```

**关键代码模式**（SankeyView.xaml.cs）：
```csharp
private async void SankeyView_Loaded(object sender, RoutedEventArgs e)
{
    // ✅ 数据加载与 WebView2 初始化并行
    _ = ViewModel.LoadDataAsync();          // 不等待，立即启动数据加载

    if (_isWebViewInitialized) return;
    await ChartWebView.EnsureCoreWebView2Async();  // UI 初始化独立进行
    // ...
}
```

**通用原则**：任何"等 UI 组件就绪再加载数据"的耦合都是脆弱点。UI 组件可能失败/慢/被跳过，数据加载应该是独立的、容错的。

#### 问题2：退出路径属于热路径，禁止昂贵操作

**错误思维**：退出时调用 Dispose 是"良好的资源管理实践"。

**正确思维**：`ServiceProvider.Dispose()` 会递归释放所有单例服务，在数据库场景中这是**极其昂贵**的操作——SQLite WAL checkpoint 将 WAL 日志文件内容合并回主 DB，可能产生数百 MB 的磁盘 I/O。在退出路径上执行此操作等于"在顾客离店时拖地"——阻塞用户直到 IO 完成。

**热路径优化原则**：
```
退出路径 = 热路径
├── 可以做的: 保存未写入数据、停止后台任务、记录日志
├── 不能做的: 磁盘 I/O 密集型操作、同步等待、远程调用
└── 交给 OS: 内存释放、文件句柄关闭、WAL 自动恢复
```

**关键代码模式**（App.xaml.cs）：
```csharp
// 步骤 5/5: 跳过 ServiceProvider.Dispose
// SQLite WAL checkpoint 可能产生大量磁盘 IO 导致系统级卡顿
// 进程退出时 OS 回收所有资源，下次启动 SQLite 自动恢复 WAL
Log.Information("步骤 5/5: 跳过服务提供者释放，直接终止");
ForceTerminate();
```

### 跨智能体经验传递的本质

本项目 14 条陷阱中，超过半数（5.5/5.6/7/8/10/12/13/14）是**架构级认知陷阱**——不是语法错误，而是"看起来正确的代码在特定上下文中是错的"。这些陷阱无法通过静态分析或 linter 发现，只能通过：

1. **运行时崩溃**（COMException 0x8001010E / WMC9999 / DllNotFoundException）
2. **性能退化**（退出卡顿 / 页面空白 / 数据丢失）
3. **本文件的记录**（唯一传递渠道）

**因此本文件的存在价值等于项目可维护性的上限。若不读此文档，任何 AI 智能体的修复都有 80% 概率重蹈覆辙。**

---

### 陷阱 18：网页统计分类不显示 + 添加域名映射流程繁琐 + 长时间浏览同一页面统计时长为0（2026-06-17 发现）

**症状**：
- 网页访问统计页的"分类"列始终显示硬编码的分类名（如 google.com→"搜索"），忽略用户在 WebsiteCategoryTab 中自定义的域名映射
- CategoryOptions 下拉列表始终为硬编码的 10 个固定分类，与 WebsiteCategoryTab 中实际创建的分类不一致
- 在 WebsiteCategoryTab 添加域名映射时需要手动输入域名（如 `github.com`），非常繁琐
- 长时间浏览同一页面不切换标签时，该页面的访问时长统计为 0 分钟

**根因（3 个独立问题）**：

1. **WebDetailsViewModel.GetCategory() 仅调用 GetCategoryFallback()**：
   `GetCategory()` 直接返回 `GetCategoryFallback(domain)`，而 `GetCategoryFallback` 是静态硬编码的域名关键词匹配（如 google→搜索），完全忽略了 `WebsiteDomainMappings` 表中的用户自定义映射数据。也没有调用 `WebsiteCategoryService.GetCategoryForDomainAsync`。

2. **WebsiteCategoryTab 的 OnAddMappingClick 只有手动输入模式**：
   旧版 `OnAddMappingClick` 只提供一个 TextBox 让用户手动输入域名模式，没有提供从最近访问域名中选择的快捷方式。用户需要记住并准确输入域名，体验很差。

3. **DataCollectionService 未定期保存活跃网页会话**：
   `RunPeriodicTimerAsync`（30 秒间隔）只调用了 `SaveBackgroundSessionsPeriodicallyAsync()` 和 `CheckDailyAggregationAsync()`。活跃的网页会话（`_activeWebSessions`）只在标签切换/关闭时才会被保存。如果用户长时间浏览同一页面不切换标签，该会话的 `EndTime` 始终为 null → `Duration` 为 `TimeSpan.Zero` → 统计显示 0 分钟。

**修复**：

**问题1** — `WebDetailsViewModel.cs`：

(1) 添加 `_domainCategoryMap` 字典字段和 `DomainMatches` 静态方法，支持通配符域名模式匹配：
```csharp
private Dictionary<string, string> _domainCategoryMap = new();

private static bool DomainMatches(string domain, string pattern)
{
    var lowerPattern = pattern.ToLower();
    if (lowerPattern.StartsWith("*."))
    {
        var suffix = lowerPattern[2..];
        return domain.EndsWith("." + suffix) || domain == suffix;
    }
    return domain == lowerPattern || domain.EndsWith("." + lowerPattern);
}
```

(2) 添加 `LoadDomainCategoryMapAsync()` 从 `WebsiteDomainMappings` 表加载用户自定义映射。

(3) 重写 `GetCategory()`：先查 `_domainCategoryMap`，命中则返回用户自定义分类；未命中才 fallback 到 `GetCategoryFallback()`。

(4) 在 `LoadStatsOnBackgroundAsync()` 的 `LoadSessionsAsync()` 之后调用 `_domainCategoryMap = await LoadDomainCategoryMapAsync()`。

(5) `CategoryOptions` 从硬编码改为从 `WebsiteCategories` 表动态加载（`LoadCategoryOptionsAsync()`），在 `LoadDataAsync()` 中调用。

**问题2** — `WebsiteCategoryTab.xaml.cs`：

重写 `OnAddMappingClick`，参考 `CategoryManagementTab.xaml.cs` 的 `OnAddMappingClick` 实现两个模式：
- 模式1："从最近的访问中添加" — ComboBox 列出最近 50 个不同域名（按最近访问时间排序），用户直接选择
- 模式2："手动输入域名" — TextBox 手动输入，支持通配符（如 `*.github.com`）

添加 `LoadRecentDomainsAsync()` 方法从 `WebSessions` 表查询最近域名。需要注入 `IServiceScopeFactory`。

**问题3** — `DataCollectionService.cs`：

(1) 添加 `SaveActiveWebSessionsPeriodicallyAsync()` 方法：每 30 秒对所有活跃网页会话做快照保存，快照后重置起始时间（与后台应用会话模式一致）。

(2) 在 `RunPeriodicTimerAsync` 回调中，`SaveBackgroundSessionsPeriodicallyAsync()` 之后插入 `await SaveActiveWebSessionsPeriodicallyAsync()`。

**教训**：
- 分类展示必须优先使用用户自定义的映射数据，硬编码只应作为兜底。这是"用户数据优先于预置规则"的通用原则。
- UI 交互中，对用户需要输入的实体（域名、程序名等），应优先提供"从已有数据中选择"的快捷方式，而不是强制手动输入。最近使用的数据是最有价值的候选列表。
- 数据收集的定时器必须覆盖所有活跃状态的持久化。只依赖"状态切换事件"来保存数据（如标签切换），在用户长时间保持同一状态时会丢失数据。原则：任何内存中的活跃状态，都必须在定时器中定期快照保存。

---

### 陷阱 19：浏览器插件弹窗活跃时间始终显示 0m（2026-06-17 发现）

**症状**：
- 点击浏览器工具栏中的 Tai Activity Tracker 插件图标，弹窗中"活跃时间"始终显示 `0m`

**根因**：
`popup.js` 中存在异步时序问题。弹窗初始化流程：

1. 第 18 行：`let sessionStart = Date.now();` — 内存变量初始化为当前时间
2. 第 105 行（旧）：`updateActiveTime();` — 同步调用，此时 `sessionStart` 仍是步骤 1 的 `Date.now()`，`Date.now() - sessionStart ≈ 0`，显示 `0m`
3. 第 93 行：`chrome.storage.local.get(...)` — 异步读取 `sessionStart`，回调中才把 `sessionStart` 更新为真实值

由于 `chrome.storage.local.get` 是异步的，第 105 行在其回调执行之前就已运行。即使用户打开弹窗后不关闭，`setInterval(updateActiveTime, 60000)` 也要等 60 秒后才更新，而浏览器插件弹窗通常几秒内就会被用户点击外部关闭，导致用户永远看不到非零值。

**修复**：

`popup.js`：将 `updateActiveTime()` 的首次调用移入 `chrome.storage.local.get` 的回调内部，确保在 `sessionStart` 被正确赋值之后再计算时间差。同时删除末尾的独立 `updateActiveTime()` 调用。

```diff
     chrome.storage.local.get(['pagesViewed', 'sessionStart', 'connected'], (result) => {
         ...
+        updateActiveTime();  // 在 sessionStart 就绪后再首次更新
     });
     
-    updateActiveTime();  // 删除：此时 sessionStart 还未从存储中加载
```

**教训**：
- 浏览器扩展的 popup 页面每次打开都会重新初始化 JS 环境，`chrome.storage.*` API 全部是异步的。任何依赖存储数据初始化的 UI 更新都必须放在存储回调内部，绝不能靠全局同步变量凑巧。
- 插件弹窗生命周期极短（用户快速瞥一眼即关闭），仅靠 `setInterval` 兜底无法覆盖首次展示。首次渲染数据必须保证在异步数据就绪后才触发。
- `sessionStart` 在 `background.js` 顶层会被 `chrome.storage.local.set({ sessionStart: Date.now() })` 写入，Service Worker 休眠后被唤醒时也会重置，今后若需要"跨插件生命周期累计活跃时间"，应考虑用 `chrome.storage.session` 或 `chrome.alarms` 替代简单的 `Date.now()` 打点。

---

### 陷阱 20：浏览器插件设置按钮打开无法访问的页面（2026-06-17 发现）

**症状**：
- 点击插件弹窗中的"设置"按钮，浏览器新标签页打开 `https://localhost:8765`，显示"无法访问此页面"

**根因（两层）**：

1. **协议错误**：`popup.js` 中设置按钮 URL 为 `https://localhost:8765`，但 `WebSocketServer` 只监听 `http://localhost:8765/`。浏览器不会自动从 HTTPS 降级到 HTTP，直接报错。

2. **服务端不支持 HTTP 页面**：`WebSocketServer.AcceptClientsAsync` 只处理 `IsWebSocketRequest == true` 的请求，非 WebSocket 请求直接返回 400 Bad Request。即使协议改为 HTTP，也无法打开任何页面。

**修复**：

**popup.js**：`https` → `http`。

**WebSocketServer.cs**：新增 `ServeSettingsPage` 方法，当 HTTP GET 请求到达时返回一个基本的设置页面（HTML），包含服务状态和客户端连接数信息。

```csharp
// AcceptClientsAsync 中新增分支：
else if (context.Request.HttpMethod == "GET")
{
    _ = Task.Run(() => ServeSettingsPage(context), cancellationToken);
}
```

**教训**：
- 在 WebSocket 服务端复用 `HttpListener` 时，非 WebSocket 的 HTTP GET 请求不应直接返回 400，可借此提供轻量的状态/设置页面，改善用户体验。
- 浏览器扩展的硬编码 URL 应考虑协议、端口与后端实际监听配置的一致性。若后端未启用 TLS，前端不应使用 `https://`。

---

### 陷阱 19：System.Timers.Timer 回调运行在线程池，重启钩子安装到非 UI 线程导致键盘统计丢失（2026-06-18 发现）

**症状**：
- 键盘使用统计数据几乎为零（如全天仅记录到 2 次按键）
- 鼠标和应用统计正常（大量点击和应用切换）
- 日志中不断出现"键盘钩子已 X 分钟无活动 (第1/2/3次检测)"和"键盘钩子重启完成，IsRunning=true"的循环
- 每次重启后 IsRunning=true，但依然无活动，立刻再次触发 3 次失效 → 重启

**根因**：
`MonitorManager` 使用 `System.Timers.Timer`（`_healthCheckTimer`）每 60 秒检查键盘/鼠标钩子是否存活。`System.Timers.Timer.Elapsed` 回调**运行在线程池线程上**，而非 UI 线程。

当健康检查检测到键盘钩子 5 分钟无活动并触发 `RestartKeyboardMonitor()` 时：
1. `_keyboardMonitor.Stop()` — 正确卸载旧钩子
2. `_keyboardMonitor.Start()` — 调用 `SetWindowsHookEx(WH_KEYBOARD_LL, ...)`，钩子被安装到**当前线程（线程池）**
3. `SetWindowsHookEx` 返回有效 `IntPtr`，`IsRunning` 被设为 `true`
4. 但线程池线程**没有消息泵**（GetMessage/PeekMessage loop），`HookCallback` **永远不会被调用**
5. 钩子持续无活动 → 3 分钟后再次触发重启 → 死循环

数据流证据（日志）：
```
07:11:36 [INF] 监控器已启动 - KeyboardMonitor: true  ← UI 线程安装，正常
07:22:36 [WRN] 键盘钩子已 6 分钟无活动 (第1次检测)
07:24:36 [WRN] 键盘钩子连续 3 次检测失效，尝试重启
07:24:36 [INF] 键盘钩子重启完成，IsRunning=true  ← 线程池安装，从此失效
07:25:36 [WRN] 键盘钩子已 9 分钟无活动 (第1次检测)  ← 立即再次触发
...无限循环...
```

**为何鼠标不受影响**：鼠标有用户持续点击，`LastActivityTime` 始终在更新，健康检查不会触发鼠标重启。若鼠标也长时间无活动，同样会触发此陷阱。

**修复**（涉及 2 个文件）：

**MonitorManager.cs** — 增加 UI 线程调度机制：

```csharp
// 新增属性：由 App 在 UI 线程上注入 DispatcherQueue 调度器
public Action<Action>? UIDispatcher { get; set; }

// RestartKeyboardMonitor 改造：通过 UIDispatcher 派发到 UI 线程
private void RestartKeyboardMonitor()
{
    if (UIDispatcher != null)
    {
        UIDispatcher(DoRestartKeyboard);  // 派发到 UI 线程
    }
    else
    {
        DoRestartKeyboard();  // 无调度器时的回退（初始安装场景）
    }
}

private void DoRestartKeyboard()
{
    try
    {
        _keyboardMonitor.Stop();
        _keyboardMonitor.Start();  // 现在在 UI 线程上安装钩子
        Log.Information("键盘钩子重启完成，IsRunning={IsRunning}", _keyboardMonitor.IsRunning);
    }
    catch (Exception ex)
    {
        Log.Error(ex, "重启键盘钩子失败");
    }
}
// RestartMouseMonitor 同样改造
```

**App.xaml.cs** — 注入 DispatcherQueue：

```csharp
_monitorManager = _serviceProvider!.GetRequiredService<MonitorManager>();
// 健康检查定时器回调在线程池执行，重启钩子时必须派发回 UI 线程
_monitorManager.UIDispatcher = action =>
    Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()
        .TryEnqueue(() => action());
```

**教训**：
- **Win32 低级钩子（WH_KEYBOARD_LL / WH_MOUSE_LL）的线程亲和性是硬约束**：钩子必须安装在有消息泵的线程上，否则会静默失效——`SetWindowsHookEx` 返回成功，`IsRunning` 为 true，但回调永不被调用。
- `System.Timers.Timer` / `System.Threading.Timer` 的回调都在线程池执行。任何需要在 UI 线程上执行的操作（钩子管理、UI 更新、WinRT 对象操作），必须通过 `DispatcherQueue.TryEnqueue()` 或等效机制显式派发。
- 此类 Bug 的隐蔽性极高：因为"重启成功"的日志明确显示 `IsRunning=true`，从代码阅读角度看不出异常。只有理解 Win32 钩子与线程的消息泵关系才能定位。
- 用 `Action<Action>` 委托替代在 Infrastructure 层直接引用 WinUI 类型（如 `Microsoft.UI.Dispatching.DispatcherQueue`），保持了 Infrastructure 层不依赖 WinUI 的清洁架构约束。
