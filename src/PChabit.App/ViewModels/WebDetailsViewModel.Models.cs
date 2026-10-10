using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Serilog;
using PChabit.Core.Entities;
using PChabit.Infrastructure.Data;


namespace PChabit.App.ViewModels;

public partial class WebDetailsViewModel : DbSafeViewModel<WebDetailsViewModel.WebStatsData>
{
    public sealed class WebStatsData
    {
        public required SummaryStatsResult Summary;
        public required List<DomainStatItem> DomainStats;
        public required List<WebHourlyActivityItem> HourlyActivity;
        public required List<DailyTrendItem> DailyTrend;
        public required List<BrowsingPatternItem> BrowsingPatterns;
        public required List<WebSessionDetailItem> RecentVisits;
        public required bool HasMore;
        public required List<Core.Entities.WebSession> AllSessions;
        public required List<TrafficAttributionItem> TrafficAttribution;

        /// <summary>3.37.0：排行与流量归因合并后的域名表。</summary>
        public List<DomainActivityRow> DomainActivity = new();

        /// <summary>3.37.0：应用 + 网页的小时活动合成行。</summary>
        public List<HourlyActivityCombinedRow> HourlyCombined = new();
    }

    public record SummaryStatsResult(
        string TotalVisits, string TotalDuration, string UniqueDomains,
        string AvgDuration, string PeakHour, string TopDomain, string FocusRatio = "-");

public class DomainStatItem
{
    public string Domain { get; init; } = string.Empty;
    public int VisitCount { get; init; }
    public double TotalDuration { get; init; }
    public double AvgDuration { get; init; }
    public string Category { get; init; } = string.Empty;
    public DateTime LastVisit { get; init; }
    public string FormattedDuration => $"{(int)TotalDuration}分钟";
    public string FormattedAvgDuration => $"{(int)AvgDuration}秒";
}

public class WebHourlyActivityItem
{
    public int Hour { get; init; }
    public string HourLabel { get; init; } = string.Empty;
    public int VisitCount { get; init; }
    public int Duration { get; init; }
    public int BarHeight { get; init; }
}

/// <summary>
/// 合并后的域名行（3.37.0）：把原「网站访问排行」与「网页流量归因」并成一张表。
/// 两者本就是同一批域名（一个按会话数/时长排序，一个按下行流量分摊），只是列不同。
/// </summary>
public sealed class DomainActivityRow
{
    public string Domain { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;

    /// <summary>会话数（= 访问次数）。</summary>
    public int VisitCount { get; init; }

    /// <summary>累计停留分钟数。</summary>
    public double TotalMinutes { get; init; }

    /// <summary>下行流量（字节/GB），无流量数据时为 0。</summary>
    public double DownBytes { get; init; }

    /// <summary>占全部下行流量的百分比。</summary>
    public double TrafficSharePct { get; init; }

    public string DurationText => $"{(int)TotalMinutes}分钟";
    public string DownText => DownBytes <= 0
        ? "—"
        : DownBytes >= 1024 * 1024 * 1024
            ? $"{DownBytes / 1024 / 1024 / 1024:0.00} GB"
            : $"{DownBytes / 1024 / 1024:0.0} MB";

    public string ShareText => DownBytes <= 0 ? "—" : $"{TrafficSharePct:0.0}%";
    public bool HasTraffic => DownBytes > 0;
}

/// <summary>
/// 某小时的综合活动（3.37.0）：把应用使用与网页访问的小时分布合成一行，
/// 两个维度都属于「活动」范围，按用户所选时间段统计。
/// </summary>
public sealed class HourlyActivityCombinedRow
{
    public int Hour { get; init; }
    public string HourLabel { get; init; } = string.Empty;

    /// <summary>该小时应用使用分钟数（全应用合计）。</summary>
    public double AppMinutes { get; init; }

    /// <summary>该小时网页访问分钟数。</summary>
    public double WebMinutes { get; init; }

    /// <summary>该小时网页会话数。</summary>
    public int WebVisits { get; init; }

    public double TotalMinutes => AppMinutes + WebMinutes;

    public string AppText => AppMinutes <= 0.5 ? "—" : $"{AppMinutes:0}分";
    public string WebText => WebMinutes <= 0.5 ? "—" : $"{WebMinutes:0}分";
    public string VisitText => WebVisits <= 0 ? "—" : $"{WebVisits}次";
}

public class DailyTrendItem
{
    public DateTime Date { get; init; }
    public string DateLabel { get; init; } = string.Empty;
    public int VisitCount { get; init; }
    public double TotalDuration { get; init; }
    public int UniqueDomains { get; init; }
}

public class BrowsingPatternItem
{
    public string Pattern { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
    public string Color { get; init; } = "#9CA3AF";
}

public class WebSessionDetailItem
{
    public string Domain { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string VisitTime { get; init; } = string.Empty;
    public string Duration { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string CategoryColor { get; init; } = "#9CA3AF";
    public int ScrollDepth { get; init; }
    public int ClickCount { get; init; }
    public bool HasInteraction { get; init; }
}

/// <summary>网页流量归因（估算）：浏览器进程流量按同小时网页有效浏览时长加权拆分到域名。</summary>
public class TrafficAttributionItem
{
    public string Domain { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string CategoryColor { get; init; } = "#9CA3AF";
    public string DownText { get; init; } = string.Empty;
    public double SharePercent { get; init; }
    public double BarWidth { get; init; }
    public string ShareText => $"{SharePercent:F1}%";
}

}

