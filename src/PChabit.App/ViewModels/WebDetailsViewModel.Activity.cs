using Microsoft.EntityFrameworkCore;
using Serilog;
using PChabit.Infrastructure.Analysis;

namespace PChabit.App.ViewModels;

/// <summary>
/// 活动数据汇总（3.37.0）—— 把原先散在多个卡片里的活动维度合并到一处。
///
/// 背景：网页访问页有 6 张卡片（网站访问排行 / 浏览习惯 / 每小时活动分布 /
/// 每日访问趋势 / 网页流量归因 / 最近访问记录），其中
/// - 「网站访问排行」与「网页流量归因」是**同一批域名**的两个视角，
///   流量归因只是一列附加信息，单独占一张卡不合理→ 合并为 <see cref="BuildDomainActivity"/>
/// - 「每小时活动分布」只统计网页，而应用使用的小时分布在另一个页面，
///   但两者都属于「活动」→ 合并为 <see cref="BuildHourlyCombined"/>
/// </summary>
public partial class WebDetailsViewModel
{
    /// <summary>
    /// 合并「网站访问排行」与「网页流量归因」为一张表。
    /// </summary>
    /// <remarks>
    /// 口径说明：两个来源本就是同一批域名——
    /// 排行按会话数/时长排序，流量归因把浏览器下行流量按各域名在该小时的时长占比分摊。
    /// 因此可按 Domain 精确join；流量为 0 的域名仍保留（仅流量列显示 "—"）。
    /// </remarks>
    private static List<DomainActivityRow> BuildDomainActivity(
        List<DomainStatItem> domainStats,
        List<TrafficAttributionItem> attribution)
    {
        // 流量按域名汇总（同一域名在多个小时被分摊，这里合并成一条）
        var traffic = attribution
            .GroupBy(a => a.Domain, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => (
                    Bytes: ParseDownText(g.First().DownText),
                    Share: g.Sum(x => x.SharePercent)),
                StringComparer.OrdinalIgnoreCase);

        var rows = new List<DomainActivityRow>(domainStats.Count);
        foreach (var d in domainStats)
        {
            traffic.TryGetValue(d.Domain, out var t);
            rows.Add(new DomainActivityRow
            {
                Domain = d.Domain,
                Category = d.Category,
                VisitCount = d.VisitCount,
                TotalMinutes = d.TotalDuration,
                DownBytes = t.Bytes,
                TrafficSharePct = t.Share
            });
        }
        return rows;
    }

    /// <summary>
    /// 合并应用使用与网页访问的小时分布为一行。
    /// 两个维度都按 <c>StartTime.Hour</c> 归集（与应用统计页口径一致），
    /// 且都限定在用户所选的时间段内。
    /// </summary>
    private static List<HourlyActivityCombinedRow> BuildHourlyCombined(
        List<WebHourlyActivityItem> webHourly,
        List<AppHourPoint> appHourly)
    {
        var result = new List<HourlyActivityCombinedRow>(24);
        for (var h = 0; h < 24; h++)
        {
            var web = webHourly.FirstOrDefault(x => x.Hour == h);
            var app = appHourly?.FirstOrDefault(x => x.Hour == h);
            result.Add(new HourlyActivityCombinedRow
            {
                Hour = h,
                HourLabel = $"{h:00}:00",
                WebVisits = web?.VisitCount ?? 0,
                WebMinutes = web?.Duration ?? 0,
                AppMinutes = app?.Minutes ?? 0
            });
        }
        return result;
    }

    /// <summary>
    /// 查询所选时间段内的应用使用分钟数，按小时聚合。
    /// 只取时间字段与时长参与聚合，不加载应用名/图标等。
    /// </summary>
    private async Task<List<AppHourPoint>> LoadAppHourlyAsync()
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var end = EndDate.AddDays(1);

            var rows = await db.AppSessions
                .AsNoTracking()
                .Where(s => s.StartTime >= StartDate && s.StartTime < end)
                .Select(s => new { s.StartTime, s.Duration })
                .ToListAsync();

            var result = new List<AppHourPoint>(24);
            for (var h = 0; h < 24; h++)
            {
                var mins = rows
                    .Where(s => s.StartTime.Hour == h)
                    .Sum(s => s.Duration.TotalMinutes);
                result.Add(new AppHourPoint(h, $"{h:00}:00", mins));
            }
            return result;
        }
        catch (Exception ex)
        {
            // 应用侧数据缺失不应影响网页统计展示
            Log.Warning(ex, "WebDetails: 应用小时活动查询失败，网页侧照常展示");
            return new List<AppHourPoint>();
        }
    }

    /// <summary>解析归因项里已格式化好的下行流量文本（如 "12.3 MB"）为字节数。</summary>
    private static double ParseDownText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var s = text.Trim();

        double factor = 1;
        if (s.EndsWith("GB", StringComparison.OrdinalIgnoreCase))
        {
            factor = 1024d * 1024 * 1024;
            s = s[..^2];
        }
        else if (s.EndsWith("MB", StringComparison.OrdinalIgnoreCase))
        {
            factor = 1024d * 1024;
            s = s[..^2];
        }
        else if (s.EndsWith("KB", StringComparison.OrdinalIgnoreCase))
        {
            factor = 1024d;
            s = s[..^2];
        }
        else if (s.EndsWith("B", StringComparison.OrdinalIgnoreCase))
        {
            s = s[..^1];
        }

        return double.TryParse(s, out var v) ? v * factor : 0;
    }
}