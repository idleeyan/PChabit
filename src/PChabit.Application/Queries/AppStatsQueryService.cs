using Microsoft.EntityFrameworkCore;
using PChabit.Infrastructure.Analysis;
using PChabit.Infrastructure.Data;

namespace PChabit.Application.Queries;

/// <summary>
/// 应用统计页查询服务（3.76.0 从 <c>AppStatsViewModel</c> 收口，改造计划阶段 E3-3）。
/// VM 不再注入 <c>IDbContextFactory</c>；报告构建透传
/// <see cref="AppStatsEngine.BuildAsync"/>（逻辑零变化）。
/// </summary>
public sealed class AppStatsQueryService
{
    private readonly IDbContextFactory<PChabitDbContext> _dbFactory;

    public AppStatsQueryService(IDbContextFactory<PChabitDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <summary>按周期/分类过滤/搜索词构造应用统计报告（透传引擎）。</summary>
    public Task<AppStatsReport> BuildReportAsync(
        AnalyticsPeriod period,
        string? categoryFilter,
        string? searchText)
        => AppStatsEngine.BuildAsync(_dbFactory, period, categoryFilter, searchText);
}
