using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Tai.Core.Entities;
using Tai.Core.Interfaces;
using Tai.Infrastructure.Data;
using Tai.Infrastructure.Monitoring;

namespace Tai.App.Services;

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
    private int _operationCount;
    
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
        _appMonitor.OnDataCollected += OnAppDataCollected;
        _appMonitor.OnWindowTitleChanged += OnWindowTitleChanged;
        _keyboardMonitor.OnDataCollected += OnKeyboardDataCollected;
        _mouseMonitor.OnMouseClick += OnMouseClick;
        _mouseMonitor.OnMouseMove += OnMouseMove;
        _mouseMonitor.OnMouseScroll += OnMouseScroll;
        _webMonitor.WebActivityReceived += OnWebActivityReceived;
        _webMonitor.ClientDisconnected += OnWebClientDisconnected;
        
        _processingTask = ProcessDataAsync(_cts.Token);
        
        Log.Information("数据收集服务已启动 - AppMonitor: {AppRunning}, KeyboardMonitor: {KeyboardRunning}, MouseMonitor: {MouseRunning}", 
            _appMonitor.IsRunning, _keyboardMonitor.IsRunning, _mouseMonitor.IsRunning);
    }
    
    public void Stop()
    {
        _appMonitor.OnDataCollected -= OnAppDataCollected;
        _appMonitor.OnWindowTitleChanged -= OnWindowTitleChanged;
        _keyboardMonitor.OnDataCollected -= OnKeyboardDataCollected;
        _mouseMonitor.OnMouseClick -= OnMouseClick;
        _mouseMonitor.OnMouseMove -= OnMouseMove;
        _mouseMonitor.OnMouseScroll -= OnMouseScroll;
        _webMonitor.WebActivityReceived -= OnWebActivityReceived;
        _webMonitor.ClientDisconnected -= OnWebClientDisconnected;
        
        _cts.Cancel();
        _dataChannel.Writer.Complete();
        
        _processingTask?.Wait(TimeSpan.FromSeconds(5));
        
        SaveCurrentSessionImmediate();
        SaveAllBackgroundSessionsImmediate();
        SaveAllWebSessionsImmediate();
        
        Log.Information("数据收集服务已停止 - 共处理 {Count} 个操作", _operationCount);
    }
    
    private async Task ProcessDataAsync(CancellationToken cancellationToken)
    {
        Log.Information("数据处理任务已启动");
        
        await Task.Run(async () =>
        {
            await foreach (var operation in _dataChannel.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                    
                    await operation.ExecuteAsync(unitOfWork);
                    var savedCount = await unitOfWork.SaveChangesAsync();
                    
                    Interlocked.Increment(ref _operationCount);
                    
                    Log.Information("数据已保存: {Description}, 影响行数: {SavedCount}", operation.Description, savedCount);
                    
                    if (_operationCount % 10 == 0)
                    {
                        Log.Debug("已处理 {Count} 个数据操作", _operationCount);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "处理数据操作失败: {Description}", operation.Description);
                }
            }
        }, cancellationToken);
        
        Log.Information("数据处理任务已结束");
    }
    
    private void EnqueueOperation(Func<IUnitOfWork, Task> operation, string description)
    {
        if (!_dataChannel.Writer.TryWrite(new DataOperation(operation, description)))
        {
            Log.Warning("无法写入数据通道: {Description}", description);
        }
        else
        {
            Log.Debug("入队操作: {Description}", description);
        }
    }
    
    private void OnAppDataCollected(object? sender, AppActiveEventArgs e)
    {
        Log.Debug("收到应用切换事件: {ProcessName} - {WindowTitle}", e.ProcessName, e.WindowTitle);
        
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
                    Log.Debug("后台应用开始追踪: {ProcessName}", e.ProcessName);
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
                Log.Debug("从后台应用切换，保存会话: {ProcessName}", _currentProcessName);
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
                Log.Debug("创建新应用会话: {ProcessName}", e.ProcessName);
            }
            else
            {
                _currentAppSession = null;
            }
            
            Log.Debug("应用切换: {ProcessName} - {WindowTitle} (后台模式: {IsBackground})", 
                e.ProcessName, e.WindowTitle, isBackgroundApp);
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
    
    private readonly Dictionary<int, (DateTime startTime, int keyCount)> _typingBursts = new();
    private const int TypingBurstThresholdMs = 1000;
    
    private void OnKeyboardDataCollected(object? sender, KeyboardEventArgs e)
    {
        Log.Information("KeyboardData: 收到键盘事件 KeyCode={KeyCode}, KeyName={KeyName}, KeyCount={KeyCount}", 
            e.KeyCode, e.KeyName, e.KeyCount);
        
        EnqueueOperation(async unitOfWork =>
        {
            var date = e.Timestamp.Date;
            var hour = e.Timestamp.Hour;
            var processName = e.ActiveProcess ?? _currentProcessName;
            
            Log.Information("KeyboardData: 查询会话 Date={Date}, Hour={Hour}", date, hour);
            
            var session = await unitOfWork.KeyboardSessions.GetByDateAndHourAsync(date, hour);
            if (session == null)
            {
                Log.Information("KeyboardData: 创建新会话");
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
            else
            {
                Log.Information("KeyboardData: 找到现有会话, 当前KeyFrequency={KeyFrequency}", 
                    System.Text.Json.JsonSerializer.Serialize(session.KeyFrequency));
            }
            
            session.TotalKeyPresses += e.KeyCount;
            Log.Information("KeyboardData: 更新总按键数 TotalKeyPresses={Total}", session.TotalKeyPresses);
            
            if (!string.IsNullOrEmpty(processName) && string.IsNullOrEmpty(session.ProcessName))
            {
                session.ProcessName = processName;
            }
            
            if (e.KeyCode > 0)
            {
                if (session.KeyFrequency.ContainsKey(e.KeyCode))
                    session.KeyFrequency[e.KeyCode] += e.KeyCount;
                else
                    session.KeyFrequency[e.KeyCode] = e.KeyCount;
                
                Log.Information("KeyboardData: 更新按键频率 KeyCode={KeyCode}, Count={Count}, KeyFrequency={KeyFrequency}", 
                    e.KeyCode, session.KeyFrequency[e.KeyCode], 
                    System.Text.Json.JsonSerializer.Serialize(session.KeyFrequency));
            }
            
            var category = GetKeyCategory(e.KeyCode, e.KeyName);
            if (!string.IsNullOrEmpty(category))
            {
                if (session.KeyCategoryFrequency.ContainsKey(category))
                    session.KeyCategoryFrequency[category] += e.KeyCount;
                else
                    session.KeyCategoryFrequency[category] = e.KeyCount;
            }
            
            if (e.KeyCode == 0x08)
                session.BackspaceCount += e.KeyCount;
            else if (e.KeyCode == 0x2E)
                session.DeleteCount += e.KeyCount;
            
            Log.Information("KeyboardData: KeyCode={KeyCode}, KeyName={KeyName}, IsCtrl={IsCtrl}, IsAlt={IsAlt}, IsShift={IsShift}, IsShortcut={IsShortcut}", 
                e.KeyCode, e.KeyName, e.IsCtrlPressed, e.IsAltPressed, e.IsShiftPressed, e.IsShortcut);
            
            if (e.IsShortcut && !string.IsNullOrEmpty(e.KeyName))
            {
                var shortcut = e.GetShortcutString();
                Log.Information("KeyboardData: 检测到快捷键 {Shortcut}", shortcut);
                session.Shortcuts ??= new List<ShortcutUsage>();
                session.Shortcuts.Add(new ShortcutUsage
                {
                    Timestamp = e.Timestamp,
                    Shortcut = shortcut,
                    Application = processName
                });
                Log.Information("KeyboardData: 快捷键已添加, 当前快捷键数量={Count}", session.Shortcuts.Count);
            }
            
            UpdateTypingSpeed(session, e);
            
            Log.Information("KeyboardData: 会话状态 KeyFrequency={KeyFrequency}", 
                System.Text.Json.JsonSerializer.Serialize(session.KeyFrequency));
        }, $"键盘数据 {e.Timestamp:HH:mm}");
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
    
    private void UpdateTypingSpeed(KeyboardSession session, KeyboardEventArgs e)
    {
        var now = e.Timestamp;
        
        if (!_typingBursts.TryGetValue(session.Hour, out var burst))
        {
            burst = (now, 0);
        }
        
        var timeSinceLastKey = (now - burst.startTime).TotalMilliseconds;
        
        if (timeSinceLastKey > TypingBurstThresholdMs && burst.keyCount > 0)
        {
            var duration = now - burst.startTime;
            var kpm = burst.keyCount > 0 && duration.TotalMinutes > 0 
                ? burst.keyCount / duration.TotalMinutes 
                : 0;
            
            session.TypingBursts ??= new List<TypingBurst>();
            session.TypingBursts.Add(new TypingBurst
            {
                StartTime = burst.startTime,
                Duration = duration,
                KeyCount = burst.keyCount
            });
            
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
        _typingBursts[session.Hour] = burst;
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
            
            Log.Debug("网页访问: {Domain} - {Title}", e.Domain, e.Title);
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
                
                Log.Debug("网页关闭: {Domain} - {Title}, 时长: {Duration}", 
                    session.Domain, session.Title, session.Duration);
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
                    
                    Log.Debug("保存断开连接的网页会话: {Domain} - {Title}, 时长: {Duration}", 
                        session.Domain, session.Title, session.Duration);
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
        
        Log.Debug("入队保存应用会话: {ProcessName}, 时长: {Duration}", session.ProcessName, session.Duration);
        
        EnqueueOperation(async unitOfWork =>
        {
            await unitOfWork.AppSessions.AddAsync(session);
        }, $"保存应用会话 {session.ProcessName}");
        
        _currentAppSession = null;
    }
    
    private void EnqueueSaveBackgroundSession(string processName)
    {
        if (!_backgroundSessions.TryGetValue(processName, out var session)) return;
        
        _backgroundSessions.Remove(processName);
        
        session.EndTime = DateTime.Now;
        var duration = session.EndTime.Value - session.StartTime;
        
        var sessionToSave = new AppSession
        {
            Id = session.Id,
            ProcessName = session.ProcessName,
            WindowTitle = session.WindowTitle,
            ExecutablePath = session.ExecutablePath,
            StartTime = session.StartTime,
            EndTime = session.EndTime,
            Duration = duration,
            AppName = session.AppName,
            Category = session.Category
        };
        
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
            
            Log.Debug("保存应用会话: {ProcessName}, 时长: {Duration}", 
                _currentAppSession.ProcessName, 
                _currentAppSession.Duration);
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
            
            Log.Debug("保存后台应用会话: {ProcessName}, 时长: {Duration}", 
                session.ProcessName, 
                session.Duration);
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
    
    public void Dispose()
    {
        Stop();
        _cts.Dispose();
    }
    
    private record DataOperation(Func<IUnitOfWork, Task> ExecuteAsync, string Description);
}
