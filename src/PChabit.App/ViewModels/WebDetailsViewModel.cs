using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Serilog;
using PChabit.Core.Entities;
using PChabit.Infrastructure.Analysis;
using PChabit.Infrastructure.Data;
using PChabit.Infrastructure.Services;


namespace PChabit.App.ViewModels;

public partial class WebDetailsViewModel : DbSafeViewModel<WebDetailsViewModel.WebStatsData>
{
    private readonly IDbContextFactory<PChabitDbContext> _dbFactory;
    private readonly IWebsiteCategoryService _websiteCategoryService;
    private bool _isCategoriesInitialized;

    public WebDetailsViewModel(
        IDbContextFactory<PChabitDbContext> dbFactory,
        IWebsiteCategoryService websiteCategoryService) : base()
    {
        _dbFactory = dbFactory;
        _websiteCategoryService = websiteCategoryService;
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
    
    public ObservableCollection<DailyTrendItem> DailyTrend { get; } = new();
    public ObservableCollection<BrowsingPatternItem> BrowsingPatterns { get; } = new();
    public ObservableCollection<WebSessionDetailItem> AllVisits { get; } = new();
    public ObservableCollection<WebSessionDetailItem> RecentVisits { get; } = new();

    /// <summary>
    /// 3.37.0：合并后的域名表（原「网站访问排行」+「网页流量归因」）。
    /// </summary>
    public ObservableCollection<DomainActivityRow> DomainActivity { get; } = new();

    /// <summary>
    /// 3.37.0：应用 + 网页的小时活动合成行，按所选时间段统计。
    /// </summary>
    public ObservableCollection<HourlyActivityCombinedRow> HourlyCombined { get; } = new();

    /// <summary>
    /// 「每小时活动分布」图表数据（3.35.0 投影到 <see cref="HourlyBarRow"/>）。
    /// 用可观察集合：控件自行订阅集合变化。
    /// 原实现进度条上限硬编码 50，此处由控件按数据自动取值。
    /// </summary>


    /// <summary>参与流量归因的浏览器进程（进程名归一化后）。豆包浏览器进程名待确认后可按需追加。</summary>
    private static readonly HashSet<string> BrowserProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox"
    };
    
    private const int PageSize = 50;
    private int _currentPage = 0;
    private bool _hasMoreData = false;
    private List<Core.Entities.WebSession> _allSessions = new();
    private Dictionary<string, string> _domainCategoryMap = new();
    private Dictionary<string, string> _categoryColorMap = new(StringComparer.OrdinalIgnoreCase);
    
    [ObservableProperty]
    private bool _hasMoreVisits = false;
    
    [ObservableProperty]
    private bool _isLoadingMore = false;
    
    public List<string> CategoryOptions { get; private set; } = new() { "全部分类" };
    
    [ObservableProperty]
    private string _selectedCategory = "全部分类";

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

    protected override async Task<WebStatsData> LoadStatsOnBackgroundAsync()
    {
        Log.Information("WebDetails: 开始查询数据库, StartDate={StartDate}, EndDate={EndDate}", StartDate, EndDate);
        var sessions = await LoadSessionsAsync();
        LoadCategoryLookups();

        Log.Information("WebDetails: 查询完成, 共 {Count} 条记录", sessions.Count);
        var summary = ComputeSummaryStats(sessions);
        var domains = ComputeDomainStats(sessions);
        var hourly = ComputeHourlyActivity(sessions);
        var daily = ComputeDailyTrend(sessions);
        var patterns = ComputeBrowsingPatterns(sessions);
        var recent = ComputeRecentVisits(sessions);
        var more = sessions.Count > PageSize;
        var browserUsage = await LoadBrowserUsageAsync();
        var attribution = ComputeTrafficAttribution(sessions, browserUsage);

        // 3.37.0：合并两类视图——排行+流量归因合成一张表；应用+网页小时活动合成一组行
        var domainActivity = BuildDomainActivity(domains, attribution);
        var appHourly = await LoadAppHourlyAsync();
        var hourlyCombined = BuildHourlyCombined(hourly, appHourly);

        return new WebStatsData
        {
            Summary = summary,
            DomainStats = domains,
            HourlyActivity = hourly,
            DailyTrend = daily,
            BrowsingPatterns = patterns,
            RecentVisits = recent,
            HasMore = more,
            AllSessions = sessions,
            TrafficAttribution = attribution,
            DomainActivity = domainActivity,
            HourlyCombined = hourlyCombined
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

        DomainActivity.Clear();
        foreach (var item in s.DomainActivity) DomainActivity.Add(item);

        HourlyCombined.Clear();
        foreach (var item in s.HourlyCombined) HourlyCombined.Add(item);

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

}

