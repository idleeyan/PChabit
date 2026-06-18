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
    private readonly IDbContextFactory<PChabitDbContext> _dbFactory;
    private bool _isCategoriesInitialized;

    public WebDetailsViewModel(IDbContextFactory<PChabitDbContext> dbFactory) : base()
    {
        _dbFactory = dbFactory;
        Title = "网页访问详情";
        _startDateOffset = new DateTimeOffset(DateTime.Today);
        _endDateOffset = new DateTimeOffset(DateTime.Today);
        _startDate = DateTime.Today;
        _endDate = DateTime.Today;
    }

    private async Task EnsureCategoriesInitializedAsync()
    {
        if (_isCategoriesInitialized) return;
        _isCategoriesInitialized = true;
    }

    private async Task LoadCategoryOptionsAsync()
    {
        await using var dbContext = await _dbFactory.CreateDbContextAsync();
        var categoryNames = await dbContext.WebsiteCategories
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .Select(c => c.Name)
            .ToListAsync();

        var options = new List<string> { "全部分类" };
        options.AddRange(categoryNames);

        await RunOnUIThreadAsync(() =>
        {
            CategoryOptions.Clear();
            CategoryOptions.AddRange(options);
            return Task.CompletedTask;
        });
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
    public ObservableCollection<WebSessionDetailItem> AllVisits { get; } = new();
    public ObservableCollection<WebSessionDetailItem> RecentVisits { get; } = new();
    
    private const int PageSize = 50;
    private int _currentPage = 0;
    private bool _hasMoreData = false;
    private List<Core.Entities.WebSession> _allSessions = new();
    private Dictionary<string, string> _domainCategoryMap = new();
    
    [ObservableProperty]
    private bool _hasMoreVisits = false;
    
    [ObservableProperty]
    private bool _isLoadingMore = false;
    
    public List<string> CategoryOptions { get; private set; } = new() { "全部分类" };
    
    [ObservableProperty]
    private string _selectedCategory = "全部分类";
    
    // === Phase 1 中间数据类（纯 POCO） ===

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

    // === New LoadDataAsync 入口（调用基类 DbSafeViewModel.LoadDataAsync） ===

    public async Task LoadDataAsync()
    {
        Log.Information("WebDetails: 开始加载数据");
        _currentPage = 0;
        _hasMoreData = true;
        _allSessions.Clear();
        
        try
        {
            await EnsureCategoriesInitializedAsync();
            await LoadCategoryOptionsAsync();
            await base.LoadDataAsync();
            Log.Information("WebDetails: 数据处理完成");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "加载网页访问详情失败");
        }
    }

    // === DbSafeViewModel 抽象方法实现 ===

    protected override async Task<WebStatsData> LoadStatsOnBackgroundAsync()
    {
        Log.Information("WebDetails: 开始查询数据库, StartDate={StartDate}, EndDate={EndDate}", StartDate, EndDate);
        var sessions = await LoadSessionsAsync();
        _domainCategoryMap = await LoadDomainCategoryMapAsync();

        Log.Information("WebDetails: 查询完成, 共 {Count} 条记录", sessions.Count);
        var summary = ComputeSummaryStats(sessions);
        var domains = ComputeDomainStats(sessions);
        var hourly = ComputeHourlyActivity(sessions);
        var daily = ComputeDailyTrend(sessions);
        var patterns = ComputeBrowsingPatterns(sessions);
        var recent = ComputeRecentVisits(sessions);
        var more = sessions.Count > PageSize;

        return new WebStatsData
        {
            Summary = summary,
            DomainStats = domains,
            HourlyActivity = hourly,
            DailyTrend = daily,
            BrowsingPatterns = patterns,
            RecentVisits = recent,
            HasMore = more,
            AllSessions = sessions
        };
    }

    protected override async Task ApplyStatsOnUIAsync(WebStatsData s)
    {
        TotalVisits = s.Summary.TotalVisits;
        TotalDuration = s.Summary.TotalDuration;
        UniqueDomains = s.Summary.UniqueDomains;
        AvgDuration = s.Summary.AvgDuration;
        PeakHour = s.Summary.PeakHour;
        TopDomain = s.Summary.TopDomain;

        _allSessions = s.AllSessions;

        DomainStats.Clear();
        foreach (var item in s.DomainStats) DomainStats.Add(item);

        HourlyActivity.Clear();
        foreach (var item in s.HourlyActivity) HourlyActivity.Add(item);

        DailyTrend.Clear();
        foreach (var item in s.DailyTrend) DailyTrend.Add(item);

        BrowsingPatterns.Clear();
        foreach (var item in s.BrowsingPatterns) BrowsingPatterns.Add(item);

        RecentVisits.Clear();
        AllVisits.Clear();
        foreach (var item in s.RecentVisits)
        {
            RecentVisits.Add(item);
            AllVisits.Add(item);
        }

        HasMoreVisits = s.HasMore;
    }
    
    private async Task<List<Core.Entities.WebSession>> LoadSessionsAsync()
    {
        try
        {
            await using var dbContext = await _dbFactory.CreateDbContextAsync();

            Log.Debug("WebDetails: 构建查询");
            var query = dbContext.WebSessions
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
                // 通过 WebsiteDomainMappings 查询该分类的域名模式
                var domainPatterns = await dbContext.WebsiteDomainMappings
                    .AsNoTracking()
                    .Include(m => m.Category)
                    .Where(m => m.Category != null && m.Category.Name == SelectedCategory)
                    .Select(m => m.DomainPattern)
                    .Distinct()
                    .ToListAsync();

                if (domainPatterns.Any())
                {
                    var lowerPatterns = domainPatterns.Select(p => p.ToLower()).ToList();
                    query = query.Where(s => lowerPatterns.Any(p => s.Domain.ToLower().Contains(p)));
                }
                else
                {
                    // 没有匹配的域名模式则返回空，避免错误的中文关键词硬匹配
                    return new List<Core.Entities.WebSession>();
                }
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
    
    private static bool DomainMatches(string domain, string pattern)
    {
        var lowerPattern = pattern.ToLower();
        if (lowerPattern.StartsWith("*."))
        {
            var suffix = lowerPattern[2..];
            return domain.EndsWith("." + suffix) || domain == suffix;
        }
        return domain == lowerPattern || domain.EndsWith("." + lowerPattern);
    }

    private async Task<Dictionary<string, string>> LoadDomainCategoryMapAsync()
    {
        await using var dbContext = await _dbFactory.CreateDbContextAsync();
        var mappings = await dbContext.WebsiteDomainMappings
            .AsNoTracking()
            .Include(m => m.Category)
            .Where(m => m.Category != null && m.Category.IsActive)
            .ToListAsync();

        var map = new Dictionary<string, string>();
        foreach (var m in mappings.Where(m => m.Category != null))
        {
            map[m.DomainPattern] = m.Category!.Name;
        }
        return map;
    }

    private SummaryStatsResult ComputeSummaryStats(List<Core.Entities.WebSession> sessions)
    {
        var totalVisits = sessions.Count;
        var totalMinutes = sessions.Sum(s => s.Duration.TotalMinutes);
        var uniqueDomains = sessions.Select(s => s.Domain).Distinct().Count();
        var avgSeconds = totalVisits > 0 ? sessions.Average(s => s.Duration.TotalSeconds) : 0;
        
        var hourlyGroups = sessions
            .GroupBy(s => s.StartTime.Hour)
            .Select(g => new { Hour = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .FirstOrDefault();
        
        var topDomain = sessions
            .GroupBy(s => s.Domain)
            .Select(g => new { Domain = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .FirstOrDefault();
        
        return new SummaryStatsResult(
            totalVisits.ToString("N0"),
            $"{(int)totalMinutes}分钟",
            uniqueDomains.ToString("N0"),
            $"{(int)avgSeconds}秒",
            hourlyGroups != null ? $"{hourlyGroups.Hour}:00" : "-",
            topDomain?.Domain ?? "-"
        );
    }
    
    private List<DomainStatItem> ComputeDomainStats(List<Core.Entities.WebSession> sessions)
    {
        return sessions
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
    }
    
    private List<WebHourlyActivityItem> ComputeHourlyActivity(List<Core.Entities.WebSession> sessions)
    {
        var result = new List<WebHourlyActivityItem>();
        for (int hour = 0; hour < 24; hour++)
        {
            var hourSessions = sessions.Where(s => s.StartTime.Hour == hour).ToList();
            var visitCount = hourSessions.Count;
            var duration = hourSessions.Sum(s => s.Duration.TotalMinutes);
            
            result.Add(new WebHourlyActivityItem
            {
                Hour = hour,
                HourLabel = $"{hour}:00",
                VisitCount = visitCount,
                Duration = (int)duration,
                BarHeight = Math.Max(10, Math.Min(100, visitCount))
            });
        }
        return result;
    }
    
    private List<DailyTrendItem> ComputeDailyTrend(List<Core.Entities.WebSession> sessions)
    {
        return sessions
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
    }
    
    private List<BrowsingPatternItem> ComputeBrowsingPatterns(List<Core.Entities.WebSession> sessions)
    {
        var result = new List<BrowsingPatternItem>();
        
        var peakHours = sessions
            .GroupBy(s => s.StartTime.Hour)
            .Select(g => new { Hour = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(3)
            .ToList();
        
        if (peakHours.Any())
        {
            result.Add(new BrowsingPatternItem
            {
                Pattern = "活跃时段",
                Description = string.Join(", ", peakHours.Select(h => $"{h.Hour}:00")),
                Icon = "\uE823",
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
            result.Add(new BrowsingPatternItem
            {
                Pattern = "高频访问",
                Description = string.Join(", ", topDomains.Select(d => d.Domain)),
                Icon = "\uE774",
                Color = "#107C10"
            });
        }
        
        var shortVisits = sessions.Count(s => s.Duration.TotalSeconds < 30);
        var longVisits = sessions.Count(s => s.Duration.TotalMinutes >= 5);
        
        result.Add(new BrowsingPatternItem
        {
            Pattern = "访问时长分布",
            Description = $"快速浏览 {shortVisits}次 / 深度访问 {longVisits}次",
            Icon = "\uE9D9",
            Color = "#FF8C00"
        });
        
        var categoryGroups = sessions
            .GroupBy(s => GetCategory(s.Domain))
            .Select(g => new { Category = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .FirstOrDefault();
        
        if (categoryGroups != null)
        {
            result.Add(new BrowsingPatternItem
            {
                Pattern = "主要活动类型",
                Description = categoryGroups.Category,
                Icon = "\uE8FD",
                Color = "#8764B8"
            });
        }
        
        return result;
    }
    
    private List<WebSessionDetailItem> ComputeRecentVisits(List<Core.Entities.WebSession> sessions)
    {
        return sessions
            .Skip(_currentPage * PageSize)
            .Take(PageSize)
            .Select(session => new WebSessionDetailItem
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
            })
            .ToList();
    }
    
    [RelayCommand]
    private async Task LoadMoreVisitsAsync()
    {
        if (!_hasMoreData || IsLoadingMore) return;

        IsLoadingMore = true;

        try
        {
            _currentPage++;
            var newItems = new List<WebSessionDetailItem>();

            await Task.Run(() =>
            {
                var pagedSessions = _allSessions.Skip(_currentPage * PageSize).Take(PageSize).ToList();
                foreach (var session in pagedSessions)
                {
                    var item = new WebSessionDetailItem
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
                    };
                    newItems.Add(item);
                }

                _hasMoreData = pagedSessions.Count == PageSize;
            });

            foreach (var item in newItems)
            {
                RecentVisits.Add(item);
                AllVisits.Add(item);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "加载更多访问记录失败");
            _currentPage--;
        }
        finally
        {
            IsLoadingMore = false;
            HasMoreVisits = _hasMoreData;
        }
    }
    
    private string GetCategory(string domain)
    {
        if (string.IsNullOrEmpty(domain)) return "浏览";
        
        var lowerDomain = domain.ToLower();
        foreach (var kvp in _domainCategoryMap)
        {
            if (DomainMatches(lowerDomain, kvp.Key))
                return kvp.Value;
        }
        return GetCategoryFallback(domain);
    }

    private static string GetCategoryFallback(string domain)
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
            lowerDomain.Contains("gmail") || (lowerDomain.Contains("qq") && lowerDomain.Contains("mail")))
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
    
    private string GetCategoryColor(string category)
    {
        return GetCategoryColorHex(category);
    }

    private static string GetCategoryColorHex(string category)
    {
        return category switch
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
    public string CategoryColor { get; init; } = "#9CA3AF";
    public int ScrollDepth { get; init; }
    public int ClickCount { get; init; }
    public bool HasInteraction { get; init; }
}
