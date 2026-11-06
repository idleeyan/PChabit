using Microsoft.EntityFrameworkCore;
using PChabit.Infrastructure.Analysis;
using PChabit.Infrastructure.Data;

namespace PChabit.Application.Queries;

/// <summary>
/// 应用详情卡片查询服务（3.77.0 从 <c>AppDetailViewModel</c> 收口，改造计划阶段 E3-4）。
/// VM 不再注入 <c>IDbContextFactory</c>；报告构建透传
/// <see cref="AppDetailEngine.BuildAsync"/>（逻辑零变化）。
/// </summary>
public sealed class AppDetailQueryService
{
    private readonly IDbContextFactory<PChabitDbContext> _dbFactory;

    public AppDetailQueryService(IDbContextFactory<PChabitDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <summary>构建应用详情报告（后台线程执行，透传引擎）。</summary>
    public Task<AppDetailReport> BuildReportAsync(
        AnalyticsPeriod period,
        string processName,
        string appName,
        string category,
        string categoryColorHex)
        => Task.Run(() => AppDetailEngine.BuildAsync(
            _dbFactory, period, processName, appName, category, categoryColorHex));
}
