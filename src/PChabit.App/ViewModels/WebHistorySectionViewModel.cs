using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Serilog;
using PChabit.Core.Entities;
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

    public ObservableCollection<HistoryDomainRow> AllDomains { get; } = new();
    public ObservableCollection<HistoryDomainRow> FilteredDomains { get; } = new();

    /// <summary>筛选结果为空时为 true（供 XAML 绑定空态提示）。</summary>
    public bool HasNoRows => !IsLoading && FilteredDomains.Count == 0;

    public WebHistorySectionViewModel(IDbContextFactory<PChabitDbContext> factory)
    {
        _factory = factory;
    }

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(HasNoRows));

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

        // 只取聚合所需的最小字段（AsNoTracking + 投影，避免加载整表 25k+ 条）
        var items = await db.BrowserHistoryItems
            .AsNoTracking()
            .Select(x => new { x.Url, x.VisitTime })
            .ToListAsync();

        var map = new Dictionary<string, (int Count, HashSet<string> Urls, DateTime Last)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var it in items)
        {
            var domain = ExtractDomain(it.Url);
            if (string.IsNullOrEmpty(domain)) continue;

            // VisitTime 是 Unix 毫秒（BrowserHistoryItem 约定）
            var visit = DateTimeOffset.FromUnixTimeMilliseconds(it.VisitTime).LocalDateTime;

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

        int total = items.Count;
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
            sb.AppendLine("域名,记录数,唯一URL数,占比,最近访问");
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