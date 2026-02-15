using System.Collections.Concurrent;
using Serilog;

namespace Tai.Infrastructure.Services;

public interface IBackgroundTaskQueue
{
    void QueueBackgroundWorkItem(Func<CancellationToken, Task> workItem);
    Task<Func<CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken);
    int Count { get; }
}

public class BackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly ConcurrentQueue<Func<CancellationToken, Task>> _workItems = new();
    private readonly SemaphoreSlim _signal = new(0);
    
    public void QueueBackgroundWorkItem(Func<CancellationToken, Task> workItem)
    {
        if (workItem == null) throw new ArgumentNullException(nameof(workItem));
        
        _workItems.Enqueue(workItem);
        _signal.Release();
    }
    
    public async Task<Func<CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken)
    {
        await _signal.WaitAsync(cancellationToken);
        _workItems.TryDequeue(out var workItem);
        
        return workItem!;
    }
    
    public int Count => _workItems.Count;
}

public interface ITaskScheduler
{
    Task ScheduleAsync(string taskName, Func<Task> task, TimeSpan? delay = null);
    Task ScheduleRecurringAsync(string taskName, Func<Task> task, TimeSpan interval);
    void Cancel(string taskName);
    void CancelAll();
}

public class TaskScheduler : ITaskScheduler, IDisposable
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _tasks = new();
    
    public async Task ScheduleAsync(string taskName, Func<Task> task, TimeSpan? delay = null)
    {
        var cts = new CancellationTokenSource();
        _tasks[taskName] = cts;
        
        try
        {
            if (delay.HasValue)
            {
                await Task.Delay(delay.Value, cts.Token);
            }
            
            if (!cts.Token.IsCancellationRequested)
            {
                await task();
            }
        }
        catch (OperationCanceledException)
        {
            Log.Information("任务 {TaskName} 已取消", taskName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "任务 {TaskName} 执行失败", taskName);
        }
        finally
        {
            _tasks.TryRemove(taskName, out _);
            cts.Dispose();
        }
    }
    
    public async Task ScheduleRecurringAsync(string taskName, Func<Task> task, TimeSpan interval)
    {
        var cts = new CancellationTokenSource();
        _tasks[taskName] = cts;
        
        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                await Task.Delay(interval, cts.Token);
                
                if (!cts.Token.IsCancellationRequested)
                {
                    await task();
                }
            }
        }
        catch (OperationCanceledException)
        {
            Log.Information("周期任务 {TaskName} 已取消", taskName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "周期任务 {TaskName} 执行失败", taskName);
        }
        finally
        {
            _tasks.TryRemove(taskName, out _);
            cts.Dispose();
        }
    }
    
    public void Cancel(string taskName)
    {
        if (_tasks.TryGetValue(taskName, out var cts))
        {
            cts.Cancel();
        }
    }
    
    public void CancelAll()
    {
        foreach (var kvp in _tasks)
        {
            kvp.Value.Cancel();
        }
    }
    
    public void Dispose()
    {
        CancelAll();
        
        foreach (var kvp in _tasks)
        {
            kvp.Value.Dispose();
        }
        
        _tasks.Clear();
    }
}

public static class TaskSchedulerExtensions
{
    public static Task ScheduleDailyStatsAggregation(this ITaskScheduler scheduler, Func<Task> aggregateTask)
    {
        return scheduler.ScheduleRecurringAsync("daily_stats_aggregation", aggregateTask, TimeSpan.FromHours(1));
    }
    
    public static Task SchedulePatternDetection(this ITaskScheduler scheduler, Func<Task> detectTask)
    {
        return scheduler.ScheduleRecurringAsync("pattern_detection", detectTask, TimeSpan.FromMinutes(30));
    }
    
    public static Task ScheduleCacheRefresh(this ITaskScheduler scheduler, Func<Task> refreshTask)
    {
        return scheduler.ScheduleRecurringAsync("cache_refresh", refreshTask, TimeSpan.FromMinutes(15));
    }
}
