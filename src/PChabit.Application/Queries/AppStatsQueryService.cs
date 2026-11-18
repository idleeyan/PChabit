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

    /// <summary>
    /// 按周期/分类过滤/搜索词构造应用统计报告。
    /// 优先走 Rust 数据面（<see cref="RustStatsProvider"/>，P3 第一块），失败则回退 C# 引擎；
    /// 两条路径口径一致（归一化、分类优先级、专注判定、环比文案、饼图 Top8+其他）。
    /// </summary>
    public async Task<AppStatsReport> BuildReportAsync(
        AnalyticsPeriod period,
        string? categoryFilter,
        string? searchText)
    {
        var rust = await RustStatsProvider.TryBuildAsync(period, categoryFilter, searchText);
        if (rust != null) return rust;

        return await AppStatsEngine.BuildAsync(_dbFactory, period, categoryFilter, searchText);
    }
}
