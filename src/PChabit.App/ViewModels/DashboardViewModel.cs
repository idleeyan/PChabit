using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Serilog;
using PChabit.App.Services;
using PChabit.Core.Entities;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Data;

namespace PChabit.App.ViewModels;

public partial class DashboardViewModel : DbSafeViewModel<DashboardViewModel.DashboardStats>
{
    private readonly IDbContextFactory<PChabitDbContext> _dbContextFactory;
    private readonly IAppIconService _iconService;
    
    public DashboardViewModel(IDbContextFactory<PChabitDbContext> dbContextFactory, IAppIconService iconService) : base()
    {
        _dbContextFactory = dbContextFactory;
        _iconService = iconService;
        Title = "仪表盘";
    }
    
    [ObservableProperty]
    private string _todayActiveTime = "0小时 0分钟";
    
    [ObservableProperty]
    private string _todayKeyPresses = "0";
    
    [ObservableProperty]
    private string _todayMouseClicks = "0";
    
    [ObservableProperty]
    private string _todayWebPages = "0";
    
    [ObservableProperty]
    private double _productivityScore = 0;
    
    [ObservableProperty]
    private string _mostUsedApp = "无数据";
    
    [ObservableProperty]
    private ImageSource? _mostUsedAppIcon;
    
    [ObservableProperty]
    private string _mostVisitedSite = "无数据";
    
    public ObservableCollection<AppUsageItem> TopApps { get; } = new();
    public ObservableCollection<HourlyActivityItem> HourlyActivity { get; } = new();
    public ObservableCollection<CategoryDistributionItem> CategoryDistribution { get; } = new();
    public ObservableCollection<WebsiteUsageItem> TopWebsites { get; } = new();
    
    [ObservableProperty]
    private bool _hasAppData = false;
    
    [ObservableProperty]
    private bool _hasWebsiteData = false;

    // === Phase 1 中间数据类（纯 POCO，无 WinRT 类型） ===

    public sealed class DashboardStats
    {
        public string TodayActiveTime = "0小时 0分钟";
        public string TodayKeyPresses = "0";
        public string TodayMouseClicks = "0";
        public string TodayWebPages = "0";
        public double ProductivityScore;
        public string MostUsedApp = "无数据";
        public string MostVisitedSite = "无数据";
        
        public List<(string ProcessName, double Duration)> TopAppsRaw = new();
        public List<HourlyActivityData> HourlyActivity = new();
        public List<(string Name, double Duration)> CategoryRaw = new();
        public List<WebsiteUsageData> TopWebsites = new();
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

    // === DbSafeViewModel 抽象方法实现 ===

    protected override async Task<DashboardStats> LoadStatsOnBackgroundAsync()
    {
        var today = DateTime.Today;
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

        // 今日必须走实时会话：DailySummary 可能被网络流量写成「仅 NetBytes」空壳
        //（TopApps=[]、ActiveMinutes=0），且日终聚合才补齐——读它会把仪表盘显示成 0。
        return await BuildStatsFromRawDataAsync(dbContext, today);
    }

    private static async Task<DashboardStats> BuildStatsFromRawDataAsync(PChabitDbContext dbContext, DateTime today)
    {
        var tomorrow = today.AddDays(1);
        var now = DateTime.Now;
        var isToday = today == DateTime.Today;

        var appSessions = await dbContext.AppSessions
            .AsNoTracking()
            .Where(s => s.StartTime >= today && s.StartTime < tomorrow)
            .ToListAsync();

        var keyboardSessions = await dbContext.KeyboardSessions
            .AsNoTracking()
            .Where(s => s.Date >= today && s.Date < tomorrow)
            .ToListAsync();

        var mouseSessions = await dbContext.MouseSessions
            .AsNoTracking()
            .Where(s => s.Date >= today && s.Date < tomorrow)
            .ToListAsync();

        // 排除 Legacy 切片，与日聚合 / 历史分析口径一致
        var webSessions = await dbContext.WebSessions
            .AsNoTracking()
            .Where(s => s.StartTime >= today && s.StartTime < tomorrow && !s.IsLegacy)
            .ToListAsync();

        Log.Information("仪表盘数据加载: AppSessions={AppCnt}, KeyboardSessions={KbCnt}({KbKeys}次按键), MouseSessions={MsCnt}({MsClicks}次点击), WebSessions={WebCnt}",
            appSessions.Count, keyboardSessions.Count, keyboardSessions.Sum(s => s.TotalKeyPresses),
            mouseSessions.Count, mouseSessions.Sum(s => s.LeftClickCount + s.RightClickCount + s.MiddleClickCount),
            webSessions.Count);

        double SessionMinutes(AppSession s) => SessionActiveMinutes(s, isToday ? now : today);

        var totalMinutes = appSessions.Sum(SessionMinutes);

        var totalKeyPresses = keyboardSessions.Sum(s => s.TotalKeyPresses);
        var totalClicks = mouseSessions.Sum(s => s.LeftClickCount + s.RightClickCount + s.MiddleClickCount);
        var totalWebPages = webSessions.Count;

        var hours = (int)(totalMinutes / 60);
        var minutes = (int)(totalMinutes % 60);

        var productiveMinutes = appSessions
            .Where(s => IsProductiveCategory(s.Category))
            .Sum(SessionMinutes);

        var stats = new DashboardStats
        {
            TodayActiveTime = $"{hours}小时 {minutes}分钟",
            TodayKeyPresses = totalKeyPresses.ToString("N0"),
            TodayMouseClicks = totalClicks.ToString("N0"),
            TodayWebPages = totalWebPages.ToString(),
            ProductivityScore = totalMinutes > 0 ? Math.Round(productiveMinutes / totalMinutes * 100, 1) : 0
        };

        // 计算 TopApps
        var topApps = appSessions
            .GroupBy(s => s.ProcessName)
            .Select(g => new { ProcessName = g.Key, Duration = g.Sum(SessionMinutes) })
            .OrderByDescending(x => x.Duration)
            .Take(5)
            .ToList();

        if (topApps.Any())
        {
            stats.MostUsedApp = topApps.First().ProcessName;
            stats.TopAppsRaw = topApps.Select(x => (x.ProcessName, x.Duration)).ToList();
        }

        // 计算 HourlyActivity（今日未来小时不画）
        for (int i = 0; i < 24; i++)
        {
            var hourStart = today.AddHours(i);
            var hourEnd = hourStart.AddHours(1);
            if (isToday && hourStart >= now) break;

            var clipEnd = isToday && hourEnd > now ? now : hourEnd;
            var hourMinutes = appSessions.Sum(s =>
            {
                var start = s.StartTime < hourStart ? hourStart : s.StartTime;
                var end = SessionEnd(s, isToday ? now : today);
                if (end > clipEnd) end = clipEnd;
                return Math.Max(0, (end - start).TotalMinutes);
            });
            stats.HourlyActivity.Add(new HourlyActivityData
            {
                Hour = $"{i}:00",
                Activity = Math.Min(100, (int)(hourMinutes / 60 * 100)),
                Category = hourMinutes > 40 ? "高效" : hourMinutes > 20 ? "中等" : "低效"
            });
        }

        // 计算 CategoryDistribution
        var categories = appSessions
            .GroupBy(s => s.Category ?? "其他")
            .Select(g => new { Name = g.Key, Duration = g.Sum(SessionMinutes) })
            .OrderByDescending(x => x.Duration)
            .Take(5)
            .ToList();
        stats.CategoryRaw = categories.Select(x => (x.Name, x.Duration)).ToList();

        // 计算 TopWebsites
        var topSites = webSessions
            .GroupBy(s => s.Domain)
            .Select(g => new { Domain = g.Key, Count = g.Count(), TotalDurationTicks = g.Sum(s => s.Duration.Ticks) })
            .OrderByDescending(x => x.Count)
            .Take(5)
            .ToList();

        if (topSites.Any())
        {
            stats.MostVisitedSite = topSites.First().Domain ?? "无数据";
            stats.TopWebsites = topSites
                .Where(s => !string.IsNullOrEmpty(s.Domain))
                .Select(s => new WebsiteUsageData { Domain = s.Domain!, Visits = s.Count, TotalDurationTicks = s.TotalDurationTicks })
                .ToList();
        }

        return stats;
    }

    /// <summary>会话有效时长（分钟）：优先 ActiveDuration；未结束会话仅今日按 clock 截断计入。</summary>
    private static double SessionActiveMinutes(AppSession s, DateTime clock)
    {
        if (s.ActiveDuration > TimeSpan.Zero)
            return Math.Max(0, s.ActiveDuration.TotalMinutes);
        return Math.Max(0, (SessionEnd(s, clock) - s.StartTime).TotalMinutes);
    }

    private static DateTime SessionEnd(AppSession s, DateTime clock)
    {
        if (s.EndTime.HasValue) return s.EndTime.Value;
        // 未结束：仅今日累计到当前时刻；历史日视为未闭合，不计
        if (clock > s.StartTime && s.StartTime.Date == DateTime.Today)
            return clock;
        return s.StartTime;
    }

    protected override async Task ApplyStatsOnUIAsync(DashboardStats stats)
    {
        TodayActiveTime = stats.TodayActiveTime;
        TodayKeyPresses = stats.TodayKeyPresses;
        TodayMouseClicks = stats.TodayMouseClicks;
        TodayWebPages = stats.TodayWebPages;
        ProductivityScore = stats.ProductivityScore;
        MostUsedApp = stats.MostUsedApp;
        MostVisitedSite = stats.MostVisitedSite;

        if (stats.TopAppsRaw.Any())
        {
            _ = LoadMostUsedAppIconAsync(stats.TopAppsRaw.First().ProcessName);

            var totalDuration = stats.TopAppsRaw.Sum(x => x.Duration);
            TopApps.Clear();
            foreach (var (processName, duration) in stats.TopAppsRaw)
            {
                var appHours = (int)(duration / 60);
                var appMins = (int)(duration % 60);
                var item = new AppUsageItem
                {
                    Name = processName,
                    ProcessName = processName,
                    Duration = $"{appHours}h {appMins}m",
                    Percentage = totalDuration > 0 ? $"{(int)(duration / totalDuration * 100)}%" : "0%",
                    Category = GetAppCategory(processName)
                };
                TopApps.Add(item);
                _ = LoadAppIconAsync(item);
            }
        }

        if (stats.TopWebsites.Any())
        {
            TopWebsites.Clear();
            foreach (var site in stats.TopWebsites)
            {
                TopWebsites.Add(new WebsiteUsageItem
                {
                    Domain = site.Domain,
                    Visits = site.Visits,
                    Duration = FormatDuration(TimeSpan.FromTicks(site.TotalDurationTicks)),
                    FaviconUrl = $"https://www.google.com/s2/favicons?domain={site.Domain}&sz=32"
                });
            }
        }

        HourlyActivity.Clear();
        foreach (var item in stats.HourlyActivity)
            HourlyActivity.Add(new HourlyActivityItem { Hour = item.Hour, Activity = item.Activity, Category = item.Category });

        var totalCategoryDuration = stats.CategoryRaw.Sum(x => x.Duration);
        CategoryDistribution.Clear();
        var colorStrings = new[] { "#512BD4", "#0078D4", "#107C10", "#FF8C00", "#6B7280" };
        for (int i = 0; i < stats.CategoryRaw.Count && i < 5; i++)
        {
            CategoryDistribution.Add(new CategoryDistributionItem
            {
                Name = stats.CategoryRaw[i].Name,
                Percentage = totalCategoryDuration > 0 ? $"{(int)(stats.CategoryRaw[i].Duration / totalCategoryDuration * 100)}%" : "0%",
                Color = CreateBrush(colorStrings[i])
            });
        }

        HasAppData = TopApps.Count > 0;
        HasWebsiteData = TopWebsites.Count > 0;
    }
    
    private async Task LoadMostUsedAppIconAsync(string processName)
    {
        try
        {
            var icon = await _iconService.GetAppIconAsync(processName, 24);
            if (icon != null)
            {
                MostUsedAppIcon = icon;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "加载应用图标失败: {ProcessName}", processName);
        }
    }
    
    private async Task LoadAppIconAsync(AppUsageItem item)
    {
        try
        {
            var icon = await _iconService.GetAppIconAsync(item.ProcessName, 20);
            if (icon != null)
            {
                item.Icon = icon;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "加载应用图标失败: {ProcessName}", item.ProcessName);
        }
    }
    
    private static string GetAppCategory(string processName)
    {
        return processName.ToLower() switch
        {
            var p when p.Contains("code") || p.Contains("visual studio") || p.Contains("idea") => "开发",
            var p when p.Contains("chrome") || p.Contains("edge") || p.Contains("firefox") => "浏览",
            var p when p.Contains("slack") || p.Contains("teams") || p.Contains("wechat") => "沟通",
            var p when p.Contains("spotify") || p.Contains("netflix") || p.Contains("youtube") => "娱乐",
            _ => "其他"
        };
    }
    
    private static string FormatDuration(TimeSpan? duration)
    {
        if (!duration.HasValue) return "0m";
        
        var totalMinutes = (int)duration.Value.TotalMinutes;
        if (totalMinutes < 60)
        {
            return $"{totalMinutes}m";
        }
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        return $"{hours}h {minutes}m";
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
    
    private static SolidColorBrush CreateBrush(string hexColor)
    {
        var color = Microsoft.UI.Colors.Transparent;
        if (hexColor.StartsWith("#") && hexColor.Length == 7)
        {
            var r = Convert.ToByte(hexColor.Substring(1, 2), 16);
            var g = Convert.ToByte(hexColor.Substring(3, 2), 16);
            var b = Convert.ToByte(hexColor.Substring(5, 2), 16);
            color = Microsoft.UI.ColorHelper.FromArgb(255, r, g, b);
        }
        return new SolidColorBrush(color);
    }
}

public class AppUsageItem : ObservableObject
{
    public string Name { get; init; } = string.Empty;
    public string ProcessName { get; init; } = string.Empty;
    public string Duration { get; init; } = string.Empty;
    public string Percentage { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    
    private ImageSource? _icon;
    public ImageSource? Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }
}

public class HourlyActivityItem
{
    public string Hour { get; init; } = string.Empty;
    public int Activity { get; init; }
    public string Category { get; init; } = string.Empty;
}

public class CategoryDistributionItem
{
    public string Name { get; init; } = string.Empty;
    public string Percentage { get; init; } = string.Empty;
    public SolidColorBrush Color { get; init; } = new SolidColorBrush();
}

public class WebsiteUsageItem
{
    public string Domain { get; init; } = string.Empty;
    public string FirstLetter => string.IsNullOrEmpty(Domain) ? "?" : Domain[0].ToString().ToUpper();
    public int Visits { get; init; }
    public string VisitsText => $"{Visits} 次访问";
    public string Duration { get; init; } = string.Empty;
    public string FaviconUrl { get; init; } = string.Empty;
}
