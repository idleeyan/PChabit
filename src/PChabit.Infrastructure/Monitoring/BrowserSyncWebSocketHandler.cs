using System.Collections.Concurrent;
using System.Text.Json;
using Serilog;
using PChabit.Core.Interfaces;
using PChabit.Core.Sync;
using PChabit.Infrastructure.Services;

namespace PChabit.Infrastructure.Monitoring;

/// <summary>
/// 扩展 → 桌面历史同步消息处理器。
/// 独立订阅 WebSocketServer.MessageReceived，与 WebMonitor 活动统计并行。
/// 书签库/书签同步模块已下线，此处仅保留浏览历史入库与扩展就绪状态。
/// </summary>
public class BrowserSyncWebSocketHandler : IDisposable
{
    private readonly WebSocketServer _webSocketServer;
    private readonly IHistoryIngestService? _historyIngest;

    private readonly ConcurrentDictionary<string, DateTime> _readyBrowsers = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>clientId → 最终显示名（含用户 override）。</summary>
    private readonly ConcurrentDictionary<string, string> _clientDisplayNames = new(StringComparer.Ordinal);

    public event EventHandler<BrowserSyncReadyEventArgs>? BrowserSyncReady;
    public event Action<string, int, int>? HistoryIngested;

    public int ReadyBrowserCount => _readyBrowsers.Count;
    public IReadOnlyCollection<string> ReadyBrowsers => _readyBrowsers.Keys.ToList();

    public BrowserSyncWebSocketHandler(
        WebSocketServer webSocketServer,
        IHistoryIngestService? historyIngest = null)
    {
        _webSocketServer = webSocketServer;
        _historyIngest = historyIngest;
        _webSocketServer.MessageReceived += OnMessageReceived;
        _webSocketServer.ClientConnected += OnClientConnected;
        _webSocketServer.ClientDisconnected += OnClientDisconnected;
        Log.Information("BrowserSyncWebSocketHandler 已初始化");
    }

    private void OnClientDisconnected(object? sender, WebSocketClientEventArgs e)
    {
        OnClientDisconnectedCleanup(e.ClientId);
    }

    private void OnClientConnected(object? sender, WebSocketClientEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.BrowserName) && e.BrowserName != "未知浏览器")
        {
            _readyBrowsers[e.BrowserName] = DateTime.UtcNow;
            Log.Information("连接进程识别为 {Browser} (client={ClientId})", e.BrowserName, e.ClientId);
            BrowserSyncReady?.Invoke(this, new BrowserSyncReadyEventArgs(e.BrowserName, null));
        }
    }

    /// <summary>
    /// 用户显式标签 &gt; 进程识别 &gt; 扩展自报 &gt; Unknown。
    /// 同时维护 clientId → 显示名映射，避免进程名与自定义名双份。
    /// </summary>
    private string ResolveBrowserName(string clientId, string? reported, bool isUserOverride = false)
    {
        var fromProcess = _webSocketServer.GetClientBrowserName(clientId);
        var final = BrowserNameResolver.Resolve(reported, isUserOverride, fromProcess);

        if (_clientDisplayNames.TryGetValue(clientId, out var old) &&
            !string.Equals(old, final, StringComparison.OrdinalIgnoreCase))
        {
            _readyBrowsers.TryRemove(old, out _);
        }
        _clientDisplayNames[clientId] = final;
        _readyBrowsers[final] = DateTime.UtcNow;
        return final;
    }

    private void OnClientDisconnectedCleanup(string clientId)
    {
        if (_clientDisplayNames.TryRemove(clientId, out var name))
        {
            if (!_clientDisplayNames.Values.Any(v => string.Equals(v, name, StringComparison.OrdinalIgnoreCase)))
                _readyBrowsers.TryRemove(name, out _);
        }
    }

    private void OnMessageReceived(object? sender, WebSocketMessageEventArgs e)
    {
        try
        {
            string? type;
            using (var peek = JsonDocument.Parse(e.Message))
            {
                if (!peek.RootElement.TryGetProperty("type", out var typeEl)) return;
                type = typeEl.GetString();
            }

            if (string.IsNullOrEmpty(type)) return;

            if (type is not ("browser_sync_ready" or "history_export"))
            {
                return;
            }

            var message = e.Message;
            var clientId = e.ClientId;
            _ = Task.Run(() => HandleMessageAsync(type, message, clientId));
        }
        catch (JsonException)
        {
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "BrowserSync 消息处理异常");
        }
    }

    private async Task HandleMessageAsync(string type, string rawMessage, string clientId)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawMessage);
            var root = doc.RootElement;
            var isUserOverride = root.TryGetProperty("isUserOverride", out var uo) && uo.GetBoolean();

            switch (type)
            {
                case "browser_sync_ready":
                    HandleReady(root, clientId, isUserOverride);
                    break;
                case "history_export":
                    await HandleHistoryExportAsync(root, clientId, isUserOverride);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "处理 BrowserSync 消息失败: {Type}", type);
        }
    }

    private void HandleReady(JsonElement root, string clientId, bool isUserOverride)
    {
        var reported = root.TryGetProperty("browser", out var b) ? b.GetString() : null;
        var version = root.TryGetProperty("extVersion", out var v) ? v.GetString() : null;
        var browser = ResolveBrowserName(clientId, reported, isUserOverride);
        Log.Information("浏览器同步就绪: {Browser} v{Version} (扩展自报={Reported}, override={Override}, client={ClientId})",
            browser, version, reported, isUserOverride, clientId);
        BrowserSyncReady?.Invoke(this, new BrowserSyncReadyEventArgs(browser, version));
    }

    private async Task HandleHistoryExportAsync(JsonElement root, string clientId, bool isUserOverride)
    {
        if (_historyIngest == null) return;

        var reported = root.TryGetProperty("browser", out var b) ? b.GetString() : null;
        var browser = ResolveBrowserName(clientId, reported, isUserOverride);
        var entries = new List<HistoryEntry>();

        if (root.TryGetProperty("entries", out var entriesEl) && entriesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in entriesEl.EnumerateArray())
            {
                var url = el.TryGetProperty("url", out var u) ? u.GetString() : null;
                if (string.IsNullOrEmpty(url)) continue;
                entries.Add(new HistoryEntry
                {
                    Url = url,
                    Title = el.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                    VisitTime = TryGetInt64(el, "visitTime", 0),
                    VisitCount = (int)Math.Clamp(TryGetInt64(el, "visitCount", 1), 1, int.MaxValue)
                });
            }
        }

        if (entries.Count == 0) return;

        var added = await _historyIngest.IngestAsync(browser, entries);
        Log.Information("历史导出入库 from {Browser}: {Added}/{Total} (batch)", browser, added, entries.Count);
        HistoryIngested?.Invoke(browser, added, entries.Count);
    }

    /// <summary>
    /// 容错读取 Int64：兼容数字与字符串两种 JSON 形式。
    /// 豆包等国产 Chromium 的 history API 可能把 lastVisitTime 返回为字符串，
    /// 直接 GetInt64 会抛 FormatException 导致整批历史丢弃。
    /// </summary>
    private static long TryGetInt64(JsonElement el, string prop, long fallback)
    {
        if (!el.TryGetProperty(prop, out var v)) return fallback;
        if (v.ValueKind == JsonValueKind.Number)
        {
            if (v.TryGetInt64(out var l)) return l;
            if (v.TryGetDouble(out var dd)) return (long)dd;
        }
        if (v.ValueKind == JsonValueKind.String
            && long.TryParse(v.GetString(), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var s))
        {
            return s;
        }
        return fallback;
    }

    /// <summary>向所有已连接扩展请求历史导出（近 N 天）。</summary>
    public async Task RequestHistoryExportAsync(int days = 30, CancellationToken ct = default)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var startTime = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, days)).ToUnixTimeMilliseconds();
        var cmd = new { type = "history_request_export", requestId, startTime };
        await _webSocketServer.BroadcastAsync(cmd);
        Log.Information("已广播 history_request_export requestId={RequestId} 近 {Days} 天，等待 {N} 个浏览器",
            requestId, days, _readyBrowsers.Count);
    }

    public void Dispose()
    {
        _webSocketServer.MessageReceived -= OnMessageReceived;
        _webSocketServer.ClientConnected -= OnClientConnected;
        _webSocketServer.ClientDisconnected -= OnClientDisconnected;
        _clientDisplayNames.Clear();
    }
}

public class BrowserSyncReadyEventArgs : EventArgs
{
    public string Browser { get; }
    public string? ExtVersion { get; }

    public BrowserSyncReadyEventArgs(string browser, string? extVersion)
    {
        Browser = browser;
        ExtVersion = extVersion;
    }
}
