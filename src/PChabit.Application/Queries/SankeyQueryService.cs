using Microsoft.EntityFrameworkCore;
using PChabit.Application.Aggregators;
using PChabit.Core.Entities;
using PChabit.Infrastructure.Analysis;
using PChabit.Infrastructure.Data;

namespace PChabit.Application.Queries;

/// <summary>桑基图查询结果：聚合数据 + 统一分类解析所需的两级映射（陷阱 43 口径）。</summary>
public sealed record SankeyQueryResult(
    SankeyData Data,
    IReadOnlyDictionary<string, (string Name, string ColorHex)> CategoryMap,
    IReadOnlyDictionary<string, string> SnapshotMap);

/// <summary>
/// 桑基图查询服务（3.74.0 从 <c>SankeyViewModel</c> 收口，改造计划阶段 E3-1）。
/// VM 不再注入 <c>IDbContextFactory</c>；分类映射 + 会话快照分类 + 聚合
/// 三步合一，保证与应用排行同口径（快照分类映射不可漏，陷阱 43）。
/// </summary>
public sealed class SankeyQueryService
{
    private readonly IDbContextFactory<PChabitDbContext> _dbFactory;
    private readonly SankeyAggregator _aggregator;

    public SankeyQueryService(
        IDbContextFactory<PChabitDbContext> dbFactory,
        SankeyAggregator aggregator)
    {
        _dbFactory = dbFactory;
        _aggregator = aggregator;
    }

    /// <summary>按日期范围与 TopN 加载桑基数据（含分类映射）。</summary>
    public async Task<SankeyQueryResult> LoadAsync(DateTime startDate, DateTime endDate, int topN)
    {
        var categoryMap = await AppStatsEngine.LoadCategoryMapAsync(_dbFactory);
        // 3.45.0：分类解析统一走 AppStatsEngine 唯一入口，需同时取会话快照分类，
        // 否则「历史记录库无映射的进程」在本页会显示「未分类」，与应用排行不一致（陷阱 43）。
        var snapshotMap = await AppStatsEngine.LoadSnapshotCategoryMapAsync(_dbFactory, startDate, endDate);
        var data = await _aggregator.GetSankeyDataAsync(startDate, endDate, topN);
        return new SankeyQueryResult(data, categoryMap, snapshotMap);
    }
}
