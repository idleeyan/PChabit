# Tai 项目重构计划书 V2.0

> 面向 AI 助手的个人使用习惯数据收集系统重构方案  
> 版本: 2.0.0  
> 最后更新: 2026-02-13  
> 状态: **Phase 7 已完成 - 项目已发布**

---

## 一、项目愿景与目标

### 1.1 核心愿景

构建一个**智能化的个人数字行为分析系统**，不仅记录用户使用PC的行为数据，更重要的是：
- **全面采集**：捕获所有能反映用户习惯的数据维度
- **智能导出**：为AI提供结构化、上下文丰富的数据
- **自我认知**：让用户通过数据更了解自己，让AI更懂用户

### 1.2 重构目标

| 目标层级 | 描述 | 关键指标 | 状态 |
|---------|------|---------|------|
| **基础层** | 稳定、高性能的数据采集 | 内存<30MB, CPU<0.5% | ✅ |
| **数据层** | 全面、细粒度的习惯数据 | 10+ 数据维度 | ✅ |
| **智能层** | AI友好的数据导出格式 | 结构化JSON/Markdown | ✅ |
| **体验层** | 现代化的Windows 11原生UI | 流畅、美观、易用 | ✅ |

---

## 二、项目架构

### 2.1 目录结构

```
src/
├── Tai.Core/              # 领域层 - 实体、接口、值对象
│   ├── Entities/          # 实体类
│   │   ├── AppSession.cs          # 应用会话实体
│   │   ├── KeyboardSession.cs     # 键盘会话实体
│   │   ├── MouseSession.cs        # 鼠标会话实体
│   │   ├── WebSession.cs          # 网页会话实体
│   │   ├── DailyPattern.cs        # 日模式实体
│   │   ├── WorkflowSession.cs     # 工作流会话实体
│   │   ├── UsagePattern.cs        # 使用模式实体
│   │   └── EntityBase.cs          # 实体基类
│   │
│   ├── Interfaces/        # 接口定义
│   │   ├── IMonitor.cs            # 监控器基础接口
│   │   ├── IAppMonitor.cs         # 应用监控接口
│   │   ├── IKeyboardMonitor.cs    # 键盘监控接口
│   │   ├── IMouseMonitor.cs       # 鼠标监控接口
│   │   ├── IWebMonitor.cs         # 网页监控接口
│   │   ├── IRepository.cs         # 仓储接口
│   │   ├── IUnitOfWork.cs         # 工作单元接口
│   │   ├── IEventBus.cs           # 事件总线接口
│   │   ├── IExportService.cs      # 导出服务接口
│   │   ├── IBehaviorAnalyzer.cs   # 行为分析接口
│   │   ├── IContextResolver.cs    # 上下文解析接口
│   │   ├── IBackgroundAppSettings.cs # 后台应用设置接口
│   │   └── ExportData.cs          # 导出数据定义
│   │
│   └── ValueObjects/      # 值对象
│       └── GeometryTypes.cs       # 几何类型定义
│
├── Tai.Infrastructure/    # 基础设施层 - 数据访问、外部服务实现
│   ├── Data/              # 数据库相关
│   │   ├── TaiDbContext.cs        # EF Core 数据库上下文
│   │   ├── TaiDbContextFactory.cs # 设计时数据库上下文工厂
│   │   ├── DatabaseInitializer.cs # 数据库初始化与迁移
│   │   ├── UnitOfWork.cs          # 工作单元实现
│   │   ├── Repository.cs          # 通用仓储实现
│   │   ├── AppSessionRepository.cs    # 应用会话仓储
│   │   ├── KeyboardSessionRepository.cs # 键盘会话仓储
│   │   ├── MouseSessionRepository.cs   # 鼠标会话仓储
│   │   ├── WebSessionRepository.cs     # 网页会话仓储
│   │   └── DailyPatternRepository.cs   # 日模式仓储
│   │
│   ├── Formatters/        # 导出格式化器
│   │   ├── JsonExportFormatter.cs     # JSON 格式导出
│   │   ├── MarkdownExportFormatter.cs # Markdown 格式导出
│   │   └── AiPromptExportFormatter.cs # AI 提示词格式导出
│   │
│   ├── Helpers/           # Win32 API 辅助类
│   │   └── Win32Helper.cs         # Windows API 封装
│   │
│   ├── Monitoring/        # 监控器实现
│   │   ├── AppMonitor.cs          # 应用监控器
│   │   ├── KeyboardMonitor.cs     # 键盘监控器
│   │   ├── MouseMonitor.cs        # 鼠标监控器
│   │   └── WebMonitor.cs          # 网页监控器
│   │
│   └── Services/          # 服务实现
│       ├── EventBus.cs            # 事件总线实现
│       ├── MonitorManager.cs      # 监控管理器
│       ├── ExportService.cs       # 导出服务
│       ├── WebSocketServer.cs     # WebSocket 服务器
│       ├── CacheService.cs        # 内存缓存服务
│       ├── TaskScheduler.cs       # 任务调度器
│       ├── PerformanceMonitor.cs  # 性能监控服务
│       ├── AppCategoryResolver.cs # 应用分类解析
│       ├── AppInfoResolver.cs     # 应用信息解析
│       ├── ShortcutDetector.cs    # 快捷键检测器
│       ├── TypingBurstDetector.cs # 打字片段检测器
│       └── SerilogConfigurator.cs # Serilog 配置
│
├── Tai.Application/       # 应用层 - 用例、查询/命令处理
│   ├── Aggregators/       # 数据聚合服务
│   │   ├── DailyAggregator.cs     # 日数据聚合
│   │   ├── SessionAggregator.cs   # 会话聚合
│   │   └── PatternDetector.cs     # 模式检测器
│   │
│   └── Analysis/          # 行为分析服务
│       ├── BehaviorAnalyzer.cs    # 行为分析器
│       └── ContextResolver.cs     # 上下文解析器
│
├── Tai.App/               # 表示层 - WinUI 3 应用
│   ├── Assets/            # 应用资源
│   │   └── (应用图标等)
│   │
│   ├── Converters/        # 值转换器
│   │   └── Converters.cs          # XAML 值转换器集合
│   │
│   ├── Services/          # 应用服务
│   │   ├── NavigationService.cs   # 导航服务
│   │   ├── ServiceConfiguration.cs # 依赖注入配置
│   │   ├── DataCollectionService.cs # 数据收集服务
│   │   ├── AppIconService.cs      # 应用图标服务
│   │   └── TrayService.cs         # 系统托盘服务
│   │
│   ├── ViewModels/        # 视图模型
│   │   ├── ViewModelBase.cs       # ViewModel 基类
│   │   ├── MainViewModel.cs       # 主视图模型
│   │   ├── DashboardViewModel.cs  # 仪表盘视图模型
│   │   ├── TimelineViewModel.cs   # 时间线视图模型
│   │   ├── AnalyticsViewModel.cs  # 分析页视图模型
│   │   ├── AppStatsViewModel.cs   # 应用统计视图模型
│   │   ├── ExportViewModel.cs     # 导出页视图模型
│   │   ├── SettingsViewModel.cs   # 设置页视图模型
│   │   └── DetailDialogViewModel.cs # 详情对话框视图模型
│   │
│   ├── Views/             # 页面视图
│   │   ├── ShellPage.xaml         # 外壳页面(导航框架)
│   │   ├── MainPage.xaml          # 主页面
│   │   ├── DashboardPage.xaml     # 仪表盘页面
│   │   ├── TimelinePage.xaml      # 时间线页面
│   │   ├── AnalyticsPage.xaml     # 分析页面
│   │   ├── AppStatsPage.xaml      # 应用统计页面
│   │   ├── ExportPage.xaml        # 导出页面
│   │   ├── SettingsPage.xaml      # 设置页面
│   │   └── DetailDialog.xaml      # 详情对话框
│   │
│   ├── App.xaml                   # 应用程序定义
│   ├── App.xaml.cs                # 应用程序入口
│   └── Package.appxmanifest       # 应用包清单
│
├── Tai.Tests/             # 测试项目
│   ├── Monitors/          # 监控器测试
│   │   └── MonitorTests.cs
│   ├── Analysis/          # 分析服务测试
│   │   └── AnalysisTests.cs
│   └── Aggregators/       # 聚合服务测试
│       └── AggregatorTests.cs
│
└── extensions/            # 浏览器扩展
    └── tai-browser-extension/
        ├── manifest.json          # Chrome/Edge 扩展清单
        ├── manifest-firefox.json  # Firefox 扩展清单
        ├── background.js          # 后台脚本
        ├── content.js             # 内容脚本
        ├── popup.html             # 弹出页面
        ├── popup.js               # 弹出脚本
        └── icons/                 # 扩展图标
```

### 2.2 架构图

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           Presentation Layer (Tai.App)                       │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                         WinUI 3 应用                                 │   │
│  │  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  ┌────────────┐ │   │
│  │  │  Dashboard  │  │  Timeline   │  │  Analytics  │  │  Settings  │ │   │
│  │  └─────────────┘  └─────────────┘  └─────────────┘  └────────────┘ │   │
│  │  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐                  │   │
│  │  │  AppStats   │  │   Export    │  │ DetailDialog│                  │   │
│  │  └─────────────┘  └─────────────┘  └─────────────┘                  │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────────┘
                                      │
                                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                      Application Layer (Tai.Application)                     │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                        Use Case Services                             │   │
│  │  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  ┌────────────┐ │   │
│  │  │DailyAggreg. │  │SessionAggr. │  │PatternDetect│  │BehaviorAnal│ │   │
│  │  └─────────────┘  └─────────────┘  └─────────────┘  └────────────┘ │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────────┘
                                      │
                                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                         Domain Layer (Tai.Core)                              │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                          Domain Entities                             │   │
│  │  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  ┌────────────┐ │   │
│  │  │  AppSession │  │KeybSession  │  │MouseSession │  │ WebSession │ │   │
│  │  └─────────────┘  └─────────────┘  └─────────────┘  └────────────┘ │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                        Domain Interfaces                             │   │
│  │  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  ┌────────────┐ │   │
│  │  │ IMonitor    │  │IRepository  │  │ IUnitOfWork │  │IEventBus   │ │   │
│  │  └─────────────┘  └─────────────┘  └─────────────┘  └────────────┘ │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────────┘
                                      │
                                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                    Infrastructure Layer (Tai.Infrastructure)                 │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                        Data Access                                   │   │
│  │  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  ┌────────────┐ │   │
│  │  │ TaiDbCtx    │  │ Repositories│  │ UnitOfWork  │  │DbInitializer│ │   │
│  │  └─────────────┘  └─────────────┘  └─────────────┘  └────────────┘ │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                        Monitoring                                    │   │
│  │  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  ┌────────────┐ │   │
│  │  │ AppMonitor  │  │KeyboardMon. │  │ MouseMonitor│  │ WebMonitor │ │   │
│  │  └─────────────┘  └─────────────┘  └─────────────┘  └────────────┘ │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                        Services                                      │   │
│  │  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  ┌────────────┐ │   │
│  │  │EventBus     │  │ExportService│  │WebSocketSrv │  │CacheService│ │   │
│  │  └─────────────┘  └─────────────┘  └─────────────┘  └────────────┘ │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────────┘
                                      │
                                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                          External Systems                                   │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  ┌────────────────────┐ │
│  │   SQLite    │  │  Win32 API  │  │  WebSocket  │  │ Browser Extensions │ │
│  │  Database   │  │  (Hooks)    │  │  (Port 8765)│  │ (Chrome/Edge/Fire) │ │
│  └─────────────┘  └─────────────┘  └─────────────┘  └────────────────────┘ │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 三、数据模型

### 3.1 核心实体

#### AppSession (应用会话)
```csharp
public class AppSession : EntityBase
{
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public TimeSpan ActiveDuration { get; set; }
    
    public string ProcessName { get; set; }
    public string AppName { get; set; }
    public string WindowTitle { get; set; }
    public string Category { get; set; }
    
    public int InputEventCount { get; set; }
    public bool IsBackgroundMode { get; set; }
}
```

#### KeyboardSession (键盘会话)
```csharp
public class KeyboardSession : EntityBase
{
    public DateTime Date { get; set; }
    public int Hour { get; set; }
    public string ProcessName { get; set; }
    
    public int TotalKeyPresses { get; set; }
    public int ShortcutCount { get; set; }
    public double AverageTypingSpeed { get; set; }
}
```

#### MouseSession (鼠标会话)
```csharp
public class MouseSession : EntityBase
{
    public DateTime Date { get; set; }
    public int Hour { get; set; }
    public string ProcessName { get; set; }
    
    public int LeftClickCount { get; set; }
    public int RightClickCount { get; set; }
    public int MiddleClickCount { get; set; }
    public int ScrollCount { get; set; }
    public double TotalMoveDistance { get; set; }
}
```

#### WebSession (网页会话)
```csharp
public class WebSession : EntityBase
{
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public TimeSpan ActiveDuration { get; set; }
    
    public string Url { get; set; }
    public string Title { get; set; }
    public string Domain { get; set; }
    
    public int ScrollDepth { get; set; }
    public int ClickCount { get; set; }
    public bool HasFormInteraction { get; set; }
    public string Category { get; set; }
}
```

---

## 四、技术栈

| 组件 | 版本 | 用途 |
|------|------|------|
| .NET | 8.0 | 运行时框架 |
| Windows App SDK | 1.8 | WinUI 3 应用框架 |
| Entity Framework Core | 8.0 | ORM 数据访问 |
| SQLite | 3.x | 本地数据库 |
| Serilog | 3.x | 日志系统 |
| CommunityToolkit.Mvvm | 8.x | MVVM 工具包 |
| xUnit | 2.7 | 单元测试框架 |
| Moq | 4.20 | Mock 框架 |
| FluentAssertions | 6.12 | 断言库 |

---

## 五、命名约定

| 类型 | 命名规范 | 示例 |
|------|----------|------|
| 类/结构体 | PascalCase | `AppMonitor`, `KeyboardSession` |
| 接口 | IPascalCase | `IMonitor`, `IExportService` |
| 方法 | PascalCase | `StartMonitoring()`, `ExportData()` |
| 属性 | PascalCase | `IsRunning`, `TotalKeyPresses` |
| 私有字段 | _camelCase | `_monitor`, `_statsCache` |
| 常量 | PascalCase | `MaxRetryCount`, `DefaultInterval` |
| 参数 | camelCase | `processName`, `keyCode` |

---

## 六、性能目标

| 指标 | 目标值 | 当前状态 |
|------|--------|---------|
| 内存占用 | < 30MB | ✅ 达标 |
| CPU 占用 | < 0.5% (空闲时) | ✅ 达标 |
| 数据库写入延迟 | < 100ms | ✅ 达标 |
| UI 响应时间 | < 16ms | ✅ 达标 |

---

## 七、Git 提交规范

```
<type>(<scope>): <subject>

Type: feat, fix, docs, style, refactor, perf, test, chore
Scope: monitor, data, export, ui, analysis, core
```

示例: `feat(monitor): 实现应用切换检测`

---

## 八、未来功能扩展 (可选)

以下功能为未来可能扩展的方向，非当前必需：

### 8.1 短期优化
- [ ] 系统托盘右键菜单增强
- [ ] 数据备份与恢复功能
- [ ] 多显示器支持优化
- [ ] 自定义统计周期

### 8.2 中期功能
- [ ] 云端数据同步
- [ ] AI 行为洞察报告
- [ ] 自定义数据导出模板
- [ ] 团队协作统计

### 8.3 长期愿景
- [ ] 跨平台支持 (macOS, Linux)
- [ ] 移动端配套应用
- [ ] 开放 API 接口
- [ ] 插件系统

---

## 九、相关文档

- [问题修复记录](docs/问题修复记录.md)
- [浏览器扩展 README](extensions/tai-browser-extension/README.md)
- [项目规则](.trae/rules/project_rules.md)

---

> **作者**: AI Assistant  
> **项目状态**: 已完成发布
