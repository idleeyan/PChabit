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

