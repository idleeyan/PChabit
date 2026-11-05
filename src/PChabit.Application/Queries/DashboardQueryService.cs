using Microsoft.EntityFrameworkCore;
using Serilog;
using PChabit.Core.Entities;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Analysis;
using PChabit.Infrastructure.Data;

namespace PChabit.Application.Queries;

/// <summary>
/// 仪表盘查询服务（3.71.0 从 <c>DashboardViewModel</c> 下沉，改造计划阶段 D4 步骤 2b+3）。
/// 承担「查库 + 聚合 + 衍生指标」，产出纯数据快照 <see cref="DashboardSnapshot"/>；
/// VM 只保留「快照 → 绑定属性」的映射。行为与原 VM 实现逐字一致：
/// 今日数据实时合并且不写库（项目硬约束），效率/基线/环比各自容错降级。
/// </summary>
public sealed class DashboardQueryService
{
    private readonly IDbContextFactory<PChabitDbContext> _dbContextFactory;
    private readonly IEfficiencyCalculator _efficiencyCalculator;

    public DashboardQueryService(
        IDbContextFactory<PChabitDbContext> dbContextFactory,
        IEfficiencyCalculator efficiencyCalculator)
    {
        _dbContextFactory = dbContextFactory;
        _efficiencyCalculator = efficiencyCalculator;
    }

    /// <summary>加载某日的仪表盘快照（仪表盘固定传「今天」）。</summary>
    public async Task<DashboardSnapshot> LoadAsync(DateTime today)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

        var stats = await BuildStatsFromRawDataAsync(dbContext, today);

        // 效率评分与个人基线走独立服务（内部各自建 DbContext，不与上面的查询并发）
        stats.EfficiencyScore = await SafeScoreAsync(today);
        var deviation = await SafeDeviationAsync(dbContext, today, stats);
        stats.ActiveDeviationPct = deviation;

        // 环比：昨日同一时刻之前的数据（口径对齐，否则拿"昨天全天"比"今天至今"不公平）
        await FillCompareAsync(dbContext, today, stats);

        // 阶段 5：当前正在使用的应用（未结束的会话）
        var current = await dbContext.AppSessions
            .AsNoTracking()
            .Where(s => s.EndTime == null)
            .OrderByDescending(s => s.StartTime)
            .FirstOrDefaultAsync();
        stats.CurrentAppName = current?.AppName ?? current?.ProcessName ?? "";
        stats.CurrentAppSince = current?.StartTime.ToString("HH:mm") ?? "";

        return stats;
    }

    /// <summary>
    /// 计算相对昨日同时段的环比。截断到「昨日的当前时刻」——
    /// 若直接用昨日全天数据，午后打开仪表盘会看到"大幅下降"的假跌。
    /// </summary>
    private static async Task FillCompareAsync(
        PChabitDbContext dbContext, DateTime today, DashboardSnapshot stats)
    {
        try
        {
            var yesterday = today.AddDays(-1);
            var now = DateTime.Now;
            // 昨日同时刻的截断点（今天与昨日的小时/分钟一致）
            var cutoff = yesterday.Add(now.TimeOfDay);
            if (cutoff <= yesterday) return; // 跨日极端情况，直接不比

            var yApp = await dbContext.AppSessions
                .AsNoTracking()
                .Where(s => s.StartTime >= yesterday && s.StartTime < cutoff)
                .ToListAsync();
            if (yApp.Count == 0) return;

            var yMinutes = yApp.Sum(s => DashboardMetricsCalculator.SessionActiveMinutes(s, cutoff));

            var yKeys = await dbContext.KeyboardSessions
                .AsNoTracking()
                .Where(s => s.Date >= yesterday && s.Date < today)
                .SumAsync(s => (long?)s.TotalKeyPresses) ?? 0;
            var yClicks = await dbContext.MouseSessions
                .AsNoTracking()
                .Where(s => s.Date >= yesterday && s.Date < today)
                .SumAsync(s => (long?)(s.LeftClickCount + s.RightClickCount + s.MiddleClickCount)) ?? 0;
            var yWeb = await dbContext.WebSessions
                .AsNoTracking()
                .CountAsync(s => s.StartTime >= yesterday && s.StartTime < cutoff && !s.IsLegacy);

            var tMinutes = stats.TopAppsRaw.Sum(x => x.Duration);
            stats.ActiveComparePct = DashboardMetricsCalculator.ComparePct(yMinutes, tMinutes);
            stats.KeysComparePct = DashboardMetricsCalculator.ComparePct(yKeys, DashboardMetricsCalculator.ParseNumber(stats.TodayKeyPresses));
            stats.ClicksComparePct = DashboardMetricsCalculator.ComparePct(yClicks, DashboardMetricsCalculator.ParseNumber(stats.TodayMouseClicks));
            stats.WebComparePct = DashboardMetricsCalculator.ComparePct(yWeb, DashboardMetricsCalculator.ParseNumber(stats.TodayWebPages));
        }
        catch (Exception ex)
        {
            // 环比是锦上添花，失败不影响主数据
            Log.Debug(ex, "仪表盘：环比计算失败");
        }
    }

    /// <summary>效率评分失败不应让整页空白（缓存查询统一 try-catch，陷阱 #12 同理）。</summary>
    private async Task<double> SafeScoreAsync(DateTime date)
    {
        try
        {
            var breakdown = await _efficiencyCalculator.CalculateDetailedScoreAsync(date);
            return breakdown.TotalScore;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "仪表盘：效率评分计算失败，降级为 0");
            return 0;
        }
    }

    /// <summary>
    /// 相对个人基线的活跃时长偏离。基线需 ≥3 个有效同类日才有意义，
    /// 由 BaselineStat.DeviationPct 自行判定（样本不足返回 null）。
    /// </summary>
    private static async Task<double?> SafeDeviationAsync(
        PChabitDbContext dbContext, DateTime today, DashboardSnapshot stats)
    {
        try
        {
            // 近 28 天足够覆盖 4 个完整周，且与 PersonalBaseline 的工作日/周末分档匹配
            var days = await HabitDayLoader.LoadAsync(dbContext, today.AddDays(-27), today.AddDays(1));
            if (days.Count == 0) return null;

            var baseline = PersonalBaselineBuilder.Build(days);
            var stat = today.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
                ? baseline.ActiveMinutesWeekend
                : baseline.ActiveMinutesWeekday;

            // 今日活跃分钟：从 TopApps 汇总（与卡片显示同源，避免两套口径）
            var activeMinutes = stats.TopAppsRaw.Sum(x => x.Duration);
            return stat.DeviationPct(activeMinutes);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "仪表盘：个人基线偏离计算失败");
            return null;
        }
    }

    private static async Task<DashboardSnapshot> BuildStatsFromRawDataAsync(
        PChabitDbContext dbContext, DateTime today)
    {
        var tomorrow = today.AddDays(1);
        var now = DateTime.Now;
        var isToday = today == DateTime.Today;

        var appSessions = await dbContext.AppSessions
            .AsNoTracking()
            .Where(s => s.StartTime >= today && s.StartTime < tomorrow)
            .ToListAsync();

        var keyboardSessions = await dbContext.KeyboardSessions
            .AsNoTracking()
            .Where(s => s.Date >= today && s.Date < tomorrow)
            .ToListAsync();

        var mouseSessions = await dbContext.MouseSessions
            .AsNoTracking()
            .Where(s => s.Date >= today && s.Date < tomorrow)
            .ToListAsync();

        // 排除 Legacy 切片，与日聚合 / 历史分析口径一致
        var webSessions = await dbContext.WebSessions
            .AsNoTracking()
            .Where(s => s.StartTime >= today && s.StartTime < tomorrow && !s.IsLegacy)
            .ToListAsync();

        // 聚合表的扩展指标（专注/切换/首末活跃）。可能为 null —— 未按 v2/v3 口径产出。
        var summary = await dbContext.DailySummaries
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Date == today.ToString("yyyy-MM-dd"));

        Log.Information("仪表盘数据加载: AppSessions={AppCnt}, KeyboardSessions={KbCnt}({KbKeys}次按键), MouseSessions={MsCnt}({MsClicks}次点击), WebSessions={WebCnt}, SummaryMetricsVersion={MV}",
            appSessions.Count, keyboardSessions.Count, keyboardSessions.Sum(s => s.TotalKeyPresses),
            mouseSessions.Count, mouseSessions.Sum(s => s.LeftClickCount + s.RightClickCount + s.MiddleClickCount),
            webSessions.Count, summary?.MetricsVersion);

        double SessionMinutes(AppSession s) => DashboardMetricsCalculator.SessionActiveMinutes(s, isToday ? now : today);

        var totalMinutes = appSessions.Sum(SessionMinutes);

        // 每日聚合只处理「前一天」，今日行常为空壳甚至缺失；三类派生指标（专注/切换/首末活跃）
        // 在聚合值缺失时用今日会话实时补齐，避免仪表盘恒显「--」。
        var categoryMap = await LoadCategoryMapAsync(dbContext);

        double? focusMinutes = summary?.FocusMinutesV2;
        if (focusMinutes is null && appSessions.Count > 0)
        {
            // 生产力分类的活跃分钟（不设单会话 ≥25 分钟门槛：切窗即断会话，带门槛会恒为 0）
            focusMinutes = appSessions
                .Where(s => AnalyticsEngine.IsProductiveCategory(
                    AppStatsEngine.ResolveCategory(s, categoryMap).Name))
                .Sum(SessionMinutes);
        }

        var appSwitches = summary?.AppSwitches;
        if (appSwitches is null && appSessions.Count > 0)
            appSwitches = Math.Max(0, appSessions.Count - 1);

        var firstActive = summary?.FirstActiveTime;
        var lastActive = summary?.LastActiveTime;
        if (appSessions.Count > 0)
        {
            if (string.IsNullOrEmpty(firstActive))
                firstActive = appSessions.Min(s => s.StartTime).ToString("HH:mm");
            if (string.IsNullOrEmpty(lastActive))
                lastActive = appSessions.Max(s => DashboardMetricsCalculator.SessionEnd(s, isToday ? now : today)).ToString("HH:mm");
        }

        var totalKeyPresses = keyboardSessions.Sum(s => s.TotalKeyPresses);
        var totalClicks = mouseSessions.Sum(s => s.LeftClickCount + s.RightClickCount + s.MiddleClickCount);
        var totalWebPages = webSessions.Count;

        var hours = (int)(totalMinutes / 60);
        var minutes = (int)(totalMinutes % 60);

        var stats = new DashboardSnapshot
        {
            TodayActiveTime = $"{hours}小时 {minutes}分钟",
            TodayKeyPresses = totalKeyPresses.ToString("N0"),
            TodayMouseClicks = totalClicks.ToString("N0"),
            TodayWebPages = totalWebPages.ToString(),
            FocusMinutes = focusMinutes,
            AppSwitches = appSwitches,
            FirstActiveTime = firstActive ?? "",
            LastActiveTime = lastActive ?? ""
        };

        // 计算 TopApps。分类在下方统一解析（与应用统计页同源，见 3.26.1 修复说明）。
        var topApps = appSessions
            .GroupBy(s => new { s.ProcessName, s.ExecutablePath })
            .Select(g => new
            {
                g.Key.ProcessName,
                g.Key.ExecutablePath,
                // 映射表未命中时的回落分类（采集侧已按权威体系写入）
                FallbackCategory = g
                    .Select(s => s.Category)
                    .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)),
                Duration = g.Sum(SessionMinutes)
            })
            .OrderByDescending(x => x.Duration)
            .Take(5)
            .ToList();

        if (topApps.Any())
        {
            stats.MostUsedApp = topApps.First().ProcessName;

            // 分类必须与应用统计页一致。
            // 3.26.1 修复：此前用 AppCategoryResolver 实时硬编码映射，绕过了 ProgramCategoryMappings
            // （200 条用户可自定义规则），导致同一进程在仪表盘与应用统计页显示不同分类。
            // 现在直接读同一张映射表，并复用 AppStatsEngine.ResolveCategory 的解析规则。
            // （映射表已在方法上方加载，供专注分钟实时补齐复用）

            stats.TopAppsRaw = topApps
                .Select(x =>
                {
                    // 统一走 AppStatsEngine：映射命中优先，回落 AppSession.Category，再回落「未分类」
                    var (catName, catColor) = AppStatsEngine.ResolveCategory(
                        new AppSession { ProcessName = x.ProcessName, Category = x.FallbackCategory },
                        categoryMap);
                    return (x.ProcessName, catName, x.Duration, catColor);
                })
                .ToList();
        }

        // 计算 HourlyActivity（今日未来小时不画）与图表柱状数据
        var nowHour = DateTime.Now.Hour;
        for (int i = 0; i < 24; i++)
        {
            var hourStart = today.AddHours(i);
            var hourEnd = hourStart.AddHours(1);
            var isFuture = isToday && i > nowHour;
            if (isToday && hourStart >= now) break;

            var clipEnd = isToday && hourEnd > now ? now : hourEnd;
            var hourMinutes = appSessions.Sum(s =>
            {
                var start = s.StartTime < hourStart ? hourStart : s.StartTime;
                var end = DashboardMetricsCalculator.SessionEnd(s, isToday ? now : today);
                if (end > clipEnd) end = clipEnd;
                return Math.Max(0, (end - start).TotalMinutes);
            });

            var level = hourMinutes > 40 ? "high" : hourMinutes > 20 ? "mid" : "low";
            stats.HourlyActivity.Add(new HourlyActivityData
            {
                Hour = $"{i}:00",
                Activity = Math.Min(100, (int)(hourMinutes / 60 * 100)),
                Category = hourMinutes > 40 ? "高效" : hourMinutes > 20 ? "中等" : "低效"
            });

            stats.Chart.Bars.Add(new ActivityBar
            {
                Label = $"{i}",
                Value = Math.Round(hourMinutes, 1),
                Level = level,
                IsFuture = isFuture
            });
        }

        // 补齐未来小时的灰色占位，让图表横轴完整覆盖 0-23 时
        if (isToday)
        {
            for (int i = stats.Chart.Bars.Count; i < 24; i++)
            {
                stats.Chart.Bars.Add(new ActivityBar { Label = $"{i}", Value = 0, Level = "low", IsFuture = true });
            }
        }

        stats.Chart.Summary = stats.Chart.Bars.Any(b => !b.IsFuture && b.Value > 0)
            ? $"今日各小时活跃分钟（截至 {now:HH:mm}）"
            : "今天还没有活动记录";

        // 计算 CategoryDistribution：与应用统计页同源（同一份映射表解析 + 同一套配色）
        var categories = stats.TopAppsRaw
            .GroupBy(x => x.CategoryName)
            .Select(g => new
            {
                Name = g.Key,
                Duration = g.Sum(x => x.Duration),
                ColorHex = g.First().CategoryColorHex
            })
            .OrderByDescending(x => x.Duration)
            .Take(5)
            .ToList();
        stats.CategoryRaw = categories
            .Select(x => (x.Name, x.Duration, x.ColorHex))
            .ToList();

        // 计算 TopWebsites
        var topSites = webSessions
            .GroupBy(s => s.Domain)
            .Select(g => new { Domain = g.Key, Count = g.Count(), TotalDurationTicks = g.Sum(s => s.Duration.Ticks) })
            .OrderByDescending(x => x.Count)
            .Take(5)
            .ToList();

        if (topSites.Any())
        {
            stats.MostVisitedSite = topSites.First().Domain ?? "无数据";
            stats.TopWebsites = topSites
                .Where(s => !string.IsNullOrEmpty(s.Domain))
                .Select(s => new WebsiteUsageData { Domain = s.Domain!, Visits = s.Count, TotalDurationTicks = s.TotalDurationTicks })
                .ToList();
        }

        return stats;
    }

    /// <summary>
    /// 加载用户自定义的「进程 → 分类」映射表。
    /// 与 AppStatsEngine.LoadCategoryMapAsync 同源同逻辑（后者需 DbContextFactory，
    /// 此处已有打开的 context，直接查即可，避免多余的工厂与连接）。
    /// </summary>
    private static async Task<Dictionary<string, (string Name, string ColorHex)>> LoadCategoryMapAsync(
        PChabitDbContext db)
    {
        var categories = await db.ProgramCategories
            .AsNoTracking()
            .Include(c => c.ProgramMappings)
            .Where(c => c.IsActive)
            .ToListAsync();

        return AppStatsEngine.BuildCategoryMap(categories);
    }
}
