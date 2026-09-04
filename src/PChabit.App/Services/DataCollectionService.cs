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
        IBackgroundAppSettings backgroundAppSettings)
    {
        _appMonitor = appMonitor;
        _keyboardMonitor = keyboardMonitor;
        _mouseMonitor = mouseMonitor;
        _webMonitor = webMonitor;
        _scopeFactory = scopeFactory;
        _backgroundAppSettings = backgroundAppSettings;

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
            TimeSpan.FromSeconds(30),
            _cts.Token,
            async () =>
            {
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
                Log.Error(ex, "批量处理数据操作失败，批次大小: {BatchSize}", batch.Count);
            }

            batch.Clear();
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

