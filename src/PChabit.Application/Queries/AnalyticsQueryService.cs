using Microsoft.EntityFrameworkCore;
using PChabit.Infrastructure.Analysis;
using PChabit.Infrastructure.Data;

namespace PChabit.Application.Queries;

/// <summary>
/// 分析页查询服务（3.74.0 从 <c>AnalyticsViewModel</c> 收口，改造计划阶段 E3-1）。
/// VM 不再注入 <c>IDbContextFactory</c>，分析数据经本服务产出
/// <see cref="AnalyticsPeriodReport"/>（计算仍在 <see cref="AnalyticsEngine"/>，零逻辑变化）。
/// </summary>
public sealed class AnalyticsQueryService
{
    private readonly IDbContextFactory<PChabitDbContext> _dbContextFactory;

    public AnalyticsQueryService(IDbContextFactory<PChabitDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    /// <summary>按周期构造分析报告（透传 <see cref="AnalyticsEngine.BuildAsync"/>）。</summary>
    public Task<AnalyticsPeriodReport> BuildReportAsync(AnalyticsPeriod period)
        => AnalyticsEngine.BuildAsync(_dbContextFactory, period);
}
