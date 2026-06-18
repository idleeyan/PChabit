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

public class DataCollectionService : IDisposable
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
                var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

                foreach (var op in batch)
                {
                    try
                    {
                        await op.ExecuteAsync(unitOfWork);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "处理数据操作失败: {Description}", op.Description);
                    }
                }

                await unitOfWork.SaveChangesAsync();
                Interlocked.Add(ref _operationCount, batch.Count);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "批量处理数据操作失败，批次大小: {BatchSize}", batch.Count);
            }

            batch.Clear();
        }
    }

    private void EnqueueOperation(Func<IUnitOfWork, Task> operation, string description)
    {
        if (!_dataChannel.Writer.TryWrite(new DataOperation(operation, description)))
        {
            Log.Warning("无法写入数据通道: {Description}", description);
        }
    }

    private void OnAppDataCollected(object? sender, AppActiveEventArgs e)
    {
        lock (_lock)
        {
            var isBackgroundApp = _backgroundAppSettings.IsBackgroundApp(e.ProcessName);

            if (isBackgroundApp)
            {
                if (!_backgroundSessions.ContainsKey(e.ProcessName))
                {
                    _backgroundSessions[e.ProcessName] = new AppSession
                    {
                        Id = Guid.NewGuid(),
                        ProcessName = e.ProcessName,
                        WindowTitle = e.WindowTitle,
                        ExecutablePath = e.ExecutablePath,
                        StartTime = e.Timestamp,
                        AppName = e.AppName,
                        Category = e.Category
                    };
                }
                else
                {
                    var existingSession = _backgroundSessions[e.ProcessName];
                    if (existingSession.WindowTitle != e.WindowTitle)
                    {
                        EnqueueSaveBackgroundSession(e.ProcessName);
                        _backgroundSessions[e.ProcessName] = new AppSession
                        {
                            Id = Guid.NewGuid(),
                            ProcessName = e.ProcessName,
                            WindowTitle = e.WindowTitle,
                            ExecutablePath = e.ExecutablePath,
                            StartTime = e.Timestamp,
                            AppName = e.AppName,
                            Category = e.Category
                        };
                    }
                }
            }

            if (_currentProcessName != e.ProcessName &&
                _backgroundAppSettings.IsBackgroundApp(_currentProcessName))
            {
                EnqueueSaveBackgroundSession(_currentProcessName);
            }
            else
            {
                EnqueueSaveCurrentSession();
            }

            _currentProcessName = e.ProcessName;

            if (!isBackgroundApp)
            {
                _currentAppSession = new AppSession
                {
                    Id = Guid.NewGuid(),
                    ProcessName = e.ProcessName,
                    WindowTitle = e.WindowTitle,
                    ExecutablePath = e.ExecutablePath,
                    StartTime = e.Timestamp,
                    AppName = e.AppName,
                    Category = e.Category
                };
            }
            else
            {
                _currentAppSession = null;
            }
        }
    }

    private void OnWindowTitleChanged(object? sender, WindowTitleChangedEventArgs e)
    {
        lock (_lock)
        {
            if (_backgroundAppSettings.IsBackgroundApp(e.ProcessName))
            {
                if (_backgroundSessions.TryGetValue(e.ProcessName, out var session))
                {
                    EnqueueSaveBackgroundSession(e.ProcessName);
                    _backgroundSessions[e.ProcessName] = new AppSession
                    {
                        Id = Guid.NewGuid(),
                        ProcessName = e.ProcessName,
                        WindowTitle = e.WindowTitle,
                        StartTime = e.Timestamp,
                        AppName = session.AppName,
                        Category = session.Category
                    };
                }
            }
            else if (_currentAppSession != null && _currentAppSession.ProcessName == e.ProcessName)
            {
                EnqueueSaveCurrentSession();
                _currentAppSession = new AppSession
                {
                    Id = Guid.NewGuid(),
                    ProcessName = e.ProcessName,
                    WindowTitle = e.WindowTitle,
                    StartTime = e.Timestamp,
                    AppName = _currentAppSession.AppName,
                    Category = _currentAppSession.Category
                };
            }
        }
    }

    private readonly Dictionary<(DateTime Date, int Hour), (DateTime startTime, int keyCount)> _typingBursts = new();
    private const int TypingBurstThresholdMs = 1000;

    private void OnKeyboardDataCollected(object? sender, KeyboardEventArgs e)
    {
        EnqueueOperation(async unitOfWork =>
        {
            var date = e.Timestamp.Date;
            var hour = e.Timestamp.Hour;
            var processName = e.ActiveProcess ?? _currentProcessName;

            var session = await unitOfWork.KeyboardSessions.GetByDateAndHourAsync(date, hour);
            if (session == null)
            {
                session = new KeyboardSession
                {
                    Id = Guid.NewGuid(),
                    Date = date,
                    Hour = hour,
                    TotalKeyPresses = 0,
                    ProcessName = processName,
                    KeyFrequency = new Dictionary<int, int>(),
                    KeyCategoryFrequency = new Dictionary<string, int>()
                };
                await unitOfWork.KeyboardSessions.AddAsync(session);
            }

            session.TotalKeyPresses += e.KeyCount;

            if (!string.IsNullOrEmpty(processName) && string.IsNullOrEmpty(session.ProcessName))
            {
                session.ProcessName = processName;
            }

            if (e.KeyCode > 0)
            {
                var freq = new Dictionary<int, int>(session.KeyFrequency);
                freq[e.KeyCode] = freq.GetValueOrDefault(e.KeyCode, 0) + e.KeyCount;
                session.KeyFrequency = freq;
            }

            var category = GetKeyCategory(e.KeyCode, e.KeyName);
            if (!string.IsNullOrEmpty(category))
            {
                var catFreq = new Dictionary<string, int>(session.KeyCategoryFrequency);
                catFreq[category] = catFreq.GetValueOrDefault(category, 0) + e.KeyCount;
                session.KeyCategoryFrequency = catFreq;
            }

            if (e.KeyCode == 0x08)
                session.BackspaceCount += e.KeyCount;
            else if (e.KeyCode == 0x2E)
                session.DeleteCount += e.KeyCount;

            if (e.IsCtrlPressed && e.KeyCode == 0x5A) // Ctrl+Z = Undo
                session.UndoCount += e.KeyCount;

            if (e.IsShortcut && !string.IsNullOrEmpty(e.KeyName))
            {
                var shortcut = e.GetShortcutString();
                var shortcuts = new List<ShortcutUsage>(session.Shortcuts?.Count > 0 ? session.Shortcuts : Enumerable.Empty<ShortcutUsage>())
                {
                    new ShortcutUsage
                    {
                        Timestamp = e.Timestamp,
                        Shortcut = shortcut,
                        Application = processName
                    }
                };
                session.Shortcuts = shortcuts;
            }

            UpdateTypingSpeed(session, e);
        }, $"键盘数据 {e.Timestamp:HH:mm}");
    }

    private void UpdateTypingSpeed(KeyboardSession session, KeyboardEventArgs e)
    {
        var now = e.Timestamp;
        var burstKey = (now.Date, session.Hour);

        if (!_typingBursts.TryGetValue(burstKey, out var burst))
        {
            // 清理超过 2 小时前的旧条目，防止内存泄漏
            var cutoff = now.AddHours(-2);
            var staleKeys = _typingBursts.Keys.Where(k => k.Date < cutoff.Date || (k.Date == cutoff.Date && k.Hour < cutoff.Hour)).ToList();
            foreach (var key in staleKeys)
                _typingBursts.Remove(key);

            burst = (now, 0);
        }

        var timeSinceLastKey = (now - burst.startTime).TotalMilliseconds;

        if (timeSinceLastKey > TypingBurstThresholdMs && burst.keyCount > 0)
        {
            var duration = now - burst.startTime;
            var kpm = burst.keyCount > 0 && duration.TotalMinutes > 0
                ? burst.keyCount / duration.TotalMinutes
                : 0;

            var existingBursts = session.TypingBursts ?? Enumerable.Empty<TypingBurst>();
            session.TypingBursts = new List<TypingBurst>(existingBursts)
            {
                new TypingBurst
                {
                    StartTime = burst.startTime,
                    Duration = duration,
                    KeyCount = burst.keyCount
                }
            };

            if (kpm > session.PeakTypingSpeed)
            {
                session.PeakTypingSpeed = kpm;
            }

            var totalBursts = session.TypingBursts.Count;
            if (totalBursts > 0)
            {
                session.AverageTypingSpeed = session.TypingBursts.Average(b => b.KeyCount / Math.Max(b.Duration.TotalMinutes, 0.001));
            }

            burst = (now, 0);
        }

        burst.keyCount += e.KeyCount;
        _typingBursts[burstKey] = burst;
    }

    private static string GetKeyCategory(int keyCode, string? keyName)
    {
        if (keyCode >= 0x41 && keyCode <= 0x5A) return "字母";
        if (keyCode >= 0x30 && keyCode <= 0x39) return "数字";
        if (keyCode >= 0x70 && keyCode <= 0x87) return "功能键";
        if (keyCode == 0x20) return "空格";
        if (keyCode == 0x0D) return "回车";
        if (keyCode == 0x08) return "退格";
        if (keyCode == 0x2E) return "删除";
        if (keyCode == 0x09) return "Tab";
        if (keyCode == 0x1B) return "Esc";
        if (keyCode >= 0x25 && keyCode <= 0x28) return "方向键";
        return "其他";
    }

    private void OnMouseClick(object? sender, MouseClickEventArgs e)
    {
        EnqueueOperation(async unitOfWork =>
        {
            var date = e.Timestamp.Date;
            var hour = e.Timestamp.Hour;

            var session = await unitOfWork.MouseSessions.GetByDateAndHourAsync(date, hour);
            if (session == null)
            {
                session = new MouseSession
                {
                    Id = Guid.NewGuid(),
                    Date = date,
                    Hour = hour,
                    ProcessName = _currentProcessName,
                    LeftClickCount = 0,
                    RightClickCount = 0,
                    MiddleClickCount = 0,
                    ScrollCount = 0,
                    TotalMoveDistance = 0
                };
                await unitOfWork.MouseSessions.AddAsync(session);
            }

            switch (e.Button)
            {
                case MouseButtonType.Left:
                    session.LeftClickCount++;
                    break;
                case MouseButtonType.Right:
                    session.RightClickCount++;
                    break;
                case MouseButtonType.Middle:
                    session.MiddleClickCount++;
                    break;
            }
        }, $"鼠标点击 {e.Button}");
    }

    private void OnMouseMove(object? sender, MouseMoveEventArgs e)
    {
        if (e.Distance < 1) return;

        EnqueueOperation(async unitOfWork =>
        {
            var date = e.Timestamp.Date;
            var hour = e.Timestamp.Hour;

            var session = await unitOfWork.MouseSessions.GetByDateAndHourAsync(date, hour);
            if (session == null)
            {
                session = new MouseSession
                {
                    Id = Guid.NewGuid(),
                    Date = date,
                    Hour = hour,
                    ProcessName = _currentProcessName,
                    LeftClickCount = 0,
                    RightClickCount = 0,
                    MiddleClickCount = 0,
                    ScrollCount = 0,
                    TotalMoveDistance = 0
                };
                await unitOfWork.MouseSessions.AddAsync(session);
            }

            session.TotalMoveDistance += e.Distance;
        }, "鼠标移动");
    }

    private void OnMouseScroll(object? sender, MouseScrollEventArgs e)
    {
        EnqueueOperation(async unitOfWork =>
        {
            var date = e.Timestamp.Date;
            var hour = e.Timestamp.Hour;

            var session = await unitOfWork.MouseSessions.GetByDateAndHourAsync(date, hour);
            if (session == null)
            {
                session = new MouseSession
                {
                    Id = Guid.NewGuid(),
                    Date = date,
                    Hour = hour,
                    ProcessName = _currentProcessName,
                    LeftClickCount = 0,
                    RightClickCount = 0,
                    MiddleClickCount = 0,
                    ScrollCount = 0,
                    TotalMoveDistance = 0
                };
                await unitOfWork.MouseSessions.AddAsync(session);
            }

            session.ScrollCount++;
        }, "鼠标滚动");
    }

    private void OnWebActivityReceived(object? sender, WebActivityEventArgs e)
    {
        var sessionKey = $"{e.ClientId}_{e.TabId}";

        switch (e.ActivityType)
        {
            case WebActivityType.PageView:
            case WebActivityType.TabSwitch:
                HandlePageView(e, sessionKey);
                break;

            case WebActivityType.PageClose:
            case WebActivityType.TabClose:
                HandlePageClose(sessionKey);
                break;

            case WebActivityType.Scroll:
                HandleScroll(e, sessionKey);
                break;

            case WebActivityType.Click:
                HandleClick(e, sessionKey);
                break;

            case WebActivityType.FormSubmit:
                HandleFormSubmit(e, sessionKey);
                break;

            case WebActivityType.Search:
                HandleSearch(e, sessionKey);
                break;
        }
    }

    private void HandlePageView(WebActivityEventArgs e, string sessionKey)
    {
        lock (_lock)
        {
            if (_activeWebSessions.TryGetValue(sessionKey, out var existingSession))
            {
                existingSession.EndTime = DateTime.Now;
                existingSession.Duration = existingSession.EndTime.Value - existingSession.StartTime;
                EnqueueSaveWebSession(existingSession);
            }

            var timestamp = e.Timestamp;
            if (timestamp.Kind == DateTimeKind.Utc)
            {
                timestamp = timestamp.ToLocalTime();
            }

            var webSession = new WebSession
            {
                Id = Guid.NewGuid(),
                Url = e.Url,
                Title = e.Title,
                Domain = e.Domain,
                Browser = e.Browser,
                TabId = e.TabId,
                StartTime = timestamp,
                ScrollDepth = 0,
                ClickCount = 0,
                HasFormInteraction = false,
                IsActiveTab = true
            };

            _activeWebSessions[sessionKey] = webSession;
        }
    }

    private void HandlePageClose(string sessionKey)
    {
        lock (_lock)
        {
            if (_activeWebSessions.TryGetValue(sessionKey, out var session))
            {
                _activeWebSessions.Remove(sessionKey);
                session.EndTime = DateTime.Now;
                session.Duration = session.EndTime.Value - session.StartTime;
                session.IsActiveTab = false;
                EnqueueSaveWebSession(session);
            }
        }
    }

    private void HandleScroll(WebActivityEventArgs e, string sessionKey)
    {
        lock (_lock)
        {
            if (_activeWebSessions.TryGetValue(sessionKey, out var session))
            {
                if (e.Metadata.TryGetValue("percentage", out var percentageObj) && percentageObj is int percentage)
                {
                    session.ScrollDepth = Math.Max(session.ScrollDepth, percentage);
                }
            }
        }
    }

    private void HandleClick(WebActivityEventArgs e, string sessionKey)
    {
        lock (_lock)
        {
            if (_activeWebSessions.TryGetValue(sessionKey, out var session))
            {
                session.ClickCount++;

                if (e.Metadata.TryGetValue("element", out var elementObj) && elementObj is Dictionary<string, object> element)
                {
                    if (element.TryGetValue("tag", out var tagObj) && tagObj is string tag)
                    {
                        session.InteractedElements.Add($"{tag}:{DateTime.Now:HH:mm:ss}");
                    }
                }
            }
        }
    }

    private void HandleFormSubmit(WebActivityEventArgs e, string sessionKey)
    {
        lock (_lock)
        {
            if (_activeWebSessions.TryGetValue(sessionKey, out var session))
            {
                session.HasFormInteraction = true;
            }
        }
    }

    private void HandleSearch(WebActivityEventArgs e, string sessionKey)
    {
        lock (_lock)
        {
            if (_activeWebSessions.TryGetValue(sessionKey, out var session))
            {
                if (e.Metadata.TryGetValue("query", out var queryObj) && queryObj is string query)
                {
                    session.SearchQueries.Add(query);
                }
            }
        }
    }

    private void OnWebClientDisconnected(object? sender, WebClientDisconnectedEventArgs e)
    {
        Log.Information("浏览器断开连接，保存相关网页会话: {ClientId}", e.ClientId);

        lock (_lock)
        {
            var keysToSave = _activeWebSessions.Keys
                .Where(k => k.StartsWith(e.ClientId))
                .ToList();

            foreach (var key in keysToSave)
            {
                if (_activeWebSessions.TryGetValue(key, out var session))
                {
                    _activeWebSessions.Remove(key);
                    session.EndTime = DateTime.Now;
                    session.Duration = session.EndTime.Value - session.StartTime;
                    session.IsActiveTab = false;
                    EnqueueSaveWebSession(session);
                }
            }
        }
    }

    private void EnqueueSaveCurrentSession()
    {
        if (_currentAppSession == null) return;

        var session = _currentAppSession;
        session.EndTime = DateTime.Now;
        session.Duration = session.EndTime.Value - session.StartTime;

        EnqueueOperation(async unitOfWork =>
        {
            await unitOfWork.AppSessions.AddAsync(session);
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

        EnqueueOperation(async unitOfWork =>
        {
            await unitOfWork.AppSessions.AddAsync(sessionToSave);
        }, $"保存后台应用会话 {processName}");
    }

    private void EnqueueSaveWebSession(WebSession session)
    {
        EnqueueOperation(async unitOfWork =>
        {
            await unitOfWork.WebSessions.AddAsync(session);
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
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            unitOfWork.AppSessions.AddAsync(_currentAppSession).GetAwaiter().GetResult();
            unitOfWork.SaveChangesAsync().GetAwaiter().GetResult();
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
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            foreach (var (_, session) in sessionsToSave)
            {
                await unitOfWork.AppSessions.AddAsync(session);
            }

            await unitOfWork.SaveChangesAsync();
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
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            unitOfWork.AppSessions.AddAsync(session).GetAwaiter().GetResult();
            unitOfWork.SaveChangesAsync().GetAwaiter().GetResult();
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
                var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                unitOfWork.WebSessions.AddAsync(session).GetAwaiter().GetResult();
                unitOfWork.SaveChangesAsync().GetAwaiter().GetResult();
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
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            foreach (var session in sessionsToSave)
            {
                await unitOfWork.WebSessions.AddAsync(session);
            }

            await unitOfWork.SaveChangesAsync();
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

    private async Task CheckDailyAggregationAsync()
    {
        // 每小时至少检查一次
        if ((DateTime.Now - _lastAggregationCheck).TotalMinutes < 60)
            return;

        _lastAggregationCheck = DateTime.Now;

        try
        {
            var yesterday = DateTime.Today.AddDays(-1);
            var dateKey = yesterday.ToString("yyyy-MM-dd");

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<PChabitDbContext>();

            var exists = await dbContext.DailySummaries
                .AnyAsync(s => s.Date == dateKey);

            if (!exists)
            {
                await AggregateDailySummaryAsync(dbContext, yesterday, dateKey);

                // 聚合后进行数据清理
                await CleanupOldDataAsync(dbContext);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "每日聚合检查失败");
        }
    }

    private static async Task AggregateDailySummaryAsync(PChabitDbContext dbContext, DateTime date, string dateKey)
    {
        var nextDay = date.AddDays(1);

        // 从 KeyboardSession 聚合
        var keyboardSessions = await dbContext.KeyboardSessions
            .AsNoTracking()
            .Where(s => s.Date >= date && s.Date < nextDay)
            .ToListAsync();

        var totalKeys = keyboardSessions.Sum(s => (long)s.TotalKeyPresses);

        // 每小时按键分布 (24 小时)
        var hourlyKeys = new int[24];
        foreach (var ks in keyboardSessions)
        {
            if (ks.Hour >= 0 && ks.Hour < 24)
                hourlyKeys[ks.Hour] += ks.TotalKeyPresses;
        }

        // 从 MouseSession 聚合
        var mouseSessions = await dbContext.MouseSessions
            .AsNoTracking()
            .Where(s => s.Date >= date && s.Date < nextDay)
            .ToListAsync();

        var totalMouseClicks = mouseSessions.Sum(s =>
            (long)(s.LeftClickCount + s.RightClickCount + s.MiddleClickCount));

        // 从 AppSession 聚合 TopApps（按总时长排序取前 10）
        var appSessions = await dbContext.AppSessions
            .AsNoTracking()
            .Where(s => s.StartTime >= date && s.StartTime < nextDay)
            .ToListAsync();

        var topApps = appSessions
            .GroupBy(s => s.ProcessName)
            .Select(g => new { Name = g.Key, Minutes = g.Sum(s =>
                s.EndTime.HasValue ? (s.EndTime!.Value - s.StartTime).TotalMinutes : 0) })
            .OrderByDescending(x => x.Minutes)
            .Take(10)
            .Select(x => new { x.Name, x.Minutes })
            .ToList();

        var activeMinutes = appSessions
            .Where(s => s.EndTime.HasValue)
            .Sum(s => (s.EndTime!.Value - s.StartTime).TotalMinutes);

        var topAppsJson = System.Text.Json.JsonSerializer.Serialize(topApps);
        var hourlyKeysJson = System.Text.Json.JsonSerializer.Serialize(hourlyKeys);

        // Upsert DailySummary
        var summary = await dbContext.DailySummaries
            .FirstOrDefaultAsync(s => s.Date == dateKey);

        if (summary == null)
        {
            summary = new DailySummary
            {
                Id = Guid.NewGuid(),
                Date = dateKey,
                TotalKeys = totalKeys,
                TotalMouseClicks = totalMouseClicks,
                ActiveMinutes = activeMinutes,
                TopApps = topAppsJson,
                HourlyKeyDistribution = hourlyKeysJson,
                LastUpdated = DateTime.Now
            };
            await dbContext.DailySummaries.AddAsync(summary);
        }
        else
        {
            summary.TotalKeys = totalKeys;
            summary.TotalMouseClicks = totalMouseClicks;
            summary.ActiveMinutes = activeMinutes;
            summary.TopApps = topAppsJson;
            summary.HourlyKeyDistribution = hourlyKeysJson;
            summary.LastUpdated = DateTime.Now;
        }

        await dbContext.SaveChangesAsync();

        Log.Information("每日聚合完成: {Date}, Keys={TotalKeys}, Clicks={TotalClicks}, ActiveMin={ActiveMin:F1}, TopApps={TopAppCount}",
            dateKey, totalKeys, totalMouseClicks, activeMinutes, topApps.Count);
    }

    private static async Task CleanupOldDataAsync(PChabitDbContext dbContext, int retentionDays = 90)
    {
        // 尝试从设置读取保留天数
        try
        {
            var settings = dbContext.Set<DailySummary>().AsNoTracking().FirstOrDefault();
            // 默认使用 90 天，实际从 ISettingsService 读取
        }
        catch { /* 忽略 */ }

        var cutoff = DateTime.Today.AddDays(-retentionDays);
        var cutoffStr = cutoff.ToString("yyyy-MM-dd");
        var deletedCount = 0;

        Log.Information("开始数据清理，截止日期: {Cutoff}，保留 {Days} 天", cutoffStr, retentionDays);

        // 清理前确保对应日期的 DailySummary 已存在
        // 清理 KeyboardSession
        var oldKeySessions = await dbContext.KeyboardSessions
            .Where(s => s.Date < cutoff)
            .CountAsync();

        if (oldKeySessions > 0)
        {
            await dbContext.KeyboardSessions
                .Where(s => s.Date < cutoff)
                .ExecuteDeleteAsync();
            deletedCount += oldKeySessions;
        }

        // 清理 MouseSession
        var oldMouseSessions = await dbContext.MouseSessions
            .Where(s => s.Date < cutoff)
            .CountAsync();

        if (oldMouseSessions > 0)
        {
            await dbContext.MouseSessions
                .Where(s => s.Date < cutoff)
                .ExecuteDeleteAsync();
            deletedCount += oldMouseSessions;
        }

        // 清理 WebSession
        var oldWebSessions = await dbContext.WebSessions
            .Where(s => s.StartTime < cutoff)
            .CountAsync();

        if (oldWebSessions > 0)
        {
            await dbContext.WebSessions
                .Where(s => s.StartTime < cutoff)
                .ExecuteDeleteAsync();
            deletedCount += oldWebSessions;
        }

        // 清理 AppSession
        var oldAppSessions = await dbContext.AppSessions
            .Where(s => s.StartTime < cutoff)
            .CountAsync();

        if (oldAppSessions > 0)
        {
            await dbContext.AppSessions
                .Where(s => s.StartTime < cutoff)
                .ExecuteDeleteAsync();
            deletedCount += oldAppSessions;
        }

        if (deletedCount > 0)
        {
            await dbContext.SaveChangesAsync();
            Log.Information("数据清理完成，共删除 {Count} 条 {Days} 天前的记录", deletedCount, retentionDays);
        }
        else
        {
            Log.Information("无过期数据需要清理（保留 {Days} 天）", retentionDays);
        }
    }

    private record DataOperation(Func<IUnitOfWork, Task> ExecuteAsync, string Description);
}
