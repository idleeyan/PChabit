using Microsoft.EntityFrameworkCore;
using PChabit.Infrastructure.Data;

namespace PChabit.Application.Queries;

/// <summary>域名聚合行（查询结果，供「详细域名统计」列表）。</summary>
public sealed record WebHistoryDomainRow(
    string Domain,
    int RecordCount,
    int UniqueUrlCount,
    DateTime LastVisit);

/// <summary>网页浏览历史查询结果：域名聚合行 + 每域明细记录索引。</summary>
public sealed record WebHistoryQueryResult(
    List<WebHistoryDomainRow> Domains,
    IReadOnlyDictionary<string, List<(string Url, string Title, DateTime Visit)>> DetailsByDomain);

/// <summary>
/// 网页浏览历史查询服务（3.78.0 从 <c>WebHistorySectionViewModel</c> 收口，
/// 改造计划阶段 E3-5）。VM 不再注入 <c>IDbContextFactory</c>；
/// 浏览记录投影查询 + 域名聚合 + 每域明细索引一并产出。
/// 口径保持：只按记录条数聚合（刻意不用 VisitCount——入库重复推送的累积值，
/// 非真实访问次数）；占比分母与行计数同口径。
/// </summary>
public sealed class WebHistoryQueryService
{
    private readonly IDbContextFactory<PChabitDbContext> _factory;

    public WebHistoryQueryService(IDbContextFactory<PChabitDbContext> factory)
    {
        _factory = factory;
    }

    /// <summary>加载全部浏览记录的域名聚合与明细索引。</summary>
    public async Task<WebHistoryQueryResult> LoadAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();

        // 只取聚合与下钻所需的最小字段（AsNoTracking + 投影，避免加载整表 25k+ 条）。
        var items = await db.BrowserHistoryItems
            .AsNoTracking()
            .Select(x => new { x.Url, x.Title, x.VisitTime })
            .ToListAsync();

        var agg = new Dictionary<string, (int Count, HashSet<string> Urls, DateTime Last)>(
            StringComparer.OrdinalIgnoreCase);
        var details = new Dictionary<string, List<(string Url, string Title, DateTime Visit)>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var it in items)
        {
            var domain = ExtractDomain(it.Url);
            if (string.IsNullOrEmpty(domain)) continue;

            // VisitTime 是 Unix 毫秒（BrowserHistoryItem 约定）
            var visit = DateTimeOffset.FromUnixTimeMilliseconds(it.VisitTime).LocalDateTime;
            if (!details.TryGetValue(domain, out var list))
            {
                list = new List<(string Url, string Title, DateTime Visit)>();
                details[domain] = list;
            }
            list.Add((it.Url ?? string.Empty, it.Title ?? string.Empty, visit));

            if (agg.TryGetValue(domain, out var cur))
            {
                cur.Count++;
                cur.Urls.Add(it.Url ?? string.Empty);
                if (visit > cur.Last) cur.Last = visit;
                agg[domain] = cur;
            }
            else
            {
                agg[domain] = (1, new HashSet<string> { it.Url ?? string.Empty }, visit);
            }
        }

        var domains = agg
            .Select(kv => new WebHistoryDomainRow(kv.Key, kv.Value.Count, kv.Value.Urls.Count, kv.Value.Last))
            .ToList();

        return new WebHistoryQueryResult(domains, details);
    }

    /// <summary>从 URL 提取域名（与前端 report.js 的 extractDomain 口径一致；无 scheme 补 http、Host 小写归一）。</summary>
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
}
