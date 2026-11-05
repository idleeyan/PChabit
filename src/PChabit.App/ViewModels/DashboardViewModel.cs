using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Serilog;
using PChabit.App.Services;
using PChabit.Infrastructure.Analysis;
using PChabit.Infrastructure.Services;

using PChabit.Application.Queries;
namespace PChabit.App.ViewModels;

public partial class DashboardViewModel : DbSafeViewModel<DashboardSnapshot>
{
    private readonly DashboardQueryService _queryService;
    private readonly IAppIconService _iconService;
    private readonly Infrastructure.Services.IAnalyticsAiService _aiService;

    public DashboardViewModel(
        DashboardQueryService queryService,
        IAppIconService iconService,
        Infrastructure.Services.IAnalyticsAiService aiService) : base()
    {
        // D4 步骤 2b+3（3.71.0）：查库/聚合/衍生指标已下沉 DashboardQueryService，
        // VM 不再接触 IDbContextFactory 与 IEfficiencyCalculator（查询链随迁）。
        _queryService = queryService;
        _iconService = iconService;
        _aiService = aiService;
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

    /// <summary>今日效率总分（0-100），来自 EfficiencyCalculator 四维加权，与「分析」页同源。</summary>
    [ObservableProperty]
    private double _efficiencyScore;

    /// <summary>今日专注分钟（DailySummary.FocusMinutesV2，可能为 null）。</summary>
    [ObservableProperty]
    private double? _focusMinutes;

    [ObservableProperty]
    private int? _appSwitches;

    [ObservableProperty]
    private string _firstActiveTime = "";

    [ObservableProperty]
    private string _lastActiveTime = "";

    [ObservableProperty]
    private string _mostUsedApp = "无数据";

    [ObservableProperty]
    private ImageSource? _mostUsedAppIcon;

    [ObservableProperty]
    private string _mostVisitedSite = "无数据";

    // === 阶段 2：状态条与环比（3.25.1）===

    /// <summary>数据截止时刻（HH:mm），让用户知道数字不是"此刻"的。</summary>
    [ObservableProperty]
    private string _dataAsOf = "";

    /// <summary>效率评分等级文本（优秀/良好/一般/偏低）。</summary>
    [ObservableProperty]
    private string _efficiencyLevel = "暂无评分";

    /// <summary>相对个人基线的偏离描述；基线样本不足时给出说明而非百分比。</summary>
    [ObservableProperty]
    private string _deviationText = "";

    // === 阶段 4：AI 速览与画像摘要（3.27.0）===

    /// <summary>今日速览文本。未配置 AI 时为规则摘要（不显示空卡）。</summary>
    [ObservableProperty]
    private string _todaySummary = "";

    /// <summary>速览来源：AI / 规则 / 空。</summary>
    [ObservableProperty]
    private string _summarySource = "";

    [ObservableProperty]
    private bool _isSummaryLoading = false;

    /// <summary>习惯画像摘要（时型 + 典型起止 + 峰值专注小时）。</summary>
    [ObservableProperty]
    private string _profileSummary = "";

    // === 阶段 5：实时化（3.28.0）===

    /// <summary>当前正在使用的应用名；无活动会话时为空。</summary>
    [ObservableProperty]
    private string _currentAppName = "";

    /// <summary>当前应用是否在线（决定"当前活动"条是否显示）。</summary>
    [ObservableProperty]
    private bool _hasCurrentApp = false;

    [ObservableProperty]
    private ImageSource? _currentAppIcon;

    /// <summary>当前活动的开始时刻（HH:mm）。</summary>
    [ObservableProperty]
    private string _currentAppSince = "";

    /// <summary>速览来源标签是否显示（无摘要时隐藏，避免留一个空标签）。</summary>
    public bool HasSummarySource => !string.IsNullOrEmpty(SummarySource);

    /// <summary>画像摘要是否显示。</summary>
    public bool HasProfileSummary => !string.IsNullOrEmpty(ProfileSummary);

    // 环比项（XAML 直接绑，避免在 XAML 里做 double? 转换）
    public CompareItem ActiveCompare { get; } = new();
    public CompareItem KeysCompare { get; } = new();
    public CompareItem ClicksCompare { get; } = new();
    public CompareItem WebCompare { get; } = new();

    // 派生文本属性：x:Bind OneWay 只在源属性 RaisePropertyChanged 时刷新，
    // 而这里是多个字段组合计算，故在 ApplyStatsOnUIAsync 里统一赋值（见 SyncDerivedText）。
    public string FocusMinutesText { get; private set; } = "--";
    public string AppSwitchesText { get; private set; } = "--";
    public string ActiveWindowText { get; private set; } = "--";
    public string DataAsOfText { get; private set; } = "";

    /// <summary>
    /// 图表 JSON（序列化自 <see cref="_chartSnapshot"/>）。
    /// WebView2 通过 ExecuteScriptAsync 注入；不设默认空串，避免在 WebView 未就绪时注入无效脚本。
    /// </summary>
        public string ChartJson { get; private set; } = "";

    private ChartSnapshot _chartSnapshot = new();

    // === 阶段 5：实时刷新 ===
    // 30 秒而非 1 秒：3.24.0 已证明高频采样代价明显（GPU 计数器单次 370ms）。
    // 仪表盘是"看趋势"的地方，30 秒足够，且空闲 CPU 增幅需 < 0.1%。
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);
    private Timer? _refreshTimer;

    /// <summary>启动实时刷新。幂等，可重复调用。</summary>
    public void StartLiveRefresh()
    {
        if (_refreshTimer != null) return;
        _refreshTimer = new Timer(
            _ => _ = OnTickAsync(),
            null, RefreshInterval, RefreshInterval);
        Log.Information("[Dashboard] 实时刷新已启动，间隔 {Sec} 秒", RefreshInterval.TotalSeconds);
    }

    /// <summary>停止实时刷新（页面离开时调用，对齐 3.24.0「进页面才 Start、离开即 Stop」的省电思路）。</summary>
    public void StopLiveRefresh()
    {
        _refreshTimer?.Dispose();
        _refreshTimer = null;
    }

    private async Task OnTickAsync()
    {
        // 页面已离开时不必再跑
        if (_refreshTimer == null) return;

        // 原子标志位防重入：单看 IsLoading 存在竞态窗口
        //（两个 tick 先后通过 IsLoading 检查后都进入 LoadDataAsync，导致并发查询）。
        // 定时器回调在线程池，用 Interlocked 而非 bool。
        if (Interlocked.Exchange(ref _tickBusy, 1) == 1) return;

        try
        {
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            // 定时器里的异常若逃逸会导致进程崩溃，必须吞掉
            Log.Debug(ex, "仪表盘定时刷新失败");
        }
        finally
        {
            Interlocked.Exchange(ref _tickBusy, 0);
        }
    }

    private int _tickBusy;

    /// <summary>仪表盘只展示前 N 条，完整榜单在应用统计 / 网页访问页。</summary>
    public const int TopPreviewCount = 5;

    public ObservableCollection<AppUsageItem> TopApps { get; } = new();
    public ObservableCollection<HourlyActivityItem> HourlyActivity { get; } = new();
    public ObservableCollection<CategoryDistributionItem> CategoryDistribution { get; } = new();
    public ObservableCollection<WebsiteUsageItem> TopWebsites { get; } = new();


    // === DbSafeViewModel 抽象方法实现 ===

    protected override async Task<DashboardSnapshot> LoadStatsOnBackgroundAsync()
    {
        // 查询链已下沉 DashboardQueryService（D4 步骤 2b+3）；
        // 今日实时合并不写库、效率/基线/环比容错降级均由查询服务保证。
        return await _queryService.LoadAsync(DateTime.Today);
    }

    protected override async Task ApplyStatsOnUIAsync(DashboardSnapshot stats)
    {
        TodayActiveTime = stats.TodayActiveTime;
        TodayKeyPresses = stats.TodayKeyPresses;
        TodayMouseClicks = stats.TodayMouseClicks;
        TodayWebPages = stats.TodayWebPages;
        EfficiencyScore = stats.EfficiencyScore;
        FocusMinutes = stats.FocusMinutes;
        AppSwitches = stats.AppSwitches;
        FirstActiveTime = stats.FirstActiveTime;
        LastActiveTime = stats.LastActiveTime;
        MostUsedApp = stats.MostUsedApp;
        MostVisitedSite = stats.MostVisitedSite;

        if (stats.TopAppsRaw.Any())
        {
            _ = LoadMostUsedAppIconAsync(stats.TopAppsRaw.First().ProcessName);

            TopApps.Clear();
            // 仪表盘只留 Top N，完整榜单交给应用统计页（顺带省掉全量图标加载）
            foreach (var (processName, categoryName, duration, categoryColor) in stats.TopAppsRaw.Take(TopPreviewCount))
            {
                var appHours = (int)(duration / 60);
                var appMins = (int)(duration % 60);
                var item = new AppUsageItem
                {
                    Name = processName,
                    ProcessName = processName,
                    Duration = $"{appHours}h {appMins}m",
                    Category = categoryName,
                    CategoryColorHex = categoryColor,
                    // WinRT 画刷只能在 UI 线程创建（陷阱 #8）
                    CategoryBrush = CreateBrush(categoryColor)
                };
                TopApps.Add(item);
                _ = LoadAppIconAsync(item);
            }
        }

        if (stats.TopWebsites.Any())
        {
            TopWebsites.Clear();
            // 同上：只留 Top N，完整榜单在网页访问页
            foreach (var site in stats.TopWebsites.Take(TopPreviewCount))
            {
                TopWebsites.Add(new WebsiteUsageItem
                {
                    Domain = site.Domain,
                    Visits = site.Visits,
                    Duration = DashboardMetricsCalculator.FormatDuration(TimeSpan.FromTicks(site.TotalDurationTicks)),
                    FaviconUrl = $"https://www.google.com/s2/favicons?domain={site.Domain}&sz=32"
                });
            }
        }

        HourlyActivity.Clear();
        foreach (var item in stats.HourlyActivity)
            HourlyActivity.Add(new HourlyActivityItem { Hour = item.Hour, Activity = item.Activity, Category = item.Category });

        var totalCategoryDuration = stats.CategoryRaw.Sum(x => x.Duration);
        CategoryDistribution.Clear();
        for (int i = 0; i < stats.CategoryRaw.Count && i < 5; i++)
        {
            var pctValue = totalCategoryDuration > 0
                ? stats.CategoryRaw[i].Duration / totalCategoryDuration * 100
                : 0;
            // 配色取自 ProgramCategories.Color，与应用统计页同一份数据
            CategoryDistribution.Add(new CategoryDistributionItem
            {
                Name = stats.CategoryRaw[i].Name,
                Percentage = $"{(int)pctValue}%",
                PercentageValue = pctValue,
                Color = CreateBrush(stats.CategoryRaw[i].ColorHex)
            });
        }

        // === 阶段 2：状态条与环比格式化 ===

        DataAsOf = DateTime.Now.ToString("HH:mm");

        EfficiencyLevel = stats.EfficiencyScore switch
        {
            >= 80 => "状态优秀",
            >= 60 => "状态良好",
            >= 40 => "状态一般",
            > 0 => "状态偏低",
            _ => "暂无评分"
        };

        DeviationText = stats.ActiveDeviationPct is { } dev
            ? dev switch
            {
                >= 15 => $"高于平时 {dev:F0}%",
                <= -15 => $"低于平时 {Math.Abs(dev):F0}%",
                _ => "与平时相当"
            }
            : "基线样本积累中";

        ApplyCompare(ActiveCompare, stats.ActiveComparePct);
        ApplyCompare(KeysCompare, stats.KeysComparePct);
        ApplyCompare(ClicksCompare, stats.ClicksComparePct);
        ApplyCompare(WebCompare, stats.WebComparePct);

        SyncDerivedText();

        // 图表数据在 UI 线程序列化（WebView2 注入用），数据源来自后台线程算好的纯 POCO
        _chartSnapshot = stats.Chart;
        ChartJson = SerializeChart(stats.Chart);
        // 普通属性不会自动抬升 PropertyChanged，页面靠这个事件把新数据推给图表
        OnPropertyChanged(nameof(ChartJson));

        // 阶段 4：规则摘要同步生成（AI 不可用时的降级路径，必须先有）
        BuildRuleSummary(stats);
        BuildProfileSummary();
        RaiseDerivedFlags();

        // 阶段 5：当前活动
        var newCurrent = stats.CurrentAppName;
        if (!string.IsNullOrEmpty(newCurrent))
        {
            CurrentAppName = newCurrent;
            CurrentAppSince = stats.CurrentAppSince;
            HasCurrentApp = true;
            // 图标加载是异步且较重，仅在应用切换时重取
            if (newCurrent != _lastIconApp)
            {
                _lastIconApp = newCurrent;
                _ = LoadCurrentAppIconAsync(newCurrent);
            }
        }
        else
        {
            CurrentAppName = "";
            CurrentAppSince = "";
            HasCurrentApp = false;
        }
    }

    private string? _lastIconApp;

    private async Task LoadCurrentAppIconAsync(string processName)
    {
        try
        {
            var icon = await _iconService.GetAppIconAsync(processName, 20);
            if (icon != null && processName == CurrentAppName)
            {
                CurrentAppIcon = icon;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "加载当前应用图标失败: {ProcessName}", processName);
        }
    }

    /// <summary>
    /// 通知派生的可见性标志刷新。
    /// x:Bind OneWay 只监听被观测属性自身的 PropertyChanged，
    /// 这些标志由 SummarySource/ProfileSummary 派生而来，需手动抬升。
    /// </summary>
    private void RaiseDerivedFlags()
    {
        OnPropertyChanged(nameof(HasSummarySource));
        OnPropertyChanged(nameof(HasProfileSummary));
    }

    /// <summary>
    /// 规则摘要：不依赖任何外部服务，纯本地计算。
    /// 这是 AI 未配置时的降级路径——宁可给普通文本，也不给空卡片。
    /// </summary>
    private void BuildRuleSummary(DashboardSnapshot stats)
    {
        if (stats.TopAppsRaw.Count == 0)
        {
            TodaySummary = "今天还没有活动记录。";
            SummarySource = "";
            return;
        }

        var parts = new List<string>();
        var top = stats.TopAppsRaw[0];
        parts.Add($"最常使用 {top.ProcessName}");

        if (stats.EfficiencyScore > 0)
        {
            parts.Add($"效率评分 {stats.EfficiencyScore:F0} 分");
        }
        if (stats.FocusMinutes is > 0)
        {
            parts.Add($"专注 {stats.FocusMinutes:F0} 分钟");
        }
        if (stats.ActiveDeviationPct is { } dev && Math.Abs(dev) >= 15)
        {
            parts.Add(dev > 0 ? $"活跃时长高于平时 {dev:F0}%" : $"活跃时长低于平时 {Math.Abs(dev):F0}%");
        }

        TodaySummary = string.Join(" · ", parts) + "。";
        SummarySource = "规则";
    }

    /// <summary>习惯画像摘要：时型 + 典型起止 + 峰值专注小时。</summary>
    private void BuildProfileSummary()
    {
        if (string.IsNullOrEmpty(FirstActiveTime) || string.IsNullOrEmpty(LastActiveTime))
        {
            ProfileSummary = "";
            return;
        }

        var chronotype = (int.TryParse(FirstActiveTime.Split(':')[0], out var h) && h >= 23)
            || int.TryParse(LastActiveTime.Split(':')[0], out var h2) && h2 <= 6
            ? "夜型"
            : int.TryParse(FirstActiveTime.Split(':')[0], out var h3) && h3 < 9 ? "早型" : "均衡型";

        ProfileSummary = $"{chronotype} · 今日 {FirstActiveTime} – {LastActiveTime}";
    }

    /// <summary>
    /// 请求 AI 生成今日速览。失败或未配置时静默保留规则摘要——
    /// 速览是锦上添花，不能因为 AI 不可用就让用户看到错误。
    /// </summary>
    public async Task RequestAiSummaryAsync()
    {
        if (!_aiService.IsConfigured || IsSummaryLoading) return;

        IsSummaryLoading = true;
        try
        {
            var prompt = BuildAiPayload();
            var reply = await _aiService.ChatFastAsync(
                "你是一个简洁的中文助手。用一到两句话概括今天的电脑使用状态，给出一句可执行建议。" +
                "不要罗列数字，不要空泛套话，总长度不超过 80 字。",
                prompt);

            if (!string.IsNullOrWhiteSpace(reply))
            {
                TodaySummary = reply.Trim();
                SummarySource = "AI";
                OnPropertyChanged(nameof(HasSummarySource));
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "仪表盘：AI 速览生成失败，保留规则摘要");
        }
        finally
        {
            IsSummaryLoading = false;
        }
    }

    private string BuildAiPayload()
    {
        var top = TopApps.Count > 0
            ? string.Join("、", TopApps.Take(3).Select(a => $"{a.Name}({a.Duration})"))
            : "无";
        return $"""
            今日效率评分：{EfficiencyScore:F0}
            活跃时长：{TodayActiveTime}
            专注时长：{FocusMinutesText}
            应用切换：{AppSwitchesText}
            活跃时段：{ActiveWindowText}
            相对平时：{DeviationText}
            主要应用：{top}
            """;
    }

    private static string SerializeChart(ChartSnapshot chart)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                bars = chart.Bars.Select(b => new
                {
                    label = b.Label,
                    value = b.Value,
                    level = b.Level,
                    // C# 的 IsFuture 需显式转成小写字段名，JS 侧读 future
                    future = b.IsFuture
                }).ToArray(),
                summary = chart.Summary
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "仪表盘：图表数据序列化失败");
            return """{"bars":[],"summary":""}""";
        }
    }

    /// <summary>
    /// 同步派生文本属性。null 显示「--」而非 0 ——「真的是 0」与「聚合表还没产出这个字段」
    /// 是两件事，混为一谈会让用户误判。
    /// </summary>
    private void SyncDerivedText()
    {
        FocusMinutesText = FocusMinutes is { } f ? $"{f:F0} 分钟" : "--";
        AppSwitchesText = AppSwitches is { } s ? $"{s} 次" : "--";
        ActiveWindowText =
            !string.IsNullOrEmpty(FirstActiveTime) && !string.IsNullOrEmpty(LastActiveTime)
                ? $"{FirstActiveTime} – {LastActiveTime}"
                : "--";
        DataAsOfText = string.IsNullOrEmpty(DataAsOf) ? "" : $"数据截至 {DataAsOf}";

        // 均为普通属性（x:Bind OneWay 不会自动监听组合计算的结果），手动抬升
        OnPropertyChanged(nameof(FocusMinutesText));
        OnPropertyChanged(nameof(AppSwitchesText));
        OnPropertyChanged(nameof(ActiveWindowText));
        OnPropertyChanged(nameof(DataAsOfText));
    }

    private static void ApplyCompare(CompareItem item, double? pct)
    {
        var src = CompareItem.FromPct(pct);
        item.IsVisible = src.IsVisible;
        item.Text = src.Text;
        item.Glyph = src.Glyph;
        item.IsUp = src.IsUp;
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

    /// <summary>分类名，与「应用统计」页同源（3.26.1 起统一走 ProgramCategoryMappings 映射表）。</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>分类色（来自 ProgramCategories.Color），与应用统计页一致。</summary>
    public string CategoryColorHex { get; init; } = "#6B7280";

    /// <summary>
    /// 分类色画刷。仅在 UI 线程赋值（陷阱 #8：WinRT 对象不能跨线程创建）。
    /// </summary>
    public SolidColorBrush? CategoryBrush { get; set; }

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

    /// <summary>
    /// 活动条高度（像素）。用「像素直出」而非 ProgressBar 的百分比最大值，
    /// 避免零值与"无数据"看起来一样（0% 的进度条几乎不可见）。
    /// </summary>
    public double BarHeight => Math.Max(2, Activity / 100.0 * 80);

    /// <summary>
    /// 活动条颜色：按 Category 三级着色。3.25.1 起真正使用 Category 字段——
    /// 此前它被算出来却从未在 UI 上体现。
    /// </summary>
    public SolidColorBrush BarBrush => Category switch
    {
        "高效" => new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 0xEF, 0x44, 0x44)),
        "中等" => new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 0xF5, 0x9E, 0x0B)),
        _ => new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 0x10, 0xB9, 0x81))
    };
}

public class CategoryDistributionItem
{
    public string Name { get; init; } = string.Empty;
    public string Percentage { get; init; } = string.Empty;

    /// <summary>占比数值（0-100），供进度条宽度绑定。</summary>
    public double PercentageValue { get; init; }

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

/// <summary>
/// 环比展示项。XAML 不能直接把 double? 绑到 Text，需转成字符串与可见性；
/// 用一个 item 承载可复用的呈现逻辑（图标、颜色、文本三处不必各写一遍转换）。
/// </summary>
public sealed class CompareItem : ObservableObject
{
    private bool _isVisible;
    private string _text = "";
    private string _glyph = "";
    private bool _isUp;

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    /// <summary>如「较昨日 +12%」；无对比数据时为空。</summary>
    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value);
    }

    /// <summary>箭头字形：▲ 上升 / ▼ 下降 / ● 持平。</summary>
    public string Glyph
    {
        get => _glyph;
        set => SetProperty(ref _glyph, value);
    }

    public bool IsUp
    {
        get => _isUp;
        set => SetProperty(ref _isUp, value);
    }

    /// <summary>构造：pct 为 null（昨日无数据）时返回不可见项。</summary>
    public static CompareItem FromPct(double? pct, string label = "较昨日")
    {
        if (pct is not { } v) return new CompareItem { IsVisible = false };

        return new CompareItem
        {
            IsVisible = true,
            Text = $"{label} {(v >= 0 ? "+" : "")}{v:F0}%",
            Glyph = v > 0.5 ? "▲" : v < -0.5 ? "▼" : "●",
            IsUp = v > 0.5
        };
    }
}
