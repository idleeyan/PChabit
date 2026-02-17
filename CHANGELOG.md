# 更新日志

所有重要的更改都将记录在此文件中。

## [2.15.0] - 2026-02-18

### 新功能
- **网站分类管理**: 新增完全独立的网站分类系统，不破坏原有程序分类功能
  - 新增 WebsiteCategory 和 WebsiteDomainMapping 实体类
  - 新增 IWebsiteCategoryService 服务接口和实现
  - 支持自定义网站分类和域名映射
  - 支持通配符匹配（如 *.github.com）
  - 内置默认分类和映射（搜索、开发、视频、社交、购物、邮件、办公、新闻、浏览）
  - 保留硬编码默认规则作为后备
  - 新增 WebsiteCategoryManagementViewModel 视图模型

### 技术改进
- 完全独立的设计，与原有的 ProgramCategory 系统互不影响
- 在 DatabaseInitializer 中添加 WebsiteCategories 和 WebsiteDomainMappings 表迁移
- 在 ServiceConfiguration 中注册 IWebsiteCategoryService 和 WebsiteCategoryManagementViewModel
- 项目版本更新到 2.15.0

## [2.14.5] - 2026-02-18

### 编译错误修复
- **MouseDetailsViewModel 编译错误修复**: 解决多个编译问题
  - 修复 `Windows.UI.ColorHelper` 和 `Windows.UI.Colors` 命名空间引用错误
    - 替换为 `Microsoft.UI.ColorHelper` 和 `Microsoft.UI.Colors`
  - 修复 TimeSpan 总和计算错误
    - 使用 `TimeSpan.FromTicks()` 和 `.Ticks` 属性正确计算总时长
  - 修复 ProgramCategory 类型转换错误
    - 正确访问 `ProgramCategory.Name` 属性而不是直接使用对象
- **CA1416 平台兼容性警告修复**: 在 Infrastructure 项目中抑制 CA1416 警告
  - 在 `PChabit.Infrastructure.csproj` 中添加 `<NoWarn>$(NoWarn);CA1416</NoWarn>`

## [2.14.4] - 2026-02-18

### 新功能
- **鼠标点击详情页面**: 在分析页面新增鼠标点击详情标签页
  - 统计卡片：总点击次数、左/右/中键点击、移动距离、滚动次数
  - 按程序统计：显示各应用程序的点击次数和移动距离
  - 每小时统计：展示一天中每小时的鼠标活动热力图
  - 详细记录：按时间顺序显示鼠标会话详情
  - 支持按今日、本周、上周筛选数据

### 技术改进
- 新增 MouseDetailsViewModel 视图模型
- 新增 MouseDetailsPage 用户控件
- 添加 HourToTimeConverter 转换器
- 在分析页面添加第三个 Pivot 标签页

## [2.14.3] - 2026-02-18

### 导航优化
- **分析页面结构调整**: 将"热力图"和"智能洞察"从主菜单移到"分析"子菜单下
  - 分析页面现在包含三个子页面：周统计、热力图、智能洞察
  - 导航结构更清晰，所有分析相关功能统一归类
  - 保持各页面的独立功能，用户可通过分析菜单访问

## [2.14.2] - 2026-02-18

### 功能优化
- **设置页优化**: 删除设置页中的数据管理模块，避免与数据管理页功能重复
  - 删除数据保存天数设置
  - 删除数据库位置显示
  - 删除数据库大小显示
  - 删除清除所有数据按钮
  - 数据管理功能统一在备份管理页中进行

### 代码清理
- SettingsViewModel: 移除数据管理相关的属性和命令
  - 移除 `DataPath`、`DatabaseSize`、`RetentionDays` 属性
  - 移除 `OnRetentionDaysChanged` 方法
  - 移除 `ClearDataCommand` 命令
  - 移除 `UpdateDatabaseSize` 方法

## [2.14.1] - 2026-02-17

### 技术改进
- 完善语言文件夹清理，添加缺失的语言代码
  - fil-PH (菲律宾语)
  - kok-IN (孔卡尼语)
  - quz-PE (库斯科语)

## [2.14.0] - 2026-02-17

### 新功能
- **图标系统完善**: 程序图标和任务栏图标统一使用 pchabit.ico
  - 窗口标题栏图标动态加载
  - 系统托盘图标动态加载
  - 图标文件自动复制到输出目录

### 技术改进
- App.xaml.cs: 添加 SetWindowIcon 方法设置窗口图标
- TrayService.cs: 改进托盘图标加载逻辑，支持从文件加载
- PChabit.App.csproj: 配置 pchabit.ico 复制到输出目录

## [2.13.9] - 2026-02-17

### 技术改进
- 消除剩余编译警告
  - CA1416: 添加 Windows 平台支持标记到 ApplyStartupSetting 方法
  - NETSDK1206: 抑制 Windows App SDK RID 警告

## [2.13.8] - 2026-02-17

### 技术改进
- 消除编译警告，提升代码质量
  - CS8604: 修复空引用警告 (App.xaml.cs)
  - CS1998: 修复异步方法无 await 警告 (WebSocketServer, BackupService, ContextResolver, CategoryManagementViewModel)
  - WMC1506: 修复 XAML 绑定通知警告 (DetailDialog.xaml)
  - MVVMTK0045: 抑制 AOT 兼容性警告 (项目配置)
  - CS8601/CS8602: 修复空引用警告 (SettingsService, AiPromptExportFormatter)
  - CA1416: 添加 Windows 平台支持标记 (SettingsService)

## [2.13.7] - 2026-02-17

### Bug 修复
- **智能洞察**: 修复 `.Include(s => s.Category)` 导致查询失败的问题
  - `Category` 是字符串字段，不是导航属性，不能使用 `Include`

### 技术改进
- InsightService.cs: 移除无效的 Include 调用
- PatternAnalyzer.cs: 移除无效的 Include 调用
- EfficiencyCalculator.cs: 移除无效的 Include 调用

## [2.13.6] - 2026-02-17

### Bug 修复
- **目标管理**: 修复 XAML 资源名称错误导致页面崩溃的问题
  - `ApplicationPageBackgroundBrush` 改为 `ApplicationPageBackgroundThemeBrush`

### 技术改进
- GoalsPage.xaml.cs: 添加诊断日志用于调试

## [2.13.5] - 2026-02-17

### Bug 修复
- **数据库**: 为所有 Guid 类型 ID 添加显式转换，确保 EF Core 将 Guid 存储为 TEXT 而非 BLOB

### 技术改进
- PChabitDbContext.cs: 为所有继承 EntityBase 的实体添加 Id 属性的 Guid 到 TEXT 转换

## [2.13.4] - 2026-02-17

### Bug 修复
- **数据库迁移**: 修复迁移执行顺序，确保先删除旧表再创建新表

### 技术改进
- DatabaseInitializer.cs: 调整 MigrateGuidTablesAsync 执行顺序到 MigrateBackupTablesAsync 和 MigrateAnalysisTablesAsync 之前

## [2.13.3] - 2026-02-17

### Bug 修复
- **目标管理**: 修复页面卡死问题，修正数据库表 ID 类型（INTEGER → TEXT）
- **数据库迁移**: 添加自动迁移逻辑，检测并修复使用错误 ID 类型的表

### 技术改进
- DatabaseInitializer.cs: 修正 UserGoals、EfficiencyScores、WorkPatterns、InsightReports、BackupRecords、ArchiveRecords 表的 ID 类型
- 添加 MigrateGuidTablesAsync 方法自动修复已存在的错误表结构

## [2.13.2] - 2026-02-17

### Bug 修复
- **备份管理**: 修复数据库路径错误（缺少 `Data` 子目录），导致备份找不到数据库文件
- **智能洞察**: 修复 ScoreBreakdown 初始化为 null 导致绑定失败的问题
- **目标管理**: 添加完整的添加目标对话框 UI，修复"+"按钮无反应问题

### 技术改进
- BackupService.cs: 修正 GetDatabasePath() 返回正确路径
- InsightsViewModel.cs: 初始化 ScoreBreakdown 为非空实例
- InsightsPage.xaml: 使用 Binding 替代 x:Bind 避免 null 问题
- GoalsPage.xaml: 添加完整的添加目标对话框界面
- GoalsPage.xaml.cs: 添加对话框事件处理方法
- GoalsViewModel.cs: 暴露 GoalService 属性供 code-behind 使用

## [2.13.1] - 2026-02-17

### Bug 修复
- **备份管理**: 修复"立即备份"卡住问题，将 IBackupService 改为 Singleton 生命周期
- **智能洞察**: 修复洞察列表不显示问题，修正 XAML 数据类型绑定
- **目标管理**: 修复点击"+"按钮无反应问题，修正事件绑定方式
- **导航结构**: 将"导出"移至"数据管理"子菜单下

### 技术改进
- 修正 InsightsPage.xaml 和 GoalsPage.xaml 的 DataTemplate 数据类型
- 添加 GoalsPage.xaml.cs 事件处理方法
- 更新 ShellPage.xaml 导航结构

## [2.13.0] - 2026-02-17

### 新功能
- **习惯模式识别**:
  - 工作时段识别（自动识别工作开始和结束时间）
  - 专注块检测（识别超过25分钟的专注时段）
  - 高峰时段分析（识别一天中最活跃的时间段）
  - 每小时活动分布统计
- **效率评分系统**:
  - 多维度效率评分（专注、任务完成、平衡、中断控制、目标达成）
  - 每日效率评分记录
  - 周效率趋势分析
- **智能洞察报告**:
  - 每日洞察（效率分析、深度工作检测、切换频率警告）
  - 周报自动生成（概览、Top应用、效率趋势）
  - 月报自动生成（月度统计、分类分析）
- **目标管理**:
  - 应用使用限制（限制特定应用每日使用时长）
  - 分类使用限制（限制特定分类每日使用时长）
  - 总时长目标设定
  - 目标进度实时追踪

### 技术改进
- 新增 UserGoal、EfficiencyScore、WorkPattern、InsightReport 实体
- 新增 IPatternAnalyzer、IEfficiencyCalculator、IInsightService、IGoalService 接口
- 新增 PatternAnalyzer、EfficiencyCalculator、InsightService、GoalService 实现
- 新增 InsightsPage 和 GoalsPage 页面
- 数据库自动迁移支持新表

## [2.12.0] - 2026-02-17

### 新功能
- **数据备份管理**:
  - 支持手动和自动备份
  - 可自定义备份路径
  - 定时备份（可配置间隔）
  - 备份历史记录管理
  - 从备份恢复数据
- **数据清理**:
  - 可配置数据保留天数
  - 清理前可选归档
  - 自动清理旧数据
- **导出增强**:
  - 新增 CSV 格式导出
  - 新增 Excel 格式导出（多工作表）
  - 支持导出统计摘要、热门应用、热门网站等

### 技术改进
- 新增 BackupRecord 和 ArchiveRecord 实体
- 新增 IBackupService 接口和 BackupService 实现
- 新增 CsvExportFormatter 和 ExcelExportFormatter
- 集成 EPPlus 和 CsvHelper 库
- 应用启动时自动执行备份服务

## [1.0.0] - 2026-02-15

### 新功能
- **应用程序追踪**: 自动记录应用程序使用时间和窗口标题
- **键盘监控**: 记录键盘按键次数、快捷键使用统计
- **鼠标监控**: 记录鼠标点击、移动距离和滚动次数
- **网页浏览追踪**: 通过浏览器扩展记录网页访问历史
- **程序分类管理**: 自定义程序分类，支持从运行中的程序添加映射
- **数据可视化**: 
  - 仪表盘概览
  - 应用使用排行
  - 按键详情分析
  - 网页访问统计
- **系统托盘**: 最小化到系统托盘，后台运行

### 浏览器扩展
- 支持 Chrome、Edge、Firefox
- 实时 WebSocket 同步浏览数据
- 自动检测搜索引擎查询
- 隐私保护：不收集密码和敏感表单数据

### 修复
- 修复快捷键检测问题（支持左右 Ctrl/Shift/Alt 键）
- 修复分类映射匹配问题（支持带/不带 .exe 后缀的进程名）
- 修复系统托盘图标创建问题（GetModuleHandle DLL 导入错误）
- 修复分析页面日期计算问题（周日日期范围计算）
- 修复网页访问详情默认日期范围

### 技术改进
- 使用 WinUI 3 构建现代化界面
- SQLite 数据库存储
- Entity Framework Core ORM
- 依赖注入架构
- Serilog 日志记录

## [0.1.0] - 初始版本

### 新功能
- 基础应用程序监控
- 简单的数据记录
