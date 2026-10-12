namespace PChabit.Infrastructure.Services;

/// <summary>
/// 内置应用详细说明预置库：规范化进程名（小写、无 .exe）→ 说明文本。
/// 覆盖本机常见软件（AI 助手 / 开发 / 浏览器 / 沟通 / 影音 / 下载 / 系统工具 / 游戏 / 办公），
/// 由 <c>AppDescriptionService.EnsureDescriptionsSeededAsync</c> 在启动期增量写入数据库；
/// 用户手动编辑过的条目（IsUserEdited=true）不会被种子覆盖。
/// </summary>
public static class DefaultAppDescriptions
{
    public sealed record DescEntry(string ProcessKey, string Description);

    public static readonly DescEntry[] Entries =
    {
        // ==================== AI 助手 / 大模型客户端 ====================
        new("marvis",
            "腾讯推出的操作系统级 AI 管家：用自然语言指令直接操控电脑——整理文件、系统管理、跨端联动。内置多 Agent 体系（主 Agent 调度 + 多个预置 Agent），支持「效率模式」（端云协同）与「本地模式」（端侧大模型、文件 0 上传）。"),
        new("chatterfly",
            "腾讯推出的 AI 原生语音输入助手（内测版，1.0.0.5567），依托混元多模态大模型：开口即成文、自动去口语化润色，内置会议纪要、工作汇报、营销文案等表达 Skill，可用语音下达翻译 / 写作指令。"),
        new("cherrystudio",
            "桌面端 AI 聚合客户端：一个应用同时接入 OpenAI、Claude、Gemini、通义、豆包等多家大模型，支持多模型会话、知识库、翻译与写作，数据保存在本地。"),
        new("doubao",
            "字节跳动豆包 AI 对话助手桌面客户端：聊天问答、创作、联网搜索、文件分析，与手机 / 网页端账号数据同步。"),
        new("doubaowork",
            "豆包面向办公场景的桌面客户端：内置文档、表格、幻灯片等办公能力与 AI 智能体，可执行会议纪要、数据分析、内容创作等任务（即当前正在使用的产品）。"),
        new("lmstudio",
            "本地大模型运行平台：图形化下载并运行 GGUF 等格式的开源模型（Llama、Qwen、Gemma 等），提供 OpenAI 兼容的本地 API 服务，支持 GPU 加速，模型数据本地存储。"),
        new("mimo",
            "小米推出的 AI 助手客户端，搭载小米 MiMo 大模型，支持本地对话与任务处理，主打端侧运行与隐私保护。"),
        new("qwenworkcn",
            "阿里巴巴通义千问的办公版客户端（千问办公）：将千问大模型能力带入办公场景，支持文档处理、写作润色、信息检索等。"),
        new("workbuddy",
            "腾讯推出的 AI 工作助手：具备长期记忆与项目跟进能力，可协助写作、任务管理与跨应用协作，是腾讯系面向办公场景的 Agent 产品。"),

        // ==================== 开发工具 ====================
        new("trae",
            "字节跳动的 AI 原生 IDE（国内版）：基于 VS Code 内核，内置 AI 对话编程、代码补全、多模态理解（看图改代码）与 Agent 化任务执行，支持多模型切换。"),
        new("node",
            "JavaScript 运行时环境（含 npm / npx / corepack）：用于运行与开发 JS / TS 服务端程序、前端构建工具链及各类命令行工具。"),
        new("python",
            "Python 解释器（本机装有多套版本，3.11 / 3.13 / 3.14 共存）：广泛用于脚本编写、自动化、数据分析、AI 与科学计算。"),
        new("pythonw",
            "Python 的无控制台窗口版本（pythonw.exe）：用于运行不需要命令行窗口的 GUI / 后台脚本。"),
        new("conda",
            "Python 环境与包管理工具：提供 conda 虚拟环境与依赖管理，常用于科学计算、机器学习等复杂依赖场景。"),
        new("miniconda3",
            "Miniconda（轻量版 Anaconda）：提供 conda 包管理与虚拟环境能力，本机 Python 3.14 内核即由此管理。"),
        new("dnspy",
            ".NET 程序集反编译 / 调试 / 编辑器：可查看并修改 C# 编译产物的 IL 代码，常用于逆向分析、程序研究与二次开发。"),
        new("wechatdevtools",
            "微信官方小程序 / 小游戏开发 IDE：代码编辑、模拟器调试、真机预览、上传发布全流程支持（NW.js 内核）。"),
        new("notepad--",
            "国产开源文本编辑器，Notepad++ 的替代品，基于 Qt5，支持多语言、插件与主题定制。"),
        new("notepad++",
            "经典代码 / 文本编辑器 Notepad++：轻量快速，语法高亮、多标签、插件生态完善。"),
        new("git",
            "分布式版本控制系统：用于代码版本管理、协作开发与发布，本机多数开发项目的仓库都由 Git 维护。"),
        new("devenv",
            "Visual Studio 2022（Community）：微软重量级 .NET / C++ 集成开发环境，本机 PChabit 等 .NET 项目的主要开发工具。"),
        new("code",
            "Visual Studio Code（VS Code）：微软出品的轻量代码编辑器，插件生态庞大，支持几乎所有主流语言。"),
        new("rustup",
            "Rust 工具链安装与版本管理器：管理 rustc / cargo 的安装与更新，本机 Rust 开发环境的基础组件。"),

        // ==================== 浏览器 ====================
        new("chrome",
            "Google Chrome 浏览器：全球使用最广的浏览器之一，以速度与扩展生态著称，也是本机 WebView 系应用的默认内核来源。"),
        new("msedge",
            "Microsoft Edge 浏览器：Windows 内置的 Chromium 内核浏览器，深度集成微软生态（账户同步、Copilot、办公联动）。"),
        new("firefox",
            "Mozilla Firefox 浏览器：开源浏览器，强调隐私保护与扩展自由，Gecko 内核独立于 Chromium 生态。"),

        // ==================== 沟通社交 ====================
        new("qq",
            "腾讯 QQ 桌面客户端（新版 NT 架构）：聊天、群组、文件传输、远程协助、空间动态等。"),
        new("weixin",
            "微信 Windows 客户端（4.x）：聊天、公众号、文件传输、小程序、视频号等。"),
        new("wetype",
            "微信输入法（WeType）：腾讯出品的输入法，支持拼音 / 手写 / 语音，与微信生态联动，可跨端同步词库。"),

        // ==================== 影音娱乐 ====================
        new("potplayermini64",
            "Daum 出品的高性能视频播放器：解码能力强、界面可深度定制；本机还挂载了 faster-whisper 本地语音识别扩展，用于视频实时字幕转写。"),
        new("foobar2000",
            "老牌音频播放器（便携汉化版）：极简界面、高度可定制（组件 / 皮肤 / DSP 插件），支持无损格式与高采样率输出。"),
        new("jianyingpro",
            "字节跳动的专业视频剪辑软件（剪映专业版）：剪辑、特效、字幕、调色，内置图文成片、数字人等 AI 能力。"),
        new("musictag",
            "音乐文件标签编辑工具：批量修改歌曲标题 / 歌手 / 专辑 / 封面 / 歌词等 ID3 信息，支持从网易云、QQ 音乐等在线匹配。"),
        new("hongguo",
            "字节跳动旗下红果短剧桌面客户端：免费在线观看短剧内容（桌面伴侣版）。"),
        new("min",
            "开源极简浏览器（Electron）：主打隐私与专注，内置广告 / 追踪拦截、阅读模式、快速拨号，界面干净轻量。"),

        // ==================== 下载网盘 ====================
        new("thunder",
            "老牌下载工具（迅雷）：支持 HTTP / BT / 磁力链等下载加速，带浏览器接管功能。"),
        new("quarkclouddrive",
            "夸克网盘桌面客户端：网盘存储、在线播放与文档处理，阿里系产品。"),
        new("verysync",
            "国产 P2P 文件同步工具（微力同步，类似 Resilio Sync）：多设备间直连同步 / 备份文件夹，不经过中转服务器；本机 E:\\SYNC 目录即由它同步。"),

        // ==================== 系统工具 ====================
        new("pchabit",
            "用户自研的 Windows 使用习惯追踪与分析桌面应用（WinUI 3 / .NET 10）：覆盖键鼠 / 应用 / 网页统计、硬件监控、分析复盘驾驶舱、浏览历史同步、任务栏小窗等模块——即本应用自身。"),
        new("adguard",
            "知名广告拦截软件桌面版：拦截网页与应用内广告、追踪器，支持 DNS 过滤、自定义规则、家长控制，驱动级网络过滤。"),
        new("360zip",
            "360 出品的压缩解压软件：支持 ZIP / RAR / 7z 等主流格式，提供加密压缩、自解压包、图片查看等功能。"),
        new("winrar",
            "老牌压缩解压软件（7.13）：支持 RAR / ZIP / 7z 等格式，界面经典，压缩率高，是压缩文件的默认关联程序之一。"),
        new("honeyview",
            "Bandisoft 出品的极速图片查看器：支持多种格式、长图拼接预览与文件夹浏览，界面简洁轻量。"),
        new("cpuz",
            "CPUID 出品的硬件检测工具：读取 CPU 型号 / 工艺 / 主频、主板芯片组、内存类型与时序等详细参数。"),
        new("gpuz",
            "TechPowerUp 出品的显卡检测工具：显示 GPU 型号、显存规格、驱动版本、温度与传感器信息。"),
        new("crystaldiscinfo",
            "硬盘健康检测工具（Shizuku 皮肤版）：基于 S.M.A.R.T. 数据监测磁盘健康状态与温度，支持 NVMe / SSD / HDD。"),
        new("diskgenius",
            "国产磁盘分区与数据恢复工具：支持分区调整 / 克隆 / 格式化，误删文件与分区恢复、坏道检测、镜像备份，是硬盘维护常用工具。"),
        new("bluescreenview",
            "NirSoft 出品的蓝屏转储分析工具：读取 minidump 文件，列出蓝屏原因、崩溃涉及的驱动与堆栈信息，帮助排查系统崩溃。"),
        new("hibituninstaller",
            "强力软件卸载工具（便携版）：支持卸载后残留扫描清理、启动项与服务管理、强制卸载顽固程序、文件粉碎。"),
        new("rufus",
            "轻量 U 盘启动盘制作工具：将 Windows / Linux 镜像写入 U 盘，支持分区方案、文件系统与镜像校验定制。"),
        new("spacesniffer",
            "磁盘空间可视化分析工具（中文版）：以矩形树图直观展示各文件夹占用比例，快速定位大文件与空间黑洞。"),
        new("steamautocrack",
            "Steam 游戏启动验证自动化处理工具：基于 Goldberg Steam Emulator 等方案，使部分游戏可离线启动。仅限个人研究 / 学习场景，请注意合规使用。"),
        new("lghub",
            "Logitech G HUB：罗技游戏外设的驱动与配置中心，管理鼠标 / 键盘 / 耳机的按键、宏、RGB 灯效与灵敏度设置。"),
        new("nvidiaapp",
            "NVIDIA App：NVIDIA 新一代显卡控制与驱动管理应用，统一游戏优化（GeForce Experience 能力）、驱动更新、录制与性能监控。"),

        // ==================== 游戏 ====================
        new("steam",
            "Valve 的 PC 游戏平台：游戏购买、库管理、云存档、社区与创意工坊；本机大型游戏大多通过 Steam 安装与启动。"),
        new("paradoxlauncher",
            "Paradox Interactive 的游戏启动器（P 社启动器）：管理旗下游戏（如群星、钢铁雄心等）的模组与启动参数。"),
        new("the scroll of taiwu",
            "太吾绘卷系列（天幕心帷 / 正式版）：茄子主导开发的武侠模拟经营 RPG，以功法、人生模拟与自由度著称，本机安装于 Steam 库。"),
        new("palworld",
            "幻兽帕鲁（Palworld）：开放世界生存 + 精灵收集玩法，捕捉帕鲁协助战斗、建造与生产，2024 年现象级爆款。"),
        new("melvoridle",
            "Melvor Idle：复古风放置（Idle）游戏，借鉴《RuneScape》的技能体系，主打挂机成长与技能规划。"),
        new("lastepoch",
            "Last Epoch（最后纪元）：暗黑类动作 RPG，以装备构筑与时间线系统著称，支持离线与在线模式。"),
        new("soulmask",
            "Soulmask（灵魂面甲）：开放世界生存建造游戏，以面甲赋予能力与部落经营为特色。"),
        new("cheatengine",
            "Cheat Engine：经典内存修改工具，用于单机游戏数值修改与内存调试，是逆向研究与游戏 MOD 开发的常用工具。"),
        new("gamecheatsmanager",
            "游戏作弊管理工具（Game Cheats Manager）：集中管理单机游戏修改器与作弊脚本的启动与调用。"),

        // ==================== 办公 ====================
        new("wps",
            "WPS Office：金山办公套件，兼容 Word / Excel / PPT，含云文档与 AI 能力；本机为命令行组件残留，主程序位于系统盘。"),
        new("wpsoffice",
            "WPS Office（进程全名）：金山办公套件，包含文字、表格、演示三大组件，支持 PDF 转换与云协作。"),
    };
}
