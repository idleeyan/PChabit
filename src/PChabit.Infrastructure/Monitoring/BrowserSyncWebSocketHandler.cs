using System.Collections.Concurrent;
using System.Text.Json;
using Serilog;
using PChabit.Core.Entities;
using PChabit.Core.Interfaces;
using PChabit.Core.Sync;
using PChabit.Infrastructure.Services;

namespace PChabit.Infrastructure.Monitoring;

/// <summary>
/// 扩展 ↔ 桌面书签/历史同步消息处理器。
/// 独立订阅 WebSocketServer.MessageReceived，与 WebMonitor 活动统计并行。
/// DB 是 source of truth；本 handler 只做 export 入库与 apply 下发。
/// </summary>
public class BrowserSyncWebSocketHandler : IDisposable
{
    private readonly WebSocketServer _webSocketServer;
    private readonly IBrowserBookmarkRepository _repository;
    private readonly IHistoryIngestService? _historyIngest;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ConcurrentDictionary<string, ExportBatchCollector> _pendingExports = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ApplyAggregateDto>> _pendingApplies = new();
    private readonly ConcurrentDictionary<string, ConcurrentBag<ApplyResultDto>> _pendingApplyBags = new();
    private readonly ConcurrentDictionary<string, DateTime> _readyBrowsers = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>clientId → 最终显示名（含用户 override）。</summary>
    private readonly ConcurrentDictionary<string, string> _clientDisplayNames = new(StringComparer.Ordinal);

    public event EventHandler<BookmarksExportedEventArgs>? BookmarksExported;
    public event EventHandler<BookmarksApplyResultEventArgs>? BookmarksApplyResult;
    public event EventHandler<BrowserSyncReadyEventArgs>? BrowserSyncReady;

    public int ReadyBrowserCount => _readyBrowsers.Count;
    public IReadOnlyCollection<string> ReadyBrowsers => _readyBrowsers.Keys.ToList();

    public BrowserSyncWebSocketHandler(
        WebSocketServer webSocketServer,
        IBrowserBookmarkRepository repository,
        IHistoryIngestService? historyIngest = null)
    {
        _webSocketServer = webSocketServer;
        _repository = repository;
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
        // 进程识别优先：比扩展自报 UA 更准（豆包/Chrome 内核 UA 几乎相同）
        if (!string.IsNullOrEmpty(e.BrowserName) && e.BrowserName != "未知浏览器")
        {
            _readyBrowsers[e.BrowserName] = DateTime.UtcNow;
            Log.Information("连接进程识别为: {Browser} (client={ClientId})", e.BrowserName, e.ClientId);
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

        // 若该 client 曾用其它名字注册过，替换 ready 表中的旧键
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
            // 仅当没有其它 client 仍使用该显示名时才从 ready 表移除
            if (!_clientDisplayNames.Values.Any(v => string.Equals(v, name, StringComparison.OrdinalIgnoreCase)))
                _readyBrowsers.TryRemove(name, out _);
        }
    }

    private void OnMessageReceived(object? sender, WebSocketMessageEventArgs e)
    {
        try
        {
            // 只 peek type；完整解析放到异步 handler，避免 JsonDocument 被提前 Dispose
            string? type;
            using (var peek = JsonDocument.Parse(e.Message))
            {
                if (!peek.RootElement.TryGetProperty("type", out var typeEl)) return;
                type = typeEl.GetString();
            }

            if (string.IsNullOrEmpty(type)) return;

            // 仅接管同步相关消息；活动统计仍走 WebMonitor
            if (type is not ("browser_sync_ready" or "bookmarks_export" or "bookmarks_apply_result"
                or "history_export"))
            {
                return;
            }

            var message = e.Message;
            var clientId = e.ClientId;
            _ = Task.Run(() => HandleMessageAsync(type, message, clientId));
        }
        catch (JsonException)
        {
            // 非 JSON 或非同步消息，忽略
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
                case "bookmarks_export":
                    await HandleBookmarksExportAsync(root, clientId, isUserOverride);
                    break;
                case "bookmarks_apply_result":
                    HandleApplyResult(root, clientId, isUserOverride);
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

    private async Task HandleBookmarksExportAsync(JsonElement root, string clientId, bool isUserOverride)
    {
        var requestId = root.TryGetProperty("requestId", out var r) ? r.GetString() ?? "" : "";
        var reported = root.TryGetProperty("browser", out var b) ? b.GetString() : null;
        var browser = ResolveBrowserName(clientId, reported, isUserOverride);
        var batchIndex = root.TryGetProperty("batchIndex", out var bi) ? bi.GetInt32() : 0;
        var batchCount = root.TryGetProperty("batchCount", out var bc) ? bc.GetInt32() : 1;
        var error = root.TryGetProperty("error", out var err) ? err.GetString() : null;

        if (!string.IsNullOrEmpty(error))
        {
            Log.Warning("书签导出失败 from {Browser}: {Error}", browser, error);
            _pendingExports.TryRemove(requestId, out _);
            return;
        }

        var items = new List<FlatItem>();
        if (root.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in itemsEl.EnumerateArray())
            {
                var item = ParseCloudItem(el);
                if (item != null) items.Add(item);
            }
        }

        var collectorKey = requestId + "\n" + browser;
        var collector = _pendingExports.GetOrAdd(collectorKey, _ => new ExportBatchCollector(batchCount, browser));
        collector.AddBatch(batchIndex, items, batchCount);

        Log.Debug("书签导出批次 {Index}/{Count} from {Browser}: {N} 条 (key={Key})",
            batchIndex + 1, batchCount, browser, items.Count, collectorKey);

        if (!collector.IsComplete) return;

        _pendingExports.TryRemove(collectorKey, out _);
        var allItems = collector.GetAllItems();
        Log.Information("书签导出完成 from {Browser}: {N} 条 (requestId={RequestId})",
            browser, allItems.Count, requestId);

        // 入库失败也要抛事件，否则 RequestExportAsync 会一直等超时
        try
        {
            var entities = allItems.Select(i => BrowserBookmarkRepository.ToEntity(i, browser)).ToList();
            await _repository.UpsertManyAsync(entities);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "书签导出入库失败 from {Browser}（仍通知同步流程）", browser);
        }

        BookmarksExported?.Invoke(this, new BookmarksExportedEventArgs(browser, allItems, requestId));
    }

    private static FlatItem? ParseCloudItem(JsonElement el)
    {
        var typeStr = el.TryGetProperty("type", out var t) ? t.GetString() : null;
        if (typeStr is not ("bookmark" or "folder")) return null;

        var url = el.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String
            ? u.GetString()
            : null;
        var title = el.TryGetProperty("title", out var ti) ? ti.GetString() ?? "" : "";
        var path = new List<string>();
        if (el.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in p.EnumerateArray())
            {
                if (s.ValueKind == JsonValueKind.String)
                    path.Add(s.GetString() ?? "");
            }
        }

        var item = new FlatItem
        {
            Type = typeStr == "folder" ? FlatItemType.Folder : FlatItemType.Bookmark,
            Title = title,
            Url = url,
            Path = path,
            DateAdded = el.TryGetProperty("dateAdded", out var da) ? da.GetInt64() : 0,
            DateModified = el.TryGetProperty("dateModified", out var dm) ? dm.GetInt64() : 0,
            Key = el.TryGetProperty("key", out var k) ? k.GetString() ?? "" : ""
        };

        if (string.IsNullOrEmpty(item.Key))
            item.Key = BookmarkMergePlanner.ItemKey(item);

        return item;
    }

    private void HandleApplyResult(JsonElement root, string clientId, bool isUserOverride)
    {
        var requestId = root.TryGetProperty("requestId", out var r) ? r.GetString() ?? "" : "";
        var reported = root.TryGetProperty("browser", out var b) ? b.GetString() ?? "Unknown" : "Unknown";
        var browser = ResolveBrowserName(clientId, reported, isUserOverride);
        var ok = root.TryGetProperty("ok", out var o) && o.GetBoolean();
        var added = root.TryGetProperty("added", out var a) ? a.GetInt32() : 0;
        var removed = root.TryGetProperty("removed", out var rm) ? rm.GetInt32() : 0;
        var renamed = root.TryGetProperty("renamed", out var rn) ? rn.GetInt32() : 0;
        var errors = new List<string>();
        if (root.TryGetProperty("errors", out var errs) && errs.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in errs.EnumerateArray())
            {
                if (e.ValueKind == JsonValueKind.String)
                    errors.Add(e.GetString() ?? "");
            }
        }
        var warnings = new List<string>();
        if (root.TryGetProperty("warnings", out var wns) && wns.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in wns.EnumerateArray())
            {
                if (e.ValueKind == JsonValueKind.String)
                    warnings.Add(e.GetString() ?? "");
            }
        }

        Log.Information("书签写回结果 from {Browser}: ok={Ok} +{Added} -{Removed} ~{Renamed} errors={ErrorCount} warnings={WarnCount}",
            browser, ok, added, removed, renamed, errors.Count, warnings.Count);

        var dto = new ApplyResultDto(browser, ok, added, removed, renamed, errors, warnings);
        var bag = _pendingApplyBags.GetOrAdd(requestId, _ => new ConcurrentBag<ApplyResultDto>());
        bag.Add(dto);

        if (_pendingApplies.TryGetValue(requestId, out var tcs))
        {
            tcs.TrySetResult(new ApplyAggregateDto(bag.ToList()));
        }

        BookmarksApplyResult?.Invoke(this, new BookmarksApplyResultEventArgs(requestId, dto));
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
        // 通知外部刷新统计
        HistoryIngested?.Invoke(browser, added, entries.Count);
    }

    public event Action<string, int, int>? HistoryIngested;

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
            if (v.TryGetDouble(out var dd)) return (long)dd; // 扩展导出的毫秒时间戳可能带小数
        }
        if (v.ValueKind == JsonValueKind.String
            && long.TryParse(v.GetString(), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var s))
        {
            return s;
        }
        return fallback;
    }

    /// <summary>
    /// 向所有已连接扩展请求书签导出，等待各浏览器都返回或超时。
    /// 返回按浏览器分组、每浏览器内部按 key 去重后的导出；浏览器名即 e.Browser（桌面端进程识别的浏览器名）。
    /// 并集由调用方自行合并。
    /// </summary>
    public async Task<IReadOnlyDictionary<string, List<FlatItem>>> RequestExportAsync(
        TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var expected = Math.Max(1, _readyBrowsers.Count);
        var exportedBrowsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var collectedByKey = new Dictionary<string, FlatItem>(StringComparer.Ordinal);
        var byBrowser = new Dictionary<string, List<FlatItem>>(StringComparer.OrdinalIgnoreCase);
        var lockObj = new object();
        var allDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnExported(object? s, BookmarksExportedEventArgs e)
        {
            if (e.RequestId != requestId) return;
            lock (lockObj)
            {
                var browser = string.IsNullOrEmpty(e.Browser) ? "Unknown" : e.Browser;
                if (!byBrowser.TryGetValue(browser, out var list))
                {
                    list = new List<FlatItem>();
                    byBrowser[browser] = list;
                }

                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in e.Items)
                {
                    var key = string.IsNullOrEmpty(item.Key) ? BookmarkMergePlanner.ItemKey(item) : item.Key;
                    item.Key = key;
                    if (seen.Add(key)) list.Add(item);
                    collectedByKey[key] = item;
                }
                exportedBrowsers.Add(browser);
                Log.Information("导出汇集: {Browser} +{N} 条，已收 {Done}/{Expected} 个浏览器，去重后共 {Total} 条",
                    browser, e.Items.Count, exportedBrowsers.Count, expected, collectedByKey.Count);

                if (exportedBrowsers.Count >= expected)
                    allDone.TrySetResult(true);
            }
        }

        BookmarksExported += OnExported;
        try
        {
            var cmd = new { type = "bookmarks_request_export", requestId };
            await _webSocketServer.BroadcastAsync(cmd);
            Log.Information("已广播 bookmarks_request_export requestId={RequestId}，等待 {N} 个浏览器", requestId, expected);

            var limit = timeout ?? TimeSpan.FromSeconds(30);
            var completed = await Task.WhenAny(allDone.Task, Task.Delay(limit, ct));
            if (completed != allDone.Task)
            {
                lock (lockObj)
                {
                    Log.Warning("书签导出部分超时 requestId={RequestId}，已收 {Done}/{Expected} 浏览器，共 {Total} 条",
                        requestId, exportedBrowsers.Count, expected, collectedByKey.Count);
                }
            }

            lock (lockObj)
            {
                // 复制一份，避免调用方修改内部集合
                var snapshot = new Dictionary<string, List<FlatItem>>(byBrowser, StringComparer.OrdinalIgnoreCase);
                foreach (var kv in snapshot)
                    snapshot[kv.Key] = new List<FlatItem>(kv.Value);
                return snapshot;
            }
        }
        finally
        {
            BookmarksExported -= OnExported;
            CleanupExportRequest(requestId);
        }
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

    /// <summary>向所有已连接扩展下发 bookmarks_apply，收集各浏览器结果（等待全部或超时）。</summary>
    public async Task<ApplyAggregateDto?> ApplyAsync(
        IReadOnlyList<FlatItem> toAdd,
        IReadOnlyList<string> toRemoveUrls,
        IReadOnlyList<RenameOp> toRename,
        TimeSpan? timeout = null,
        CancellationToken ct = default,
        IReadOnlyList<object>? toRemoveFolders = null)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<ApplyAggregateDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingApplies[requestId] = tcs;
        _pendingApplyBags[requestId] = new ConcurrentBag<ApplyResultDto>();

        var expected = Math.Max(1, ReadyBrowserCount);
        var cmd = new
        {
            type = "bookmarks_apply",
            requestId,
            toAdd = toAdd.Select(i => new
            {
                type = i.Type == FlatItemType.Bookmark ? "bookmark" : "folder",
                title = i.Title,
                url = i.Url,
                path = i.Path
            }),
            toRemoveUrls,
            toRename = toRename.Select(r => new { url = r.Key.StartsWith("b:") ? r.Key[2..] : r.Key, title = r.Title }),
            toRemoveFolders = toRemoveFolders ?? Array.Empty<object>()
        };

        await _webSocketServer.BroadcastAsync(cmd);
        Log.Information("已广播 bookmarks_apply requestId={RequestId} +{Add} -{Remove} ~{Rename} 删夹 {Folders}，等待约 {Expected} 个浏览器",
            requestId, toAdd.Count, toRemoveUrls.Count, toRename.Count, toRemoveFolders?.Count ?? 0, expected);

        var limit = timeout ?? TimeSpan.FromSeconds(45);
        var deadline = DateTime.UtcNow + limit;
        while (DateTime.UtcNow < deadline)
        {
            var bag = _pendingApplyBags.GetValueOrDefault(requestId);
            if (bag != null && bag.Count >= expected)
                break;
            await Task.Delay(200, ct);
        }

        var results = _pendingApplyBags.TryGetValue(requestId, out var finalBag)
            ? finalBag.ToList()
            : new List<ApplyResultDto>();
        _pendingApplies.TryRemove(requestId, out _);
        _pendingApplyBags.TryRemove(requestId, out _);

        if (results.Count == 0)
        {
            Log.Warning("书签写回超时 requestId={RequestId}，无浏览器返回结果", requestId);
            return null;
        }

        Log.Information("书签写回汇总 requestId={RequestId}: {N}/{Expected} 个浏览器已响应",
            requestId, results.Count, expected);
        return new ApplyAggregateDto(results);
    }

    /// <summary>仅向指定浏览器显示名的扩展下发（无匹配则回退广播）。</summary>
    public async Task<ApplyAggregateDto?> ApplyToBrowsersAsync(
        IReadOnlyList<string> browsers,
        IReadOnlyList<FlatItem> toAdd,
        IReadOnlyList<string> toRemoveUrls,
        IReadOnlyList<RenameOp> toRename,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var targetIds = new List<string>();
        foreach (var name in browsers.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var kv in _clientDisplayNames)
            {
                if (string.Equals(kv.Value, name, StringComparison.OrdinalIgnoreCase))
                    targetIds.Add(kv.Key);
            }
            foreach (var id in _webSocketServer.GetClientIdsByBrowserName(name))
            {
                if (!targetIds.Contains(id)) targetIds.Add(id);
            }
        }

        if (targetIds.Count == 0)
            return await ApplyAsync(toAdd, toRemoveUrls, toRename, timeout, ct);

        var requestId = Guid.NewGuid().ToString("N");
        var bag = new ConcurrentBag<ApplyResultDto>();
        _pendingApplies[requestId] = new TaskCompletionSource<ApplyAggregateDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingApplyBags[requestId] = bag;

        var cmd = new
        {
            type = "bookmarks_apply",
            requestId,
            toAdd = toAdd.Select(i => new
            {
                type = i.Type == FlatItemType.Bookmark ? "bookmark" : "folder",
                title = i.Title,
                url = i.Url,
                path = i.Path
            }),
            toRemoveUrls,
            toRename = toRename.Select(r => new { url = r.Key.StartsWith("b:") ? r.Key[2..] : r.Key, title = r.Title })
        };

        foreach (var id in targetIds)
            await _webSocketServer.SendAsync(id, cmd);

        Log.Information("定向书签写回 requestId={RequestId} clients={Clients} +{Add}",
            requestId, targetIds.Count, toAdd.Count);

        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(45));
        while (DateTime.UtcNow < deadline && bag.Count < targetIds.Count)
            await Task.Delay(200, ct);

        var results = bag.ToList();
        _pendingApplies.TryRemove(requestId, out _);
        _pendingApplyBags.TryRemove(requestId, out _);
        return results.Count == 0 ? null : new ApplyAggregateDto(results);
    }

    /// <summary>清理未完成的导出 collector（requestId 前缀）。</summary>
    private void CleanupExportRequest(string requestId)
    {
        foreach (var key in _pendingExports.Keys.Where(k => k.StartsWith(requestId, StringComparison.Ordinal)).ToList())
            _pendingExports.TryRemove(key, out _);
    }

    public void Dispose()
    {
        _webSocketServer.MessageReceived -= OnMessageReceived;
        _webSocketServer.ClientConnected -= OnClientConnected;
        _webSocketServer.ClientDisconnected -= OnClientDisconnected;
        foreach (var tcs in _pendingApplies.Values)
        {
            tcs.TrySetCanceled();
        }
        _pendingApplies.Clear();
        _pendingApplyBags.Clear();
        _pendingExports.Clear();
        _clientDisplayNames.Clear();
    }

    private class ExportBatchCollector
    {
        private int _batchCount;
        private readonly string _browser;
        private readonly Dictionary<int, List<FlatItem>> _batches = new();
        private readonly object _lock = new();

        public ExportBatchCollector(int batchCount, string browser)
        {
            _batchCount = Math.Max(1, batchCount);
            _browser = browser;
        }

        public void AddBatch(int index, List<FlatItem> items, int batchCount)
        {
            lock (_lock)
            {
                if (batchCount > _batchCount) _batchCount = batchCount;
                _batches[index] = items;
            }
        }

        public bool IsComplete
        {
            get
            {
                lock (_lock)
                {
                    if (_batches.Count < _batchCount) return false;
                    for (var i = 0; i < _batchCount; i++)
                    {
                        if (!_batches.ContainsKey(i)) return false;
                    }
                    return true;
                }
            }
        }

        public List<FlatItem> GetAllItems()
        {
            lock (_lock)
            {
                var result = new List<FlatItem>();
                for (var i = 0; i < _batchCount; i++)
                {
                    if (_batches.TryGetValue(i, out var batch))
                        result.AddRange(batch);
                }
                return result;
            }
        }
    }
}

public record ApplyResultDto(
    string Browser,
    bool Ok,
    int Added,
    int Removed,
    int Renamed,
    List<string> Errors,
    List<string>? Warnings = null)
{
    public IReadOnlyList<string> WarningList => Warnings ?? (IReadOnlyList<string>)Array.Empty<string>();
}

/// <summary>多浏览器写回结果汇总。</summary>
public record ApplyAggregateDto(IReadOnlyList<ApplyResultDto> Results)
{
    public int TotalAdded => Results.Sum(r => r.Added);
    public int TotalRemoved => Results.Sum(r => r.Removed);
    public int TotalRenamed => Results.Sum(r => r.Renamed);
    public List<string> AllErrors => Results.SelectMany(r => r.Errors).ToList();
    public string Summary =>
        Results.Count == 0
            ? "无结果"
            : string.Join("；", Results.Select(r => $"{r.Browser}:+{r.Added}/-{r.Removed}/~{r.Renamed}{(r.Ok ? "" : "(err)") }"));
}

public class BookmarksExportedEventArgs : EventArgs
{
    public string Browser { get; }
    public List<FlatItem> Items { get; }
    public string RequestId { get; }

    public BookmarksExportedEventArgs(string browser, List<FlatItem> items, string requestId)
    {
        Browser = browser;
        Items = items;
        RequestId = requestId;
    }
}

public class BookmarksApplyResultEventArgs : EventArgs
{
    public string RequestId { get; }
    public ApplyResultDto Result { get; }

    public BookmarksApplyResultEventArgs(string requestId, ApplyResultDto result)
    {
        RequestId = requestId;
        Result = result;
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
