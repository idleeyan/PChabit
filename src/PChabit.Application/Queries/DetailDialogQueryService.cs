using Microsoft.EntityFrameworkCore;
using PChabit.Core.Entities;
using PChabit.Infrastructure.Data;

namespace PChabit.Application.Queries;

/// <summary>
/// 详情弹窗查询服务（3.77.0 从 <c>DetailDialogViewModel</c> 收口，改造计划阶段 E3-4）。
/// VM 不再注入 <c>IDbContextFactory</c>；按键/鼠标/网页三个查询各自带
/// 容错降级（空列表 + Log.Warning），行为与原 VM 逐字一致；聚合与展示留 VM。
/// </summary>
public sealed class DetailDialogQueryService
{
    private readonly IDbContextFactory<PChabitDbContext> _dbFactory;

    public DetailDialogQueryService(IDbContextFactory<PChabitDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <summary>加载某日的键盘会话（查询失败降级空列表）。</summary>
    public async Task<List<KeyboardSession>> LoadKeyboardSessionsAsync(DateTime date)
    {
        await using var dbContext = await _dbFactory.CreateDbContextAsync();
        try
        {
            return await dbContext.KeyboardSessions
                .Where(s => s.Date >= date && s.Date < date.AddDays(1))
                .ToListAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "加载 KeyboardSessions 失败");
            return new List<KeyboardSession>();
        }
    }

    /// <summary>加载某日的鼠标会话（查询失败降级空列表）。</summary>
    public async Task<List<MouseSession>> LoadMouseSessionsAsync(DateTime date)
    {
        await using var dbContext = await _dbFactory.CreateDbContextAsync();
        try
        {
            return await dbContext.MouseSessions
                .Where(s => s.Date >= date && s.Date < date.AddDays(1))
                .ToListAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "加载 MouseSessions 失败");
            return new List<MouseSession>();
        }
    }

    /// <summary>加载某日的网页会话（按开始时间倒序；查询失败降级空列表）。</summary>
    public async Task<List<WebSession>> LoadWebSessionsAsync(DateTime date)
    {
        var tomorrow = date.AddDays(1);
        await using var dbContext = await _dbFactory.CreateDbContextAsync();
        try
        {
            return await dbContext.WebSessions
                .AsNoTracking()
                .Where(s => s.StartTime >= date && s.StartTime < tomorrow)
                .OrderByDescending(s => s.StartTime)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "加载 WebSessions 失败");
            return new List<WebSession>();
        }
    }
}
