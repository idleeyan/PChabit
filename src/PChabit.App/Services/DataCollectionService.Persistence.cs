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
        foreach (var kvp in _activeWebSessions.ToList())
        {
            var session = kvp.Value;
            session.EndTime = DateTime.Now;
            session.Duration = session.EndTime.Value - session.StartTime;
            session.IsActiveTab = false;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<PChabitDbContext>();
                dbContext.WebSessions.Add(session);
                dbContext.SaveChangesAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "保存网页会话失败");
            }
        }
        _activeWebSessions.Clear();
    }

    private async Task SaveActiveWebSessionsPeriodicallyAsync()
    {
        List<WebSession> sessionsToSave;

        lock (_lock)
        {
            sessionsToSave = _activeWebSessions.Values
                .Select(session =>
                {
                    var snapshot = new WebSession
                    {
                        Id = Guid.NewGuid(),
                        Url = session.Url,
                        Title = session.Title,
                        Domain = session.Domain,
                        Browser = session.Browser,
                        TabId = session.TabId,
                        StartTime = session.StartTime,
                        EndTime = DateTime.Now,
                        Duration = DateTime.Now - session.StartTime,
                        ScrollDepth = session.ScrollDepth,
                        ClickCount = session.ClickCount,
                        HasFormInteraction = session.HasFormInteraction,
                        InteractedElements = new List<string>(session.InteractedElements),
                        IsActiveTab = session.IsActiveTab
                    };

                    // 重置活跃会话起始时间
                    session.StartTime = DateTime.Now;
                    session.ScrollDepth = 0;
                    session.ClickCount = 0;
                    session.HasFormInteraction = false;
                    session.InteractedElements = new List<string>();

                    return snapshot;
                })
                .ToList();
        }

        if (sessionsToSave.Count == 0) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<PChabitDbContext>();

            foreach (var session in sessionsToSave)
            {
                dbContext.WebSessions.Add(session);
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

