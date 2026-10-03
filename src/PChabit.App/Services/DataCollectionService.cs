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
    private readonly IAppMonitor _appMonitor;
    private readonly IKeyboardMonitor _keyboardMonitor;
    private readonly IMouseMonitor _mouseMonitor;
    private readonly IWebMonitor _webMonitor;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBackgroundAppSettings _backgroundAppSettings;
    private readonly ISettingsService _settingsService;

    private AppSession? _currentAppSession;
    private string _currentProcessName = string.Empty;

    private readonly Dictionary<string, AppSession> _backgroundSessions = new();
    private readonly Dictionary<string, WebSession> _activeWebSessions = new();

    private readonly object _lock = new();

    private readonly Channel<DataOperation> _dataChannel;
    private readonly CancellationTokenSource _cts;
    private Task? _processingTask;
    private Task? _backgroundTimerTask;
    private int _operationCount;

    private const int FlushIntervalMs = 5000; // 保留字段名以兼容现有引用

    public DataCollectionService(
        IAppMonitor appMonitor,
        IKeyboardMonitor keyboardMonitor,
        IMouseMonitor mouseMonitor,
        IWebMonitor webMonitor,
        IServiceScopeFactory scopeFactory,
        IBackgroundAppSettings backgroundAppSettings,
        ISettingsService settingsService)
    {
        _appMonitor = appMonitor;
        _keyboardMonitor = keyboardMonitor;
        _mouseMonitor = mouseMonitor;
        _webMonitor = webMonitor;
        _scopeFactory = scopeFactory;
        _backgroundAppSettings = backgroundAppSettings;
        _settingsService = settingsService;

        _dataChannel = Channel.CreateUnbounded<DataOperation>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _cts = new CancellationTokenSource();
    }

    public void Start()
    {
        SubscribeEvents();

        _processingTask = ProcessDataAsync(_cts.Token);

        _backgroundTimerTask = RunPeriodicTimerAsync(
            TimeSpan.FromSeconds(15),
            _cts.Token,
            async () =>
            {
                // 键盘/鼠标内存累计每 15s 落库一次，避免钩子路径驱动高频 SQLite 写入
                FlushLiveInputSessions();
                await FlushBrowserHistoryQueueAsync();
                await SaveBackgroundSessionsPeriodicallyAsync();
                await SaveActiveWebSessionsPeriodicallyAsync();
                await CheckDailyAggregationAsync();
            },
            "后台会话定时保存失败");

        Log.Information("数据收集服务已启动 - AppMonitor: {AppRunning}, KeyboardMonitor: {KeyboardRunning}, MouseMonitor: {MouseRunning}",
            _appMonitor.IsRunning, _keyboardMonitor.IsRunning, _mouseMonitor.IsRunning);
    }

    public void Stop()
    {
        UnsubscribeEvents();

        try
        {
            _cts.Cancel();
            _dataChannel.Writer.Complete();
        }
        catch { }

        try
        {
            if (_processingTask != null && !_processingTask.Wait(TimeSpan.FromSeconds(1)))
            {
                Log.Warning("数据处理任务等待超时，强制继续关闭");
            }
        }
        catch { }

        try
        {
            if (_backgroundTimerTask != null && !_backgroundTimerTask.Wait(TimeSpan.FromSeconds(1)))
            {
                Log.Warning("后台定时器任务等待超时，强制继续关闭");
            }
        }
        catch { }

        try
        {
            FlushLiveInputSessions();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "落库键盘/鼠标会话失败");
        }

        try
        {
            FlushBrowserHistoryQueueAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "落库浏览历史队列失败");
        }

        try
        {
            SaveCurrentSessionImmediate();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "保存当前会话失败");
        }

        try
        {
            SaveAllBackgroundSessionsImmediate();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "保存后台会话失败");
        }

        try
        {
            SaveAllWebSessionsImmediate();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "保存网页会话失败");
        }

        Log.Information("数据收集服务已停止 - 共处理 {Count} 个操作", _operationCount);
    }

    private async Task ProcessDataAsync(CancellationToken cancellationToken)
    {
        const int batchSize = 50;
        var batch = new List<DataOperation>(batchSize);

        await foreach (var operation in _dataChannel.Reader.ReadAllAsync(cancellationToken))
        {
            batch.Add(operation);

            // 尝试填满批次
            while (batch.Count < batchSize && _dataChannel.Reader.TryRead(out var op))
            {
                batch.Add(op);
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<PChabitDbContext>();

                foreach (var op in batch)
                {
                    try
                    {
                        await op.ExecuteAsync(dbContext);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "处理数据操作失败: {Description}", op.Description);
                    }
                }

                await dbContext.SaveChangesAsync();
                Interlocked.Add(ref _operationCount, batch.Count);
            }
            catch (Exception ex)
            {
                // 整批失败：通常是某一条脏数据（如主键冲突）拖累。
                // 3.26.2 起逐条隔离重试——否则一个坏操作会让整批（最多 50 条）全部丢失。
                Log.Error(ex, "批量落库失败，改为逐条重试，批次大小: {BatchSize}", batch.Count);
                await RetryIndividuallyAsync(batch);
            }

            batch.Clear();
        }
    }

    /// <summary>
    /// 逐条重试：每条操作用独立的 DbContext，单条失败不影响其余。
    /// 批次内共用一个 DbContext 时，若两条操作动了同一行（例如同一网页会话的
    /// 首次入队与周期落库撞在同一批），EF 会在 SaveChanges 时抛 UNIQUE 约束冲突。
    /// 隔离后每条各自提交，冲突最多损失一条。
    /// </summary>
    private async Task RetryIndividuallyAsync(List<DataOperation> batch)
    {
        foreach (var op in batch)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<PChabitDbContext>();
                await op.ExecuteAsync(dbContext);
                await dbContext.SaveChangesAsync();
                Interlocked.Increment(ref _operationCount);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "单条重试仍失败，已丢弃: {Description}", op.Description);
            }
        }
    }

    private void EnqueueOperation(Func<PChabitDbContext, Task> operation, string description)
    {
        if (!_dataChannel.Writer.TryWrite(new DataOperation(operation, description)))
        {
            Log.Warning("无法写入数据通道: {Description}", description);
        }
    }

    private bool _eventsSubscribed;

    private void SubscribeEvents()
    {
        if (_eventsSubscribed) return;

        _appMonitor.OnDataCollected += OnAppDataCollected;
        _appMonitor.OnWindowTitleChanged += OnWindowTitleChanged;
        _keyboardMonitor.OnDataCollected += OnKeyboardDataCollected;
        _mouseMonitor.OnMouseClick += OnMouseClick;
        _mouseMonitor.OnMouseMove += OnMouseMove;
        _mouseMonitor.OnMouseScroll += OnMouseScroll;
        _webMonitor.WebActivityReceived += OnWebActivityReceived;
        _webMonitor.ClientDisconnected += OnWebClientDisconnected;

        _eventsSubscribed = true;
    }

    private void UnsubscribeEvents()
    {
        if (!_eventsSubscribed) return;

        _appMonitor.OnDataCollected -= OnAppDataCollected;
        _appMonitor.OnWindowTitleChanged -= OnWindowTitleChanged;
        _keyboardMonitor.OnDataCollected -= OnKeyboardDataCollected;
        _mouseMonitor.OnMouseClick -= OnMouseClick;
        _mouseMonitor.OnMouseMove -= OnMouseMove;
        _mouseMonitor.OnMouseScroll -= OnMouseScroll;
        _webMonitor.WebActivityReceived -= OnWebActivityReceived;
        _webMonitor.ClientDisconnected -= OnWebClientDisconnected;

        _eventsSubscribed = false;
    }

    public void Dispose()
    {
        UnsubscribeEvents();
        Stop();
        _cts.Dispose();
    }

    // === 每日聚合与数据清理 ===

    private DateTime _lastAggregationCheck = DateTime.MinValue;

    private record DataOperation(Func<PChabitDbContext, Task> ExecuteAsync, string Description);

}

