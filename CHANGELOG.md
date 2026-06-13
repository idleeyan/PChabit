# 更新日志

所有重要的更改都将记录在此文件中。

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
