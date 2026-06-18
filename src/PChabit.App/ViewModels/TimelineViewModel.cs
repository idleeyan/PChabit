using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Serilog;
using PChabit.App.Services;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Data;

namespace PChabit.App.ViewModels;

public partial class TimelineViewModel : DbSafeViewModel<TimelineViewModel.TimelineStats>
{
    private readonly IDbContextFactory<PChabitDbContext> _dbFactory;
    private readonly IAppIconService _iconService;

    [ObservableProperty]
    private DateTime _selectedDate = DateTime.Today;

    [ObservableProperty]
    private bool _hasData;

    public ObservableCollection<TimelineHourGroup> HourGroups { get; } = new();
    public ObservableCollection<DateSummary> RecentDates { get; } = new();

    public TimelineViewModel(IDbContextFactory<PChabitDbContext> dbFactory, IAppIconService iconService) : base()
    {
        _dbFactory = dbFactory;
        _iconService = iconService;
        Title = "时间线";
    }

    // === Phase 1 中间数据 ===

    public sealed class TimelineStats
    {
        public List<(TimelineHourGroup Group, bool IsCurrent)> BuiltGroups = new();
        public List<DateSummary> RecentDates = new();
        public List<Core.Entities.AppSession> AllSessions = new();
    }

    // === Custom LoadDataAsync（支持 CancellationToken，覆盖基类） ===

    public async Task LoadDataAsync(CancellationToken cancellationToken = default)
    {
        var totalSw = System.Diagnostics.Stopwatch.StartNew();
        if (IsLoading) return;
        IsLoading = true;

        try
        {
            var stats = await Task.Run(() => LoadStatsOnBackgroundAsync(cancellationToken), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            await RunOnUIThreadAsync(() =>
            {
                ApplyStatsOnUIAsync(stats);
                return Task.CompletedTask;
            });

            _ = LoadIconsForCurrentHourAsync(stats);
        }
        catch (OperationCanceledException)
        {
            Log.Debug("[Timeline] 加载被取消");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "加载时间线数据失败");
        }
        finally
        {
            totalSw.Stop();
            Log.Information("[Timeline] LoadDataAsync 完成, 总耗时: {ElapsedMs}ms", totalSw.ElapsedMilliseconds);
            IsLoading = false;
        }
    }

    protected override async Task<TimelineStats> LoadStatsOnBackgroundAsync()
    {
        return await LoadStatsOnBackgroundAsync(CancellationToken.None);
    }

    private async Task<TimelineStats> LoadStatsOnBackgroundAsync(CancellationToken cancellationToken)
    {
        var selectedDate = SelectedDate.Date;
        var nextDay = selectedDate.AddDays(1);

        await using var dbContext = await _dbFactory.CreateDbContextAsync();

        List<Core.Entities.AppSession> appSessions;
        try
        {
            appSessions = await dbContext.AppSessions
                .AsNoTracking()
                .Where(s => s.StartTime >= selectedDate && s.StartTime < nextDay)
                .OrderByDescending(s => s.StartTime)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "加载 AppSessions 失败");
            appSessions = new List<Core.Entities.AppSession>();
        }

        cancellationToken.ThrowIfCancellationRequested();

        var hourlyGroups = appSessions
            .GroupBy(s => s.StartTime.Hour)
            .OrderByDescending(g => g.Key)
            .Select(g =>
            {
                var sessions = g.OrderByDescending(s => s.StartTime).ToList();
                double totalMinutes = 0;
                foreach (var s in sessions)
                {
                    if (s.EndTime.HasValue)
                        totalMinutes += (s.EndTime.Value - s.StartTime).TotalMinutes;
                }
                return new { Hour = g.Key, Sessions = sessions, TotalMinutes = totalMinutes };
            })
            .ToList();

        var currentHour = DateTime.Now.Hour;
        var builtGroups = new List<(TimelineHourGroup Group, bool IsCurrent)>();
        var hasCurrentHourData = false;

        foreach (var hg in hourlyGroups)
        {
            var hourGroup = new TimelineHourGroup
            {
                Hour = hg.Hour,
                Date = selectedDate,
                TimeRange = $"{hg.Hour:D2}:00 - {hg.Hour:D2}:59",
                TotalDuration = FormatDuration(hg.TotalMinutes),
                ActivityCount = hg.Sessions.Count,
                IsExpanded = false,
                IsLoaded = false
            };

            if (hg.TotalMinutes > 0)
            {
                var categoryMinutes = hg.Sessions
                    .Where(s => s.EndTime.HasValue)
                    .GroupBy(s => s.Category ?? "其他")
                    .Select(g => new { Category = g.Key, Minutes = g.Sum(s => (s.EndTime!.Value - s.StartTime).TotalMinutes) })
                    .OrderByDescending(x => x.Minutes)
                    .ToList();

                foreach (var cat in categoryMinutes)
                {
                    hourGroup.BarSegments.Add(new TimelineBarSegment
                    {
                        Color = GetCategoryColor(cat.Category),
                        WidthPercent = cat.Minutes / hg.TotalMinutes * 100
                    });
                }
            }

            var isCurrent = hg.Hour == currentHour || (!hasCurrentHourData && hg == hourlyGroups.First());
            if (isCurrent)
            {
                hasCurrentHourData = true;
                hourGroup.IsExpanded = true;
                hourGroup.IsLoaded = true;
                foreach (var session in hg.Sessions)
                    hourGroup.Activities.Add(CreateActivity(session));
            }

            builtGroups.Add((hourGroup, isCurrent));
        }

        // 计算 RecentDates
        var sessionsByDate = appSessions.GroupBy(s => s.StartTime.Date).ToDictionary(g => g.Key, g => g.ToList());
        var newDateSummaries = new List<DateSummary>(7);
        for (int i = 0; i < 7; i++)
        {
            var date = DateTime.Today.AddDays(-i);
            var daySessions = sessionsByDate.TryGetValue(date, out var ds) ? ds : null;
            double totalMinutes = 0;
            int productivityScore = 0;

            if (daySessions != null && daySessions.Count > 0)
            {
                foreach (var s in daySessions)
                {
                    if (s.EndTime.HasValue)
                    {
                        var duration = (s.EndTime.Value - s.StartTime).TotalMinutes;
                        totalMinutes += duration;
                        if (IsProductiveCategory(s.Category))
                            productivityScore += (int)duration;
                    }
                }
                productivityScore = totalMinutes > 0 ? (int)(productivityScore / totalMinutes * 100) : 0;
            }

            newDateSummaries.Add(new DateSummary
            {
                Date = date, DayName = date.ToString("ddd"), DayNumber = date.Day.ToString(),
                TotalTime = FormatDuration(totalMinutes), IsToday = i == 0, ProductivityScore = productivityScore
            });
        }

        return new TimelineStats
        {
            BuiltGroups = builtGroups,
            RecentDates = newDateSummaries,
            AllSessions = appSessions
        };
    }

    protected override async Task ApplyStatsOnUIAsync(TimelineStats stats)
    {
        HourGroups.Clear();
        foreach (var (hourGroup, _) in stats.BuiltGroups)
            HourGroups.Add(hourGroup);

        RecentDates.Clear();
        foreach (var summary in stats.RecentDates)
            RecentDates.Add(summary);

        HasData = HourGroups.Any();
    }

    private async Task LoadIconsForCurrentHourAsync(TimelineStats stats)
    {
        foreach (var (hourGroup, isCurrent) in stats.BuiltGroups)
        {
            if (isCurrent)
            {
                var processNames = hourGroup.Activities
                    .Select(a => a.ProcessName)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Distinct()
                    .ToList();
                _ = LoadIconsForGroupAsync(hourGroup, processNames);
            }
        }
    }

    private static TimelineActivity CreateActivity(Core.Entities.AppSession session)
    {
        var duration = session.EndTime.HasValue ? (session.EndTime.Value - session.StartTime).TotalMinutes : 0;
        return new TimelineActivity
        {
            Time = session.StartTime.ToString("HH:mm"),
            Duration = FormatDuration(duration),
            Title = session.AppName ?? session.ProcessName ?? "",
            Subtitle = session.WindowTitle ?? "",
            Category = session.Category ?? "其他",
            CategoryColor = GetCategoryColor(session.Category),
            ProcessName = session.ProcessName ?? ""
        };
    }

    public async Task ExpandHourGroupAsync(TimelineHourGroup group)
    {
        if (group.IsLoaded) { group.IsExpanded = true; return; }

        try
        {
            var selectedDate = group.Date;
            var hourStart = selectedDate.AddHours(group.Hour);
            var hourEnd = hourStart.AddHours(1);

            var sessions = await Task.Run(async () =>
            {
                await using var dbContext = await _dbFactory.CreateDbContextAsync();
                return await dbContext.AppSessions
                    .AsNoTracking()
                    .Where(s => s.StartTime >= hourStart && s.StartTime < hourEnd)
                    .OrderByDescending(s => s.StartTime)
                    .ToListAsync();
            });

            group.Activities.Clear();
            foreach (var session in sessions)
                group.Activities.Add(CreateActivity(session));

            var processNames = sessions
                .Where(s => !string.IsNullOrEmpty(s.ProcessName))
                .Select(s => s.ProcessName!)
                .Distinct().ToList();

            _ = LoadIconsForGroupAsync(group, processNames);
            group.IsLoaded = true;
            group.IsExpanded = true;
        }
        catch (Exception ex) { Log.Error(ex, "展开时段失败: {Hour}", group.Hour); }
    }

    private async Task LoadIconsForGroupAsync(TimelineHourGroup group, List<string> processNames)
    {
        try
        {
            await Task.Delay(100);
            var iconDict = await _iconService.GetIconsBatchAsync(processNames, 20);
            var batch = new List<(TimelineActivity activity, ImageSource icon)>();

            foreach (var activity in group.Activities)
            {
                if (iconDict.TryGetValue(activity.ProcessName, out var icon) && icon != null)
                {
                    batch.Add((activity, icon));
                    if (batch.Count >= 10)
                    {
                        var currentBatch = batch.ToList();
                        RunOnUIThread(() => { foreach (var (a, i) in currentBatch) a.Icon = i; });
                        batch.Clear();
                        await Task.Delay(16);
                    }
                }
            }

            if (batch.Count > 0)
            {
                var finalBatch = batch.ToList();
                RunOnUIThread(() => { foreach (var (a, i) in finalBatch) a.Icon = i; });
            }
        }
        catch (Exception ex) { Log.Warning(ex, "[Timeline] 加载时段图标失败"); }
    }

    private static readonly Dictionary<string, bool> ProductiveCategories = new() { ["开发"] = true, ["开发工具"] = true, ["办公"] = true, ["办公软件"] = true };
    private static bool IsProductiveCategory(string? category) => !string.IsNullOrEmpty(category) && ProductiveCategories.TryGetValue(category, out var isProd) && isProd;

    private static string GetCategoryColor(string? category) => !string.IsNullOrEmpty(category) && CategoryColors.TryGetValue(category, out var color) ? color : "#6B7280";
    private static readonly Dictionary<string, string> CategoryColors = new() { ["开发"] = "#4A90E4", ["浏览"] = "#50C878", ["沟通"] = "#FF6B6B", ["娱乐"] = "#9B59B6", ["办公"] = "#F39C12", ["设计"] = "#E74C3C", ["其他"] = "#95A5A6" };

    private static string FormatDuration(double totalMinutes)
    {
        if (totalMinutes < 1) { var seconds = (int)(totalMinutes * 60); return seconds > 0 ? $"{seconds}秒" : "0秒"; }
        var hours = (int)(totalMinutes / 60);
        var minutes = (int)(totalMinutes % 60);
        if (hours > 0 && minutes > 0) return $"{hours}小时{minutes}分钟";
        if (hours > 0) return $"{hours}小时";
        return $"{minutes}分钟";
    }

    private CancellationTokenSource? _loadCts;
    partial void OnSelectedDateChanged(DateTime value)
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        _ = LoadDataAsync(_loadCts.Token);
    }
}

public class TimelineActivity : ObservableObject
{
    public string Time { get; init; } = string.Empty;
    public string Duration { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string CategoryColor { get; init; } = string.Empty;
    public string ProcessName { get; init; } = string.Empty;
    private ImageSource? _icon;
    public ImageSource? Icon { get => _icon; set => SetProperty(ref _icon, value); }
}

public class DateSummary
{
    public DateTime Date { get; init; }
    public string DayName { get; init; } = string.Empty;
    public string DayNumber { get; init; } = string.Empty;
    public string TotalTime { get; init; } = string.Empty;
    public bool IsToday { get; init; }
    public int ProductivityScore { get; init; }
}

public class TimelineHourGroup : ObservableObject
{
    public int Hour { get; init; }
    public string TimeRange { get; init; } = string.Empty;
    public string TotalDuration { get; init; } = string.Empty;
    public int ActivityCount { get; init; }
    public DateTime Date { get; init; }
    public ObservableCollection<TimelineBarSegment> BarSegments { get; } = new();
    private bool _isExpanded;
    public bool IsExpanded { get => _isExpanded; set => SetProperty(ref _isExpanded, value); }
    private bool _isLoaded;
    public bool IsLoaded { get => _isLoaded; set => SetProperty(ref _isLoaded, value); }
    public ObservableCollection<TimelineActivity> Activities { get; } = new();
}

public class TimelineBarSegment
{
    private const double TotalBarWidth = 200;
    public string Color { get; init; } = "#6B7280";
    public double WidthPercent { get; init; }
    public double PixelWidth => Math.Max(2, WidthPercent / 100 * TotalBarWidth);
}
