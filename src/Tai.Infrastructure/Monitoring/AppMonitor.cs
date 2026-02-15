using System.Diagnostics;
using Tai.Core.Interfaces;
using Tai.Infrastructure.Helpers;
using Tai.Infrastructure.Services;

namespace Tai.Infrastructure.Monitoring;

public class AppMonitor : IAppMonitor
{
    public bool IsRunning { get; private set; }
    public event EventHandler<AppActiveEventArgs>? OnDataCollected;
    
    public event EventHandler<WindowTitleChangedEventArgs>? OnWindowTitleChanged;
    
    private IntPtr _foregroundHook;
    private IntPtr _titleChangeHook;
    private Win32Helper.WinEventDelegate? _foregroundDelegate;
    private Win32Helper.WinEventDelegate? _titleChangeDelegate;
    
    private readonly AppInfoResolver _appInfoResolver;
    private readonly AppCategoryResolver _categoryResolver;
    
    private AppInfo? _currentApp;
    private readonly object _lock = new();
    
    private readonly System.Threading.Channels.Channel<IntPtr> _windowChangeChannel;
    private readonly CancellationTokenSource _cts;
    private Task? _processingTask;
    
    public AppMonitor()
    {
        _appInfoResolver = new AppInfoResolver();
        _categoryResolver = new AppCategoryResolver();
        
        _windowChangeChannel = System.Threading.Channels.Channel.CreateUnbounded<IntPtr>(
            new System.Threading.Channels.UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
        _cts = new CancellationTokenSource();
    }
    
    public void Start()
    {
        if (IsRunning) return;
        
        _foregroundDelegate = ForegroundEventCallback;
        _titleChangeDelegate = TitleChangeEventCallback;
        
        _processingTask = ProcessWindowChangesAsync(_cts.Token);
        
        _foregroundHook = Win32Helper.SetWinEventHook(
            Win32Helper.EVENT_SYSTEM_FOREGROUND,
            Win32Helper.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _foregroundDelegate, 0, 0, Win32Helper.WINEVENT_OUTOFCONTEXT);
        
        _titleChangeHook = Win32Helper.SetWinEventHook(
            Win32Helper.EVENT_OBJECT_NAMECHANGE,
            Win32Helper.EVENT_OBJECT_NAMECHANGE,
            IntPtr.Zero, _titleChangeDelegate, 0, 0, Win32Helper.WINEVENT_OUTOFCONTEXT);
        
        if (_foregroundHook != IntPtr.Zero)
        {
            IsRunning = true;
            Task.Run(() => HandleForegroundWindow());
            Debug.WriteLine("AppMonitor 已启动");
        }
        else
        {
            Debug.WriteLine("AppMonitor 启动失败: 无法设置事件钩子");
        }
    }
    
    public void Stop()
    {
        if (!IsRunning) return;
        
        _cts.Cancel();
        _windowChangeChannel.Writer.TryComplete();
        
        _processingTask?.Wait(TimeSpan.FromSeconds(2));
        
        if (_foregroundHook != IntPtr.Zero)
        {
            Win32Helper.UnhookWinEvent(_foregroundHook);
            _foregroundHook = IntPtr.Zero;
        }
        
        if (_titleChangeHook != IntPtr.Zero)
        {
            Win32Helper.UnhookWinEvent(_titleChangeHook);
            _titleChangeHook = IntPtr.Zero;
        }
        
        IsRunning = false;
        Debug.WriteLine("AppMonitor 已停止");
    }
    
    private void ForegroundEventCallback(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, 
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (idObject != 0 || idChild != 0) return;
        
        _windowChangeChannel.Writer.TryWrite(hwnd);
    }
    
    private void TitleChangeEventCallback(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, 
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (idObject != 0 || idChild != 0) return;
        
        var foregroundWindow = Win32Helper.GetForegroundWindow();
        if (hwnd == foregroundWindow)
        {
            _windowChangeChannel.Writer.TryWrite(hwnd);
        }
    }
    
    private async Task ProcessWindowChangesAsync(CancellationToken cancellationToken)
    {
        await Task.Run(async () =>
        {
            await foreach (var hwnd in _windowChangeChannel.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    HandleWindow(hwnd);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"处理窗口变化失败: {ex.Message}");
                }
            }
        }, cancellationToken);
    }
    
    private void HandleForegroundWindow()
    {
        var hwnd = Win32Helper.GetForegroundWindow();
        if (hwnd != IntPtr.Zero)
        {
            HandleWindow(hwnd);
        }
    }
    
    private void HandleWindow(IntPtr hwnd)
    {
        AppInfo? appInfo;
        string categoryName;
        
        try
        {
            appInfo = _appInfoResolver.Resolve(hwnd);
            var category = _categoryResolver.Resolve(appInfo.ProcessName, appInfo.ExecutablePath);
            categoryName = _categoryResolver.GetCategoryName(category);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"解析窗口信息失败: {ex.Message}");
            return;
        }
        
        if (appInfo == null) return;
        
        lock (_lock)
        {
            bool isForegroundChange = _currentApp?.ProcessName != appInfo.ProcessName;
            
            if (isForegroundChange)
            {
                _currentApp = appInfo;
                
                var args = new AppActiveEventArgs(
                    appInfo.ProcessName,
                    appInfo.WindowTitle,
                    appInfo.ExecutablePath,
                    DateTime.Now)
                {
                    AppName = appInfo.AppName,
                    AppVersion = appInfo.AppVersion,
                    Publisher = appInfo.Publisher,
                    WindowClass = appInfo.WindowClass,
                    WindowX = appInfo.WindowX,
                    WindowY = appInfo.WindowY,
                    WindowWidth = appInfo.WindowWidth,
                    WindowHeight = appInfo.WindowHeight,
                    IsMaximized = appInfo.IsMaximized,
                    Category = categoryName
                };
                
                Debug.WriteLine($"应用切换: {appInfo.ProcessName} - {appInfo.WindowTitle}");
                OnDataCollected?.Invoke(this, args);
            }
            else if (_currentApp?.WindowTitle != appInfo.WindowTitle)
            {
                _currentApp = appInfo;
                Debug.WriteLine($"窗口标题变化: {appInfo.ProcessName} - {appInfo.WindowTitle}");
                OnWindowTitleChanged?.Invoke(this, new WindowTitleChangedEventArgs(
                    appInfo.ProcessName, appInfo.WindowTitle, DateTime.Now));
            }
        }
    }
}
