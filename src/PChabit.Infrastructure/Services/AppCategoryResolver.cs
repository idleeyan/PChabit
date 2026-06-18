namespace PChabit.Infrastructure.Services;

public class AppCategoryResolver
{
    private static readonly Dictionary<string, AppCategory> CategoryRules = new(StringComparer.OrdinalIgnoreCase)
    {
        { "code", AppCategory.Development },
        { "visualstudio", AppCategory.Development },
        { "devenv", AppCategory.Development },
        { "idea", AppCategory.Development },
        { "rider", AppCategory.Development },
        { "pycharm", AppCategory.Development },
        { "webstorm", AppCategory.Development },
        { "sublime", AppCategory.Development },
        { "notepad++", AppCategory.Development },
        { "vim", AppCategory.Development },
        { "nvim", AppCategory.Development },
        { "cursor", AppCategory.Development },
        { "zed", AppCategory.Development },

        { "chrome", AppCategory.Browser },
        { "firefox", AppCategory.Browser },
        { "msedge", AppCategory.Browser },
        { "edge", AppCategory.Browser },
        { "opera", AppCategory.Browser },
        { "brave", AppCategory.Browser },

        { "word", AppCategory.Office },
        { "excel", AppCategory.Office },
        { "powerpnt", AppCategory.Office },
        { "outlook", AppCategory.Office },
        { "onenote", AppCategory.Office },
        { "winword", AppCategory.Office },

        { "teams", AppCategory.Communication },
        { "zoom", AppCategory.Communication },
        { "discord", AppCategory.Communication },
        { "slack", AppCategory.Communication },
        { "telegram", AppCategory.Communication },
        { "wechat", AppCategory.Communication },
        { "qq", AppCategory.Communication },
        { "skype", AppCategory.Communication },

        { "spotify", AppCategory.Media },
        { "vlc", AppCategory.Media },
        { "netflix", AppCategory.Media },
        { "bilibili", AppCategory.Media },
        { "music", AppCategory.Media },

        { "photoshop", AppCategory.Design },
        { "illustrator", AppCategory.Design },
        { "figma", AppCategory.Design },
        { "sketch", AppCategory.Design },
        { "blender", AppCategory.Design },

        { "steam", AppCategory.Gaming },
        { "epicgameslauncher", AppCategory.Gaming },
        { "minecraft", AppCategory.Gaming },
        { "leagueoflegends", AppCategory.Gaming },

        { "explorer", AppCategory.System },
        { "taskmgr", AppCategory.System },
        { "settings", AppCategory.System },
        { "cmd", AppCategory.System },
        { "powershell", AppCategory.System },
        { "terminal", AppCategory.System },
    };

    public AppCategory Resolve(string processName, string? executablePath = null)
    {
        if (string.IsNullOrEmpty(processName))
            return AppCategory.Other;

        foreach (var rule in CategoryRules)
        {
            if (processName.Contains(rule.Key, StringComparison.OrdinalIgnoreCase))
            {
                return rule.Value;
            }
        }

        if (!string.IsNullOrEmpty(executablePath))
        {
            if (executablePath.Contains("\\windows\\", StringComparison.OrdinalIgnoreCase))
                return AppCategory.System;

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
            AppCategory.Browser => "浏览器",
            AppCategory.Office => "办公",
            AppCategory.Communication => "通讯工具",
            AppCategory.Media => "媒体播放",
            AppCategory.Design => "设计工具",
            AppCategory.Gaming => "游戏",
            AppCategory.System => "系统应用",
            AppCategory.Productivity => "生产力工具",
            _ => "其他"
        };
    }
}

public enum AppCategory
{
    Development,
    Browser,
    Office,
    Communication,
    Media,
    Design,
    Gaming,
    System,
    Productivity,
    Other
}
