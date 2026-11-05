namespace PChabit.Application.Queries;

// === Phase 1 中间数据类（纯 POCO，无 WinRT 类型） ===

public sealed class DashboardSnapshot
{
    public string TodayActiveTime = "0小时 0分钟";
    public string TodayKeyPresses = "0";
    public string TodayMouseClicks = "0";
    public string TodayWebPages = "0";
    public double EfficiencyScore;
    public double? ActiveDeviationPct;
    public double? FocusMinutes;
    public int? AppSwitches;
    public string FirstActiveTime = "";
    public string LastActiveTime = "";
    public string MostUsedApp = "无数据";
    public string MostVisitedSite = "无数据";

    public List<(string ProcessName, string CategoryName, double Duration, string CategoryColorHex)> TopAppsRaw = new();
    public List<HourlyActivityData> HourlyActivity = new();
    public List<(string Name, double Duration, string ColorHex)> CategoryRaw = new();
    public List<WebsiteUsageData> TopWebsites = new();

    // 环比（对比昨日同时段，避免"昨天全天"与"今天截至此刻"的口径错位）
    public double? ActiveComparePct;
    public double? KeysComparePct;
    public double? ClicksComparePct;
    public double? WebComparePct;

    /// <summary>图表数据（WebView2 用）。</summary>
    public ChartSnapshot Chart = new();

    /// <summary>当前正在使用的应用（阶段 5）。</summary>
    public string CurrentAppName = "";
    public string CurrentAppSince = "";
}

public sealed class HourlyActivityData
{
    public string Hour = string.Empty;
    public int Activity;
    public string Category = string.Empty;
}

public sealed class WebsiteUsageData
{
    public string Domain = string.Empty;
    public int Visits;
    public long TotalDurationTicks;
}

/// <summary>
/// 图表用的柱状图数据。纯 POCO，可安全序列化给 WebView2。
/// 未来小时用 <see cref="IsFuture"/> 标记，让图表画成灰色占位而非"零活跃"。
/// </summary>
public sealed class ActivityBar
{
    public string Label { get; set; } = string.Empty;
    public double Value { get; set; }
    public string Level { get; set; } = "low";
    public bool IsFuture { get; set; }
}

/// <summary>图表数据快照（Phase 1 纯 POCO，可在后台线程构造）。</summary>
public sealed class ChartSnapshot
{
    public List<ActivityBar> Bars { get; init; } = new();

    /// <summary>可写（非 init）：后台线程分步填充 Bars 与 Summary。</summary>
    public string Summary { get; set; } = "";
}
