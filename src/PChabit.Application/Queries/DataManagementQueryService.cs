using Microsoft.EntityFrameworkCore;
using PChabit.Infrastructure.Data;

namespace PChabit.Application.Queries;

/// <summary>数据概览统计（四表记录数 + 最早/最新记录时间）。</summary>
public sealed record DataOverviewStats(
    int AppSessions,
    int KeySessions,
    int MouseSessions,
    int WebSessions,
    DateTime? Earliest,
    DateTime? Latest);

/// <summary>
/// 数据管理页查询服务（3.81.0 从 <c>DataManagementViewModel</c> 收口，
/// 改造计划阶段 E3-8 收官批）。VM 不再注入 <c>IDbContextFactory</c>；
/// 概览统计（四表 Count + Min/Max）在此产出，文件大小/备份列表等
/// I/O 与服务依赖保持原位。
/// </summary>
public sealed class DataManagementQueryService
{
    private readonly IDbContextFactory<PChabitDbContext> _dbFactory;

    public DataManagementQueryService(IDbContextFactory<PChabitDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <summary>加载四表记录数与最早/最新应用会话时间。</summary>
    public async Task<DataOverviewStats> LoadOverviewStatsAsync()
    {
        await using var dbContext = await _dbFactory.CreateDbContextAsync();

        var appCount = await dbContext.AppSessions.CountAsync();
        var keyCount = await dbContext.KeyboardSessions.CountAsync();
        var mouseCount = await dbContext.MouseSessions.CountAsync();
        var webCount = await dbContext.WebSessions.CountAsync();

        var earliest = await dbContext.AppSessions.MinAsync(s => (DateTime?)s.StartTime);
        var latest = await dbContext.AppSessions.MaxAsync(s => (DateTime?)s.StartTime);

        return new DataOverviewStats(appCount, keyCount, mouseCount, webCount, earliest, latest);
    }
}
