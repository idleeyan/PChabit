namespace PChabit.Infrastructure.Services;

public class AppCategoryResolver
{
    private static readonly Dictionary<string, AppCategory> CategoryByCatalogKey = new(StringComparer.OrdinalIgnoreCase)
    {
        [DefaultAppCatalog.CatDev] = AppCategory.Development,
        [DefaultAppCatalog.CatAi] = AppCategory.AiAssistant,
        [DefaultAppCatalog.CatBrowser] = AppCategory.Browser,
        [DefaultAppCatalog.CatChat] = AppCategory.Communication,
        [DefaultAppCatalog.CatOffice] = AppCategory.Office,
        [DefaultAppCatalog.CatDesign] = AppCategory.Design,
        [DefaultAppCatalog.CatMedia] = AppCategory.Media,
        [DefaultAppCatalog.CatCloud] = AppCategory.Cloud,
        [DefaultAppCatalog.CatEntertainment] = AppCategory.Gaming,
        [DefaultAppCatalog.CatSystem] = AppCategory.System,
        [DefaultAppCatalog.CatOther] = AppCategory.Other,
    };

    // 兜底规则（预设未覆盖时）
    private static readonly (string Key, AppCategory Category)[] FallbackRules =
    {
        ("claude", AppCategory.AiAssistant),
        ("chatgpt", AppCategory.AiAssistant),
        ("openai", AppCategory.AiAssistant),
        ("copilot", AppCategory.AiAssistant),
        ("gemini", AppCategory.AiAssistant),
        ("ollama", AppCategory.AiAssistant),
        ("cursor", AppCategory.Development),
        ("code", AppCategory.Development),
        ("devenv", AppCategory.Development),
        ("idea", AppCategory.Development),
        ("pycharm", AppCategory.Development),
        ("webstorm", AppCategory.Development),
        ("rider", AppCategory.Development),
        ("chrome", AppCategory.Browser),
        ("firefox", AppCategory.Browser),
        ("msedge", AppCategory.Browser),
        ("edge", AppCategory.Browser),
        ("opera", AppCategory.Browser),
        ("brave", AppCategory.Browser),
        ("winword", AppCategory.Office),
        ("excel", AppCategory.Office),
        ("powerpnt", AppCategory.Office),
        ("outlook", AppCategory.Office),
        ("wps", AppCategory.Office),
        ("teams", AppCategory.Communication),
        ("zoom", AppCategory.Communication),
        ("discord", AppCategory.Communication),
        ("slack", AppCategory.Communication),
        ("telegram", AppCategory.Communication),
        ("wechat", AppCategory.Communication),
        ("weixin", AppCategory.Communication),
        ("qq", AppCategory.Communication),
        ("photoshop", AppCategory.Design),
        ("illustrator", AppCategory.Design),
        ("figma", AppCategory.Design),
        ("spotify", AppCategory.Media),
        ("vlc", AppCategory.Media),
        ("steam", AppCategory.Gaming),
        ("explorer", AppCategory.System),
        ("taskmgr", AppCategory.System),
        ("cmd", AppCategory.System),
        ("powershell", AppCategory.System),
        ("terminal", AppCategory.System),
    };

    public AppCategory Resolve(string processName, string? executablePath = null)
    {
        if (string.IsNullOrEmpty(processName))
            return AppCategory.Other;

        if (DefaultAppCatalog.IgnoreProcessNames.Contains(processName))
            return AppCategory.System;

        var catalogHit = DefaultAppCatalog.Find(processName);
        if (catalogHit != null && CategoryByCatalogKey.TryGetValue(catalogHit.CategoryKey, out var mapped))
        {
            return mapped;
        }

        foreach (var (key, category) in FallbackRules)
        {
            if (processName.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                return category;
            }
        }

        if (!string.IsNullOrEmpty(executablePath))
        {
            if (executablePath.Contains("\\windows\\", StringComparison.OrdinalIgnoreCase) ||
                executablePath.Contains("\\windowsapps\\", StringComparison.OrdinalIgnoreCase))
                return AppCategory.System;

            // 仅在无法识别时才用安装目录粗分，避免把 AI 客户端误标成「生产力」
            if (executablePath.Contains("\\program files\\", StringComparison.OrdinalIgnoreCase) ||
                executablePath.Contains("\\program files (x86)\\", StringComparison.OrdinalIgnoreCase))
                return AppCategory.Productivity;
        }

        return AppCategory.Other;
    }

    public string GetCategoryName(AppCategory category)
    {
        return category switch
        {
            AppCategory.Development => "开发",
            AppCategory.AiAssistant => "AI 助手",
            AppCategory.Browser => "浏览",
            AppCategory.Office => "办公",
            AppCategory.Communication => "沟通",
            AppCategory.Media => "媒体创作",
            AppCategory.Design => "设计",
            AppCategory.Cloud => "云盘下载",
            AppCategory.Gaming => "娱乐",
            AppCategory.System => "系统工具",
            AppCategory.Productivity => "生产力",
            _ => "其他"
        };
    }
}

public enum AppCategory
{
    Development,
    AiAssistant,
    Browser,
    Office,
    Communication,
    Media,
    Design,
    Cloud,
    Gaming,
    System,
    Productivity,
    Other
}
