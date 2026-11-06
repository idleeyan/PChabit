using Microsoft.EntityFrameworkCore;
using PChabit.Core.Entities;
using PChabit.Infrastructure.Analysis;
using PChabit.Infrastructure.Data;

namespace PChabit.Application.Queries;

/// <summary>流量页查询结果：分钟样本 + 进程小时行 + 统一分类映射。</summary>
public sealed record NetworkTrafficQueryResult(
    List<NetworkTrafficSample> Samples,
    List<ProcessNetworkUsage> ProcessRows,
    IReadOnlyDictionary<string, (string Name, string ColorHex)> CategoryMap);

/// <summary>
/// 流量页查询服务（3.75.0 从 <c>NetworkTrafficViewModel</c> 收口，改造计划阶段 E3-2）。
/// VM 不再注入 <c>IDbContextFactory</c>；样本与进程行两查询 + 分类映射三合一，
/// 分类口径复用 <see cref="AppStatsEngine"/>（映射加载失败时降级为空映射，
/// 行为与原 VM 逐字一致）。
/// </summary>
public sealed class NetworkTrafficQueryService
{
    private readonly IDbContextFactory<PChabitDbContext> _dbFactory;

    public NetworkTrafficQueryService(IDbContextFactory<PChabitDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <summary>按日期范围（yyyy-MM-dd 字符串，含起止）加载流量数据与分类映射。</summary>
    public async Task<NetworkTrafficQueryResult> LoadAsync(string fromDate, string toDateExclusive, string toDateInclusive)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var samples = await db.NetworkTrafficSamples.AsNoTracking()
            .Where(x => string.Compare(x.Timestamp, fromDate) >= 0 && string.Compare(x.Timestamp, toDateExclusive) < 0)
            .ToListAsync();

        var procRows = await db.ProcessNetworkUsages.AsNoTracking()
            .Where(x => string.Compare(x.Date, fromDate) >= 0 && string.Compare(x.Date, toDateInclusive) <= 0)
            .ToListAsync();

        // 分类映射复用 AppStatsEngine（设置-应用分类，进程名 → 分类名/颜色）
        IReadOnlyDictionary<string, (string Name, string ColorHex)> categoryMap;
        try
        {
            categoryMap = await AppStatsEngine.LoadCategoryMapAsync(_dbFactory);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "加载应用分类映射失败，分类聚合按未分类处理");
            categoryMap = new Dictionary<string, (string Name, string ColorHex)>();
        }

        return new NetworkTrafficQueryResult(samples, procRows, categoryMap);
    }
}
