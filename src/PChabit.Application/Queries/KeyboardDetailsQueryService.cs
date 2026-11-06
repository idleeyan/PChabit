using Microsoft.EntityFrameworkCore;
using PChabit.Core.Entities;
using PChabit.Infrastructure.Data;

namespace PChabit.Application.Queries;

/// <summary>键鼠详情某日查询结果：键盘小时会话 + 鼠标小时会话。</summary>
public sealed record KeyboardDetailsDayResult(
    List<KeyboardSession> KeySessions,
    List<MouseSession> MouseSessions);

/// <summary>
/// 键鼠详情页查询服务（3.76.0 从 <c>KeyboardDetailsViewModel</c> 收口，改造计划阶段 E3-3）。
/// VM 不再注入 <c>IDbContextFactory</c>；只提供当日两表原始行，
/// 键位聚合/快捷键 Top/每小时统计等计算留在 VM（消费其内嵌 stats 模型）。
/// </summary>
public sealed class KeyboardDetailsQueryService
{
    private readonly IDbContextFactory<PChabitDbContext> _dbContextFactory;

    public KeyboardDetailsQueryService(IDbContextFactory<PChabitDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    /// <summary>加载某日的键盘会话与鼠标会话（小时粒度行）。</summary>
    public async Task<KeyboardDetailsDayResult> LoadAsync(DateTime selectedDate)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

        var keySessions = await dbContext.KeyboardSessions
            .AsNoTracking()
            .Where(s => s.Date >= selectedDate && s.Date < selectedDate.AddDays(1))
            .ToListAsync();

        Serilog.Log.Information("键盘数据加载: 日期={Date}, 会话数={Count}, 总按键={TotalKeys}",
            selectedDate.ToString("yyyy-MM-dd"), keySessions.Count, keySessions.Sum(s => s.TotalKeyPresses));

        var mouseSessions = await dbContext.MouseSessions
            .AsNoTracking()
            .Where(s => s.Date >= selectedDate && s.Date < selectedDate.AddDays(1))
            .ToListAsync();

        Serilog.Log.Information("鼠标数据加载: 日期={Date}, 会话数={Count}, 总点击={TotalClicks}",
            selectedDate.ToString("yyyy-MM-dd"), mouseSessions.Sum(s => s.LeftClickCount + s.RightClickCount + s.MiddleClickCount));

        return new KeyboardDetailsDayResult(keySessions, mouseSessions);
    }
}
