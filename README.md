# PChabit - 电脑使用习惯追踪

一款 Windows 桌面习惯追踪工具：记录应用程序、键鼠操作、网页访问与硬件状态，并把这些数据整理成可复盘的分析报告。**所有数据仅存本地，不会上传到任何服务器。**

## 功能特性

### 核心监控

- **应用程序追踪**：自动记录应用使用时长与窗口标题
- **键盘监控**：按键次数与快捷键使用统计
- **鼠标监控**：点击、移动距离、滚动次数
- **网页浏览追踪**：通过浏览器扩展记录访问会话与有效浏览时长

### 硬件监控

- **实时硬件状态**：CPU / GPU / 内存 / 磁盘 / 网络 / 电池 / 风扇
- **分钟级采样**：硬件样本持续落库，可回溯任意时段的负载曲线
- **趋势视图**：硬件趋势图与历史对照
- **应用 × 硬件融合画像**：查看某个应用运行期间的硬件占用情况

### 数据分析与复盘

- **复盘驾驶舱**：多源 KPI、构成 / 节奏分析、时序图
- **深度周报**：周度复盘与数据质量校验
- **AI 深度解读**（可选）：接入模型生成结构化解读，支持本地模型
- **历史报告**：独立的历史报告页，图表联动
- **单应用详情**：任意应用的详情卡片
- **Excel 导出**：多工作表导出分析结果

### 浏览历史与网站分类

- **浏览历史实时记录**：与「网页访问」统一为实时写入
- **浏览器精准识别**：区分 Chrome / Edge / 豆包等浏览器
- **自定义识别标签**：用户指定的识别结果优先于自动识别
- **网站分类管理**：支持通配符域名映射；在网页访问页右键即可直接设置分类

### 数据可视化

- **仪表盘**：统计卡片与活动趋势概览
- **热力图**：周 / 月热力图展示使用模式
- **时间线**：详细活动记录与时间分布
- **应用流向图**：可视化应用切换关系，支持日期范围与 TopN 筛选
- **智能洞察**：习惯模式识别与效率评分

### 数据管理

- **程序分类管理**：自定义分类，支持从运行中的程序添加映射
- **SQLite 数据库**：本地存储（WAL 模式）
- **数据备份**：手动 / 自动备份，可从备份恢复
- **WebDAV 云端同步**：上传备份到 WebDAV 服务器做异地容灾
- **数据清理**：可配置数据保留天数，自动清理旧数据

### 任务栏小窗

- **两行实时文本**：LiteMonitor 风格，直接在任务栏显示关键指标
- **显示项自定义**：自行选择要展示的条目

### 桌面悬浮插件

- **独立悬浮窗**：桌面常驻显示 CPU（含温度）/ 内存 / GPU（含温度）/ 显存 / 网速 / 磁盘 / 今日活跃，每秒刷新，深浅主题自适应
- **可拖动、位置记忆**：拖动改变位置，窗口位置与所在屏幕自动记忆，重启恢复
- **置顶 / 鼠标穿透 / 显示项可配**：双击悬浮窗打开硬件监控页；主窗口最小化到托盘时仍常驻显示（行为规格照抄 LiteMonitor，MIT）

### 数据导出

- **JSON**：结构化数据
- **Markdown**：人类可读报告
- **AI-Prompt**：便于 AI 分析的格式
- **CSV**：表格数据
- **Excel**：多工作表导出

### 其他

- **系统托盘**：最小化到托盘后台运行
- **全局操作日志**：侧栏常驻日志面板，跨页面不丢失，便于排查
- **深色模式**：浅色 / 深色 / 系统默认三主题切换
- **高性能**：低内存占用，低 CPU 使用率

> **升级说明**：目标管理模块（应用 / 分类使用限制、总时长目标、托盘进度环）已移除；浏览器书签库与书签同步模块也已下线，书签同步请使用各浏览器自带的账号同步。

## 安装

### 系统要求

- Windows 10 1809 或更高版本
- Windows 11
- 无需预装 .NET 运行时（自包含部署）

### 下载安装

1. 从 [Releases](https://github.com/idleeyan/PChabit/releases) 页面下载最新版本
2. 解压到任意目录
3. 运行 `PChabit.exe`

## 浏览器扩展

### 安装步骤

#### Chrome

1. 打开 `chrome://extensions/`
2. 启用 "开发者模式"
3. 点击 "加载已解压的扩展程序"
4. 选择 `extensions/tai-browser-extension` 文件夹

#### Edge

1. 打开 `edge://extensions/`
2. 启用 "开发人员模式"
3. 点击 "加载解压缩的扩展"
4. 选择 `extensions/tai-browser-extension` 文件夹

#### Firefox

1. 打开 `about:debugging#/runtime/this-firefox`
2. 点击 "临时载入附加组件"
3. 选择 `extensions/tai-browser-extension` 文件夹中的 `manifest.json`

> Firefox 需使用 Manifest V2 版本，请改用 `manifest-firefox.json`。

### 扩展配置

默认通过 WebSocket 连接到本机 `8765` 端口。如需修改：

1. 点击扩展图标
2. 修改 WebSocket 端口
3. 保存并重新连接

端口也可在主程序「设置」中调整并下发到扩展。**更新扩展版本后，请在浏览器中重新加载扩展。**

## 开发

### 环境要求

- Visual Studio 2022 或 Build Tools
- **.NET 10 SDK**
- **Windows App SDK 2.5.1**

### 编译步骤

```bash
# 克隆仓库
git clone https://github.com/idleeyan/PChabit.git
cd PChabit

# 还原并构建
dotnet build src/PChabit.App/PChabit.App.csproj -c Release -r win-x64 -p:Platform=x64

# 发布（自包含，输出到 publish\）
dotnet publish src/PChabit.App/PChabit.App.csproj -c Release -r win-x64 --self-contained -p:Platform=x64 -o publish
```

### 项目结构

```
PChabit/
├── src/
│   ├── PChabit.Core/                # 领域核心：实体与接口（零依赖）
│   ├── PChabit.Infrastructure/      # 基础设施：EF Core / 监控器 / 服务 / 分析引擎
│   ├── PChabit.Application/         # 应用层服务
│   ├── PChabit.HardwareMonitor/     # 硬件监控（CPU/GPU/内存/磁盘/网络/电池）
│   ├── PChabit.App/                 # WinUI 3 应用程序（表示层）
│   └── PChabit.Tests/               # 单元测试
├── extensions/
│   └── tai-browser-extension/       # 浏览器扩展
├── scripts/                          # 构建和图标生成脚本
├── publish/                          # 发布输出目录
├── CHANGELOG.md                      # 更新日志
└── README.md                         # 说明文档
```

## 性能指标

| 指标 | 目标值 |
|------|--------|
| 内存占用 | < 30MB |
| CPU 占用 (空闲时) | < 0.5% |
| 数据库写入延迟 | < 100ms |
| UI 响应时间 | < 16ms |

## 隐私说明

- 所有数据仅在本地处理，不会上传到任何服务器
- 浏览器扩展不会收集密码字段内容
- 浏览器扩展不会收集敏感表单数据
- 用户可随时暂停监控或卸载程序

## 许可证

[MIT License](LICENSE)

## 贡献

欢迎提交 Issue 和 Pull Request！

## 联系方式

- 项目地址: [https://github.com/idleeyan/PChabit](https://github.com/idleeyan/PChabit)
