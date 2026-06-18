using Microsoft.EntityFrameworkCore;
using PChabit.Core.Entities;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Data;
using Serilog;

namespace PChabit.App.Services;

/// <summary>
/// 定时计算"今日已用时长 / 每日总目标"，调用 TrayService.UpdateProgress 刷新托盘图标。
///
/// 刷新策略：
///   - 默认 60s 一次（避免 IO/计算过频）
///   - 状态切换（运行→暂停/禁用）时立即刷新（由 TrayService 自身节流保证）
///   - 数据库读取失败时降级显示 Running + 0%
///
/// 设计动机：让托盘图标本身就是"今日进度"的实时仪表盘，用户无需打开主窗口。
///
/// 优化：使用后台 PeriodicTimer 替代 DispatcherQueueTimer，避免数据库查询阻塞 UI 线程。
/// </summary>
public class TrayProgressRefresher : IDisposable
{
    private readonly TrayService _trayService;
    private readonly IDbContextFactory<PChabitDbContext> _dbContextFactory;
    private readonly CancellationTokenSource _cts = new();
    private Task? _timerTask;
    private bool _disposed;

    /// <summary>刷新间隔。生产环境 60s；调试时可缩短。</summary>
    public static TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(60);

    public TrayProgressRefresher(
        TrayService trayService,
        IDbContextFactory<PChabitDbContext> dbContextFactory)
    {
        _trayService       = trayService;
        _dbContextFactory  = dbContextFactory;
    }

    public void Start()
    {
        if (_disposed) return;
        _timerTask = RunTimerAsync(_cts.Token);
        // 立即触发一次，避免用户看到 60s 空白环
        _ = RefreshAsync();
        Log.Information("托盘进度刷新器已启动，间隔 {Seconds}s", Interval.TotalSeconds);
    }

    public void Stop()
    {
        _cts.Cancel();
        try { _timerTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
    }

    public void ForceRefresh()
    {
        _trayService.ForceRefresh();
        _ = RefreshAsync();
    }

    private async Task RunTimerAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await RefreshAsync();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            var (progress, status) = await ComputeProgressAsync();
            _trayService.UpdateProgress(progress, status);
        }
        catch (Exception ex)
        {
            // 任何异常都不应影响主流程，仅记录
            Log.Warning(ex, "托盘进度刷新失败（已降级显示）");
            _trayService.UpdateProgress(0.0, TrayStatus.Running);
        }
    }

    /// <summary>
    /// 计算"今日已用时长 / 每日总目标"比例
    /// 优先级：
    ///   1) TotalTime 类型目标（综合目标）→ 全局最准
    ///   2) 各 Category 目标的最大占用
    ///   3) 没有任何目标 → 返回 Disabled
    /// </summary>
    private async Task<(double? progress, TrayStatus status)> ComputeProgressAsync()
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();
        var today = DateTime.Today;

        // 已用时长（按 AppSession 累计分钟数）
        var usedMinutes = await db.AppSessions
            .Where(s => s.StartTime >= today && s.StartTime < today.AddDays(1))
            .SumAsync(s => (double?)s.Duration.TotalMinutes) ?? 0.0;

        // 目标时长（取所有 TotalTime 类型目标的 TargetMinutes 总和；其他类型目标取最大值）
        var goalList = await db.UserGoals
            .Where(g => g.IsActive)
            .Select(g => new { g.TargetType, g.DailyTargetMinutes, g.DailyLimitMinutes })
            .ToListAsync();

        if (goalList.Count == 0)
        {
            // 没有目标 → 显示纯显示器（无进度环）
            return (null, TrayStatus.Running);
        }

        double? totalTargetMinutes = null;

        // 1. TotalTime 目标：作为综合目标
        var totalTimeGoals = goalList
            .Where(g => g.TargetType == nameof(GoalTargetType.TotalTime)
                        && g.DailyTargetMinutes.HasValue
                        && g.DailyTargetMinutes.Value > 0)
            .ToList();
        if (totalTimeGoals.Count > 0)
        {
            totalTargetMinutes = totalTimeGoals.Sum(g => g.DailyTargetMinutes!.Value);
        }

        // 2. 否则取各目标的 DailyLimit 最小值（最严格的目标）
        if (!totalTargetMinutes.HasValue)
        {
            var limits = goalList
                .Where(g => g.DailyLimitMinutes.HasValue && g.DailyLimitMinutes.Value > 0)
                .Select(g => (double)g.DailyLimitMinutes!.Value)
                .ToList();
            if (limits.Count > 0)
            {
                totalTargetMinutes = limits.Min();
            }
        }

        // 3. 再否则取 DailyTarget 最大值（最积极的目标）
        if (!totalTargetMinutes.HasValue)
        {
            var targets = goalList
                .Where(g => g.DailyTargetMinutes.HasValue && g.DailyTargetMinutes.Value > 0)
                .Select(g => (double)g.DailyTargetMinutes!.Value)
                .ToList();
            if (targets.Count > 0)
            {
                totalTargetMinutes = targets.Max();
            }
        }

        if (!totalTargetMinutes.HasValue || totalTargetMinutes.Value <= 0)
        {
            return (null, TrayStatus.Running);
        }

        var progress = Math.Min(usedMinutes / totalTargetMinutes.Value, 1.0);
        return (progress, TrayStatus.Running);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        try { _timerTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _cts.Dispose();
    }
}
