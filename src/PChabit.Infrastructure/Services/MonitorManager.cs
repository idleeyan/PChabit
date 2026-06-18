using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Monitoring;
using Serilog;

namespace PChabit.Infrastructure.Services;

public class MonitorManager : IDisposable
{
    private readonly IAppMonitor _appMonitor;
    private readonly IKeyboardMonitor _keyboardMonitor;
    private readonly IMouseMonitor _mouseMonitor;
    private readonly IWebMonitor _webMonitor;
    private readonly WebSocketServer _webSocketServer;

    public bool IsRunning { get; private set; }
    public bool WebMonitoringEnabled { get; set; } = true;

    private readonly System.Timers.Timer _healthCheckTimer;
    private readonly System.Timers.Timer _processSyncTimer;
    private int _consecutiveKeyboardFailures;
    private int _consecutiveMouseFailures;
    private const int MaxConsecutiveFailures = 3;
    private static readonly TimeSpan HookInactivityThreshold = TimeSpan.FromMinutes(5);

    /// <summary>
    /// UI 线程调度器。System.Timers.Timer 回调运行在线程池，而 WH_KEYBOARD_LL / WH_MOUSE_LL
    /// 低级钩子必须安装在有消息泵的线程上。重启钩子时必须通过此调度器派发到 UI 线程。
    /// 由 App.xaml.cs 在 UI 线程上设置: action => DispatcherQueue.TryEnqueue(() => action())
    /// </summary>
    public Action<Action>? UIDispatcher { get; set; }

    public MonitorManager(
        IAppMonitor appMonitor,
        IKeyboardMonitor keyboardMonitor,
        IMouseMonitor mouseMonitor,
        IWebMonitor webMonitor,
        WebSocketServer webSocketServer)
    {
        _appMonitor = appMonitor;
        _keyboardMonitor = keyboardMonitor;
        _mouseMonitor = mouseMonitor;
        _webMonitor = webMonitor;
        _webSocketServer = webSocketServer;

        _healthCheckTimer = new System.Timers.Timer(60000); // 每60秒检查一次
        _healthCheckTimer.Elapsed += OnHealthCheck;

        // 进程同步定时器：每秒将 AppMonitor 的当前进程同步到 KeyboardMonitor/MouseMonitor
        _processSyncTimer = new System.Timers.Timer(1000);
        _processSyncTimer.Elapsed += OnProcessSync;
    }

    private void OnProcessSync(object? sender, System.Timers.ElapsedEventArgs e)
    {
        try
        {
            if (!IsRunning) return;

            // 键盘钩子已在回调中直接获取前台进程，无需定时器同步
            // 仅同步鼠标钩子的进程归属
            var currentProcess = _appMonitor.GetCurrentProcess();
            _mouseMonitor.SetCurrentProcess(currentProcess);
        }
        catch
        {
            // 进程同步失败不应影响主流程
        }
    }

    private void OnHealthCheck(object? sender, System.Timers.ElapsedEventArgs e)
    {
        try
        {
            if (!IsRunning) return;

            var now = DateTime.Now;

            // 检查键盘钩子
            if (_keyboardMonitor.IsRunning)
            {
                var kbInactive = now - _keyboardMonitor.LastActivityTime;
                if (_keyboardMonitor.LastActivityTime != DateTime.MinValue && kbInactive > HookInactivityThreshold)
                {
                    _consecutiveKeyboardFailures++;
                    Log.Warning("键盘钩子已 {InactiveMinutes:F0} 分钟无活动 (第{Count}次检测)",
                        kbInactive.TotalMinutes, _consecutiveKeyboardFailures);

                    if (_consecutiveKeyboardFailures >= MaxConsecutiveFailures)
                    {
                        Log.Warning("键盘钩子连续 {Count} 次检测失效，尝试重启", _consecutiveKeyboardFailures);
                        RestartKeyboardMonitor();
                        _consecutiveKeyboardFailures = 0;
                    }
                }
                else
                {
                    _consecutiveKeyboardFailures = 0;
                }
            }
            else if (IsRunning)
            {
                // 钩子标记为未运行但 MonitorManager 还在运行，尝试重启
                Log.Warning("键盘钩子未运行，尝试重启");
                RestartKeyboardMonitor();
            }

            // 检查鼠标钩子
            if (_mouseMonitor.IsRunning)
            {
                var msInactive = now - _mouseMonitor.LastActivityTime;
                if (_mouseMonitor.LastActivityTime != DateTime.MinValue && msInactive > HookInactivityThreshold)
                {
                    _consecutiveMouseFailures++;
                    Log.Warning("鼠标钩子已 {InactiveMinutes:F0} 分钟无活动 (第{Count}次检测)",
                        msInactive.TotalMinutes, _consecutiveMouseFailures);

                    if (_consecutiveMouseFailures >= MaxConsecutiveFailures)
                    {
                        Log.Warning("鼠标钩子连续 {Count} 次检测失效，尝试重启", _consecutiveMouseFailures);
                        RestartMouseMonitor();
                        _consecutiveMouseFailures = 0;
                    }
                }
                else
                {
                    _consecutiveMouseFailures = 0;
                }
            }
            else if (IsRunning)
            {
                Log.Warning("鼠标钩子未运行，尝试重启");
                RestartMouseMonitor();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "健康检查异常");
        }
    }

    private void RestartKeyboardMonitor()
    {
        if (UIDispatcher != null)
        {
            UIDispatcher(DoRestartKeyboard);
        }
        else
        {
            DoRestartKeyboard();
        }
    }

    private void DoRestartKeyboard()
    {
        try
        {
            _keyboardMonitor.Stop();
            _keyboardMonitor.Start();
            Log.Information("键盘钩子重启完成，IsRunning={IsRunning}", _keyboardMonitor.IsRunning);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "重启键盘钩子失败");
        }
    }

    private void RestartMouseMonitor()
    {
        if (UIDispatcher != null)
        {
            UIDispatcher(DoRestartMouse);
        }
        else
        {
            DoRestartMouse();
        }
    }

    private void DoRestartMouse()
    {
        try
        {
            _mouseMonitor.Stop();
            _mouseMonitor.Start();
            Log.Information("鼠标钩子重启完成，IsRunning={IsRunning}", _mouseMonitor.IsRunning);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "重启鼠标钩子失败");
        }
    }
    
    public async Task StartAllAsync()
    {
        if (IsRunning) return;

        _appMonitor.Start();
        _keyboardMonitor.Start();
        _mouseMonitor.Start();

        if (WebMonitoringEnabled)
        {
            await _webSocketServer.StartAsync();
            _webMonitor.Start();
        }

        _healthCheckTimer.Start();
        _processSyncTimer.Start();
        IsRunning = true;
    }

    public async Task StopAllAsync()
    {
        if (!IsRunning) return;

        _healthCheckTimer.Stop();
        _processSyncTimer.Stop();

        _appMonitor.Stop();
        _keyboardMonitor.Stop();
        _mouseMonitor.Stop();

        if (WebMonitoringEnabled)
        {
            _webMonitor.Stop();
            await _webSocketServer.StopAsync();
        }

        IsRunning = false;
    }
    
    public void StartAppMonitor() => _appMonitor.Start();
    public void StopAppMonitor() => _appMonitor.Stop();
    
    public void StartKeyboardMonitor() => _keyboardMonitor.Start();
    public void StopKeyboardMonitor() => _keyboardMonitor.Stop();
    
    public void StartMouseMonitor() => _mouseMonitor.Start();
    public void StopMouseMonitor() => _mouseMonitor.Stop();
    
    public async Task StartWebMonitorAsync()
    {
        await _webSocketServer.StartAsync();
        _webMonitor.Start();
    }
    
    public async Task StopWebMonitorAsync()
    {
        _webMonitor.Stop();
        await _webSocketServer.StopAsync();
    }
    
    public int GetConnectedBrowserCount() => _webSocketServer.ClientCount;

    public void Dispose()
    {
        _healthCheckTimer?.Stop();
        _healthCheckTimer?.Dispose();
        _processSyncTimer?.Stop();
        _processSyncTimer?.Dispose();
        GC.SuppressFinalize(this);
    }
}
