using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Serilog;
using PChabit.Core.Entities;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Data;
using PChabit.Infrastructure.Monitoring;

namespace PChabit.Infrastructure.Services;

/// <summary>
/// 浏览历史同步：扩展导出 → SQLite 入库 → WebDAV 云合并。
/// 合并键用规范化 URL（不用 chrome.history 本地 id）。
/// 云文件兼容读旧裸数组 browser-history-total.json，写 pchabit/browser-history-v1.json。
/// </summary>
public class HistorySyncService
{
    public const string CloudFilePath = "pchabit/browser-history-v1.json";
    public const string LegacyCloudFile = "browser-history-total.json";

    private readonly IHistoryIngestService _ingest;
    private readonly IDbContextFactory<PChabitDbContext> _dbFactory;
    private readonly IWebDAVSyncService _webDav;
    private readonly ISettingsService _settings;
    private readonly BrowserSyncWebSocketHandler _wsHandler;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public event EventHandler<string>? ProgressChanged;

    public HistorySyncService(
        IHistoryIngestService ingest,
        IDbContextFactory<PChabitDbContext> dbFactory,
        IWebDAVSyncService webDav,
        ISettingsService settings,
        BrowserSyncWebSocketHandler wsHandler)
    {
        _ingest = ingest;
        _dbFactory = dbFactory;
        _webDav = webDav;
        _settings = settings;
        _wsHandler = wsHandler;
    }

    private void Report(string step)
    {
        Log.Information("[HistorySync] {Step}", step);
        ProgressChanged?.Invoke(this, step);
    }

    public async Task<(bool Ok, string Message, int Ingested, int CloudMerged)> SyncAsync(
        int days = 30, CancellationToken ct = default)
    {
        if (!await _lock.WaitAsync(0, ct))
            return (false, "历史同步进行中，请稍候", 0, 0);

        try
        {
            // 整体超时兜底：WebDAV 网络/流读取异常时避免无限挂起（HttpClient 只覆盖请求头等待）
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(150));
            ct = cts.Token;

            if (!_settings.BrowserSyncEnabled || !_settings.BrowserHistoryIngestEnabled)
                return (false, "历史同步已关闭", 0, 0);

            var url = _settings.WebDAVUrl;
            var user = _settings.WebDAVUsername;
            var pass = _settings.WebDAVPassword;

            Report($"请求各浏览器导出近 {days} 天历史…");
            await _wsHandler.RequestHistoryExportAsync(days, ct);
            // 扩展异步推送 history_export，由 handler 入库；给一点时间收完
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(15, 3 + days / 10)), ct);

            var localCount = await _ingest.GetCountAsync(ct);
            Report($"本机历史库：{localCount} 条");

            if (string.IsNullOrWhiteSpace(url))
                return (true, $"已采集本机历史 {localCount} 条（未配置 WebDAV，跳过云端）", localCount, 0);

            Report("读取云端历史…");
            var cloudItems = await DownloadCloudAsync(url, user, pass, ct);
            Report($"云端历史：{cloudItems.Count} 条");

            var localItems = await LoadLocalAsCloudItemsAsync(ct);
            var merged = MergeByUrl(localItems, cloudItems);
            Report($"合并后：{merged.Count} 条（按 URL 去重，取较新 lastVisitTime）");

            // 把合并结果写回本库（补上云端独有的）
            // 用规范化 URL 哈希预计算本机最新访问时间，避免 O(n^2) 两两比较（历史上万条后卡死）
            var localLookup = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var l in localItems)
            {
                var lk = NormalizeUrl(l.Url);
                if (string.IsNullOrEmpty(lk)) continue;
                if (!localLookup.TryGetValue(lk, out var lt) || l.LastVisitTime > lt)
                    localLookup[lk] = l.LastVisitTime;
            }
            var ingestFromCloud = new List<HistoryEntry>();
            foreach (var m in merged)
            {
                var mk = NormalizeUrl(m.Url);
                if (string.IsNullOrEmpty(mk)) continue;
                if (localLookup.TryGetValue(mk, out var maxLocal) && maxLocal >= m.LastVisitTime)
                    continue;
                ingestFromCloud.Add(new HistoryEntry
                {
                    Url = m.Url,
                    Title = m.Title,
                    VisitTime = m.LastVisitTime,
                    VisitCount = m.VisitCount
                });
            }

            var added = 0;
            if (ingestFromCloud.Count > 0)
            {
                added = await _ingest.IngestAsync("cloud", ingestFromCloud, ct);
                Report($"从云端补入本机：{added} 条");
            }

            Report("上传云端历史…");
            var payload = new
            {
                version = 1,
                updatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                total = merged.Count,
                items = merged
            };
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            await _webDav.UploadFileAsync(url, user, pass, CloudFilePath, Encoding.UTF8.GetBytes(json), ct);
            await using (var metaDb = await _dbFactory.CreateDbContextAsync(ct))
            {
                await metaDb.SetMetaIfPossibleAsync(BrowserSyncMetaKeys.HistoryLastSyncAt,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(), ct);
            }

            Report("历史同步完成");
            return (true, $"本机 {localCount} · 云端合并 {merged.Count} · 补入 {added}", localCount + added, merged.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "历史同步失败");
            return (false, $"历史同步失败：{ex.Message}", 0, 0);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<List<CloudHistoryItem>> DownloadCloudAsync(string url, string user, string pass, CancellationToken ct)
    {
        var merged = new List<CloudHistoryItem>();
        HashSet<string>? seenKeys = null;

        void Take(List<CloudHistoryItem> items)
        {
            if (items.Count == 0) return;
            seenKeys ??= new HashSet<string>(StringComparer.Ordinal);
            foreach (var it in items)
            {
                var key = NormalizeUrl(it.Url);
                if (seenKeys.Add(key)) merged.Add(it);
            }
        }

        // 1) 本版本云文件
        try
        {
            var bytes = await _webDav.DownloadFileAsync(url, user, pass, CloudFilePath, ct);
            if (bytes is { Length: > 0 })
                Take(ParseCloudHistory(Encoding.UTF8.GetString(bytes)));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "下载云端历史失败: {Path}", CloudFilePath);
        }

        // 2) 旧 Browser history sync 全量文件
        try
        {
            var bytes = await _webDav.DownloadFileAsync(url, user, pass, LegacyCloudFile, ct);
            if (bytes is { Length: > 0 })
            {
                var items = ParseCloudHistory(Encoding.UTF8.GetString(bytes));
                if (items.Count > 0)
                {
                    Report($"已读旧 Browser history sync 全量文件 {LegacyCloudFile}（{items.Count} 条）");
                    Take(items);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "下载旧全量历史失败: {Path}", LegacyCloudFile);
        }

        // 3) 旧 Browser history sync 增量文件 browser-history-increment-*.json
        try
        {
            var files = await _webDav.ListFilesAsync(url, user, pass, ct: ct);
            var increments = files
                .Where(f => f.Name.StartsWith("browser-history-increment-", StringComparison.OrdinalIgnoreCase)
                    && f.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.Name)
                .ToList();
            if (increments.Count > 0)
            {
                Report($"发现旧增量历史文件 {increments.Count} 个，逐个读取…");
                foreach (var f in increments)
                {
                    try
                    {
                        var bytes = await _webDav.DownloadFileAsync(url, user, pass, f.Name, ct);
                        if (bytes is { Length: > 0 })
                            Take(ParseCloudHistory(Encoding.UTF8.GetString(bytes)));
                    }
                    catch (Exception ex)
                    {
                        // 加密文件或损坏文件跳过，不影响主流程
                        Log.Warning(ex, "读取增量历史文件失败: {Path}", f.Name);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "枚举 WebDAV 增量历史文件失败（可能服务器不支持 PROPFIND）");
        }

        return merged;
    }

    internal static List<CloudHistoryItem> ParseCloudHistory(string json)
    {
        var list = new List<CloudHistoryItem>();
        if (string.IsNullOrWhiteSpace(json)) return list;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            JsonElement array;
            if (root.ValueKind == JsonValueKind.Array)
            {
                array = root; // 旧裸数组
            }
            else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("items", out var items))
            {
                array = items;
            }
            else return list;

            if (array.ValueKind != JsonValueKind.Array) return list;

            foreach (var el in array.EnumerateArray())
            {
                var url = el.TryGetProperty("url", out var u) ? u.GetString() : null;
                if (string.IsNullOrWhiteSpace(url)) continue;
                list.Add(new CloudHistoryItem
                {
                    Url = url,
                    Title = el.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                    LastVisitTime = TryReadLong(el, "lastVisitTime", "visitTime"),
                    VisitCount = (int)Math.Max(1, TryReadLong(el, "visitCount"))
                });
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "解析云端历史 JSON 失败");
        }
        return list;
    }

    /// <summary>容错读取 Int64（数字/浮点/字符串均可），供云端历史解析使用；全部失败返回 0。</summary>
    private static long TryReadLong(JsonElement el, params string[] props)
    {
        foreach (var p in props)
        {
            if (!el.TryGetProperty(p, out var v)) continue;
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
        }
        return 0;
    }

    private async Task<List<CloudHistoryItem>> LoadLocalAsCloudItemsAsync(CancellationToken ct)
    {
        var recent = await _ingest.QueryRecentAsync(200000, ct);
        // 按 URL 聚合：取最新 VisitTime、最大 VisitCount、非空 Title
        var map = new Dictionary<string, CloudHistoryItem>(StringComparer.Ordinal);
        foreach (var r in recent)
        {
            if (string.IsNullOrWhiteSpace(r.Url)) continue;
            var key = NormalizeUrl(r.Url);
            if (!map.TryGetValue(key, out var cur))
            {
                map[key] = new CloudHistoryItem
                {
                    Url = r.Url,
                    Title = r.Title,
                    LastVisitTime = r.VisitTime,
                    VisitCount = r.VisitCount
                };
            }
            else
            {
                if (r.VisitTime > cur.LastVisitTime)
                {
                    cur.LastVisitTime = r.VisitTime;
                    if (!string.IsNullOrEmpty(r.Title)) cur.Title = r.Title;
                }
                if (r.VisitCount > cur.VisitCount) cur.VisitCount = r.VisitCount;
            }
        }
        return map.Values.ToList();
    }

    /// <summary>按规范化 URL 合并，保留较新 lastVisitTime 与较大 visitCount。</summary>
    public static List<CloudHistoryItem> MergeByUrl(
        IEnumerable<CloudHistoryItem> a, IEnumerable<CloudHistoryItem> b)
    {
        var map = new Dictionary<string, CloudHistoryItem>(StringComparer.Ordinal);
        void Take(CloudHistoryItem item)
        {
            if (string.IsNullOrWhiteSpace(item.Url)) return;
            var key = NormalizeUrl(item.Url);
            if (!map.TryGetValue(key, out var cur))
            {
                map[key] = new CloudHistoryItem
                {
                    Url = item.Url,
                    Title = item.Title ?? "",
                    LastVisitTime = item.LastVisitTime,
                    VisitCount = Math.Max(1, item.VisitCount)
                };
            }
            else
            {
                if (item.LastVisitTime > cur.LastVisitTime)
                {
                    cur.LastVisitTime = item.LastVisitTime;
                    if (!string.IsNullOrEmpty(item.Title)) cur.Title = item.Title;
                }
                if (item.VisitCount > cur.VisitCount) cur.VisitCount = item.VisitCount;
            }
        }

        foreach (var i in a) Take(i);
        foreach (var i in b) Take(i);
        return map.Values.OrderByDescending(i => i.LastVisitTime).ToList();
    }

    public static string NormalizeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return url?.Trim() ?? string.Empty;

        if (Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u)
            && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps))
        {
            var path = u.AbsolutePath.TrimEnd('/');
            return $"{u.GetLeftPart(UriPartial.Authority)}{path}{u.Query}";
        }

        return url.Trim();
    }

    public class CloudHistoryItem
    {
        public string Url { get; set; } = "";
        public string Title { get; set; } = "";
        public long LastVisitTime { get; set; }
        public int VisitCount { get; set; }
    }
}
