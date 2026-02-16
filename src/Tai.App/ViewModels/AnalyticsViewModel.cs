using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Tai.Core.Interfaces;
using Tai.Infrastructure.Data;

namespace Tai.App.ViewModels;

public partial class AnalyticsViewModel : ViewModelBase
{
    private readonly TaiDbContext _dbContext;
    
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
    
    public AnalyticsViewModel(TaiDbContext dbContext) : base()
    {
        _dbContext = dbContext;
        Title = "数据分析";
    }
    
    public async Task LoadDataAsync()
    {
        IsLoading = true;
        
        try
        {
            var today = DateTime.Today;
            
            // 计算本周开始（周一为一周的第一天）
            var diff = (int)today.DayOfWeek - (int)DayOfWeek.Monday;
            if (diff < 0) diff += 7;
            var weekStart = today.AddDays(-diff);
            var weekEnd = weekStart.AddDays(7);
            
            Log.Information("AnalyticsViewModel: 今天={Today} ({DayOfWeek}), 周开始={WeekStart}, 周结束={WeekEnd}", today, today.DayOfWeek, weekStart, weekEnd);
            
            try { _dbContext.ChangeTracker.Clear(); } catch { }
            
            List<Core.Entities.AppSession> appSessions;
            List<Core.Entities.KeyboardSession> keyboardSessions;
            List<Core.Entities.MouseSession> mouseSessions;
            List<Core.Entities.WebSession> webSessions;
            
            try
            {
                appSessions = await _dbContext.AppSessions
                    .Where(s => s.StartTime >= weekStart && s.StartTime < weekEnd)
                    .ToListAsync();
                Log.Information("AnalyticsViewModel: 本周 AppSessions 数量={Count}", appSessions.Count);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "加载 AppSessions 失败");
                appSessions = new List<Core.Entities.AppSession>();
            }
            
            try
            {
                keyboardSessions = await _dbContext.KeyboardSessions
                    .Where(s => s.Date >= weekStart && s.Date < weekEnd)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "加载 KeyboardSessions 失败");
                keyboardSessions = new List<Core.Entities.KeyboardSession>();
            }
            
            try
            {
                mouseSessions = await _dbContext.MouseSessions
                    .Where(s => s.Date >= weekStart && s.Date < weekEnd)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "加载 MouseSessions 失败");
                mouseSessions = new List<Core.Entities.MouseSession>();
            }
            
            try
            {
                webSessions = await _dbContext.WebSessions
                    .Where(s => s.StartTime >= weekStart && s.StartTime < weekEnd)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "加载 WebSessions 失败");
                webSessions = new List<Core.Entities.WebSession>();
            }
            
            var totalMinutes = appSessions
                .Where(s => s.EndTime.HasValue)
                .Sum(s => (s.EndTime!.Value - s.StartTime).TotalMinutes);
            
            var totalHours = (int)(totalMinutes / 60);
            var totalMins = (int)(totalMinutes % 60);
            
            var daysWithData = appSessions
                .Where(s => s.EndTime.HasValue)
                .Select(s => s.StartTime.Date)
                .Distinct()
                .Count();
            
            var avgMinutes = daysWithData > 0 ? totalMinutes / daysWithData : 0;
            var avgHours = (int)(avgMinutes / 60);
            var avgMins = (int)(avgMinutes % 60);
            
            var totalKeyPresses = keyboardSessions.Sum(s => s.TotalKeyPresses);
            var totalClicks = mouseSessions.Sum(s => s.LeftClickCount + s.RightClickCount + s.MiddleClickCount);
            var totalWebPages = webSessions.Count;
            
            var productiveMinutes = appSessions
                .Where(s => IsProductiveCategory(s.Category))
                .Sum(s => s.EndTime.HasValue ? (s.EndTime!.Value - s.StartTime).TotalMinutes : 0);
            
            var avgProductivity = totalMinutes > 0 ? Math.Round(productiveMinutes / totalMinutes * 100, 1) : 0;
            
            Log.Information("AnalyticsViewModel: 总时间={TotalMinutes:F1}分钟, 有数据天数={DaysWithData}, 平均时间={AvgMinutes:F1}分钟", 
                totalMinutes, daysWithData, avgMinutes);
            Log.Information("AnalyticsViewModel: 生产力时间={ProductiveMinutes:F1}分钟, 平均效率={AvgProductivity}%", 
                productiveMinutes, avgProductivity);
            
            var categories = appSessions
                .Where(s => s.EndTime.HasValue)
                .GroupBy(s => s.Category)
                .Select(g => new { Category = g.Key, Minutes = g.Sum(s => (s.EndTime!.Value - s.StartTime).TotalMinutes) })
                .ToList();
            foreach (var cat in categories)
            {
                Log.Information("AnalyticsViewModel: 分类 {Category} = {Minutes:F1}分钟", cat.Category, cat.Minutes);
            }
            
            var dayStats = appSessions
                .Where(s => s.EndTime.HasValue)
                .GroupBy(s => s.StartTime.DayOfWeek)
                .Select(g => new { Day = g.Key, Minutes = g.Sum(s => (s.EndTime!.Value - s.StartTime).TotalMinutes) })
                .OrderByDescending(x => x.Minutes)
                .FirstOrDefault();
            
            var mostProductiveDay = dayStats != null 
                ? GetDayName(dayStats.Day) 
                : "无数据";
            
            var hourStats = appSessions
                .Where(s => s.EndTime.HasValue)
                .GroupBy(s => s.StartTime.Hour)
                .Select(g => new { Hour = g.Key, Minutes = g.Sum(s => (s.EndTime!.Value - s.StartTime).TotalMinutes) })
                .OrderByDescending(x => x.Minutes)
                .FirstOrDefault();
            
            var mostProductiveHour = hourStats != null 
                ? $"{hourStats.Hour}:00 - {hourStats.Hour + 1}:00" 
                : "无数据";
            
            TotalActiveTime = $"{totalHours}小时 {totalMins}分钟";
            AverageDailyTime = $"{avgHours}小时 {avgMins}分钟";
            AverageProductivity = avgProductivity;
            MostProductiveDay = mostProductiveDay;
            MostProductiveHour = mostProductiveHour;
            TotalKeyPresses = totalKeyPresses;
            TotalMouseClicks = totalClicks;
            TotalWebPages = totalWebPages;
            
            LoadWeeklyData(appSessions, weekStart);
            await LoadTrendsAsync(appSessions, webSessions, weekStart);
            LoadPatterns(appSessions);
            LoadInsights(appSessions, avgProductivity);
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
    
    private void LoadWeeklyData(List<Core.Entities.AppSession> sessions, DateTime weekStart)
    {
        WeeklyData.Clear();
        
        Log.Information("LoadWeeklyData: 开始加载周数据, weekStart={WeekStart}", weekStart);
        
        for (int i = 0; i < 7; i++)
        {
            var dayStart = weekStart.AddDays(i);
            var dayEnd = dayStart.AddDays(1);
            
            var daySessions = sessions
                .Where(s => s.StartTime >= dayStart && s.StartTime < dayEnd && s.EndTime.HasValue)
                .ToList();
            
            var dayMinutes = daySessions.Sum(s => (s.EndTime!.Value - s.StartTime).TotalMinutes);
            
            var productiveMinutes = daySessions
                .Where(s => IsProductiveCategory(s.Category))
                .Sum(s => (s.EndTime!.Value - s.StartTime).TotalMinutes);
            
            var productivity = dayMinutes > 0 ? (int)(productiveMinutes / dayMinutes * 100) : 0;
            
            Log.Information("LoadWeeklyData: {Day} ({Date}), 会话数={Count}, 分钟={Minutes:F1}, 生产力={Productivity}%", 
                GetDayName(dayStart.DayOfWeek), dayStart.ToString("yyyy-MM-dd"), daySessions.Count, dayMinutes, productivity);
            
            WeeklyData.Add(new WeeklyDataItem
            {
                Day = GetDayName(dayStart.DayOfWeek),
                Hours = (int)(dayMinutes / 60),
                Productivity = productivity
            });
        }
    }
    
    private async Task LoadTrendsAsync(List<Core.Entities.AppSession> sessions, List<Core.Entities.WebSession> webSessions, DateTime weekStart)
    {
        Trends.Clear();
        
        var currentWeekDevMinutes = sessions
            .Where(s => s.StartTime >= weekStart && IsProductiveCategory(s.Category) && s.EndTime.HasValue)
            .Sum(s => (s.EndTime!.Value - s.StartTime).TotalMinutes);
        
        var previousWeekStart = weekStart.AddDays(-7);
        var previousWeekEnd = weekStart;
        
        List<Core.Entities.AppSession> previousWeekSessions;
        try
        {
            previousWeekSessions = await _dbContext.AppSessions
                .Where(s => s.StartTime >= previousWeekStart && s.StartTime < previousWeekEnd)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "加载上周 AppSessions 失败");
            previousWeekSessions = new List<Core.Entities.AppSession>();
        }
        
        var previousWeekDevMinutes = previousWeekSessions
            .Where(s => IsProductiveCategory(s.Category) && s.EndTime.HasValue)
            .Sum(s => (s.EndTime!.Value - s.StartTime).TotalMinutes);
        
        Log.Information("LoadTrends: 本周开发时间={CurrentWeek:F1}分钟, 上周开发时间={PreviousWeek:F1}分钟", 
            currentWeekDevMinutes, previousWeekDevMinutes);
        
        if (previousWeekDevMinutes > 0)
        {
            var change = (int)((currentWeekDevMinutes - previousWeekDevMinutes) / previousWeekDevMinutes * 100);
            Trends.Add(new TrendItem
            {
                Name = "开发时间",
                Change = $"{(change >= 0 ? "+" : "")}{change}%",
                Direction = change >= 0 ? "up" : "down",
                Description = $"相比上周{(change >= 0 ? "增加" : "减少")}了 {Math.Abs(currentWeekDevMinutes - previousWeekDevMinutes) / 60:F1} 小时"
            });
        }
        else
        {
            Trends.Add(new TrendItem
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
        
        Trends.Add(new TrendItem
        {
            Name = "社交媒体",
            Change = socialMinutes > 0 ? $"{(int)(socialMinutes / 60)}h" : "0h",
            Direction = socialMinutes > 120 ? "down" : "up",
            Description = socialMinutes > 120 ? "建议减少社交媒体使用" : "社交媒体使用时间正常"
        });
        
        var focusSessions = sessions
            .Where(s => s.EndTime.HasValue && (s.EndTime!.Value - s.StartTime).TotalMinutes >= 25)
            .Count();
        
        Trends.Add(new TrendItem
        {
            Name = "专注时长",
            Change = $"{focusSessions} 次",
            Direction = focusSessions > 10 ? "up" : "down",
            Description = focusSessions > 10 ? "深度工作状态良好" : "尝试增加专注时间"
        });
    }
    
    private void LoadPatterns(List<Core.Entities.AppSession> sessions)
    {
        Patterns.Clear();
        
        var hourStats = sessions
            .Where(s => s.EndTime.HasValue)
            .GroupBy(s => s.StartTime.Hour)
            .Select(g => new { Hour = g.Key, Count = g.Count(), Minutes = g.Sum(s => (s.EndTime!.Value - s.StartTime).TotalMinutes) })
            .OrderByDescending(x => x.Minutes)
            .ToList();
        
        if (hourStats.Any())
        {
            var topHour = hourStats.First();
            Patterns.Add(new PatternItem
            {
                Title = "高效时段",
                Description = $"{topHour.Hour}:00 - {topHour.Hour + 1}:00 是你最专注的时段",
                Icon = "\uEC92"
            });
        }
        else
        {
            Patterns.Add(new PatternItem
            {
                Title = "高效时段",
                Description = "暂无足够数据",
                Icon = "\uEC92"
            });
        }
        
        var appSwitches = sessions.Count;
        var daysWithData = sessions.Select(s => s.StartTime.Date).Distinct().Count();
        var avgSwitchesPerHour = daysWithData > 0 ? (double)appSwitches / (daysWithData * 8) : 0;
        
        Patterns.Add(new PatternItem
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
            Patterns.Add(new PatternItem
            {
                Title = "休息模式",
                Description = $"通常在 {string.Join(", ", breakHours.Take(2).Select(h => $"{h}:00"))} 休息",
                Icon = "\uE708"
            });
        }
        else
        {
            Patterns.Add(new PatternItem
            {
                Title = "休息模式",
                Description = "暂无足够数据分析休息模式",
                Icon = "\uE708"
            });
        }
    }
    
    private void LoadInsights(List<Core.Entities.AppSession> sessions, double avgProductivity)
    {
        Insights.Clear();
        
        if (avgProductivity >= 70)
        {
            Insights.Add(new InsightItem
            {
                Type = "success",
                Title = "效率良好",
                Message = $"本周你的平均生产力为 {avgProductivity:F1}%，继续保持！"
            });
        }
        else if (avgProductivity >= 50)
        {
            Insights.Add(new InsightItem
            {
                Type = "info",
                Title = "效率一般",
                Message = $"本周你的平均生产力为 {avgProductivity:F1}%，可以尝试减少干扰。"
            });
        }
        else
        {
            Insights.Add(new InsightItem
            {
                Type = "warning",
                Title = "效率较低",
                Message = $"本周你的平均生产力为 {avgProductivity:F1}%，建议专注时间管理。"
            });
        }
        
        var longSessions = sessions
            .Where(s => s.EndTime.HasValue && (s.EndTime!.Value - s.StartTime).TotalHours >= 3)
            .Count();
        
        if (longSessions > 0)
        {
            Insights.Add(new InsightItem
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
            Insights.Add(new InsightItem
            {
                Type = "info",
                Title = "最常用应用",
                Message = $"你本周使用 {topApp.Key} 的时间最多"
            });
        }
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
