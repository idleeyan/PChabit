using PChabit.Core.ValueObjects;

namespace PChabit.Infrastructure.Analysis;

/// <summary>
/// 行为语义推断（纯函数，可单测）：进程/分类/域名/键入密度 → 活动标签。
/// 窗口标题与域名仅在本地参与匹配，调用方不得把原文写入出域 payload 或日志。
/// </summary>
public static class ActivityLabeler
{
    private static readonly string[] MeetingProcesses =
        { "teams", "ms-teams", "zoom", "feishu", "lark", "dingtalk", "wemeet", "voov", "webex", "skype" };

    private static readonly string[] CommsProcesses =
        { "wechat", "weixin", "qq", "tim", "telegram", "slack", "discord", "feishu", "dingtalk" };

    private static readonly string[] GameProcesses =
        { "steam", "epicgameslauncher", "unity", "unreal", "leagueclient", "csgo", "valorant", "minecraft" };

    private static readonly string[] CodeProcesses =
        { "devenv", "code", "idea64", "idea", "pycharm", "webstorm", "rider", "rider64", "sublime_text",
          "windowsterminal", "wt", "cmd", "powershell", "pwsh", "cursor", "zed", "clion", "goland", "phpstorm" };

    private static readonly string[] DocProcesses =
        { "winword", "excel", "powerpnt", "wps", "et", "wpp", "notion", "obsidian", "typora", "acrobat", "acrord32" };

    private static readonly string[] MeetingTitleHints =
        { "会议", "meeting", "zoom", "teams", "腾讯会议", "飞书会议", "钉钉", "通话", "call", "webinar", "meet" };

    private static readonly string[] LearnTitleHints =
        { "文档", "docs", "documentation", "教程", "课程", "lesson", "tutorial", "wiki", "learn", "书", "book", "api" };

    private static readonly string[] FunTitleHints =
        { "抖音", "bilibili", "b站", "youtube", "netflix", "爱奇艺", "优酷", "腾讯视频", "淘宝", "京东", "pdd",
          "微博", "知乎贴吧", "小说", "小说", "游戏", "直播" };

    private static readonly string[] AdminProcesses =
        { "explorer", "taskmgr", "control", "regedit", "ms-settings", "settings", "appinstaller",
          "totalcmd", "listary", "everything", "winrar", "7zfm", "geek" };

    /// <summary>浏览器进程（网页会话侧优先用 WebCategory，此处仅作应用会话兜底）。</summary>
    private static readonly string[] BrowserProcesses =
        { "chrome", "msedge", "firefox", "brave", "opera", "iexplore", "360chrome", "qqbrowser", "sogouexplorer", "librewolf" };

    public static ActivityLabelResult Label(ActivityLabelInput input)
    {
        var process = (input.ProcessName ?? "").Trim().ToLowerInvariant();
        var appCat = (input.AppCategory ?? "").Trim();
        var webCat = (input.WebCategory ?? "").Trim();
        var title = input.WindowTitle ?? "";
        var minutes = Math.Max(0, input.SessionMinutes);
        var keysPerMin = Math.Max(0, input.KeysPerMinute);

        // 1) 无有效输入的长块 → idle
        if (minutes >= 5 && keysPerMin < 1 && string.IsNullOrEmpty(webCat) && LooksLikeNoInput(process, appCat))
            return new ActivityLabelResult(ActivityLabels.Idle, 0.75, "long-session-low-input");

        // 2) 会议：进程或标题强信号
        if (ContainsAny(process, MeetingProcesses) || ContainsAny(title, MeetingTitleHints, ignoreCase: true))
            return new ActivityLabelResult(ActivityLabels.Meeting, 0.85, "meeting-signal");

        // 3) 游戏
        if (ContainsAny(process, GameProcesses) || appCat.Contains("游戏") || appCat.Equals("娱乐", StringComparison.Ordinal))
        {
            // 「娱乐」分类可能是视频而非游戏；进程更具体时才判 game
            if (ContainsAny(process, GameProcesses) || appCat.Contains("游戏"))
                return new ActivityLabelResult(ActivityLabels.Game, 0.8, "game-signal");
        }

        // 4) 即时通讯（会议类已优先）
        if (ContainsAny(process, CommsProcesses) || appCat.Contains("沟通") || appCat.Contains("聊天") || appCat.Contains("社交"))
            return new ActivityLabelResult(ActivityLabels.Comms, 0.75, "comms-signal");

        // 5) 本地推断的工作模式（高键入）
        if (ContainsAny(process, CodeProcesses) || appCat.Contains("开发"))
        {
            var conf = keysPerMin >= 20 ? 0.9 : 0.75;
            return new ActivityLabelResult(ActivityLabels.WorkCode, conf, "code-signal");
        }

        if (ContainsAny(process, DocProcesses) || appCat.Contains("办公") || appCat.Contains("文档") || appCat.Contains("表格"))
        {
            var conf = keysPerMin >= 15 ? 0.85 : 0.7;
            return new ActivityLabelResult(ActivityLabels.WorkDoc, conf, "doc-signal");
        }

        if (appCat.Contains("AI") || appCat.Contains("ai ") || process.Contains("copilot") || process.Contains("claude") ||
            process.Contains("chatgpt") || process.Contains("doubao") || process.Contains("kimi") || process.Contains("mimo"))
        {
            // 与 AI 结对写代码/写文档时键入偏高
            if (keysPerMin >= 25)
                return new ActivityLabelResult(ActivityLabels.WorkCode, 0.7, "ai-assist-typing");
            return new ActivityLabelResult(ActivityLabels.Learn, 0.6, "ai-assist");
        }

        // 6) 网页语义
        var web = LabelWeb(webCat, title, keysPerMin);
        if (web is not null) return web;

        // 7) 浏览器应用会话但无网页分类
        if (ContainsAny(process, BrowserProcesses))
        {
            if (ContainsAny(title, LearnTitleHints, ignoreCase: true))
                return new ActivityLabelResult(ActivityLabels.Learn, 0.65, "browser-learn-title");
            if (ContainsAny(title, FunTitleHints, ignoreCase: true))
                return new ActivityLabelResult(ActivityLabels.BrowseFun, 0.65, "browser-fun-title");
            return new ActivityLabelResult(ActivityLabels.BrowseInfo, 0.55, "browser-generic");
        }

        // 8) 系统/杂务
        if (ContainsAny(process, AdminProcesses) || appCat.Contains("系统") || appCat.Contains("工具") || appCat.Contains("下载"))
            return new ActivityLabelResult(ActivityLabels.Admin, 0.7, "admin-signal");

        // 9) 媒体播放
        if (appCat.Contains("媒体") || appCat.Contains("影音") || appCat.Contains("视频") ||
            process.Contains("potplayer") || process.Contains("vlc") || process.Contains("spotify") || process.Contains("cloudmusic"))
            return new ActivityLabelResult(ActivityLabels.BrowseFun, 0.65, "media-signal");

        return new ActivityLabelResult(ActivityLabels.Other, 0.4, "fallback");
    }

    /// <summary>把一天的会话按时长加权聚合成标签分钟字典（键为 ActivityLabels 字面量）。</summary>
    public static Dictionary<string, double> AggregateLabelMinutes(IEnumerable<ActivityLabelInput> inputs)
    {
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var label in ActivityLabels.All)
            result[label] = 0;

        foreach (var input in inputs)
        {
            var minutes = Math.Max(0, input.SessionMinutes);
            if (minutes <= 0) continue;
            var labeled = Label(input);
            result[labeled.Label] = Math.Round(result[labeled.Label] + minutes, 1);
        }

        return result;
    }

    /// <summary>夜间分钟：23:00–次日 06:00 与 [start,end) 的重叠。</summary>
    public static double OverlapNightMinutes(DateTime start, DateTime end)
    {
        if (end <= start) return 0;
        var total = 0.0;

        // 按日切开再与当日夜间窗 [23:00, 24:00) ∪ [00:00, 06:00) 求交
        for (var day = start.Date; day <= end.Date; day = day.AddDays(1))
        {
            var nightStart = day.AddHours(23);
            var nightEnd = day.AddHours(24 + 6); // 次日 06:00
            // 拆成两段更直观：当日 23–24、次日 0–6
            total += Overlap(start, end, nightStart, nightStart.AddHours(1));
            total += Overlap(start, end, day.AddHours(0), day.AddHours(6));
        }

        return Math.Round(total, 1);
    }

    private static double Overlap(DateTime aStart, DateTime aEnd, DateTime bStart, DateTime bEnd)
    {
        var s = aStart > bStart ? aStart : bStart;
        var e = aEnd < bEnd ? aEnd : bEnd;
        return e > s ? (e - s).TotalMinutes : 0;
    }

    private static ActivityLabelResult? LabelWeb(string webCat, string title, double keysPerMin)
    {
        if (string.IsNullOrEmpty(webCat) && string.IsNullOrEmpty(title))
            return null;

        if (webCat.Contains("开发") || webCat.Contains("文档"))
        {
            if (keysPerMin >= 20)
                return new ActivityLabelResult(ActivityLabels.WorkCode, 0.75, "web-dev-typing");
            return new ActivityLabelResult(ActivityLabels.Learn, 0.7, "web-dev");
        }

        if (webCat.Contains("搜索") || webCat.Contains("新闻") || webCat.Contains("资讯") || webCat.Contains("办公") ||
            webCat.Contains("邮件") || webCat.Contains("浏览"))
            return new ActivityLabelResult(ActivityLabels.BrowseInfo, 0.7, "web-info");

        if (webCat.Contains("视频") || webCat.Contains("社交") || webCat.Contains("购物") || webCat.Contains("娱乐"))
            return new ActivityLabelResult(ActivityLabels.BrowseFun, 0.75, "web-fun");

        if (ContainsAny(title, LearnTitleHints, ignoreCase: true))
            return new ActivityLabelResult(ActivityLabels.Learn, 0.6, "web-learn-title");

        if (ContainsAny(title, FunTitleHints, ignoreCase: true))
            return new ActivityLabelResult(ActivityLabels.BrowseFun, 0.6, "web-fun-title");

        if (!string.IsNullOrEmpty(webCat))
            return new ActivityLabelResult(ActivityLabels.BrowseInfo, 0.5, "web-other-cat");

        return null;
    }

    private static bool LooksLikeNoInput(string process, string appCat)
    {
        // 屏保/锁屏/纯挂机特征：无分类且非浏览器/办公
        return string.IsNullOrEmpty(appCat)
               && !ContainsAny(process, BrowserProcesses)
               && !ContainsAny(process, CodeProcesses)
               && !ContainsAny(process, DocProcesses);
    }

    private static bool ContainsAny(string haystack, string[] needles, bool ignoreCase = false)
    {
        if (string.IsNullOrEmpty(haystack)) return false;
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var n in needles)
        {
            if (haystack.Contains(n, comparison)) return true;
        }
        return false;
    }
}
