using Microsoft.EntityFrameworkCore;
using PChabit.Core.Entities;
using PChabit.Infrastructure.Analysis;
using PChabit.Infrastructure.Data;

namespace PChabit.Application.Queries;

/// <summary>时间线某日查询结果：当日会话 + 当日网页会话 + 统一分类映射。</summary>
public sealed record TimelineDayResult(
    List<AppSession> AppSessions,
    List<WebSession> WebSessions,
    IReadOnlyDictionary<string, (string Name, string ColorHex)> CategoryMap);

/// <summary>时间线某时段查询结果：应用会话 + 网页会话 + 统一分类映射。</summary>
public sealed record TimelineHourResult(
    List<AppSession> AppSessions,
    List<WebSession> WebSessions,
    IReadOnlyDictionary<string, (string Name, string ColorHex)> CategoryMap);

/// <summary>
/// 时间线查询服务（3.75.0 从 <c>TimelineViewModel</c> 收口，改造计划阶段 E3-2）。
/// VM 不再注入 <c>IDbContextFactory</c>；主查询与展开时段的按需查询都经本服务，
/// 分类映射统一由 <see cref="AppStatsEngine.LoadCategoryMapAsync"/> 产出（口径不变）。
/// 查询失败时的容错降级行为（空列表 + Log.Warning）与原 VM 逐字一致。
/// </summary>
public sealed class TimelineQueryService
{
    private readonly IDbContextFactory<PChabitDbContext> _dbFactory;

    public TimelineQueryService(IDbContextFactory<PChabitDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <summary>加载某日的应用会话 + 网页会话 + 分类映射（时间线主查询）。</summary>
    public async Task<TimelineDayResult> LoadDayAsync(DateTime selectedDate, CancellationToken cancellationToken = default)
    {
        var nextDay = selectedDate.AddDays(1);

        await using var dbContext = await _dbFactory.CreateDbContextAsync();

        List<AppSession> appSessions;
        try
        {
            appSessions = await dbContext.AppSessions
                .AsNoTracking()
                .Where(s => s.StartTime >= selectedDate && s.StartTime < nextDay)
                .OrderByDescending(s => s.StartTime)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "加载 AppSessions 失败");
            appSessions = new List<AppSession>();
        }

        List<WebSession> webSessions;
        try
        {
            webSessions = await dbContext.WebSessions
                .AsNoTracking()
                .Where(s => s.StartTime >= selectedDate && s.StartTime < nextDay && !s.IsLegacy)
                .OrderByDescending(s => s.StartTime)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "加载 WebSessions 失败");
            webSessions = new List<WebSession>();
        }

        cancellationToken.ThrowIfCancellationRequested();

        // 分类统一走 AppStatsEngine：用户映射表优先，会话自带分类仅作回落
        var categoryMap = await AppStatsEngine.LoadCategoryMapAsync(_dbFactory, cancellationToken);

        return new TimelineDayResult(appSessions, webSessions, categoryMap);
    }

    /// <summary>加载某小时的应用会话 + 网页会话 + 分类映射（展开时段按需查询）。</summary>
    public async Task<TimelineHourResult> LoadHourAsync(DateTime hourStart, CancellationToken cancellationToken = default)
    {
        var hourEnd = hourStart.AddHours(1);

        var sessions = await Task.Run(async () =>
        {
            await using var dbContext = await _dbFactory.CreateDbContextAsync();
            return await dbContext.AppSessions
                .AsNoTracking()
                .Where(s => s.StartTime >= hourStart && s.StartTime < hourEnd)
                .OrderByDescending(s => s.StartTime)
                .ToListAsync(cancellationToken);
        }, cancellationToken);

        var webInHour = await Task.Run(async () =>
        {
            await using var dbContext = await _dbFactory.CreateDbContextAsync();
            return await dbContext.WebSessions
                .AsNoTracking()
                .Where(s => s.StartTime >= hourStart && s.StartTime < hourEnd && !s.IsLegacy)
                .OrderByDescending(s => s.StartTime)
                .ToListAsync(cancellationToken);
        }, cancellationToken);

        var categoryMap = await AppStatsEngine.LoadCategoryMapAsync(_dbFactory);

        return new TimelineHourResult(sessions, webInHour, categoryMap);
    }
}
