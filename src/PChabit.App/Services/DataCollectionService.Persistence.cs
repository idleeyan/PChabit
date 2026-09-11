using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using PChabit.Core.Entities;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Data;
using PChabit.Infrastructure.Monitoring;

namespace PChabit.App.Services;

public partial class DataCollectionService : IDisposable
{
    private void EnqueueSaveCurrentSession()
    {
        if (_currentAppSession == null) return;

        var session = _currentAppSession;
        session.EndTime = DateTime.Now;
        session.Duration = session.EndTime.Value - session.StartTime;

        EnqueueOperation(dbContext =>
        {
            dbContext.AppSessions.Add(session);
            return Task.CompletedTask;
        }, $"保存应用会话 {session.ProcessName}");

        _currentAppSession = null;
    }

    private void EnqueueSaveBackgroundSession(string processName)
    {
        if (!_backgroundSessions.TryGetValue(processName, out var session)) return;

        session.EndTime = DateTime.Now;
        var duration = session.EndTime.Value - session.StartTime;

        var sessionToSave = new AppSession
        {
            Id = Guid.NewGuid(),
            ProcessName = session.ProcessName,
            WindowTitle = session.WindowTitle,
            ExecutablePath = session.ExecutablePath,
            StartTime = session.StartTime,
            EndTime = session.EndTime,
            Duration = duration,
            AppName = session.AppName,
            Category = session.Category
        };

        session.ProcessName = processName;
        session.WindowTitle = "";
        session.StartTime = DateTime.Now;

        EnqueueOperation(dbContext =>
        {
            dbContext.AppSessions.Add(sessionToSave);
            return Task.CompletedTask;
        }, $"保存后台应用会话 {processName}");
    }

    private void EnqueueSaveWebSession(WebSession session)
    {
        EnqueueOperation(dbContext =>
        {
            dbContext.WebSessions.Add(session);
            return Task.CompletedTask;
        }, $"保存网页会话 {session.Domain}");
    }

    private void SaveCurrentSessionImmediate()
    {
        if (_currentAppSession == null) return;

        try
        {
            _currentAppSession.EndTime = DateTime.Now;
            _currentAppSession.Duration = _currentAppSession.EndTime.Value - _currentAppSession.StartTime;

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<PChabitDbContext>();
            dbContext.AppSessions.Add(_currentAppSession);
            dbContext.SaveChangesAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "保存应用会话失败");
        }
        finally
        {
            _currentAppSession = null;
        }
    }

    private void SaveAllBackgroundSessionsImmediate()
    {
        foreach (var processName in _backgroundSessions.Keys.ToList())
        {
            SaveBackgroundSessionImmediate(processName);
        }
        _backgroundSessions.Clear();
    }

    private async Task SaveBackgroundSessionsPeriodicallyAsync()
    {
        List<(string ProcessName, AppSession Session)> sessionsToSave;

        lock (_lock)
        {
            sessionsToSave = _backgroundSessions
                .Where(kvp => kvp.Value.StartTime < DateTime.Now)
                .Select(kvp =>
                {
                    var session = kvp.Value;
                    session.EndTime = DateTime.Now;
                    session.Duration = session.EndTime.Value - session.StartTime;

                    var sessionToSave = new AppSession
                    {
                        Id = Guid.NewGuid(),
                        ProcessName = session.ProcessName,
                        WindowTitle = session.WindowTitle,
                        ExecutablePath = session.ExecutablePath,
                        StartTime = session.StartTime,
                        EndTime = session.EndTime,
                        Duration = session.Duration,
                        AppName = session.AppName,
                        Category = session.Category
                    };

                    _backgroundSessions[kvp.Key] = new AppSession
                    {
                        Id = Guid.NewGuid(),
                        ProcessName = session.ProcessName,
                        WindowTitle = session.WindowTitle,
                        ExecutablePath = session.ExecutablePath,
                        StartTime = DateTime.Now,
                        AppName = session.AppName,
                        Category = session.Category
                    };

                    return (kvp.Key, sessionToSave);
                })
                .ToList();
        }

        if (sessionsToSave.Count == 0) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<PChabitDbContext>();

            foreach (var (_, session) in sessionsToSave)
            {
                dbContext.AppSessions.Add(session);
            }

            await dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "定期批量保存后台会话失败");
        }
    }

    private void SaveBackgroundSessionImmediate(string processName)
    {
        if (!_backgroundSessions.TryGetValue(processName, out var session)) return;

        try
        {
            session.EndTime = DateTime.Now;
            session.Duration = session.EndTime.Value - session.StartTime;

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<PChabitDbContext>();
            dbContext.AppSessions.Add(session);
            dbContext.SaveChangesAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "保存后台应用会话失败");
        }
    }

    private void SaveAllWebSessionsImmediate()
    {
        lock (_lock)
        {
            foreach (var kvp in _activeWebSessions.ToList())
            {
                var key = kvp.Key;
                var session = kvp.Value;
                var now = DateTime.Now;

                if (_webRuntime.TryGetValue(key, out var rt))
                {
                    if (rt.IsCurrentlyActive)
                    {
                        var delta = now - rt.LastActiveAt;
                        if (delta > TimeSpan.Zero) rt.AccumulatedActive += delta;
                    }
                    session.ActiveDuration = rt.AccumulatedActive;
                    var wall = now - session.StartTime;
                    session.IdleDuration = wall > session.ActiveDuration ? wall - session.ActiveDuration : TimeSpan.Zero;
                }

                session.EndTime = now;
                session.Duration = now - session.StartTime;
                session.IsActiveTab = false;
                if (session.ActiveDuration > session.Duration)
                {
                    session.ActiveDuration = session.Duration;
                }

                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<PChabitDbContext>();
                    var existing = dbContext.WebSessions.Find(session.Id);
                    if (existing == null)
                    {
                        dbContext.WebSessions.Add(session);
                    }
                    else
                    {
                        existing.EndTime = session.EndTime;
                        existing.Duration = session.Duration;
                        existing.ActiveDuration = session.ActiveDuration;
                        existing.IdleDuration = session.IdleDuration;
                        existing.ScrollDepth = session.ScrollDepth;
                        existing.ClickCount = session.ClickCount;
                        existing.HasFormInteraction = session.HasFormInteraction;
                        existing.IsActiveTab = false;
                    }
                    dbContext.SaveChangesAsync().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "保存网页会话失败");
                }
            }
            _activeWebSessions.Clear();
            _webRuntime.Clear();
        }
    }

    /// <summary>
    /// 周期检查点：为崩溃恢复把活跃会话 upsert 到库中。
    /// 保持同一 Session Id，不重置 StartTime / 计数，杜绝切片虚增。
    /// </summary>
    private async Task SaveActiveWebSessionsPeriodicallyAsync()
    {
        List<WebSession> snapshots;

        lock (_lock)
        {
            var now = DateTime.Now;
            snapshots = new List<WebSession>(_activeWebSessions.Count);

            foreach (var kvp in _activeWebSessions)
            {
                var key = kvp.Key;
                var session = kvp.Value;

                // 在运行时状态上累计当前活跃区间（不关闭会话）
                if (_webRuntime.TryGetValue(key, out var rt))
                {
                    if (rt.IsCurrentlyActive)
                    {
                        var delta = now - rt.LastActiveAt;
                        if (delta > TimeSpan.Zero)
                        {
                            rt.AccumulatedActive += delta;
                            rt.LastActiveAt = now;
                        }
                    }
                    session.ActiveDuration = rt.AccumulatedActive;
                    var wall = now - session.StartTime;
                    session.IdleDuration = wall > session.ActiveDuration ? wall - session.ActiveDuration : TimeSpan.Zero;
                }

                session.EndTime = now;
                session.Duration = now - session.StartTime;
                if (session.ActiveDuration > session.Duration)
                {
                    session.ActiveDuration = session.Duration;
                }

                snapshots.Add(session);
            }
        }

        if (snapshots.Count == 0) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<PChabitDbContext>();

            foreach (var session in snapshots)
            {
                var existing = await dbContext.WebSessions.FindAsync(session.Id);
                if (existing == null)
                {
                    dbContext.WebSessions.Add(session);
                    session.IsPersisted = true;
                }
                else
                {
                    existing.EndTime = session.EndTime;
                    existing.Duration = session.Duration;
                    existing.ActiveDuration = session.ActiveDuration;
                    existing.IdleDuration = session.IdleDuration;
                    existing.ScrollDepth = session.ScrollDepth;
                    existing.ClickCount = session.ClickCount;
                    existing.HasFormInteraction = session.HasFormInteraction;
                    existing.IsActiveTab = session.IsActiveTab;
                    existing.Title = session.Title;
                    existing.Favicon = session.Favicon;
                }
            }

            await dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "定期批量保存网页会话失败");
        }
    }

    private static async Task RunPeriodicTimerAsync(TimeSpan interval, CancellationToken cancellationToken, Func<Task> action, string errorMessage)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    await action();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, errorMessage);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

}

