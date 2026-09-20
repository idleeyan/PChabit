namespace PChabit.Infrastructure.Services;

/// <summary>
/// 内置程序分类预设：进程名（不含 .exe，忽略大小写）→ 分类键 + 展示名。
/// 用于启动种子、增量补齐映射，以及数据库未命中时的实时解析。
/// </summary>
public static class DefaultAppCatalog
{
    public sealed record AppEntry(string ProcessKey, string CategoryKey, string DisplayName);

    public const string CatDev = "开发";
    public const string CatBrowser = "浏览";
    public const string CatChat = "沟通";
    public const string CatEntertainment = "娱乐";
    public const string CatOffice = "办公";
    public const string CatDesign = "设计";
    public const string CatAi = "AI 助手";
    public const string CatMedia = "媒体创作";
    public const string CatCloud = "云盘下载";
    public const string CatSystem = "系统工具";
    public const string CatOther = "其他";

    /// <summary>分类定义（Name 必须与 DB 中 ProgramCategories.Name 一致）</summary>
    public static readonly (string Name, string Description, string Color, string Icon, int SortOrder)[] Categories =
    {
        (CatDev, "开发工具与 IDE", "#4A90E4", "💻", 1),
        (CatAi, "AI 助手 / 大模型客户端", "#00B4D8", "🤖", 2),
        (CatBrowser, "浏览器", "#50C878", "🌐", 3),
        (CatChat, "即时通讯与协作", "#FF6B6B", "💬", 4),
        (CatOffice, "办公与文档", "#F39C12", "📊", 5),
        (CatDesign, "设计与创作", "#E74C3C", "🎨", 6),
        (CatMedia, "影音播放与剪辑", "#9B59B6", "🎬", 7),
        (CatCloud, "云盘与下载", "#20B2AA", "☁️", 8),
        (CatEntertainment, "游戏娱乐", "#8E44AD", "🎮", 9),
        (CatSystem, "系统与工具", "#7F8C8D", "🛠️", 10),
        (CatOther, "未分类程序", "#95A5A6", "📁", 99),
    };

    public static readonly AppEntry[] Apps =
    {
        // === AI 智能体 / 大模型客户端 ===
        new("Doubao", CatAi, "豆包"),
        new("doubao", CatAi, "豆包"),
        new("XiaomiMiMo", CatAi, "小米 MiMo"),
        new("Xiaomi MiMo", CatAi, "小米 MiMo"),
        new("MiMo", CatAi, "小米 MiMo"),
        new("WorkBuddy", CatAi, "WorkBuddy"),
        new("Cherry Studio", CatAi, "Cherry Studio"),
        new("Cherry-Studio", CatAi, "Cherry Studio"),
        new("LM Studio", CatAi, "LM Studio"),
        new("LM-Studio", CatAi, "LM Studio"),
        new("lmstudio", CatAi, "LM Studio"),
        new("MiniMaxHub", CatAi, "MiniMax Hub"),
        new("MiniMaxHub Data", CatAi, "MiniMax Hub"),
        new("freellmapi-desktop", CatAi, "FreeLLM API"),
        new("copilotapp", CatAi, "Copilot"),
        new("Copilot", CatAi, "Copilot"),
        new("CopilotUpdate", CatAi, "Copilot"),
        new("ChatGPT", CatAi, "ChatGPT"),
        new("claude", CatAi, "Claude"),
        new("Claude", CatAi, "Claude"),
        new("Perplexity", CatAi, "Perplexity"),
        new("Gemini", CatAi, "Gemini"),
        new("Kimi", CatAi, "Kimi"),
        new("kimi", CatAi, "Kimi"),
        new("wpsai", CatAi, "WPS AI"),
        new("AI_Novel_Generator", CatAi, "AI 小说生成器"),
        new("Marvis", CatAi, "Marvis"),
        new("MarvisData", CatAi, "Marvis"),
        new("dashscope", CatAi, "百炼 DashScope"),

        // === 开发 ===
        new("code", CatDev, "VS Code"),
        new("Code", CatDev, "VS Code"),
        new("devenv", CatDev, "Visual Studio"),
        new("idea64", CatDev, "IntelliJ IDEA"),
        new("idea", CatDev, "IntelliJ IDEA"),
        new("pycharm64", CatDev, "PyCharm"),
        new("pycharm", CatDev, "PyCharm"),
        new("webstorm64", CatDev, "WebStorm"),
        new("webstorm", CatDev, "WebStorm"),
        new("rider64", CatDev, "Rider"),
        new("cursor", CatDev, "Cursor"),
        new("zed", CatDev, "Zed"),
        new("sublime_text", CatDev, "Sublime Text"),
        new("notepad++", CatDev, "Notepad++"),
        new("Notepad--", CatDev, "Notepad--"),
        new("nvim", CatDev, "Neovim"),
        new("vim", CatDev, "Vim"),
        new("dnSpy", CatDev, "dnSpy"),
        new("dnSpy-x86", CatDev, "dnSpy"),
        new("TRAE", CatDev, "Trae"),
        new("TRAE SOLO CN", CatDev, "Trae Solo"),
        new("Trae", CatDev, "Trae"),
        new("TraeCN", CatDev, "Trae"),
        new("wechatdevtools", CatDev, "微信开发者工具"),
        new("微信web开发者工具", CatDev, "微信开发者工具"),
        new("WeChat DevTools", CatDev, "微信开发者工具"),
        new("git", CatDev, "Git"),
        new("node", CatDev, "Node.js"),
        new("python", CatDev, "Python"),
        new("pythonw", CatDev, "Python"),
        new("conda", CatDev, "Conda"),
        new("anaconda", CatDev, "Anaconda"),
        new("miniconda3", CatDev, "Miniconda"),
        new("docker", CatDev, "Docker"),
        new("Docker Desktop", CatDev, "Docker Desktop"),
        new("CodeSetup", CatDev, "VS Code"),

        // === 浏览器 ===
        new("chrome", CatBrowser, "Google Chrome"),
        new("msedge", CatBrowser, "Microsoft Edge"),
        new("firefox", CatBrowser, "Mozilla Firefox"),
        new("opera", CatBrowser, "Opera"),
        new("brave", CatBrowser, "Brave"),
        new("brave-browser", CatBrowser, "Brave"),
        new("vivaldi", CatBrowser, "Vivaldi"),
        new("iexplore", CatBrowser, "Internet Explorer"),
        new("360chrome", CatBrowser, "360 浏览器"),
        new("360se", CatBrowser, "360 浏览器"),
        new("QQBrowser", CatBrowser, "QQ 浏览器"),
        new("SogouExplorer", CatBrowser, "搜狗浏览器"),

        // === 沟通 ===
        new("Weixin", CatChat, "微信"),
        new("WeChat", CatChat, "微信"),
        new("wechat", CatChat, "微信"),
        new("QQ", CatChat, "QQ"),
        new("QQNT", CatChat, "QQ"),
        new("TIM", CatChat, "TIM"),
        new("Teams", CatChat, "Microsoft Teams"),
        new("ms-teams", CatChat, "Microsoft Teams"),
        new("Discord", CatChat, "Discord"),
        new("slack", CatChat, "Slack"),
        new("Telegram", CatChat, "Telegram"),
        new("Feishu", CatChat, "飞书"),
        new("Lark", CatChat, "飞书"),
        new("DingTalk", CatChat, "钉钉"),
        new("wxwork", CatChat, "企业微信"),
        new("Skype", CatChat, "Skype"),
        new("zoom", CatChat, "Zoom"),
        new("YY", CatChat, "YY"),

        // === 办公 ===
        new("WINWORD", CatOffice, "Word"),
        new("EXCEL", CatOffice, "Excel"),
        new("POWERPNT", CatOffice, "PowerPoint"),
        new("OUTLOOK", CatOffice, "Outlook"),
        new("ONENOTE", CatOffice, "OneNote"),
        new("wps", CatOffice, "WPS"),
        new("wpp", CatOffice, "WPS 演示"),
        new("et", CatOffice, "WPS 表格"),
        new("wpsoffice", CatOffice, "WPS Office"),
        new("wpscloudsvr", CatOffice, "WPS"),
        new("soffice", CatOffice, "LibreOffice"),
        new("notion", CatOffice, "Notion"),
        new("Obsidian", CatOffice, "Obsidian"),
        new("obsidian", CatOffice, "Obsidian"),
        new("Typora", CatOffice, "Typora"),
        new("yuque", CatOffice, "语雀"),

        // === 设计 ===
        new("Photoshop", CatDesign, "Photoshop"),
        new("Illustrator", CatDesign, "Illustrator"),
        new("Figma", CatDesign, "Figma"),
        new("figma", CatDesign, "Figma"),
        new("XD", CatDesign, "Adobe XD"),
        new("Sketch", CatDesign, "Sketch"),
        new("Blender", CatDesign, "Blender"),
        new("draw.io", CatDesign, "draw.io"),
        new("Excalidraw", CatDesign, "Excalidraw"),
        new("InDesign", CatDesign, "InDesign"),
        new("AfterFX", CatDesign, "After Effects"),
        new("Premiere Pro", CatDesign, "Premiere"),

        // === 媒体创作/播放 ===
        new("JianyingPro", CatMedia, "剪映"),
        new("CapCut", CatMedia, "CapCut"),
        new("foobar2000", CatMedia, "foobar2000"),
        new("MusicFree", CatMedia, "MusicFree"),
        new("MusicTag", CatMedia, "MusicTag"),
        new("vlc", CatMedia, "VLC"),
        new("PotPlayer", CatMedia, "PotPlayer"),
        new("PotPlayerMini64", CatMedia, "PotPlayer"),
        new("wmplayer", CatMedia, "Windows Media Player"),
        new("Honeyview", CatMedia, "Honeyview"),
        new("Photos", CatMedia, "照片"),
        new("360AblumViewer", CatMedia, "360 看图"),
        new("QQPlayer", CatMedia, "QQ 影音"),
        new("cloudmusic", CatMedia, "网易云音乐"),
        new("KuGou", CatMedia, "酷狗音乐"),
        new("kugou", CatMedia, "酷狗音乐"),
        new("KWMusic", CatMedia, "酷我音乐"),

        // === 云盘 / 下载 ===
        new("BaiduNetdisk", CatCloud, "百度网盘"),
        new("BaiduNetdiskHost", CatCloud, "百度网盘"),
        new("BaiduNetdiskUnite", CatCloud, "百度网盘"),
        new("Thunder", CatCloud, "迅雷"),
        new("迅雷", CatCloud, "迅雷"),
        new("XunLei", CatCloud, "迅雷"),
        new("Weiyun", CatCloud, "微云"),
        new("AliyunDrive", CatCloud, "阿里云盘"),
        new("aria2", CatCloud, "aria2"),
        new("IDMan", CatCloud, "IDM"),
        new("verysync", CatCloud, "VerySync"),
        new("坚果云", CatCloud, "坚果云"),
        new("Nutstore", CatCloud, "坚果云"),

        // === 娱乐游戏 ===
        new("steam", CatEntertainment, "Steam"),
        new("steamwebhelper", CatEntertainment, "Steam"),
        new("epicgameslauncher", CatEntertainment, "Epic Games"),
        new("EpicWebHelper", CatEntertainment, "Epic Games"),
        new("EpicGamesLauncher", CatEntertainment, "Epic Games"),
        new("steam++", CatEntertainment, "Steam++"),
        new("SteamAutoCrack", CatEntertainment, "Steam"),
        new("MuMuPlayer", CatEntertainment, "MuMu 模拟器"),
        new("MuMuNxVbox", CatEntertainment, "MuMu 模拟器"),
        new("minecraft", CatEntertainment, "Minecraft"),
        new("LeagueClient", CatEntertainment, "英雄联盟"),
        new("Valorant", CatEntertainment, "无畏契约"),

        // === 系统工具 ===
        new("explorer", CatSystem, "资源管理器"),
        new("taskmgr", CatSystem, "任务管理器"),
        new("cmd", CatSystem, "命令提示符"),
        new("powershell", CatSystem, "PowerShell"),
        new("pwsh", CatSystem, "PowerShell"),
        new("WindowsTerminal", CatSystem, "Windows Terminal"),
        new("ApplicationFrameHost", CatSystem, "应用框架"),
        new("SystemSettings", CatSystem, "设置"),
        new("WinRAR", CatSystem, "WinRAR"),
        new("360zip", CatSystem, "360 压缩"),
        new("DiskGenius", CatSystem, "DiskGenius"),
        new("CrystalDiskInfo", CatSystem, "CrystalDiskInfo"),
        new("CPU-Z", CatSystem, "CPU-Z"),
        new("GPU-Z", CatSystem, "GPU-Z"),
        new("cpuz_x64", CatSystem, "CPU-Z"),
        new("Adguard", CatSystem, "AdGuard"),
        new("AdGuard", CatSystem, "AdGuard"),
        new("LGHUB", CatSystem, "Logitech G HUB"),
        new("lghub_agent", CatSystem, "Logitech G HUB"),
        new("lghub_system_tray", CatSystem, "Logitech G HUB"),
        new("nvcontainer", CatSystem, "NVIDIA Container"),
        new("PictureButler", CatSystem, "Picture Butler"),
        new("Rufus", CatSystem, "Rufus"),
        new("spacesniffer", CatSystem, "SpaceSniffer"),
        new("HiBitUninstaller", CatSystem, "HiBit Uninstaller"),
        new("geek", CatSystem, "Geek Uninstaller"),
        new("QtScrcpy", CatSystem, "QtScrcpy"),
        new("PChabit", CatSystem, "PChabit"),
    };

    /// <summary>忽略统计的系统/宿主进程（通常不是用户「使用」的应用）</summary>
    public static readonly HashSet<string> IgnoreProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "svchost", "conhost", "RuntimeBroker", "backgroundTaskHost", "taskhostw",
        "sihost", "SearchHost", "ShellExperienceHost", "ShellHost", "StartMenuExperienceHost",
        "LockApp", "smartscreen", "unsecapp", "UserOOBEBroker", "TextInputHost",
        "ApplicationFrameHost", "dllhost", "fontdrvhost", "csrss", "winlogon",
        "dwm", "Memory Compression", "System", "Registry", "Idle",
        "msedgewebview2", "msedgewebview2.exe",
        "PushNotificationsLongRunningTask", "DynamicDependencyLifetimeManagerShadow",
        "CrossDeviceService", "CrossDeviceResume", "PhoneExperienceHost",
        "WidgetService", "Widgets", "SecurityHealthService", "wetype_server", "wetype_update",
        "YunDetectService", "sandbox-center", "logi_crashpad_handler", "crashpad_handler",
        "Adguard.BrowserExtensionHost", "aha_doctor", "wpscloudsvr",
    };

    public static Dictionary<string, AppEntry> CreateLookup()
    {
        var map = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in Apps)
        {
            map[app.ProcessKey] = app;
            // 兼容带 .exe 的历史数据
            map[app.ProcessKey + ".exe"] = app;
        }
        return map;
    }

    public static AppEntry? Find(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return null;
        var lookup = CreateLookup();
        if (lookup.TryGetValue(processName, out var exact)) return exact;

        var bare = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4]
            : processName;
        if (lookup.TryGetValue(bare, out var byBare)) return byBare;

        // 模糊：进程名包含预设键（短键除外，避免误伤）
        foreach (var app in Apps)
        {
            if (app.ProcessKey.Length < 3) continue;
            if (bare.Contains(app.ProcessKey, StringComparison.OrdinalIgnoreCase) ||
                app.ProcessKey.Contains(bare, StringComparison.OrdinalIgnoreCase))
            {
                return app;
            }
        }
        return null;
    }
}
