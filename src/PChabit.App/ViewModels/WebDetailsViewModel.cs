using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml.Media;
using Serilog;
using PChabit.Infrastructure.Data;

namespace PChabit.App.ViewModels;

public partial class WebDetailsViewModel : ViewModelBase
{
    private readonly PChabitDbContext _dbContext;
    
    public WebDetailsViewModel(PChabitDbContext dbContext) : base()
    {
        _dbContext = dbContext;
        Title = "网页访问详情";
        _startDateOffset = new DateTimeOffset(DateTime.Today);
        _endDateOffset = new DateTimeOffset(DateTime.Today);
        _startDate = DateTime.Today;
        _endDate = DateTime.Today;
    }
    
    private DateTimeOffset _startDateOffset;
    public DateTimeOffset StartDateOffset
    {
        get => _startDateOffset;
        set
        {
            if (SetProperty(ref _startDateOffset, value))
            {
                _startDate = value.DateTime;
                OnPropertyChanged(nameof(StartDate));
            }
        }
    }
    
    private DateTimeOffset _endDateOffset;
    public DateTimeOffset EndDateOffset
    {
        get => _endDateOffset;
        set
        {
            if (SetProperty(ref _endDateOffset, value))
            {
                _endDate = value.DateTime;
                OnPropertyChanged(nameof(EndDate));
            }
        }
    }
    
    private DateTime _startDate;
    public DateTime StartDate => _startDate;
    
    private DateTime _endDate;
    public DateTime EndDate => _endDate;
    
    [ObservableProperty]
    private string _searchText = string.Empty;
    
    [ObservableProperty]
    private string _totalVisits = "0";
    
    [ObservableProperty]
    private string _totalDuration = "0分钟";
    
    [ObservableProperty]
    private string _uniqueDomains = "0";
    
    [ObservableProperty]
    private string _avgDuration = "0秒";
    
    [ObservableProperty]
    private string _peakHour = "-";
    
    [ObservableProperty]
    private string _topDomain = "-";
    
    public ObservableCollection<DomainStatItem> DomainStats { get; } = new();
    public ObservableCollection<WebHourlyActivityItem> HourlyActivity { get; } = new();
    public ObservableCollection<DailyTrendItem> DailyTrend { get; } = new();
    public ObservableCollection<BrowsingPatternItem> BrowsingPatterns { get; } = new();
    public ObservableCollection<WebSessionDetailItem> RecentVisits { get; } = new();
    
    public List<string> CategoryOptions { get; } = new() { "全部分类", "搜索", "开发", "视频", "社交", "购物", "邮件", "办公", "新闻", "浏览" };
    
    [ObservableProperty]
    private string _selectedCategory = "全部分类";
    
    public async Task LoadDataAsync()
    {
        Log.Information("WebDetails: 开始加载数据");
        IsLoading = true;
        
        try
        {
            Log.Information("WebDetails: 开始查询数据库, StartDate={StartDate}, EndDate={EndDate}", StartDate, EndDate);
            
            var sessions = await LoadSessionsAsync();
            Log.Information("WebDetails: 查询完成, 共 {Count} 条记录", sessions.Count);
            
            Log.Information("WebDetails: 开始处理数据");
            UpdateSummaryStats(sessions);
            LoadDomainStats(sessions);
            LoadHourlyActivity(sessions);
            LoadDailyTrend(sessions);
            LoadBrowsingPatterns(sessions);
            LoadRecentVisits(sessions);
            Log.Information("WebDetails: 数据处理完成");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "加载网页访问详情失败");
        }
        finally
        {
            IsLoading = false;
            Log.Information("WebDetails: 加载完成");
        }
    }
    
    private async Task<List<Core.Entities.WebSession>> LoadSessionsAsync()
    {
        try
        {
            Log.Debug("WebDetails: 清除 ChangeTracker");
            try { _dbContext.ChangeTracker.Clear(); } catch { }
            
            Log.Debug("WebDetails: 构建查询");
            var query = _dbContext.WebSessions
                .AsNoTracking()
                .Where(s => s.StartTime >= StartDate && s.StartTime <= EndDate.AddDays(1));
            
            if (!string.IsNullOrEmpty(SearchText))
            {
                var searchLower = SearchText.ToLower();
                query = query.Where(s => 
                    s.Domain.ToLower().Contains(searchLower) || 
                    s.Title.ToLower().Contains(searchLower) ||
                    s.Url.ToLower().Contains(searchLower));
            }
            
            if (SelectedCategory != "全部分类")
            {
                query = query.Where(s => s.Domain.Contains(SelectedCategory) || s.Url.Contains(SelectedCategory));
            }
            
            Log.Debug("WebDetails: 执行查询");
            var result = await query.OrderByDescending(s => s.StartTime).ToListAsync();
            Log.Debug("WebDetails: 查询返回 {Count} 条记录", result.Count);
            return result;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "加载 WebSessions 失败");
            return new List<Core.Entities.WebSession>();
        }
    }
    
    private void UpdateSummaryStats(List<Core.Entities.WebSession> sessions)
    {
        var totalVisits = sessions.Count;
        var totalMinutes = sessions.Sum(s => s.Duration.TotalMinutes);
        var uniqueDomains = sessions.Select(s => s.Domain).Distinct().Count();
        var avgSeconds = totalVisits > 0 ? sessions.Average(s => s.Duration.TotalSeconds) : 0;
        
        TotalVisits = totalVisits.ToString("N0");
        TotalDuration = $"{(int)totalMinutes}分钟";
        UniqueDomains = uniqueDomains.ToString("N0");
        AvgDuration = $"{(int)avgSeconds}秒";
        
        var hourlyGroups = sessions
            .GroupBy(s => s.StartTime.Hour)
            .Select(g => new { Hour = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .FirstOrDefault();
        
        PeakHour = hourlyGroups != null ? $"{hourlyGroups.Hour}:00" : "-";
        
        var topDomain = sessions
            .GroupBy(s => s.Domain)
            .Select(g => new { Domain = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .FirstOrDefault();
        
        TopDomain = topDomain?.Domain ?? "-";
    }
    
    private void LoadDomainStats(List<Core.Entities.WebSession> sessions)
    {
        DomainStats.Clear();
        
        var domainGroups = sessions
            .GroupBy(s => s.Domain)
            .Select(g => new DomainStatItem
            {
                Domain = g.Key,
                VisitCount = g.Count(),
                TotalDuration = g.Sum(s => s.Duration.TotalMinutes),
                AvgDuration = g.Average(s => s.Duration.TotalSeconds),
                Category = GetCategory(g.Key),
                LastVisit = g.Max(s => s.StartTime)
            })
            .OrderByDescending(x => x.TotalDuration)
            .Take(20)
            .ToList();
        
        foreach (var item in domainGroups)
        {
            DomainStats.Add(item);
        }
    }
    
    private void LoadHourlyActivity(List<Core.Entities.WebSession> sessions)
    {
        HourlyActivity.Clear();
        
        for (int hour = 0; hour < 24; hour++)
        {
            var hourSessions = sessions.Where(s => s.StartTime.Hour == hour).ToList();
            var visitCount = hourSessions.Count;
            var duration = hourSessions.Sum(s => s.Duration.TotalMinutes);
            
            HourlyActivity.Add(new WebHourlyActivityItem
            {
                Hour = hour,
                HourLabel = $"{hour}:00",
                VisitCount = visitCount,
                Duration = (int)duration,
                BarHeight = Math.Max(10, Math.Min(100, visitCount))
            });
        }
    }
    
    private void LoadDailyTrend(List<Core.Entities.WebSession> sessions)
    {
        DailyTrend.Clear();
        
        var dailyGroups = sessions
            .GroupBy(s => s.StartTime.Date)
            .Select(g => new DailyTrendItem
            {
                Date = g.Key,
                DateLabel = g.Key.ToString("MM/dd"),
                VisitCount = g.Count(),
                TotalDuration = g.Sum(s => s.Duration.TotalMinutes),
                UniqueDomains = g.Select(s => s.Domain).Distinct().Count()
            })
            .OrderBy(x => x.Date)
            .ToList();
        
        foreach (var item in dailyGroups)
        {
            DailyTrend.Add(item);
        }
    }
    
    private void LoadBrowsingPatterns(List<Core.Entities.WebSession> sessions)
    {
        BrowsingPatterns.Clear();
        
        var peakHours = sessions
            .GroupBy(s => s.StartTime.Hour)
            .Select(g => new { Hour = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(3)
            .ToList();
        
        if (peakHours.Any())
        {
            BrowsingPatterns.Add(new BrowsingPatternItem
            {
                Pattern = "活跃时段",
                Description = string.Join(", ", peakHours.Select(h => $"{h.Hour}:00")),
                Icon = "&#xE823;",
                Color = "#0078D4"
            });
        }
        
        var topDomains = sessions
            .GroupBy(s => s.Domain)
            .Select(g => new { Domain = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(3)
            .ToList();
        
        if (topDomains.Any())
        {
            BrowsingPatterns.Add(new BrowsingPatternItem
            {
                Pattern = "高频访问",
                Description = string.Join(", ", topDomains.Select(d => d.Domain)),
                Icon = "&#xE774;",
                Color = "#107C10"
            });
        }
        
        var shortVisits = sessions.Count(s => s.Duration.TotalSeconds < 30);
        var longVisits = sessions.Count(s => s.Duration.TotalMinutes >= 5);
        
        BrowsingPatterns.Add(new BrowsingPatternItem
        {
            Pattern = "访问时长分布",
            Description = $"快速浏览 {shortVisits}次 / 深度访问 {longVisits}次",
            Icon = "&#xE9D9;",
            Color = "#FF8C00"
        });
        
        var categoryGroups = sessions
            .GroupBy(s => GetCategory(s.Domain))
            .Select(g => new { Category = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .FirstOrDefault();
        
        if (categoryGroups != null)
        {
            BrowsingPatterns.Add(new BrowsingPatternItem
            {
                Pattern = "主要活动类型",
                Description = categoryGroups.Category,
                Icon = "&#xE8FD;",
                Color = "#8764B8"
            });
        }
    }
    
    private void LoadRecentVisits(List<Core.Entities.WebSession> sessions)
    {
        RecentVisits.Clear();
        
        foreach (var session in sessions.Take(50))
        {
            RecentVisits.Add(new WebSessionDetailItem
            {
                Domain = session.Domain,
                Title = session.Title,
                Url = session.Url,
                VisitTime = session.StartTime.ToString("HH:mm:ss"),
                Duration = session.Duration.TotalSeconds > 0 
                    ? $"{(int)session.Duration.TotalMinutes}分{(int)session.Duration.Seconds}秒"
                    : "-",
                Category = GetCategory(session.Domain),
                CategoryColor = GetCategoryColor(GetCategory(session.Domain)),
                ScrollDepth = session.ScrollDepth,
                ClickCount = session.ClickCount,
                HasInteraction = session.HasFormInteraction || session.ClickCount > 0
            });
        }
    }
    
    private static string GetCategory(string domain)
    {
        if (string.IsNullOrEmpty(domain)) return "浏览";
        
        var lowerDomain = domain.ToLower();
        
        if (lowerDomain.Contains("google") || lowerDomain.Contains("baidu") || 
            lowerDomain.Contains("bing") || lowerDomain.Contains("sogou") ||
            lowerDomain.Contains("duckduckgo"))
            return "搜索";
        
        if (lowerDomain.Contains("github") || lowerDomain.Contains("gitlab") ||
            lowerDomain.Contains("stackoverflow") || lowerDomain.Contains("csdn") ||
            lowerDomain.Contains("juejin") || lowerDomain.Contains("segmentfault") ||
            lowerDomain.Contains("npmjs") || lowerDomain.Contains("nuget"))
            return "开发";
        
        if (lowerDomain.Contains("youtube") || lowerDomain.Contains("bilibili") ||
            lowerDomain.Contains("netflix") || lowerDomain.Contains("youku") ||
            lowerDomain.Contains("iqiyi") || lowerDomain.Contains("douyin"))
            return "视频";
        
        if (lowerDomain.Contains("twitter") || lowerDomain.Contains("weibo") ||
            lowerDomain.Contains("facebook") || lowerDomain.Contains("instagram") ||
            lowerDomain.Contains("linkedin") || lowerDomain.Contains("zhihu") ||
            lowerDomain.Contains("xiaohongshu") || lowerDomain.Contains("douban"))
            return "社交";
        
        if (lowerDomain.Contains("amazon") || lowerDomain.Contains("taobao") ||
            lowerDomain.Contains("jd") || lowerDomain.Contains("tmall") ||
            lowerDomain.Contains("pinduoduo"))
            return "购物";
        
        if (lowerDomain.Contains("mail") || lowerDomain.Contains("outlook") ||
            lowerDomain.Contains("gmail") || lowerDomain.Contains("qq") && lowerDomain.Contains("mail"))
            return "邮件";
        
        if (lowerDomain.Contains("notion") || lowerDomain.Contains("docs.qq") ||
            lowerDomain.Contains("yuque") || lowerDomain.Contains("confluence") ||
            lowerDomain.Contains("feishu") || lowerDomain.Contains("dingtalk"))
            return "办公";
        
        if (lowerDomain.Contains("news") || lowerDomain.Contains("bbc") ||
            lowerDomain.Contains("cnn") || lowerDomain.Contains("sina") ||
            lowerDomain.Contains("sohu") || lowerDomain.Contains("163"))
            return "新闻";
        
        return "浏览";
    }
    
    private static SolidColorBrush GetCategoryColor(string category)
    {
        var hexColor = category switch
        {
            "搜索" => "#0078D4",
            "开发" => "#512BD4",
            "视频" => "#FF8C00",
            "社交" => "#107C10",
            "购物" => "#E81123",
            "邮件" => "#00B7C3",
            "办公" => "#6B7280",
            "新闻" => "#8764B8",
            _ => "#9CA3AF"
        };
        return CreateBrush(hexColor);
    }
    
    private static SolidColorBrush CreateBrush(string hexColor)
    {
        if (hexColor.StartsWith("#") && hexColor.Length == 7)
        {
            var r = Convert.ToByte(hexColor.Substring(1, 2), 16);
            var g = Convert.ToByte(hexColor.Substring(3, 2), 16);
            var b = Convert.ToByte(hexColor.Substring(5, 2), 16);
            return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, r, g, b));
        }
        return new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }
    
    [RelayCommand]
    private async Task SearchAsync()
    {
        await LoadDataAsync();
    }
    
    [RelayCommand]
    private async Task RefreshAsync()
    {
        SearchText = string.Empty;
        SelectedCategory = "全部分类";
        await LoadDataAsync();
    }
    
    public void SetStartDate(DateTime date)
    {
        _startDate = date;
        _startDateOffset = new DateTimeOffset(date);
        OnPropertyChanged(nameof(StartDate));
        OnPropertyChanged(nameof(StartDateOffset));
    }
    
    public void SetEndDate(DateTime date)
    {
        _endDate = date;
        _endDateOffset = new DateTimeOffset(date);
        OnPropertyChanged(nameof(EndDate));
        OnPropertyChanged(nameof(EndDateOffset));
    }
    
    public void SetDateRange(string range)
    {
        var endDate = DateTime.Today;
        var startDate = range switch
        {
            "today" => DateTime.Today,
            "week" => DateTime.Today.AddDays(-7),
            "month" => DateTime.Today.AddMonths(-1),
            _ => DateTime.Today.AddDays(-7)
        };
        
        _startDateOffset = new DateTimeOffset(startDate);
        _endDateOffset = new DateTimeOffset(endDate);
        _startDate = startDate;
        _endDate = endDate;
        
        OnPropertyChanged(nameof(StartDateOffset));
        OnPropertyChanged(nameof(EndDateOffset));
        OnPropertyChanged(nameof(StartDate));
        OnPropertyChanged(nameof(EndDate));
    }
    
    [RelayCommand]
    private async Task SetDateRangeAsync(string range)
    {
        var endDate = DateTime.Today;
        var startDate = range switch
        {
            "today" => DateTime.Today,
            "week" => DateTime.Today.AddDays(-7),
            "month" => DateTime.Today.AddMonths(-1),
            _ => DateTime.Today.AddDays(-7)
        };
        
        _startDateOffset = new DateTimeOffset(startDate);
        _endDateOffset = new DateTimeOffset(endDate);
        _startDate = startDate;
        _endDate = endDate;
        
        OnPropertyChanged(nameof(StartDateOffset));
        OnPropertyChanged(nameof(EndDateOffset));
        OnPropertyChanged(nameof(StartDate));
        OnPropertyChanged(nameof(EndDate));
        
        await LoadDataAsync();
    }
    
    partial void OnSelectedCategoryChanged(string value)
    {
        _ = LoadDataAsync();
    }
}

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
    public SolidColorBrush CategoryColor { get; init; } = new SolidColorBrush();
    public int ScrollDepth { get; init; }
    public int ClickCount { get; init; }
    public bool HasInteraction { get; init; }
}
