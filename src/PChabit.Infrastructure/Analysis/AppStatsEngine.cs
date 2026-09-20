using Microsoft.EntityFrameworkCore;
using PChabit.Core.Entities;
using PChabit.Infrastructure.Data;

namespace PChabit.Infrastructure.Analysis;

// ==================== 报告 DTO（纯 POCO，无 WinRT 类型） ====================

/// <summary>应用排行行</summary>
public record AppRankRow(
    string ProcessName,
    string AppName,
    string Category,
    string CategoryColorHex,
    double DurationMinutes,
    int Sessions,
    double AvgDurationMinutes,
    double Percentage,
    double FocusMinutes,
    string DeltaText,
    string DeltaDirection); // up | down | flat | none

/// <summary>饼图切片（Top N + 其他）</summary>
public record AppPieSlice(string Name, double Minutes, string ColorHex);

/// <summary>分类构成</summary>
public record AppCategoryShare(string Category, double Minutes, double Percentage, string ColorHex);

/// <summary>小时活跃点</summary>
public record AppHourPoint(int Hour, string Label, double Minutes);

/// <summary>应用统计报告</summary>
public record AppStatsReport(
    AnalyticsPeriod Period,
    AnalyticsPeriod Previous,
    double TotalMinutes,
    int ActiveApps,
    string TopAppName,
    double TopAppMinutes,
    int FocusSessions,
    IReadOnlyList<AppRankRow> Rows,
    IReadOnlyList<AppPieSlice> PieSlices,
    IReadOnlyList<AppCategoryShare> CategoryShares,
    IReadOnlyList<AppHourPoint> Hourly);

/// <summary>
/// 应用统计计算引擎：范围聚合、排行、构成、饼图、小时分布、环比。
/// 纯计算（Compute）可单测；BuildAsync 只负责查询。
/// 复用 <see cref="AnalyticsEngine"/> 的周期与生产力分类口径。
/// </summary>
public static class AppStatsEngine
{
    private const int PieTopN = 8;

    // 无自定义分类色时的兜底色板（旧系统分类）
    private static readonly (string[] Categories, string Color)[] DefaultPalette =
    {
        (new[] { "开发", "开发工具" }, "#512BD4"),
        (new[] { "浏览", "浏览器" }, "#0078D4"),
        (new[] { "沟通", "通讯" }, "#107C10"),
        (new[] { "娱乐", "视频", "游戏" }, "#FF8C00"),
        (new[] { "办公", "办公软件" }, "#00B7C3")
    };

    /// <summary>归一化进程名：小写、去 .exe 后缀。</summary>
    public static string NormalizeProcessName(string processName)
    {
        var p = (processName ?? "").Trim();
        if (p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            p = p[..^4];
        return p.ToLowerInvariant();
    }

    /// <summary>分类兜底色。</summary>
    public static string DefaultColorFor(string? category)
    {
        if (!string.IsNullOrEmpty(category))
        {
            foreach (var (cats, color) in DefaultPalette)
            {
                if (cats.Any(c => category.Contains(c, StringComparison.OrdinalIgnoreCase)))
                    return color;
            }
        }
        return "#6B7280";
    }

    /// <summary>从会话取展示名：首个非空 AppName，否则进程名。</summary>
    public static string ResolveDisplayName(IEnumerable<AppSession> sessions, string processName)
    {
        var name = sessions.Select(s => s.AppName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
        return string.IsNullOrWhiteSpace(name) ? processName : name;
    }

    /// <summary>构建 进程名(规范化) → (分类名, 颜色Hex) 映射（含 .exe 变体）。</summary>
    public static Dictionary<string, (string Name, string ColorHex)> BuildCategoryMap(
        IReadOnlyList<ProgramCategory> categories)
    {
        var map = new Dictionary<string, (string Name, string ColorHex)>(StringComparer.OrdinalIgnoreCase);
        foreach (var cat in categories)
        {
            if (cat.ProgramMappings == null) continue;
            foreach (var m in cat.ProgramMappings)
            {
                if (string.IsNullOrEmpty(m.ProcessName)) continue;
                var normalized = NormalizeProcessName(m.ProcessName);
                if (normalized.Length == 0) continue;
                map[normalized] = (cat.Name, string.IsNullOrWhiteSpace(cat.Color) ? DefaultColorFor(cat.Name) : cat.Color);
            }
        }
        return map;
    }

    /// <summary>加载分类映射（含自定义色）。</summary>
    public static async Task<Dictionary<string, (string Name, string ColorHex)>> LoadCategoryMapAsync(
        IDbContextFactory<PChabitDbContext> dbFactory,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var categories = await db.ProgramCategories
            .AsNoTracking()
            .Include(c => c.ProgramMappings)
            .Where(c => c.IsActive)
            .ToListAsync(ct);
        return BuildCategoryMap(categories);
    }

    /// <summary>查询 + 计算。当前期与上一周期一次查询后在内存切分。</summary>
    public static async Task<AppStatsReport> BuildAsync(
        IDbContextFactory<PChabitDbContext> dbFactory,
        AnalyticsPeriod period,
        string? categoryFilter = null,
        string? search = null,
        CancellationToken ct = default)
    {
        var previous = period.Previous();
        var categoryMap = await LoadCategoryMapAsync(dbFactory, ct);

        var queryStart = previous.Start;
        var queryEnd = period.EndExclusive;

        List<AppSession> sessions;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            sessions = await db.AppSessions.AsNoTracking()
                .Where(s => s.StartTime >= queryStart && s.StartTime < queryEnd)
                .Select(s => new AppSession
                {
                    Id = s.Id,
                    ProcessName = s.ProcessName,
                    AppName = s.AppName,
                    Category = s.Category,
                    StartTime = s.StartTime,
                    EndTime = s.EndTime
                })
                .ToListAsync(ct);
        }

        var cur = sessions.Where(s => s.StartTime >= period.Start && s.StartTime < period.EndExclusive).ToList();
        var prev = sessions.Where(s => s.StartTime >= previous.Start && s.StartTime < previous.EndExclusive).ToList();

        return Compute(period, previous, cur, prev, categoryMap, categoryFilter, search);
    }

    /// <summary>纯计算入口（可单测）。</summary>
    public static AppStatsReport Compute(
        AnalyticsPeriod period,
        AnalyticsPeriod previous,
        IReadOnlyList<AppSession> sessions,
        IReadOnlyList<AppSession> prevSessions,
        IReadOnlyDictionary<string, (string Name, string ColorHex)> categoryMap,
        string? categoryFilter = null,
        string? search = null)
    {
        static double MinutesOf(AppSession s) =>
            s.EndTime.HasValue ? (s.EndTime.Value - s.StartTime).TotalMinutes : 0;

        // 过滤（分类 / 搜索）作用于当前期会话
        var filtered = sessions.Where(s =>
        {
            if (!string.IsNullOrEmpty(categoryFilter))
            {
                var (catName, _) = ResolveCategory(s, categoryMap);
                if (!string.Equals(catName, categoryFilter, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            if (!string.IsNullOrWhiteSpace(search))
            {
                var display = ResolveDisplayName(new[] { s }, s.ProcessName ?? "");
                var q = search.Trim();
                if (!display.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                    !(s.ProcessName ?? "").Contains(q, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }).ToList();

        var totalMinutes = filtered.Sum(MinutesOf);
        var activeApps = filtered.Select(s => s.ProcessName).Distinct().Count();

        // 按进程分组
        var groups = filtered
            .GroupBy(s => s.ProcessName ?? "")
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .Select(g =>
            {
                var processName = g.Key;
                var (category, colorHex) = ResolveCategory(g.First(), categoryMap);
                var duration = g.Sum(MinutesOf);
                var focusMinutes = g.Where(s => MinutesOf(s) >= 25 && AnalyticsEngine.IsProductiveCategory(category)).Sum(MinutesOf);
                var prevDuration = prevSessions
                    .Where(s => string.Equals(s.ProcessName, processName, StringComparison.OrdinalIgnoreCase))
                    .Sum(MinutesOf);
                var delta = duration - prevDuration;
                var direction = Math.Abs(delta) < 5 ? "flat"
                    : prevDuration <= 0 ? "up"
                    : delta > 0 ? "up" : "down";
                var deltaText = direction switch
                {
                    "flat" => "—",
                    "up" when prevDuration <= 0 => "新增",
                    "up" => $"+{delta / prevDuration * 100:F0}%",
                    _ => $"{delta / prevDuration * 100:F0}%"
                };
                return new AppRankRow(
                    processName,
                    ResolveDisplayName(g, processName),
                    category,
                    colorHex,
                    duration,
                    g.Count(),
                    g.Count() > 0 ? duration / g.Count() : 0,
                    totalMinutes > 0 ? duration / totalMinutes * 100 : 0,
                    focusMinutes,
                    deltaText,
                    direction);
            })
            .OrderByDescending(x => x.DurationMinutes)
            .ThenBy(x => x.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var topApp = groups.FirstOrDefault();
        var focusSessions = filtered.Count(s =>
        {
            var (catName, _) = ResolveCategory(s, categoryMap);
            return MinutesOf(s) >= 25 && AnalyticsEngine.IsProductiveCategory(catName);
        });

        // 饼图：Top 8 + 其他
        var pie = new List<AppPieSlice>();
        foreach (var row in groups.Take(PieTopN))
            pie.Add(new AppPieSlice(row.AppName, row.DurationMinutes, row.CategoryColorHex));
        var otherMinutes = groups.Skip(PieTopN).Sum(x => x.DurationMinutes);
        if (otherMinutes > 0)
            pie.Add(new AppPieSlice("其他", otherMinutes, "#8B5CF6"));

        // 分类构成
        var categoryShares = filtered
            .GroupBy(s => ResolveCategory(s, categoryMap).Name)
            .Select(g => new AppCategoryShare(
                g.Key,
                g.Sum(MinutesOf),
                totalMinutes > 0 ? g.Sum(MinutesOf) / totalMinutes * 100 : 0,
                ResolveCategory(g.First(), categoryMap).ColorHex))
            .OrderByDescending(x => x.Minutes)
            .ToList();

        // 24 小时活跃（按小时分组累加即各日同小时合计，只读聚合无跨天状态残留问题）
        var hourly = new List<AppHourPoint>();
        for (var h = 0; h < 24; h++)
        {
            var mins = filtered.Where(s => s.StartTime.Hour == h).Sum(MinutesOf);
            hourly.Add(new AppHourPoint(h, $"{h:00}:00", mins));
        }

        return new AppStatsReport(
            period, previous, totalMinutes, activeApps,
            topApp?.AppName ?? "无", topApp?.DurationMinutes ?? 0,
            focusSessions, groups, pie, categoryShares, hourly);
    }

    /// <summary>解析会话分类：映射命中优先，否则回退会话自带分类字段。</summary>
    public static (string Name, string ColorHex) ResolveCategory(
        AppSession session,
        IReadOnlyDictionary<string, (string Name, string ColorHex)> categoryMap)
    {
        var normalized = NormalizeProcessName(session.ProcessName ?? "");
        if (normalized.Length > 0 && categoryMap.TryGetValue(normalized, out var hit))
            return hit;
        var fallback = session.Category;
        return (string.IsNullOrWhiteSpace(fallback) ? "未分类" : fallback, DefaultColorFor(fallback));
    }
}
