# PChabit 项目规则

> 版本: 2.10.28  
> 最后更新: 2026-02-16  
> 项目状态: **已完成发布**

## 一、全局规则遵从

本项目必须遵从用户设定的全局规则，包括但不限于：
- 必须用中文和用户交流
- 项目创建时，先写下更新规则，每次更新必须更新版本号
- 模块化开发规则，核心原则：独立模块对应独立文件，防止单文件过大
- **问题修复规则：每次修改问题之后，将遇到的问题和尝试的办法写入 `docs/问题修复记录.md`**

## 二、文件大小限制

| 文件类型 | 建议上限 | 警告上限 | 必须拆分 |
|---------|---------|---------|---------|
| C# (.cs) | 500行 | 800行 | 1000行 |
| XAML (.xaml) | 300行 | 500行 | 800行 |

当文件接近警告上限时，应主动考虑拆分策略。

## 三、项目架构

```
src/
├── Tai.Core/              # 领域层 - 实体、接口、值对象
│   ├── Entities/          # 实体类 (AppSession, KeyboardSession, MouseSession, WebSession 等)
│   ├── Interfaces/        # 接口定义 (IMonitor, IRepository, IUnitOfWork 等)
│   └── ValueObjects/      # 值对象
│
├── Tai.Infrastructure/    # 基础设施层 - 数据访问、外部服务实现
│   ├── Data/              # 数据库上下文、仓储实现、UnitOfWork、DatabaseInitializer
│   ├── Formatters/        # 导出格式化器 (JSON, Markdown, AI-Prompt)
│   ├── Helpers/           # Win32 API 辅助类
│   ├── Monitoring/        # 监控器实现 (AppMonitor, KeyboardMonitor, MouseMonitor, WebMonitor)
│   └── Services/          # 服务实现 (WebSocket, EventBus, Export, Cache, TaskScheduler)
│
├── Tai.Application/       # 应用层 - 用例、查询/命令处理
│   ├── Aggregators/       # 数据聚合服务 (DailyAggregator, SessionAggregator, PatternDetector, HeatmapAggregator)
│   └── Analysis/          # 行为分析服务 (BehaviorAnalyzer, ContextResolver)
│
├── Tai.App/               # 表示层 - WinUI 3 应用
│   ├── Assets/            # 应用资源
│   ├── Converters/        # XAML 值转换器
│   ├── Services/          # 应用服务 (Navigation, DataCollection, AppIcon, Tray)
│   ├── ViewModels/        # 视图模型
│   └── Views/             # 页面视图
│
├── Tai.Tests/             # 测试项目
│
└── extensions/            # 浏览器扩展
    └── tai-browser-extension/
```

## 四、命名约定

| 类型 | 命名规范 | 示例 |
|------|----------|------|
| 类/结构体 | PascalCase | `AppMonitor`, `KeyboardSession` |
| 接口 | IPascalCase | `IMonitor`, `IExportService` |
| 方法 | PascalCase | `StartMonitoring()`, `ExportData()` |
| 属性 | PascalCase | `IsRunning`, `TotalKeyPresses` |
| 私有字段 | _camelCase | `_monitor`, `_statsCache` |
| 常量 | PascalCase | `MaxRetryCount`, `DefaultInterval` |
| 参数 | camelCase | `processName`, `keyCode` |

## 五、性能目标

| 指标 | 目标值 | 当前状态 |
|------|--------|---------|
| 内存占用 | < 30MB | ✅ 达标 |
| CPU 占用 | < 0.5% (空闲时) | ✅ 达标 |
| 数据库写入延迟 | < 100ms | ✅ 达标 |
| UI 响应时间 | < 16ms | ✅ 达标 |

## 六、技术栈

| 组件 | 版本 | 用途 |
|------|------|------|
| .NET SDK | 8.0.418 | 运行时框架 |
| Windows App SDK | 1.4.231115000 | WinUI 3 应用框架 |
| Entity Framework Core | 8.0.* | ORM 数据访问 |
| SQLite | 3.x | 本地数据库 |
| Serilog | 3.* | 日志系统 |
| CommunityToolkit.Mvvm | 8.* | MVVM 工具包 |
| xUnit | 2.7.0 | 单元测试框架 |
| Moq | 4.20.70 | Mock 框架 |
| FluentAssertions | 6.12.0 | 断言库 |

## 七、发布规则

**发布输出目录统一为项目根目录的 `publish` 文件夹。**

发布命令：
```bash
dotnet publish src/Tai.App/Tai.App.csproj -c Release -p:Platform=x64 -p:PublishProfile=win-x64
```

**发布注意事项**：
1. PRI 文件必须复制到发布目录
2. XBF 文件必须复制到 Views 目录
3. 推荐使用 Visual Studio IDE 发布

## 八、Git 提交规范

```
<type>(<scope>): <subject>

Type: feat, fix, docs, style, refactor, perf, test, chore
Scope: monitor, data, export, ui, analysis, core
```

示例: `feat(monitor): 实现应用切换检测`

## 九、已实现功能

### 核心监控
- ✅ 应用监控 (前台窗口追踪、应用分类、窗口标题)
- ✅ 键盘监控 (按键统计、快捷键检测、打字片段)
- ✅ 鼠标监控 (点击、移动、滚动)
- ✅ 网页监控 (Chrome/Edge/Firefox 扩展)

### 数据管理
- ✅ SQLite 数据库存储
- ✅ UnitOfWork + Repository 模式
- ✅ 数据库自动迁移

### 数据分析
- ✅ 日数据聚合
- ✅ 行为模式检测
- ✅ 生产力分析

### 数据可视化
- ✅ 热力图 (周热力图、月热力图)
- ✅ 时间线 (活动详情)
- ✅ 分析页 (周统计、趋势分析)

### 导出功能
- ✅ JSON 格式导出
- ✅ Markdown 格式导出
- ✅ AI-Prompt 格式导出

### 用户界面
- ✅ 仪表盘 (统计卡片、活动趋势)
- ✅ 时间线 (活动详情)
- ✅ 分析页 (周统计、趋势分析)
- ✅ 应用统计 (详细使用数据)
- ✅ 导出页 (格式选择)
- ✅ 设置页 (配置管理)

### 其他
- ✅ 系统托盘
- ✅ 后台应用模式
- ✅ 性能监控
- ✅ 发布脚本

## 十、版本规划摘要

### v1.1.0 (2026年3月)
- 数据备份与恢复
- 系统托盘增强
- 自定义统计周期
- 深色/浅色主题切换

### v1.2.0 (2026年6月)
- 云端数据同步 (WebDAV)
- AI 行为洞察报告
- 目标设置与提醒
- Sankey 图

### v2.0.0 (2026年12月)
- 插件系统
- 开放 API
- 跨平台支持调研

## 十一、故障排查快速参考

| 问题 | 解决方案 |
|------|---------|
| .NET SDK 版本冲突 | 删除预览版 SDK，更新 global.json |
| PRI 资源生成失败 | 安装 VS UWP 工作负载 |
| IDbContextFactory 未注册 | 添加 AddDbContextFactory 注册 |
| Windows App Runtime 缺失 | 安装 Windows App Runtime MSIX |
| XAML 解析失败 | 复制 XBF/PRI 文件到发布目录 |

## 十二、相关文档

| 文档 | 路径 |
|------|------|
| 项目状态报告 | `docs/项目状态报告.md` |
| 问题修复记录 | `docs/问题修复记录.md` |
| 重构计划书 | `Tai重构计划书_v2.md` |
| 浏览器扩展 README | `extensions/tai-browser-extension/README.md` |
| 更新日志 | `CHANGELOG.md` |
