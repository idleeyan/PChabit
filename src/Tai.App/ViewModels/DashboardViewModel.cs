using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Serilog;
using Tai.App.Services;
using Tai.Core.Interfaces;
using Tai.Infrastructure.Data;

namespace Tai.App.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly TaiDbContext _dbContext;
    private readonly IAppIconService _iconService;
    
    public DashboardViewModel(TaiDbContext dbContext, IAppIconService iconService) : base()
    {
        _dbContext = dbContext;
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
    
    public async Task LoadDataAsync()
    {
        IsLoading = true;
        
        try
        {
            var today = DateTime.Today;
            var tomorrow = today.AddDays(1);
            
            try { _dbContext.ChangeTracker.Clear(); } catch { }
            
            List<Core.Entities.AppSession> appSessions;
            List<Core.Entities.KeyboardSession> keyboardSessions;
            List<Core.Entities.MouseSession> mouseSessions;
            List<Core.Entities.WebSession> webSessions;
            
            try
            {
                appSessions = await _dbContext.AppSessions
                    .Where(s => s.StartTime >= today && s.StartTime < tomorrow)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "加载 AppSessions 失败");
                appSessions = new List<Core.Entities.AppSession>();
            }
            
            try
            {
                keyboardSessions = await _dbContext.KeyboardSessions
                    .Where(s => s.Date == today)
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
                    .Where(s => s.Date == today)
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
                    .Where(s => s.StartTime >= today && s.StartTime < tomorrow)
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
            
            var totalKeyPresses = keyboardSessions.Sum(s => s.TotalKeyPresses);
            var totalClicks = mouseSessions.Sum(s => s.LeftClickCount + s.RightClickCount + s.MiddleClickCount);
            var totalWebPages = webSessions.Count;
            
            var hours = (int)(totalMinutes / 60);
            var minutes = (int)(totalMinutes % 60);
            
            TodayActiveTime = $"{hours}小时 {minutes}分钟";
            TodayKeyPresses = totalKeyPresses.ToString("N0");
            TodayMouseClicks = totalClicks.ToString("N0");
            TodayWebPages = totalWebPages.ToString();
            
            var topApps = appSessions
                .GroupBy(s => s.ProcessName)
                .Select(g => new { ProcessName = g.Key, Duration = g.Sum(s => s.EndTime.HasValue ? (s.EndTime!.Value - s.StartTime).TotalMinutes : 0) })
                .OrderByDescending(x => x.Duration)
                .Take(5)
                .ToList();
            
            if (topApps.Any())
            {
                MostUsedApp = topApps.First().ProcessName;
                _ = LoadMostUsedAppIconAsync(topApps.First().ProcessName);
                
                var totalDuration = topApps.Sum(x => x.Duration);
                TopApps.Clear();
                foreach (var app in topApps)
                {
                    var appHours = (int)(app.Duration / 60);
                    var appMins = (int)(app.Duration % 60);
                    var item = new AppUsageItem
                    {
                        Name = app.ProcessName,
                        ProcessName = app.ProcessName,
                        Duration = $"{appHours}h {appMins}m",
                        Percentage = totalDuration > 0 ? $"{(int)(app.Duration / totalDuration * 100)}%" : "0%",
                        Category = GetAppCategory(app.ProcessName)
                    };
                    TopApps.Add(item);
                    _ = LoadAppIconAsync(item);
                }
            }
            
            var topSites = webSessions
                .GroupBy(s => s.Domain)
                .Select(g => new { Domain = g.Key, Count = g.Count(), TotalDurationTicks = g.Sum(s => s.Duration.Ticks) })
                .OrderByDescending(x => x.Count)
                .ToList();
            
            if (topSites.Any())
            {
                MostVisitedSite = topSites.First().Domain ?? "无数据";
                
                TopWebsites.Clear();
                foreach (var site in topSites.Take(5))
                {
                    if (!string.IsNullOrEmpty(site.Domain))
                    {
                        TopWebsites.Add(new WebsiteUsageItem
                        {
                            Domain = site.Domain,
                            Visits = site.Count,
                            Duration = FormatDuration(TimeSpan.FromTicks(site.TotalDurationTicks)),
                            FaviconUrl = $"https://www.google.com/s2/favicons?domain={site.Domain}&sz=32"
                        });
                    }
                }
            }
            
            HourlyActivity.Clear();
            for (int i = 0; i < 24; i++)
            {
                var hourStart = today.AddHours(i);
                var hourEnd = hourStart.AddHours(1);
                
                var hourMinutes = appSessions
                    .Where(s => s.StartTime < hourEnd && (s.EndTime == null || s.EndTime > hourStart))
                    .Sum(s => 
                    {
                        var start = s.StartTime < hourStart ? hourStart : s.StartTime;
                        var end = s.EndTime == null || s.EndTime > hourEnd ? hourEnd : s.EndTime.Value;
                        return (end - start).TotalMinutes;
                    });
                
                HourlyActivity.Add(new HourlyActivityItem
                {
                    Hour = $"{i}:00",
                    Activity = Math.Min(100, (int)(hourMinutes / 60 * 100)),
                    Category = hourMinutes > 40 ? "高效" : hourMinutes > 20 ? "中等" : "低效"
                });
            }
            
            var categories = appSessions
                .GroupBy(s => s.Category ?? "其他")
                .Select(g => new { Category = g.Key, Duration = g.Sum(s => s.EndTime.HasValue ? (s.EndTime!.Value - s.StartTime).TotalMinutes : 0) })
                .OrderByDescending(x => x.Duration)
                .ToList();
            
            var totalCategoryDuration = categories.Sum(x => x.Duration);
            CategoryDistribution.Clear();
            var colorStrings = new[] { "#512BD4", "#0078D4", "#107C10", "#FF8C00", "#6B7280" };
            for (int i = 0; i < categories.Count && i < 5; i++)
            {
                CategoryDistribution.Add(new CategoryDistributionItem
                {
                    Name = categories[i].Category,
                    Percentage = totalCategoryDuration > 0 ? $"{(int)(categories[i].Duration / totalCategoryDuration * 100)}%" : "0%",
                    Color = CreateBrush(colorStrings[i])
                });
            }
            
            var productiveMinutes = appSessions
                .Where(s => IsProductiveCategory(s.Category))
                .Sum(s => s.EndTime.HasValue ? (s.EndTime!.Value - s.StartTime).TotalMinutes : 0);
            
            ProductivityScore = totalMinutes > 0 ? Math.Round(productiveMinutes / totalMinutes * 100, 1) : 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "加载仪表盘数据失败");
        }
        finally
        {
            IsLoading = false;
        }
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
