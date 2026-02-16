# PChabit - 电脑使用习惯追踪

一款电脑使用习惯追踪工具，帮助您了解自己的电脑使用模式。

## 功能特性

### 核心监控
- 📊 **应用程序追踪**: 自动记录应用程序使用时间和窗口标题
- ⌨️ **键盘监控**: 记录键盘按键次数、快捷键使用统计
- 🖱️ **鼠标监控**: 记录鼠标点击、移动距离和滚动次数
- 🌐 **网页浏览追踪**: 通过浏览器扩展记录网页访问历史

### 数据管理
- 📁 **程序分类管理**: 自定义程序分类，支持从运行中的程序添加映射
- � **SQLite 数据库**: 本地存储，数据安全可靠

### 数据可视化
- 📈 **仪表盘**: 统计卡片、活动趋势概览
- 📅 **热力图**: 周热力图、月热力图展示使用模式
- ⏱️ **时间线**: 详细的活动记录和时间分布
- 📊 **分析页**: 周统计、趋势分析
- 🔀 **应用流向图**: 可视化应用切换关系

### 数据导出
- � **JSON 格式**: 结构化数据导出
- 📝 **Markdown 格式**: 人类可读的报告
- 🤖 **AI-Prompt 格式**: 便于 AI 分析的格式

### 其他功能
- �🔔 **系统托盘**: 最小化到系统托盘，后台运行
- ⚡ **高性能**: 低内存占用，低 CPU 使用率

## 安装

### 系统要求
- Windows 10 1809 或更高版本
- Windows 11

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

### 扩展配置

默认连接到 `ws://localhost:8765`。如需修改：
1. 点击扩展图标
2. 修改 WebSocket 服务器地址
3. 保存并重新连接

## 开发

### 环境要求
- Visual Studio 2022
- .NET 8 SDK
- Windows App SDK 1.4

### 编译步骤

```bash
# 克隆仓库
git clone https://github.com/idleeyan/PChabit.git
cd PChabit

# 编译
dotnet build src/Tai.App/Tai.App.csproj -c Release

# 发布 (推荐使用 Visual Studio)
# 或使用 MSBuild
"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" src/Tai.App/Tai.App.csproj /t:Publish /p:Configuration=Release /p:Platform=x64
```

### 项目结构

```
PChabit/
├── src/
│   ├── Tai.App/                 # WinUI 3 应用程序 (表示层)
│   ├── Tai.Core/                # 核心接口和实体 (领域层)
│   ├── Tai.Infrastructure/      # 基础设施实现
│   ├── Tai.Application/         # 应用层服务
│   └── Tai.Tests/               # 单元测试
├── extensions/
│   └── tai-browser-extension/   # 浏览器扩展
├── CHANGELOG.md                 # 更新日志
└── README.md                    # 说明文档
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
