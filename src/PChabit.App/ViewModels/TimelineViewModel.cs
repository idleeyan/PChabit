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

public partial class TimelineViewModel : ViewModelBase
{
    private readonly PChabitDbContext _dbContext;
    private readonly IAppIconService _iconService;
    
    [ObservableProperty]
    private DateTime _selectedDate = DateTime.Today;
    
    [ObservableProperty]
    private string _viewMode = "day";
    
    [ObservableProperty]
    private bool _hasData;
    
    public ObservableCollection<TimelineHourGroup> HourGroups { get; } = new();
    public ObservableCollection<DateSummary> RecentDates { get; } = new();
    
    public TimelineViewModel(PChabitDbContext dbContext, IAppIconService iconService) : base()
    {
        _dbContext = dbContext;
        _iconService = iconService;
        Title = "时间线";
    }
    
    public async Task LoadDataAsync()
    {
        var totalSw = System.Diagnostics.Stopwatch.StartNew();
        IsLoading = true;
        
        try
        {
            var selectedDate = SelectedDate.Date;
            var nextDay = selectedDate.AddDays(1);
            
            List<Core.Entities.AppSession> appSessions;
            
            try
            {
                var dbSw = System.Diagnostics.Stopwatch.StartNew();
                appSessions = await _dbContext.AppSessions
                    .AsNoTracking()
                    .Where(s => s.StartTime >= selectedDate && s.StartTime < nextDay)
                    .OrderByDescending(s => s.StartTime)
                    .ToListAsync();
                dbSw.Stop();
                Log.Information("[Timeline] 数据库查询完成, 数量: {Count}, 耗时: {ElapsedMs}ms", appSessions.Count, dbSw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "加载 AppSessions 失败");
                appSessions = new List<Core.Entities.AppSession>();
            }
            
            HourGroups.Clear();
            
            var hourlyGroups = appSessions
                .GroupBy(s => s.StartTime.Hour)
                .Select(g => new
                {
                    Hour = g.Key,
                    Sessions = g.OrderByDescending(s => s.StartTime).ToList(),
                    TotalMinutes = g.Where(s => s.EndTime.HasValue)
                        .Sum(s => (s.EndTime!.Value - s.StartTime).TotalMinutes)
                })
                .OrderByDescending(g => g.Hour)
                .ToList();
            
            var currentHour = DateTime.Now.Hour;
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
                
                if (hg.Hour == currentHour || (!hasCurrentHourData && hg == hourlyGroups.First()))
                {
                    hourGroup.IsExpanded = true;
                    hourGroup.IsLoaded = true;
                    hasCurrentHourData = true;
                    
                    foreach (var session in hg.Sessions)
                    {
                        hourGroup.Activities.Add(CreateActivity(session));
                    }
                    
                    var processNames = hg.Sessions
                        .Where(s => !string.IsNullOrEmpty(s.ProcessName))
                        .Select(s => s.ProcessName!)
                        .Distinct()
                        .ToList();
                    _ = LoadIconsForGroupAsync(hourGroup, processNames);
                }
                
                HourGroups.Add(hourGroup);
            }
            
            HasData = HourGroups.Any();
            
            UpdateRecentDates(appSessions);
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
    
    private TimelineActivity CreateActivity(Core.Entities.AppSession session)
    {
        var duration = session.EndTime.HasValue 
            ? (session.EndTime.Value - session.StartTime).TotalMinutes 
            : 0;
        
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
    
    private void UpdateRecentDates(List<Core.Entities.AppSession> appSessions)
    {
        var sessionsByDate = appSessions
            .GroupBy(s => s.StartTime.Date)
            .ToDictionary(g => g.Key, g => g.ToList());
        
        var newDateSummaries = new List<DateSummary>();
        for (int i = 0; i < 7; i++)
        {
            var date = DateTime.Today.AddDays(-i);
            
            var daySessions = sessionsByDate.TryGetValue(date, out var ds) ? ds : new List<Core.Entities.AppSession>();
            var totalMinutes = daySessions
                .Where(s => s.EndTime.HasValue)
                .Sum(s => (s.EndTime!.Value - s.StartTime).TotalMinutes);
            
            newDateSummaries.Add(new DateSummary
            {
                Date = date,
                DayName = date.ToString("ddd"),
                DayNumber = date.Day.ToString(),
                TotalTime = FormatDuration(totalMinutes),
                IsToday = i == 0,
                ProductivityScore = CalculateProductivityScore(daySessions)
            });
        }
        
        RecentDates.Clear();
        foreach (var summary in newDateSummaries)
        {
            RecentDates.Add(summary);
        }
    }
    
    public async Task ExpandHourGroupAsync(TimelineHourGroup group)
    {
        if (group.IsLoaded) 
        {
            group.IsExpanded = true;
            return;
        }
        
        try
        {
            var selectedDate = group.Date;
            var hourStart = selectedDate.AddHours(group.Hour);
            var hourEnd = hourStart.AddHours(1);
            
            var sessions = await _dbContext.AppSessions
                .AsNoTracking()
                .Where(s => s.StartTime >= hourStart && s.StartTime < hourEnd)
                .OrderByDescending(s => s.StartTime)
                .ToListAsync();
            
            group.Activities.Clear();
            foreach (var session in sessions)
            {
                group.Activities.Add(CreateActivity(session));
            }
            
            var processNames = sessions
                .Where(s => !string.IsNullOrEmpty(s.ProcessName))
                .Select(s => s.ProcessName!)
                .Distinct()
                .ToList();
            
            _ = LoadIconsForGroupAsync(group, processNames);
            
            group.IsLoaded = true;
            group.IsExpanded = true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "展开时段失败: {Hour}", group.Hour);
        }
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
                        RunOnUIThread(() =>
                        {
                            foreach (var (a, i) in currentBatch)
                            {
                                a.Icon = i;
                            }
                        });
                        batch.Clear();
                        await Task.Delay(16);
                    }
                }
            }
            
            if (batch.Count > 0)
            {
                var finalBatch = batch.ToList();
                RunOnUIThread(() =>
                {
                    foreach (var (a, i) in finalBatch)
                    {
                        a.Icon = i;
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Timeline] 加载时段图标失败");
        }
    }
    
    private static int CalculateProductivityScore(List<Core.Entities.AppSession> sessions)
    {
        if (!sessions.Any()) return 0;
        
        var totalMinutes = sessions
            .Where(s => s.EndTime.HasValue)
            .Sum(s => (s.EndTime!.Value - s.StartTime).TotalMinutes);
        
        var productiveMinutes = sessions
            .Where(s => s.EndTime.HasValue && IsProductiveCategory(s.Category))
            .Sum(s => (s.EndTime!.Value - s.StartTime).TotalMinutes);
        
        return totalMinutes > 0 ? (int)(productiveMinutes / totalMinutes * 100) : 0;
    }
    
    private static bool IsProductiveCategory(string? category)
    {
        if (string.IsNullOrEmpty(category))
            return false;
        
        return category switch
        {
            "开发" => true,
            "开发工具" => true,
            "办公" => true,
            "办公软件" => true,
            _ => false
        };
    }
    
    private static string GetCategoryColor(string? category)
    {
        return category switch
        {
            "开发" => "#512BD4",
            "浏览" => "#0078D4",
            "沟通" => "#107C10",
            "娱乐" => "#FF8C00",
            _ => "#6B7280"
        };
    }
    
    private static string FormatDuration(double totalMinutes)
    {
        if (totalMinutes < 1)
        {
            var seconds = (int)(totalMinutes * 60);
            return seconds > 0 ? $"{seconds}秒" : "0秒";
        }
        
        var hours = (int)(totalMinutes / 60);
        var minutes = (int)(totalMinutes % 60);
        
        if (hours > 0 && minutes > 0)
            return $"{hours}小时{minutes}分钟";
        if (hours > 0)
            return $"{hours}小时";
        return $"{minutes}分钟";
    }
    
    partial void OnSelectedDateChanged(DateTime value)
    {
        _ = LoadDataAsync();
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
    public ImageSource? Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }
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
    
    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }
    
    private bool _isLoaded;
    public bool IsLoaded
    {
        get => _isLoaded;
        set => SetProperty(ref _isLoaded, value);
    }
    
    public ObservableCollection<TimelineActivity> Activities { get; } = new();
}
