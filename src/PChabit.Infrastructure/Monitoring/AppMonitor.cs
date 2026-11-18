using System.Diagnostics;
using System.Text.Json;
using PChabit.Core.Interfaces;
using PChabit.Core.Runtime;
using PChabit.Infrastructure.Helpers;
using PChabit.Infrastructure.Services;
using Serilog;

namespace PChabit.Infrastructure.Monitoring;

public class AppMonitor : IAppMonitor
{
    public bool IsRunning { get; private set; }
    public DateTime LastActivityTime { get; private set; } = DateTime.MinValue;
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

    // ---- Rust 前台窗口监控（P2） ----
    private const string RustEnvVar = "PCHABIT_RUST_APPMON";
    private System.Threading.Timer? _rustTimer;
    private int _rustBusy;
    private string? _rustLastProc;
    private string? _rustLastTitle;
    
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

        // 优先走 Rust 内核：1 Hz 轮询前台窗口，钩子不再依赖本进程的消息循环
        if (TryStartRustPath()) return;
        
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
        }
    }
    
    public void Stop()
    {
        if (!IsRunning) return;

        _rustTimer?.Dispose();
        _rustTimer = null;
        
        _cts.Cancel();
        _windowChangeChannel.Writer.TryComplete();

        // 避免在输入钩子线程上长时间阻塞（StopAllAsync 会在钩子线程执行）
        try
        {
            _processingTask?.Wait(TimeSpan.FromMilliseconds(500));
        }
        catch (Exception ex)
        {
            // 预期：任务已取消（关闭期），Stop 继续执行
            Log.Debug(ex, "等待前台监控处理任务结束时出现异常");
        }
        
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
    }

    public string? GetCurrentProcess()
    {
        lock (_lock)
        {
            return _currentApp?.ProcessName;
        }
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
        await foreach (var hwnd in _windowChangeChannel.Reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                HandleWindow(hwnd);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"处理窗口变更失败: {ex.Message}");
            }
        }
    }
    
    private void HandleForegroundWindow()
    {
        var hwnd = Win32Helper.GetForegroundWindow();
        if (hwnd != IntPtr.Zero)
        {
            HandleWindow(hwnd);
        }
    }
    
    /// <summary>
    /// 启用 Rust 前台窗口监控：内核按 1 Hz 轮询 GetForegroundWindow，本类只做比对与派发，
    /// 事件语义与 WinEvent 钩子路径完全一致（进程名变化 → OnDataCollected，标题变化 → OnWindowTitleChanged）。
    /// </summary>
    private bool TryStartRustPath()
    {
        if (!RustEnabled) return false;
        if (DaemonPipe.Shared.Send("APP") == null)
        {
            Log.Warning("Rust 内核不可用，前台窗口监控回退到 WinEvent 钩子");
            return false;
        }

        _rustTimer = new System.Threading.Timer(_ => PollRust(), null, 1000, 1000);
        IsRunning = true;
        LastActivityTime = DateTime.Now;
        Log.Information("已启用 Rust 前台窗口监控（{Var}=1）", RustEnvVar);
        return true;
    }

    private static bool RustEnabled
    {
        get
        {
            var v = Environment.GetEnvironmentVariable(RustEnvVar);
            if (string.IsNullOrWhiteSpace(v)) return true;
            return !(v.Trim() == "0"
                     || v.Trim().Equals("false", StringComparison.OrdinalIgnoreCase)
                     || v.Trim().Equals("off", StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>拉一次快照；只有进程名或标题变化才触发解析与派发。</summary>
    private void PollRust()
    {
        if (Interlocked.Exchange(ref _rustBusy, 1) == 1) return;
        try
        {
            var reply = DaemonPipe.Shared.Send("APP");
            if (reply == null) return;

            using var doc = JsonDocument.Parse(reply);
            var root = doc.RootElement;
            if (!root.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True) return;

            var hwnd = new IntPtr(root.GetProperty("hwnd").GetInt64());
            var proc = root.TryGetProperty("proc", out var p) ? p.GetString() ?? "" : "";
            var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";

            if (proc == _rustLastProc && title == _rustLastTitle) return;

            _rustLastProc = proc;
            _rustLastTitle = title;
            LastActivityTime = DateTime.Now;
            HandleWindow(hwnd);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "解析 Rust 前台窗口快照失败，本拍跳过");
        }
        finally
        {
            Interlocked.Exchange(ref _rustBusy, 0);
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
