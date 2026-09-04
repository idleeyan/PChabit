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
    }

    public record SummaryStatsResult(
        string TotalVisits, string TotalDuration, string UniqueDomains,
        string AvgDuration, string PeakHour, string TopDomain);

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

}

