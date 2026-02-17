using System.Collections.Concurrent;
using System.Text.Json;
using Serilog;
using PChabit.Core.Entities;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Services;

namespace PChabit.Infrastructure.Monitoring;

public class WebMonitor : IWebMonitor, IDisposable
{
    private readonly WebSocketServer _webSocketServer;
    private readonly ConcurrentDictionary<string, WebSession> _activeSessions;
    private readonly ConcurrentDictionary<string, DateTime> _lastActivity;
    private bool _isRunning;
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    
    public bool IsRunning => _isRunning;
    
    public event EventHandler<WebActivityEventArgs>? WebActivityReceived;
    public event EventHandler<MonitorStatusEventArgs>? StatusChanged;
    public event EventHandler<WebClientDisconnectedEventArgs>? ClientDisconnected;
    
    public WebMonitor(WebSocketServer webSocketServer)
    {
        _webSocketServer = webSocketServer;
        _activeSessions = new ConcurrentDictionary<string, WebSession>();
        _lastActivity = new ConcurrentDictionary<string, DateTime>();
        
        _webSocketServer.MessageReceived += OnWebSocketMessageReceived;
        _webSocketServer.ClientConnected += OnClientConnected;
        _webSocketServer.ClientDisconnected += OnClientDisconnected;
        
        Log.Information("WebMonitor 已初始化，WebSocketServer ID: {Id}", webSocketServer.GetHashCode());
    }
    
    public void Start()
    {
        if (_isRunning) return;
        
        _isRunning = true;
        StatusChanged?.Invoke(this, new MonitorStatusEventArgs(true, "Web 监控已启动"));
        Log.Information("Web 监控已启动");
    }
    
    public void Stop()
    {
        if (!_isRunning) return;
        
        _isRunning = false;
        _activeSessions.Clear();
        _lastActivity.Clear();
        
        StatusChanged?.Invoke(this, new MonitorStatusEventArgs(false, "Web 监控已停止"));
        Log.Information("Web 监控已停止");
    }
    
    private void OnWebSocketMessageReceived(object? sender, WebSocketMessageEventArgs e)
    {
        try
        {
            Log.Debug("收到浏览器消息: {Message}", e.Message);
            var message = JsonSerializer.Deserialize<BrowserExtensionMessage>(e.Message, JsonOptions);
            if (message == null)
            {
                Log.Warning("浏览器消息反序列化为 null");
                return;
            }
            
            Log.Debug("解析后: Type={Type}, Url={Url}, Browser={Browser}, TabId={TabId}", 
                message.Type, message.Url, message.Browser, message.TabId);
            
            ProcessMessage(message, e.ClientId);
        }
        catch (JsonException ex)
        {
            Log.Warning(ex, "解析浏览器消息失败: {Message}", e.Message);
        }
    }
    
    private void ProcessMessage(BrowserExtensionMessage message, string clientId)
    {
        if (message.Type == "connection")
        {
            Log.Debug("浏览器连接确认: {Browser}", message.Browser);
            return;
        }
        
        if (string.IsNullOrEmpty(message.Url))
        {
            Log.Debug("忽略无 URL 的消息: Type={Type}", message.Type);
            return;
        }
        
        var metadata = new Dictionary<string, object>();
        
        if (message.Percentage.HasValue)
            metadata["percentage"] = message.Percentage.Value;
        
        if (message.Element != null)
            metadata["element"] = message.Element;
        
        if (!string.IsNullOrEmpty(message.Query))
            metadata["query"] = message.Query;
        
        if (!string.IsNullOrEmpty(message.Engine))
            metadata["engine"] = message.Engine;
        
        if (message.Coordinates != null)
            metadata["coordinates"] = message.Coordinates;
        
        var args = new WebActivityEventArgs
        {
            Url = message.Url ?? "",
            Title = message.Title ?? "",
            Domain = ExtractDomain(message.Url),
            Browser = message.Browser ?? "Unknown",
            ClientId = clientId,
            TabId = message.TabId ?? 0,
            ActivityType = ParseActivityType(message.Type),
            Timestamp = message.Timestamp ?? DateTime.Now,
            Metadata = metadata
        };
        
        WebActivityReceived?.Invoke(this, args);
        
        UpdateSession(message, clientId);
        
        Log.Debug("Web 活动: {Browser} - {Domain} - {Type}", args.Browser, args.Domain, args.ActivityType);
    }
    
    private void UpdateSession(BrowserExtensionMessage message, string clientId)
    {
        if (string.IsNullOrEmpty(message.Url)) return;
        
        var sessionKey = $"{clientId}_{message.TabId}";
        var now = DateTime.Now;
        
        if (message.Type == "pageView" || message.Type == "tabActivate")
        {
            var session = new WebSession
            {
                Url = message.Url,
                Title = message.Title ?? "",
                Domain = ExtractDomain(message.Url),
                Browser = message.Browser ?? "Unknown",
                StartTime = now,
                TabId = message.TabId ?? 0
            };
            
            _activeSessions[sessionKey] = session;
        }
        else if (message.Type == "pageClose" || message.Type == "tabClose")
        {
            _activeSessions.TryRemove(sessionKey, out _);
        }
        
        _lastActivity[sessionKey] = now;
    }
    
    private void OnClientConnected(object? sender, WebSocketClientEventArgs e)
    {
        Log.Information("浏览器扩展已连接: {ClientId}", e.ClientId);
    }
    
    private void OnClientDisconnected(object? sender, WebSocketClientEventArgs e)
    {
        var keysToRemove = _activeSessions.Keys
            .Where(k => k.StartsWith(e.ClientId))
            .ToList();
        
        foreach (var key in keysToRemove)
        {
            _activeSessions.TryRemove(key, out _);
            _lastActivity.TryRemove(key, out _);
        }
        
        ClientDisconnected?.Invoke(this, new WebClientDisconnectedEventArgs { ClientId = e.ClientId });
        
        Log.Information("浏览器扩展已断开: {ClientId}", e.ClientId);
    }
    
    public IReadOnlyList<WebSession> GetCurrentSessions()
    {
        return _activeSessions.Values.ToList().AsReadOnly();
    }
    
    public Task<IEnumerable<WebSession>> GetSessionsAsync(DateTime startTime, DateTime endTime)
    {
        var sessions = _activeSessions.Values
            .Where(s => s.StartTime >= startTime && s.StartTime <= endTime);
        
        return Task.FromResult(sessions);
    }
    
    private static string ExtractDomain(string? url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        
        try
        {
            var uri = new Uri(url);
            return uri.Host;
        }
        catch
        {
            return "";
        }
    }
    
    private static WebActivityType ParseActivityType(string? type)
    {
        return type?.ToLower() switch
        {
            "pageview" => WebActivityType.PageView,
            "pageclose" => WebActivityType.PageClose,
            "tabswitch" => WebActivityType.TabSwitch,
            "tabactivate" => WebActivityType.TabSwitch,
            "tabclose" => WebActivityType.TabClose,
            "search" => WebActivityType.Search,
            "click" => WebActivityType.Click,
            "scroll" => WebActivityType.Scroll,
            "formsubmit" => WebActivityType.FormSubmit,
            "navigation" => WebActivityType.Navigation,
            _ => WebActivityType.PageView
        };
    }
    
    public void Dispose()
    {
        _webSocketServer.MessageReceived -= OnWebSocketMessageReceived;
        _webSocketServer.ClientConnected -= OnClientConnected;
        _webSocketServer.ClientDisconnected -= OnClientDisconnected;
        
        _activeSessions.Clear();
        _lastActivity.Clear();
    }
}

internal class BrowserExtensionMessage
{
    public string? Type { get; set; }
    public string? Url { get; set; }
    public string? Title { get; set; }
    public string? Browser { get; set; }
    public int? TabId { get; set; }
    public DateTime? Timestamp { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
    
    public int? Percentage { get; set; }
    public string? Direction { get; set; }
    public Dictionary<string, object>? Element { get; set; }
    public Dictionary<string, int>? Coordinates { get; set; }
    public string? Query { get; set; }
    public string? Engine { get; set; }
}
