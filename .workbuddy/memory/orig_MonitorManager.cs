using PChabit.Core.Interfaces;

namespace PChabit.Infrastructure.Services;

public class MonitorManager
{
    private readonly IAppMonitor _appMonitor;
    private readonly IKeyboardMonitor _keyboardMonitor;
    private readonly IMouseMonitor _mouseMonitor;
    private readonly IWebMonitor _webMonitor;
    private readonly WebSocketServer _webSocketServer;
    
    public bool IsRunning { get; private set; }
    public bool WebMonitoringEnabled { get; set; } = true;
    
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
        
        IsRunning = true;
    }
    
    public async Task StopAllAsync()
    {
        if (!IsRunning) return;
        
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
}
