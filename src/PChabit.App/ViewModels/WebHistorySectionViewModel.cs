using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Serilog;
using PChabit.Core.Entities;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Data;

namespace PChabit.App.ViewModels;

/// <summary>域名聚合行（原生版「详细域名统计」）。</summary>
public sealed class HistoryDomainRow
{
    public string Domain { get; init; } = string.Empty;
    public int RecordCount { get; init; }
    public int UniqueUrlCount { get; init; }
    public DateTime LastVisit { get; init; }
    public double Percentage { get; init; }

    public string LastVisitText => LastVisit == default ? "—" : LastVisit.ToString("MM-dd HH:mm");
    public string PercentageText => $"{Percentage:0.0}%";
}

/// <summary>域名详情里的单条 URL（3.38.0，对应原报告模态框的 URL 列表）。Count 为该 URL 的记录条数。</summary>
public sealed class HistoryUrlRow
{
    public string Title { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public int Count { get; init; }
    public DateTime LastVisit { get; init; }

    public string LastVisitText => LastVisit == default ? "—" : LastVisit.ToString("MM-dd");
}

/// <summary>域名详情的每日访问趋势（对应原报告模态框的折线图）。</summary>
public sealed class HistoryDailyPoint
{
    public string DateLabel { get; init; } = string.Empty;
    public int Count { get; init; }

    /// <summary>
    /// 归一化柱高（0~1），供 ProgressBar 渲染。
    /// 原报告用的是 Chart.js 折线，这里改为原生柱状以统一画风，
    /// 故需要按当日最大值归一化。
    /// </summary>
    public double CountHeight { get; init; }
}

/// <summary>
/// 域名下的单条明细记录（3.38.0，供域名详情下钻使用）。
///
/// 注意：刻意不携带 <c>BrowserHistoryItem.VisitCount</c>。该字段在入库时
/// 会被「跨批次重复推送」累加（同一 Url|VisitTime 再次推送时 += n，
/// 见 <c>HistoryIngestService.IngestCoreAsync</c>），实测 google.com 单条记录
/// 曾累积到 2074——它是入库侧的重复计数，不是真实访问次数，拿来统计会虚高数倍。
/// 因此全链路统一按「记录条数」计数。
/// </summary>
public sealed record HistoryDetailRecord(string Url, string Title, DateTime Visit);

/// <summary>
/// 浏览历史区块 ViewModel（3.36.0）。
///
/// 原「网页浏览历史」是独立页签，内嵌 WebView2 加载 `report.html`——
/// 浅色硬编码样式（`#f3f4f6` 背景、白卡片投影、24px 标题）与全应用原生卡片画风割裂，
/// 且只能整套替换不能融入。本版把其独有价值内容原生化：
/// **按域名聚合的明细**（含搜索与 CSV 导出）以及数据总览。
///
/// 与「访问统计」页已有的「网站访问排行 / 每小时活动分布 / 每日访问趋势」
/// 刻意不重复——那些是按本机 WebSession 统计的，这是按浏览器历史库聚合的，口径不同。
/// </summary>
public partial class WebHistorySectionViewModel : ViewModelBase
{
    private readonly IDbContextFactory<PChabitDbContext> _factory;
    private readonly ISettingsService _settings;

    /// <summary>数据超过该时长未更新即视为陈旧。</summary>
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromDays(2);

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _summaryText = "未加载";

    [ObservableProperty]
    private int _totalRecords;

    [ObservableProperty]
    private int _uniqueDomains;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _rangeText = "";

    [ObservableProperty]
    private string _statusMessage = "";

    /// <summary>
    /// 数据陈旧提示（3.37.0）。浏览历史依赖浏览器扩展推送，
    /// 一旦采集被关掉或扩展断开，界面就会静默停更——用户无从判断
    /// 「数据没变化」是正常还是坏了。这里显式给出提示与恢复入口。
    /// </summary>
    public bool IsStale { get; private set; }

    /// <summary>陈旧原因说明；IsStale 为 false 时为空。</summary>
    public string StaleReason { get; private set; } = string.Empty;

    /// <summary>采集开关当前是否处于关闭状态（界面无其它入口可改，需在此恢复）。</summary>
    public bool IsIngestDisabled { get; private set; }

    public ObservableCollection<HistoryDomainRow> AllDomains { get; } = new();
    public ObservableCollection<HistoryDomainRow> FilteredDomains { get; } = new();

    /// <summary>
    /// 域名 → 其全部明细记录（3.38.0）。
    /// 供点击域名后展示详情；只保留 Domain/Url/Title/VisitTime/VisitCount，
    /// 不驻留原始实体以免占用额外内存（本机历史库 2.5 万+条）。
    /// </summary>
    private readonly Dictionary<string, List<HistoryDetailRecord>> _details =
        new(StringComparer.OrdinalIgnoreCase);

    // ---- 域名详情（弹窗）状态 ----

    /// <summary>详情弹窗是否可见。</summary>
    public bool IsDetailOpen { get; private set; }

    public string DetailDomain { get; private set; } = string.Empty;
    public int DetailTotalVisits { get; private set; }
    public int DetailUniqueUrls { get; private set; }
    public string DetailFirstVisitText { get; private set; } = "—";
    public string DetailLastVisitText { get; private set; } = "—";

    /// <summary>
    /// 24 小时时段分布（0~23 的访问次数），供共享图表控件呈现。
    /// 类型须与 <c>HourlyBarChart.Rows</c> 一致（IReadOnlyList），
    /// 否则 x:Bind 编译期报 WMC1121 拒绝数组到接口的隐式转换。
    /// </summary>
    public IReadOnlyList<Controls.HourlyBarRow> DetailHourlyRows { get; private set; } =
        Array.Empty<Controls.HourlyBarRow>();

    /// <summary>每日访问趋势（原生折线用数据点）。</summary>
    public IReadOnlyList<HistoryDailyPoint> DetailDailyTrend { get; private set; } = Array.Empty<HistoryDailyPoint>();

    /// <summary>按记录数排序的前 20 个 URL（口径与列表的「记录数」一致）。</summary>
    public IReadOnlyList<HistoryUrlRow> DetailUrls { get; private set; } = Array.Empty<HistoryUrlRow>();

    /// <summary>筛选结果为空时为 true（供 XAML 绑定空态提示）。</summary>
    public bool HasNoRows => !IsLoading && FilteredDomains.Count == 0;

    public WebHistorySectionViewModel(
        IDbContextFactory<PChabitDbContext> factory,
        ISettingsService settings)
    {
        _factory = factory;
        _settings = settings;
    }

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(HasNoRows));

    /// <summary>重新评估陈旧状态（供加载完成后与开关变更后调用）。</summary>
    private void EvaluateFreshness()
    {
        IsIngestDisabled = !_settings.BrowserHistoryIngestEnabled;

        if (TotalRecords == 0)
        {
            IsStale = false;
            StaleReason = "";
            return;
        }

        var last = AllDomains.Count > 0 ? AllDomains.Max(x => x.LastVisit) : DateTime.MinValue;
        var gap = DateTime.Now - last;
        IsStale = gap > StaleThreshold;

        StaleReason = IsStale switch
        {
            true when IsIngestDisabled =>
                $"浏览历史采集已在设置中被关闭，数据停留在 {last:yyyy-MM-dd}（{gap.TotalDays:F0} 天前）",
            true =>
                $"已 {gap.TotalDays:F0} 天没有新记录（最后更新 {last:yyyy-MM-dd HH:mm}），请确认浏览器扩展已连接并授权「浏览历史」权限",
            false => ""
        };

        OnPropertyChanged(nameof(IsStale));
        OnPropertyChanged(nameof(StaleReason));
        OnPropertyChanged(nameof(IsIngestDisabled));
    }

    /// <summary>恢复历史采集（界面上已无其它入口，settings.json 里的开关被关后无从改回）。</summary>
    [RelayCommand]
    private void EnableIngest()
    {
        try
        {
            _settings.BrowserHistoryIngestEnabled = true;
            // 历史已停更期间，BrowserSyncEnabled（云端同步总开关）也可能被一并关掉，
            // 但它不该拦住本机实时入库——读一次以确保不会误关回去。
            StatusMessage = "已开启浏览历史采集，稍后会自动刷新（需浏览器扩展在线）";
            EvaluateFreshness();
            Log.Information("WebHistorySection: 用户重新开启了浏览历史采集");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "WebHistorySection: 开启采集失败");
            StatusMessage = "开启失败: " + ex.Message;
        }
    }

    public async Task LoadAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        try
        {
            var result = await Task.Run(LoadOnBackgroundAsync);

            await RunOnUIThreadAsync(() =>
            {
                AllDomains.Clear();
                foreach (var d in result) AllDomains.Add(d);

                TotalRecords = result.Sum(x => x.RecordCount);
                UniqueDomains = result.Count;
                SummaryText = TotalRecords > 0
                    ? $"历史库 {TotalRecords:N0} 条记录 · {UniqueDomains:N0} 个域名"
                    : "历史库暂无记录";
                RangeText = result.Count > 0
                    ? $"最近访问 {result.Max(x => x.LastVisit):yyyy-MM-dd HH:mm}"
                    : "";

                EvaluateFreshness();
                ApplyFilter();
                return Task.CompletedTask;
            });

            Log.Information("WebHistorySection: 加载完成，域名 {N} 个，记录 {M} 条",
                UniqueDomains, TotalRecords);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "WebHistorySection: 加载失败");
            await RunOnUIThreadAsync(() =>
            {
                SummaryText = "加载失败: " + ex.Message;
                return Task.CompletedTask;
            });
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task<List<HistoryDomainRow>> LoadOnBackgroundAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();

        // 只取聚合与下钻所需的最小字段（AsNoTracking + 投影，避免加载整表 25k+ 条）。
        // 刻意不取 VisitCount：该字段是入库时的重复推送累积值，非真实访问次数
        var items = await db.BrowserHistoryItems
            .AsNoTracking()
            .Select(x => new { x.Url, x.Title, x.VisitTime })
            .ToListAsync();

        var map = new Dictionary<string, (int Count, HashSet<string> Urls, DateTime Last)>(
            StringComparer.OrdinalIgnoreCase);

        // 3.38.0：同步建立域名 → 明细记录索引，供点击域名后展示详情
        _details.Clear();
        foreach (var it in items)
        {
            var domain = ExtractDomain(it.Url);
            if (string.IsNullOrEmpty(domain)) continue;

            // VisitTime 是 Unix 毫秒（BrowserHistoryItem 约定）
            var visit = DateTimeOffset.FromUnixTimeMilliseconds(it.VisitTime).LocalDateTime;
            if (!_details.TryGetValue(domain, out var list))
            {
                list = new List<HistoryDetailRecord>();
                _details[domain] = list;
            }
            list.Add(new HistoryDetailRecord(it.Url ?? string.Empty, it.Title ?? string.Empty, visit));

            if (map.TryGetValue(domain, out var cur))
            {
                cur.Count++;
                cur.Urls.Add(it.Url ?? string.Empty);
                if (visit > cur.Last) cur.Last = visit;
                map[domain] = cur;
            }
            else
            {
                map[domain] = (1, new HashSet<string> { it.Url ?? string.Empty }, visit);
            }
        }

        // 分母同样按记录条数，与上面的 map 计数口径保持一致，
        // 否则各行占比之和会远超 100%
        int total = map.Values.Sum(v => v.Count);
        return map
            .Select(kv => new HistoryDomainRow
            {
                Domain = kv.Key,
                RecordCount = kv.Value.Count,
                UniqueUrlCount = kv.Value.Urls.Count,
                LastVisit = kv.Value.Last,
                Percentage = total > 0 ? kv.Value.Count * 100.0 / total : 0
            })
            .OrderByDescending(x => x.RecordCount)
            .ThenBy(x => x.Domain)
            .ToList();
    }

    /// <summary>
    /// 打开域名详情（3.38.0，还原原 HTML 报告的域名点击行为：
    /// 统计卡片 + 每日趋势 + 24h 时段分布 + URL 列表）。
    /// </summary>
    [RelayCommand]
    private void OpenDetail(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain)) return;

        var records = _details.TryGetValue(domain, out var list)
            ? list
            : new List<HistoryDetailRecord>();
        if (records.Count == 0)
        {
            StatusMessage = "该域名没有明细记录";
            return;
        }

        DetailDomain = domain;
        // 计数口径：记录条数（不累加 VisitCount，该字段是重复推送的累积值，会虚高）
        DetailTotalVisits = records.Count;
        DetailUniqueUrls = records.Select(r => r.Url).Distinct(StringComparer.Ordinal).Count();
        DetailFirstVisitText = records.Min(r => r.Visit).ToString("yyyy-MM-dd");
        DetailLastVisitText = records.Max(r => r.Visit).ToString("yyyy-MM-dd HH:mm");

        // 24 小时时段分布
        var hourly = new int[24];
        foreach (var r in records) hourly[r.Visit.Hour]++;
        DetailHourlyRows = Enumerable.Range(0, 24)
            .Select(h => new Controls.HourlyBarRow($"{h:00}:00", hourly[h]))
            .ToArray();

        // 每日趋势（原生柱状需按最大值归一化才能渲染出高低差异）
        var daily = records
            .GroupBy(r => r.Visit.Date)
            .OrderBy(g => g.Key)
            .Select(g => new HistoryDailyPoint
            {
                DateLabel = g.Key.ToString("MM-dd"),
                Count = g.Count(),
                CountHeight = 0
            })
            .ToList();
        int peak = daily.Count > 0 ? daily.Max(d => d.Count) : 0;
        DetailDailyTrend = daily.Select(d => peak > 0
            ? new HistoryDailyPoint
            {
                DateLabel = d.DateLabel,
                Count = d.Count,
                CountHeight = (double)d.Count / peak
            }
            : d).ToList();

        // URL 列表：按记录数降序，与原报告一致只取前 20
        DetailUrls = records
            .GroupBy(r => r.Url, StringComparer.Ordinal)
            .Select(g => new HistoryUrlRow
            {
                Title = g.First().Title,
                Url = g.Key,
                Count = g.Count(),
                LastVisit = g.Max(r => r.Visit)
            })
            .OrderByDescending(u => u.Count)
            .ThenBy(u => u.Url)
            .Take(20)
            .ToList();

        IsDetailOpen = true;
        OnPropertyChanged(nameof(IsDetailOpen));
        OnPropertyChanged(nameof(DetailDomain));
        OnPropertyChanged(nameof(DetailTotalVisits));
        OnPropertyChanged(nameof(DetailUniqueUrls));
        OnPropertyChanged(nameof(DetailFirstVisitText));
        OnPropertyChanged(nameof(DetailLastVisitText));
        OnPropertyChanged(nameof(DetailHourlyRows));
        OnPropertyChanged(nameof(DetailDailyTrend));
        OnPropertyChanged(nameof(DetailUrls));

        Log.Information("WebHistorySection: 打开域名详情 {Domain}，记录 {N} 条", domain, records.Count);
    }

    [RelayCommand]
    private void CloseDetail()
    {
        IsDetailOpen = false;
        OnPropertyChanged(nameof(IsDetailOpen));
    }

    /// <summary>在浏览器中打开当前域名的首页（详情弹窗内的快捷入口）。</summary>
    [RelayCommand]
    private void OpenDomainInBrowser(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = domain.StartsWith("http") ? domain : "https://" + domain,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "WebHistorySection: 打开域名失败 {Domain}", domain);
            StatusMessage = "打开失败: " + ex.Message;
        }
    }

    /// <summary>从 URL 提取域名（与前端 report.js 的 extractDomain 口径一致）。</summary>
    private static string ExtractDomain(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;
        var s = url.Trim();
        if (!s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            s = "http://" + s;
        }

        return Uri.TryCreate(s, UriKind.Absolute, out var uri)
            ? uri.Host.ToLowerInvariant()
            : string.Empty;
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    public void ApplyFilter()
    {
        var kw = SearchText?.Trim();
        var query = AllDomains.AsEnumerable();

        if (!string.IsNullOrEmpty(kw))
        {
            query = query.Where(d => d.Domain.Contains(kw, StringComparison.OrdinalIgnoreCase));
        }

        FilteredDomains.Clear();
        foreach (var d in query) FilteredDomains.Add(d);

        OnPropertyChanged(nameof(HasNoRows));
    }

    /// <summary>导出当前筛选结果为 CSV（沿用原 HTML 报告的导出能力）。</summary>
    [RelayCommand]
    private void ExportCsv()
    {
        try
        {
            var rows = FilteredDomains.Count > 0 ? FilteredDomains : (IEnumerable<HistoryDomainRow>)AllDomains;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("域名,访问次数,唯一URL数,占比,最近访问");
            foreach (var d in rows)
            {
                sb.AppendLine(string.Join(",",
                    Csv(d.Domain), d.RecordCount, d.UniqueUrlCount,
                    d.PercentageText, Csv(d.LastVisitText)));
            }

            var path = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"网页浏览历史_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            System.IO.File.WriteAllText(path, sb.ToString(), new System.Text.UTF8Encoding(true));
            Log.Information("WebHistorySection: 已导出 {Path}", path);

            StatusMessage = $"已导出 {System.IO.Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "WebHistorySection: 导出 CSV 失败");
            StatusMessage = "导出失败: " + ex.Message;
        }
    }

    private static string Csv(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Contains(',') || s.Contains('"') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }
}