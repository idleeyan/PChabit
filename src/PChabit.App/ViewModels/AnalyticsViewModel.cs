using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Serilog;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Data;

namespace PChabit.App.ViewModels;

public partial class AnalyticsViewModel : ViewModelBase
{
    private readonly IDbContextFactory<PChabitDbContext> _dbContextFactory;
    
    [ObservableProperty]
    private string _selectedPeriod = "本周";
    
    [ObservableProperty]
    private string _totalActiveTime = "0小时 0分钟";
    
    [ObservableProperty]
    private string _averageDailyTime = "0小时 0分钟";
    
    [ObservableProperty]
    private double _averageProductivity = 0;
    
    [ObservableProperty]
    private string _mostProductiveDay = "无数据";
    
    [ObservableProperty]
    private string _mostProductiveHour = "无数据";
    
    [ObservableProperty]
    private int _totalKeyPresses = 0;
    
    [ObservableProperty]
    private int _totalMouseClicks = 0;
    
    [ObservableProperty]
    private int _totalWebPages = 0;
    
    public ObservableCollection<WeeklyDataItem> WeeklyData { get; } = new();
    public ObservableCollection<TrendItem> Trends { get; } = new();
    public ObservableCollection<PatternItem> Patterns { get; } = new();
    public ObservableCollection<InsightItem> Insights { get; } = new();
    
    public AnalyticsViewModel(IDbContextFactory<PChabitDbContext> dbContextFactory) : base()
    {
        _dbContextFactory = dbContextFactory;
        Title = "数据分析";
    }
    
    private sealed class WeeklyStats
    {
        public List<Core.Entities.AppSession> AppSessions = new();
        public List<Core.Entities.WebSession> WebSessions = new();
        public List<Core.Entities.KeyboardSession> KeyboardSessions = new();
        public List<Core.Entities.MouseSession> MouseSessions = new();
        
        public int TotalHours;
        public int TotalMins;
        public int AvgHours;
        public int AvgMins;
        public double AvgProductivity;
        public string MostProductiveDay = "无数据";
        public string MostProductiveHour = "无数据";
        public int TotalKeyPresses;
        public int TotalClicks;
        public int TotalWebPages;
        
        public List<WeeklyDataItem> WeeklyDataItems = new();
        public List<TrendItem> TrendItems = new();
        public List<PatternItem> PatternItems = new();
        public List<InsightItem> InsightItems = new();
    }
    
    public async Task LoadDataAsync()
    {
        IsLoading = true;
        
        try
        {
            var stats = await Task.Run(ComputeWeeklyStatsAsync);
            if (stats == null) return;
            
            await RunOnUIThreadAsync(async () =>
            {
                TotalActiveTime = $"{stats.TotalHours}小时 {stats.TotalMins}分钟";
                AverageDailyTime = $"{stats.AvgHours}小时 {stats.AvgMins}分钟";
                AverageProductivity = stats.AvgProductivity;
                MostProductiveDay = stats.MostProductiveDay;
                MostProductiveHour = stats.MostProductiveHour;
                TotalKeyPresses = stats.TotalKeyPresses;
                TotalMouseClicks = stats.TotalClicks;
                TotalWebPages = stats.TotalWebPages;
                
                WeeklyData.Clear();
                foreach (var item in stats.WeeklyDataItems)
                    WeeklyData.Add(item);
                
                Trends.Clear();
                foreach (var item in stats.TrendItems)
                    Trends.Add(item);
                
                Patterns.Clear();
                foreach (var item in stats.PatternItems)
                    Patterns.Add(item);
                
                Insights.Clear();
                foreach (var item in stats.InsightItems)
                    Insights.Add(item);
            });
            
            Log.Information("AnalyticsViewModel: Phase 2 完成, WeeklyData={W}, Trends={T}, Patterns={P}, Insights={I}",
                stats.WeeklyDataItems.Count, stats.TrendItems.Count, stats.PatternItems.Count, stats.InsightItems.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "加载分析数据失败");
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    private async Task<WeeklyStats> ComputeWeeklyStatsAsync()
    {
        var stats = new WeeklyStats();
        
        var today = DateTime.Today;
        var diff = (int)today.DayOfWeek - (int)DayOfWeek.Monday;
        if (diff < 0) diff += 7;
        var weekStart = today.AddDays(-diff);
        var weekEnd = weekStart.AddDays(7);
        
        Log.Information("AnalyticsViewModel: 今天={Today} ({DayOfWeek}), 周开始={WeekStart}, 周结束={WeekEnd}", 
            today, today.DayOfWeek, weekStart, weekEnd);
        
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        
        try
        {
            stats.AppSessions = await dbContext.AppSessions
                .AsNoTracking()
                .Where(s => s.StartTime >= weekStart && s.StartTime < weekEnd)
                .ToListAsync();
            Log.Information("AnalyticsViewModel: 本周 AppSessions 数量={Count}", stats.AppSessions.Count);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "加载 AppSessions 失败");
        }
        
        try
        {
            stats.KeyboardSessions = await dbContext.KeyboardSessions
                .AsNoTracking()
                .Where(s => s.Date >= weekStart && s.Date < weekEnd)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "加载 KeyboardSessions 失败");
        }
        
        try
        {
            stats.MouseSessions = await dbContext.MouseSessions
                .AsNoTracking()
                .Where(s => s.Date >= weekStart && s.Date < weekEnd)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "加载 MouseSessions 失败");
        }
        
        try
        {
            stats.WebSessions = await dbContext.WebSessions
                .AsNoTracking()
                .Where(s => s.StartTime >= weekStart && s.StartTime < weekEnd)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "加载 WebSessions 失败");
        }
        
        var sessions = stats.AppSessions;
        
        var totalMinutes = sessions
            .Where(s => s.EndTime.HasValue)
            .Sum(s => (s.EndTime!.Value - s.StartTime).TotalMinutes);
        
        stats.TotalHours = (int)(totalMinutes / 60);
        stats.TotalMins = (int)(totalMinutes % 60);
        
        var daysWithData = sessions
            .Where(s => s.EndTime.HasValue)
            .Select(s => s.StartTime.Date)
            .Distinct()
            .Count();
        
        var avgMinutes = daysWithData > 0 ? totalMinutes / daysWithData : 0;
        stats.AvgHours = (int)(avgMinutes / 60);
        stats.AvgMins = (int)(avgMinutes % 60);
        
        stats.TotalKeyPresses = stats.KeyboardSessions.Sum(s => s.TotalKeyPresses);
        stats.TotalClicks = stats.MouseSessions.Sum(s => s.LeftClickCount + s.RightClickCount + s.MiddleClickCount);
        stats.TotalWebPages = stats.WebSessions.Count;
        
        var productiveMinutes = sessions
            .Where(s => IsProductiveCategory(s.Category))
            .Sum(s => s.EndTime.HasValue ? (s.EndTime!.Value - s.StartTime).TotalMinutes : 0);
        
        stats.AvgProductivity = totalMinutes > 0 ? Math.Round(productiveMinutes / totalMinutes * 100, 1) : 0;
        
        Log.Information("AnalyticsViewModel: 总时间={TotalMinutes:F1}分钟, 有数据天数={DaysWithData}, 平均时间={AvgMinutes:F1}分钟", 
            totalMinutes, daysWithData, avgMinutes);
        Log.Information("AnalyticsViewModel: 生产力时间={ProductiveMinutes:F1}分钟, 平均效率={AvgProductivity}%", 
            productiveMinutes, stats.AvgProductivity);
        
        var dayStats = sessions
            .Where(s => s.EndTime.HasValue)
            .GroupBy(s => s.StartTime.DayOfWeek)
            .Select(g => new { Day = g.Key, Minutes = g.Sum(s => (s.EndTime!.Value - s.StartTime).TotalMinutes) })
            .OrderByDescending(x => x.Minutes)
            .FirstOrDefault();
        
        stats.MostProductiveDay = dayStats != null ? GetDayName(dayStats.Day) : "无数据";
        
        var topHourStats = sessions
            .Where(s => s.EndTime.HasValue)
            .GroupBy(s => s.StartTime.Hour)
            .Select(g => new { Hour = g.Key, Minutes = g.Sum(s => (s.EndTime!.Value - s.StartTime).TotalMinutes) })
            .OrderByDescending(x => x.Minutes)
            .FirstOrDefault();
        
        stats.MostProductiveHour = topHourStats != null 
            ? $"{topHourStats.Hour}:00 - {topHourStats.Hour + 1}:00" 
            : "无数据";
        
        for (int i = 0; i < 7; i++)
        {
            var dayStart = weekStart.AddDays(i);
            var dayEnd = dayStart.AddDays(1);
            
            var daySessions = sessions
                .Where(s => s.StartTime >= dayStart && s.StartTime < dayEnd && s.EndTime.HasValue)
                .ToList();
            
            var dayMinutes = daySessions.Sum(s => (s.EndTime!.Value - s.StartTime).TotalMinutes);
            var dayProductiveMinutes = daySessions
                .Where(s => IsProductiveCategory(s.Category))
                .Sum(s => (s.EndTime!.Value - s.StartTime).TotalMinutes);
            var productivity = dayMinutes > 0 ? (int)(dayProductiveMinutes / dayMinutes * 100) : 0;
            
            stats.WeeklyDataItems.Add(new WeeklyDataItem
            {
                Day = GetDayName(dayStart.DayOfWeek),
                Hours = (int)(dayMinutes / 60),
                Productivity = productivity
            });
        }
        
        var currentWeekDevMinutes = sessions
            .Where(s => s.StartTime >= weekStart && IsProductiveCategory(s.Category) && s.EndTime.HasValue)
            .Sum(s => (s.EndTime!.Value - s.StartTime).TotalMinutes);
        
        var previousWeekStart = weekStart.AddDays(-7);
        var previousWeekEnd = weekStart;
        var previousWeekDevMinutes = 0.0;
        
        try
        {
            var prevSessions = await dbContext.AppSessions
                .AsNoTracking()
                .Where(s => s.StartTime >= previousWeekStart && s.StartTime < previousWeekEnd)
                .ToListAsync();
            
            previousWeekDevMinutes = prevSessions
                .Where(s => IsProductiveCategory(s.Category) && s.EndTime.HasValue)
                .Sum(s => (s.EndTime!.Value - s.StartTime).TotalMinutes);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "加载上周 AppSessions 失败");
        }
        
        Log.Information("LoadTrends: 本周开发时间={CurrentWeek:F1}分钟, 上周开发时间={PreviousWeek:F1}分钟", 
            currentWeekDevMinutes, previousWeekDevMinutes);
        
        if (previousWeekDevMinutes > 0)
        {
            var change = (int)((currentWeekDevMinutes - previousWeekDevMinutes) / previousWeekDevMinutes * 100);
            stats.TrendItems.Add(new TrendItem
            {
                Name = "开发时间",
                Change = $"{(change >= 0 ? "+" : "")}{change}%",
                Direction = change >= 0 ? "up" : "down",
                Description = $"相比上周{(change >= 0 ? "增加" : "减少")}了 {Math.Abs(currentWeekDevMinutes - previousWeekDevMinutes) / 60:F1} 小时"
            });
        }
        else
        {
            stats.TrendItems.Add(new TrendItem
            {
                Name = "开发时间",
                Change = "新增",
                Direction = "up",
                Description = $"本周开发时间 {currentWeekDevMinutes / 60:F1} 小时"
            });
        }
        
        var socialMinutes = sessions
            .Where(s => s.StartTime >= weekStart && s.Category == "社交" && s.EndTime.HasValue)
            .Sum(s => (s.EndTime!.Value - s.StartTime).TotalMinutes);
        
        stats.TrendItems.Add(new TrendItem
        {
            Name = "社交媒体",
            Change = socialMinutes > 0 ? $"{(int)(socialMinutes / 60)}h" : "0h",
            Direction = socialMinutes > 120 ? "down" : "up",
            Description = socialMinutes > 120 ? "建议减少社交媒体使用" : "社交媒体使用时间正常"
        });
        
        var focusSessions = sessions
            .Where(s => s.EndTime.HasValue && (s.EndTime!.Value - s.StartTime).TotalMinutes >= 25)
            .Count();
        
        stats.TrendItems.Add(new TrendItem
        {
            Name = "专注时长",
            Change = $"{focusSessions} 次",
            Direction = focusSessions > 10 ? "up" : "down",
            Description = focusSessions > 10 ? "深度工作状态良好" : "尝试增加专注时间"
        });
        
        var hourStats = sessions
            .Where(s => s.EndTime.HasValue)
            .GroupBy(s => s.StartTime.Hour)
            .Select(g => new { Hour = g.Key, Count = g.Count(), Minutes = g.Sum(s => (s.EndTime!.Value - s.StartTime).TotalMinutes) })
            .OrderByDescending(x => x.Minutes)
            .ToList();
        
        if (hourStats.Any())
        {
            var topHour = hourStats.First();
            stats.PatternItems.Add(new PatternItem
            {
                Title = "高效时段",
                Description = $"{topHour.Hour}:00 - {topHour.Hour + 1}:00 是你最专注的时段",
                Icon = "\uEC92"
            });
        }
        else
        {
            stats.PatternItems.Add(new PatternItem
            {
                Title = "高效时段",
                Description = "暂无足够数据",
                Icon = "\uEC92"
            });
        }
        
        var appSwitches = sessions.Count;
        var patternDaysWithData = sessions.Select(s => s.StartTime.Date).Distinct().Count();
        var avgSwitchesPerHour = patternDaysWithData > 0 ? (double)appSwitches / (patternDaysWithData * 8) : 0;
        
        stats.PatternItems.Add(new PatternItem
        {
            Title = "应用切换",
            Description = $"平均每小时切换 {avgSwitchesPerHour:F1} 次应用",
            Icon = "\uE8FD"
        });
        
        var breakHours = sessions
            .Where(s => s.EndTime.HasValue)
            .GroupBy(s => s.StartTime.Hour)
            .Where(g => g.Sum(s => (s.EndTime!.Value - s.StartTime).TotalMinutes) < 5)
            .Select(g => g.Key)
            .OrderBy(h => h)
            .ToList();
        
        if (breakHours.Any())
        {
            stats.PatternItems.Add(new PatternItem
            {
                Title = "休息模式",
                Description = $"通常在 {string.Join(", ", breakHours.Take(2).Select(h => $"{h}:00"))} 休息",
                Icon = "\uE708"
            });
        }
        else
        {
            stats.PatternItems.Add(new PatternItem
            {
                Title = "休息模式",
                Description = "暂无足够数据分析休息模式",
                Icon = "\uE708"
            });
        }
        
        if (stats.AvgProductivity >= 70)
        {
            stats.InsightItems.Add(new InsightItem
            {
                Type = "success",
                Title = "效率良好",
                Message = $"本周你的平均生产力为 {stats.AvgProductivity:F1}%，继续保持！"
            });
        }
        else if (stats.AvgProductivity >= 50)
        {
            stats.InsightItems.Add(new InsightItem
            {
                Type = "info",
                Title = "效率一般",
                Message = $"本周你的平均生产力为 {stats.AvgProductivity:F1}%，可以尝试减少干扰。"
            });
        }
        else
        {
            stats.InsightItems.Add(new InsightItem
            {
                Type = "warning",
                Title = "效率较低",
                Message = $"本周你的平均生产力为 {stats.AvgProductivity:F1}%，建议专注时间管理。"
            });
        }
        
        var longSessions = sessions
            .Where(s => s.EndTime.HasValue && (s.EndTime!.Value - s.StartTime).TotalHours >= 3)
            .Count();
        
        if (longSessions > 0)
        {
            stats.InsightItems.Add(new InsightItem
            {
                Type = "warning",
                Title = "注意休息",
                Message = $"本周有 {longSessions} 次连续工作超过 3 小时，建议适当休息"
            });
        }
        
        var topApp = sessions
            .GroupBy(s => s.ProcessName)
            .OrderByDescending(g => g.Sum(s => s.EndTime.HasValue ? (s.EndTime!.Value - s.StartTime).TotalMinutes : 0))
            .FirstOrDefault();
        
        if (topApp != null)
        {
            stats.InsightItems.Add(new InsightItem
            {
                Type = "info",
                Title = "最常用应用",
                Message = $"你本周使用 {topApp.Key} 的时间最多"
            });
        }
        
        return stats;
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
    
    private static string GetDayName(DayOfWeek day)
    {
        return day switch
        {
            DayOfWeek.Monday => "周一",
            DayOfWeek.Tuesday => "周二",
            DayOfWeek.Wednesday => "周三",
            DayOfWeek.Thursday => "周四",
            DayOfWeek.Friday => "周五",
            DayOfWeek.Saturday => "周六",
            DayOfWeek.Sunday => "周日",
            _ => "未知"
        };
    }
}

public class WeeklyDataItem
{
    public string Day { get; init; } = string.Empty;
    public int Hours { get; init; }
    public int Productivity { get; init; }
}

public class TrendItem
{
    public string Name { get; init; } = string.Empty;
    public string Change { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}

public class PatternItem
{
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
}

public class InsightItem
{
    public string Type { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
