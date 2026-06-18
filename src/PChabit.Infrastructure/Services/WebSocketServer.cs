using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Serilog;

namespace PChabit.Infrastructure.Services;

public class WebSocketServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly List<WebSocket> _clients;
    private readonly CancellationTokenSource _cts;
    private bool _isRunning;
    private readonly object _lock = new();
    
    public int Port { get; }
    public bool IsRunning => _isRunning;
    public int ClientCount { get { lock (_lock) { return _clients.Count; } } }
    
    public event EventHandler<WebSocketMessageEventArgs>? MessageReceived;
    public event EventHandler<WebSocketClientEventArgs>? ClientConnected;
    public event EventHandler<WebSocketClientEventArgs>? ClientDisconnected;
    
    public WebSocketServer(int port = 8765)
    {
        Port = port;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{port}/");
        _clients = new List<WebSocket>();
        _cts = new CancellationTokenSource();
    }
    
    public Task StartAsync()
    {
        if (_isRunning) return Task.CompletedTask;
        
        try
        {
            _listener.Start();
            _isRunning = true;
            Log.Information("WebSocket 服务器启动在端口 {Port}", Port);
            
            _ = Task.Run(() => AcceptClientsAsync(_cts.Token));
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "启动 WebSocket 服务器失败");
            throw;
        }
    }
    
    public Task StopAsync()
    {
        if (!_isRunning) return Task.CompletedTask;
        
        _cts.Cancel();
        
        lock (_lock)
        {
            foreach (var client in _clients.ToList())
            {
                try
                {
                    client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server shutting down", CancellationToken.None).Wait();
                }
                catch { }
            }
            _clients.Clear();
        }
        
        _listener.Stop();
        _isRunning = false;
        Log.Information("WebSocket 服务器已停止");
        return Task.CompletedTask;
    }
    
    private async Task AcceptClientsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _isRunning)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                
                if (context.Request.IsWebSocketRequest)
                {
                    _ = Task.Run(() => HandleClientAsync(context, cancellationToken), cancellationToken);
                }
                else if (context.Request.HttpMethod == "GET")
                {
                    _ = Task.Run(() => ServeSettingsPage(context), cancellationToken);
                }
                else
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                }
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "接受客户端连接时发生错误");
            }
        }
    }
    
    private async Task HandleClientAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        WebSocket? webSocket = null;
        var clientId = Guid.NewGuid().ToString("N")[..8];
        
        try
        {
            var wsContext = await context.AcceptWebSocketAsync(null);
            webSocket = wsContext.WebSocket;
            
            lock (_lock)
            {
                _clients.Add(webSocket);
            }
            
            var clientInfo = GetClientInfo(context);
            ClientConnected?.Invoke(this, new WebSocketClientEventArgs(clientId, clientInfo));
            Log.Information("WebSocket 客户端已连接: {ClientId} ({ClientInfo})", clientId, clientInfo);
            
            var buffer = new byte[4096];
            
            while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
                
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    Log.Debug("WebSocket 收到消息: {ClientId}, 长度: {Length}, 内容: {Message}, 订阅者数量: {Count}", 
                        clientId, result.Count, message, MessageReceived?.GetInvocationList().Length ?? 0);
                    MessageReceived?.Invoke(this, new WebSocketMessageEventArgs(clientId, message));
                }
            }
        }
        catch (WebSocketException ex)
        {
            Log.Warning(ex, "WebSocket 连接异常: {ClientId}", clientId);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Error(ex, "处理客户端连接时发生错误: {ClientId}", clientId);
        }
        finally
        {
            if (webSocket != null)
            {
                lock (_lock)
                {
                    _clients.Remove(webSocket);
                }
                
                ClientDisconnected?.Invoke(this, new WebSocketClientEventArgs(clientId, ""));
                Log.Information("WebSocket 客户端已断开: {ClientId}", clientId);
                
                webSocket.Dispose();
            }
        }
    }
    
    public async Task BroadcastAsync<T>(T data)
    {
        var json = JsonSerializer.Serialize(data);
        var bytes = Encoding.UTF8.GetBytes(json);
        
        List<WebSocket> clientsCopy;
        lock (_lock)
        {
            clientsCopy = _clients.Where(c => c.State == WebSocketState.Open).ToList();
        }
        
        var tasks = clientsCopy.Select(async client =>
        {
            try
            {
                await client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "发送消息到客户端失败");
            }
        });
        
        await Task.WhenAll(tasks);
    }
    
    public async Task SendAsync<T>(string clientId, T data)
    {
        WebSocket? client;
        lock (_lock)
        {
            client = _clients.FirstOrDefault(c => c.State == WebSocketState.Open);
        }
        
        if (client == null) return;
        
        var json = JsonSerializer.Serialize(data);
        var bytes = Encoding.UTF8.GetBytes(json);
        
        await client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
    }
    
    private static string GetClientInfo(HttpListenerContext context)
    {
        var userAgent = context.Request.Headers["User-Agent"] ?? "Unknown";
        var origin = context.Request.Headers["Origin"] ?? "Unknown";
        return $"{origin} - {userAgent}";
    }

    private static async Task ServeSettingsPage(HttpListenerContext context)
    {
        try
        {
            var html = @"<!DOCTYPE html>
<html lang=""zh-CN"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Tai Activity Tracker - 设置</title>
    <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            background: #1a1a2e; color: #eee;
            min-height: 100vh; display: flex; justify-content: center; align-items: center;
        }
        .container { max-width: 480px; width: 100%; padding: 32px; }
        .card {
            background: #16213e; border-radius: 12px; padding: 24px;
            margin-bottom: 16px;
        }
        h1 { font-size: 20px; margin-bottom: 8px; }
        .status-row { display: flex; align-items: center; gap: 10px; margin: 16px 0; }
        .dot {
            width: 10px; height: 10px; border-radius: 50%;
            background: #22c55e; animation: pulse 2s infinite;
        }
        @keyframes pulse { 0%,100% { opacity:1; } 50% { opacity:0.5; } }
        .label { font-size: 12px; color: #888; margin-bottom: 4px; }
        .value { font-size: 14px; }
        .footer { text-align: center; font-size: 11px; color: #555; margin-top: 16px; }
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""card"">
            <h1>Tai Activity Tracker</h1>
            <p style=""color:#888;font-size:13px;"">浏览器活动追踪插件</p>
            <div class=""status-row"">
                <div class=""dot""></div>
                <span>服务运行中 — 端口 " + context.Request.LocalEndPoint!.Port + @"</span>
            </div>
            <div>
                <div class=""label"">客户端数量</div>
                <div class=""value"">已连接 WebSocket 客户端: <span id=""clientCount"">-</span></div>
            </div>
        </div>
        <div class=""card"">
            <h1 style=""font-size:16px;"">使用说明</h1>
            <p style=""font-size:13px;color:#aaa;line-height:1.6;margin-top:8px;"">
                安装插件后，浏览器网页浏览活动将自动记录到 PChabit。
                点击浏览器工具栏中的插件图标可查看实时统计。
            </p>
        </div>
        <div class=""footer"">Tai-AI v1.0</div>
    </div>
</body>
</html>";

            var buffer = Encoding.UTF8.GetBytes(html);
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = buffer.Length;
            context.Response.StatusCode = 200;
            await context.Response.OutputStream.WriteAsync(buffer);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "提供设置页面时发生错误");
        }
    }
    
    public void Dispose()
    {
        _cts.Cancel();
        
        lock (_lock)
        {
            foreach (var client in _clients)
            {
                client.Dispose();
            }
            _clients.Clear();
        }
        
        _listener.Close();
        _cts.Dispose();
    }
}

public class WebSocketMessageEventArgs : EventArgs
{
    public string ClientId { get; }
    public string Message { get; }
    
    public WebSocketMessageEventArgs(string clientId, string message)
    {
        ClientId = clientId;
        Message = message;
    }
}

public class WebSocketClientEventArgs : EventArgs
{
    public string ClientId { get; }
    public string ClientInfo { get; }
    
    public WebSocketClientEventArgs(string clientId, string clientInfo)
    {
        ClientId = clientId;
        ClientInfo = clientInfo;
    }
}
