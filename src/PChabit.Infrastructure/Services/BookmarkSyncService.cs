using System.Text;
using System.Text.Json;
using Serilog;
using PChabit.Core.Entities;
using PChabit.Core.Interfaces;
using PChabit.Core.Sync;
using PChabit.Infrastructure.Monitoring;

namespace PChabit.Infrastructure.Services;

/// <summary>
/// 书签智能同步编排：读云 → 三路合并 → 写库/写回扩展 → 写云 → 存基线。
/// DB 是 source of truth。首次无基线只做 union，禁止传播删除。
/// </summary>
public class BookmarkSyncService : IBookmarkSyncService
{
    /// <summary>云文件命名空间，避免与旧扩展抢文件。</summary>
    public const string CloudFilePath = "pchabit/browser-bookmarks-v3.json";

    private readonly IBrowserBookmarkRepository _repository;
    private readonly IWebDAVSyncService _webDav;
    private readonly ISettingsService _settings;
    private readonly BrowserSyncWebSocketHandler _wsHandler;
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public event EventHandler<BookmarkSyncProgressEventArgs>? ProgressChanged;

    public BookmarkSyncService(
        IBrowserBookmarkRepository repository,
        IWebDAVSyncService webDav,
        ISettingsService settings,
        BrowserSyncWebSocketHandler wsHandler)
    {
        _repository = repository;
        _webDav = webDav;
        _settings = settings;
        _wsHandler = wsHandler;
    }

    public async Task<int> GetLocalCountAsync(CancellationToken ct = default)
        => await _repository.GetActiveCountAsync(ct);

    public async Task<BookmarkSyncResult> SyncAsync(CancellationToken ct = default)
    {
        if (!await _syncLock.WaitAsync(0, ct))
        {
            return new BookmarkSyncResult { Success = false, Message = "同步正在进行中，请稍候" };
        }

        try
        {
            // 整体超时兜底：WebDAV 网络/流读取异常时避免无限挂起
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(150));
            return await SyncCoreAsync(cts.Token);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private async Task<BookmarkSyncResult> SyncCoreAsync(CancellationToken ct)
    {
        var result = new BookmarkSyncResult();
        var steps = new List<string>();

        void Report(string step)
        {
            steps.Add(step);
            Log.Information("[BookmarkSync] {Step}", step);
            ProgressChanged?.Invoke(this, new BookmarkSyncProgressEventArgs(step));
        }

        try
        {
            if (!_settings.BrowserSyncEnabled || !_settings.BrowserBookmarkSyncEnabled)
            {
                result.Message = "书签同步已关闭";
                return result;
            }

            // 1) 校验 WebDAV
            var url = _settings.WebDAVUrl;
            var user = _settings.WebDAVUsername;
            var pass = _settings.WebDAVPassword;
            if (string.IsNullOrWhiteSpace(url))
            {
                result.Message = "未配置 WebDAV 地址，请先在数据管理页设置";
                return result;
            }

            Report("读取云端快照…");
            List<FlatItem> cloudItems = new();
            try
            {
                var bytes = await _webDav.DownloadFileAsync(url, user, pass, CloudFilePath);
                if (bytes is { Length: > 0 })
                {
                    cloudItems = ParseCloudPayload(Encoding.UTF8.GetString(bytes));
                    Report($"读取云端：{cloudItems.Count} 条");
                }
                else
                {
                    Report("云端无快照（首次备份）");
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "下载云端书签失败");
                Report($"云端读取失败：{ex.Message}（继续本机合并）");
            }

            // 2) 请求扩展导出（按浏览器分组）→ DB
            Report("向浏览器扩展请求书签导出…");
            var exports = new Dictionary<string, List<FlatItem>>(StringComparer.OrdinalIgnoreCase);
            var exported = new List<FlatItem>();
            try
            {
                var exportResult = await _wsHandler.RequestExportAsync(TimeSpan.FromSeconds(25), ct);
                foreach (var kv in exportResult)
                    exports[kv.Key] = kv.Value;
                exported = exports.Values.SelectMany(x => x).ToList();
                var uniqueKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var i in exported)
                    uniqueKeys.Add(string.IsNullOrEmpty(i.Key) ? BookmarkMergePlanner.ItemKey(i) : i.Key);
                var browsers = _wsHandler.ReadyBrowsers;
                Report($"扩展导出：{exported.Count} 条原始 / 去重后 {uniqueKeys.Count} 条（来自 {string.Join("、", browsers)}）");
                if (exported.Count > 0)
                {
                    // 按真实浏览器分别入库，避免全部挂到第一个 key
                    foreach (var kv in exports)
                    {
                        if (kv.Value is not { Count: > 0 }) continue;
                        var entities = kv.Value.Select(i => BrowserBookmarkRepository.ToEntity(i, kv.Key)).ToList();
                        await _repository.UpsertManyAsync(entities, ct);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "扩展导出失败");
                Report($"扩展导出失败：{ex.Message}（仅用本地 DB）");
            }

            // 3) 读 DB local + baseline
            var localEntities = await _repository.GetAllActiveAsync(ct);
            var localItems = localEntities.Select(BrowserBookmarkRepository.ToFlatItem).ToList();
            Report($"本地库：{localItems.Count} 条");

            var baselineJson = await _repository.GetBaselineAsync(ct);
            var lastItems = BrowserBookmarkRepository.DeserializeItems(baselineJson);
            var hasHistory = lastItems.Count > 0;
            Report(hasHistory ? $"同步基线：{lastItems.Count} 条" : "首次同步（无基线，不做删除）");

            if (cloudItems.Count == 0 && lastItems.Count > 0)
            {
                Report($"⚠ 云端快照为空但基线有 {lastItems.Count} 条：已进入安全合并（禁止批量删除，按本地∪基线保留）");
                Log.Warning("BookmarkSync 安全模式：cloud empty, baseline={Baseline}, local later", lastItems.Count);
            }

            // 2.5) 浏览器侧删除检测：本次导出的浏览器，相对其上次导出快照缺失的 URL 书签 = 该浏览器删除 → 全局传播
            // 不依赖全局 baseline；只要该浏览器有上次快照即可检测
            var browserDeletedKeys = new HashSet<string>(StringComparer.Ordinal);
            var browserDeletedFolderKeys = new HashSet<string>(StringComparer.Ordinal);
            if (exports.Count > 0)
            {
                var lastExportsJson = await _repository.GetMetaAsync(BrowserSyncMetaKeys.BrowserLastExports, ct);
                var lastExports = DeserializeBrowserExports(lastExportsJson);
                browserDeletedKeys = BookmarkMergePlanner.DetectBrowserDeletions(exports, lastExports);
                browserDeletedFolderKeys = BookmarkMergePlanner.DetectBrowserFolderDeletions(exports, lastExports);
                if (browserDeletedKeys.Count > 0)
                {
                    cloudItems = cloudItems
                        .Where(i => !browserDeletedKeys.Contains(i.Key ?? BookmarkMergePlanner.ItemKey(i)))
                        .ToList();
                    Report($"浏览器删除检测：检出 {browserDeletedKeys.Count} 条，将从各端删除");
                }
                else
                {
                    Report(lastExports.Count == 0
                        ? "浏览器删除检测：尚无上次快照（本次建立，下次可传播删除）"
                        : "浏览器删除检测：无删除（0 条）");
                }
                if (browserDeletedFolderKeys.Count > 0)
                {
                    cloudItems = cloudItems
                        .Where(i => i.Type != FlatItemType.Folder
                                    || !browserDeletedFolderKeys.Contains(i.Key ?? BookmarkMergePlanner.ItemKey(i)))
                        .ToList();
                    Report($"浏览器文件夹删除检测：{browserDeletedFolderKeys.Count} 个文件夹");
                }
            }

            // 4) 三路合并
            var plan = BookmarkMergePlanner.PlanThreeWayMerge(localItems, cloudItems, lastItems);

            // 4.5) 强制应用浏览器删除：即使无全局 baseline，也要从 DB/云端剔除
            if (browserDeletedKeys.Count > 0)
            {
                var removedExtra = 0;
                foreach (var key in browserDeletedKeys)
                {
                    var localHit = localItems.FirstOrDefault(i =>
                        (i.Key ?? BookmarkMergePlanner.ItemKey(i)) == key && i.Type == FlatItemType.Bookmark);
                    if (localHit != null && !plan.ToRemoveLocal.Any(x => x.Key == key))
                    {
                        plan.ToRemoveLocal.Add(localHit);
                        removedExtra++;
                    }
                    plan.MergedCloud.RemoveAll(i => (i.Key ?? BookmarkMergePlanner.ItemKey(i)) == key);
                }
                plan.Counts.RemoveLocal = plan.ToRemoveLocal.Count;
                plan.Counts.Merged = plan.MergedCloud.Count;
                if (removedExtra > 0)
                    Report($"强制删除（浏览器侧）：{removedExtra} 条");
            }

            if (browserDeletedFolderKeys.Count > 0)
            {
                foreach (var key in browserDeletedFolderKeys)
                    plan.MergedCloud.RemoveAll(i => i.Type == FlatItemType.Folder
                                                   && (i.Key ?? BookmarkMergePlanner.ItemKey(i)) == key);
            }

            // 孤儿文件夹：无任何书签引用时剔除，避免 Edge 删除文件夹后被写回重建
            var mergedBeforePrune = plan.MergedCloud.Count;
            plan.MergedCloud = BookmarkMergePlanner.PruneOrphanFolders(plan.MergedCloud);
            if (plan.MergedCloud.Count != mergedBeforePrune)
                Report($"清理无内容文件夹：{mergedBeforePrune - plan.MergedCloud.Count} 个");

            plan.ToRemoveFolders = BookmarkMergePlanner.ComputeFoldersToRemove(
                localItems, plan.MergedCloud, browserDeletedFolderKeys);
            plan.Counts.Merged = plan.MergedCloud.Count;

            Report($"合并计划：+{plan.Counts.AddLocal} -{plan.Counts.RemoveLocal} ~{plan.Counts.RenameLocal}，结果 {plan.Counts.Merged} 条，待删文件夹 {plan.ToRemoveFolders.Count}");

            // 5) 写回浏览器
            //    不要把「文件夹」类型直接下发重建：文件夹仅在新增书签时按路径按需创建。
            //    空文件夹/已删除文件夹通过 toRemoveFolders 下发删除（扩展仅删空文件夹）。
            var applyItems = plan.MergedCloud
                .Where(i => i.Type == FlatItemType.Bookmark && !BookmarkMergePlanner.ShouldSkipUrl(i.Url))
                .ToList();
            var removeUrls = plan.ToRemoveLocal
                .Select(i => i.Url)
                .Where(u => !string.IsNullOrEmpty(u))
                .Select(u => u!)
                .ToList();
            var renameWithUrl = plan.ToRenameLocal
                .Select(r =>
                {
                    var url = r.Key.StartsWith("b:") ? r.Key[2..] : r.Key;
                    return new RenameOp { Id = r.Id, Title = r.Title, Key = url };
                })
                .ToList();
            var removeFolders = plan.ToRemoveFolders
                .Select(f => (object)new { path = f.Path?.ToList() ?? new List<string>(), title = f.Title ?? "" })
                .ToList();

            var readyBrowsers = _wsHandler.ReadyBrowsers.ToList();
            ApplyAggregateDto? applySnapshot = null;
            if (readyBrowsers.Count > 0 && (applyItems.Count > 0 || removeUrls.Count > 0 || removeFolders.Count > 0))
            {
                Report($"下发书签到浏览器（书签 {applyItems.Count} · 删URL {removeUrls.Count} · 删文件夹 {removeFolders.Count}）：{string.Join("、", readyBrowsers)}…");
                try
                {
                    var applyResult = await _wsHandler.ApplyAsync(
                        applyItems, removeUrls, renameWithUrl, TimeSpan.FromSeconds(45), ct, removeFolders);
                    applySnapshot = applyResult;

                    if (applyResult != null)
                    {
                        result.Added = applyResult.TotalAdded;
                        result.Removed = applyResult.TotalRemoved;
                        result.Renamed = applyResult.TotalRenamed;
                        foreach (var r in applyResult.Results)
                        {
                            foreach (var e in r.Errors)
                                result.Errors.Add($"[{r.Browser}] {e}");
                            // 「文件夹非空跳过删除」等属预期保护，不计入失败
                            foreach (var w in r.WarningList)
                                Report($"[{r.Browser}] {w}");
                            if (!r.Ok && r.Errors.Count == 0)
                                result.Errors.Add($"[{r.Browser}] 写回未成功（+{r.Added} -{r.Removed} ~{r.Renamed}）");
                        }
                        if (applyResult.Results.Count == 0)
                            result.Errors.Add("无浏览器返回写回结果");
                        Report($"扩展写回汇总：{applyResult.Summary}");
                        foreach (var r in applyResult.Results)
                            Log.Information("写回明细 {Browser}: ok={Ok} +{Added} -{Removed} ~{Renamed}",
                                r.Browser, r.Ok, r.Added, r.Removed, r.Renamed);
                    }
                    else
                    {
                        Report("扩展写回超时（无浏览器返回结果）");
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "扩展写回失败");
                    Report($"扩展写回失败：{ex.Message}");
                    result.Errors.Add(ex.Message);
                }
            }
            else if (plan.ToAddLocal.Count > 0 || plan.ToRemoveLocal.Count > 0 || plan.ToRenameLocal.Count > 0)
            {
                Report("当前无已连接浏览器扩展，跳过写回（仅更新本地库与云端）");
            }
            else
            {
                Report("合并集为空或无浏览器可写回");
            }

            // 6) 应用 plan 到 DB（权威集合 = merged）
            Report("写入本地库…");
            await ApplyPlanToDbAsync(plan, exports.Keys.FirstOrDefault() ?? "cloud", ct);

            // 7) 导出 DB 权威 items → 上传 WebDAV
            var afterEntities = await _repository.GetAllActiveAsync(ct);
            var afterItems = afterEntities.Select(BrowserBookmarkRepository.ToFlatItem).ToList();
            result.LocalTotal = afterItems.Count;

            Report($"上传云端：{afterItems.Count} 条…");
            var payload = new
            {
                version = 3,
                updatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                stats = new
                {
                    bookmarks = afterItems.Count(i => i.Type == FlatItemType.Bookmark),
                    folders = afterItems.Count(i => i.Type == FlatItemType.Folder),
                    total = afterItems.Count
                },
                items = afterItems.Select(i => new
                {
                    type = i.Type == FlatItemType.Bookmark ? "bookmark" : "folder",
                    title = i.Title,
                    url = i.Url,
                    path = i.Path,
                    dateAdded = i.DateAdded,
                    dateModified = i.DateModified,
                    key = i.Key
                })
            };

            var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
            var uploadPath = await _webDav.UploadFileAsync(url, user, pass, CloudFilePath, Encoding.UTF8.GetBytes(payloadJson), ct);
            if (uploadPath != null)
            {
                Report("云端上传成功");
            }
            else
            {
                Report("云端上传失败");
                result.Errors.Add("云端上传失败");
            }

            // 8) 保存基线 + meta（含各浏览器导出快照，供下次删除检测）
            var itemsJson = JsonSerializer.Serialize(payload.items, JsonOptions);
            await _repository.SetBaselineAsync(itemsJson, ct);
            await _repository.SetMetaAsync(BrowserSyncMetaKeys.BookmarksLastSyncAt,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(), ct);

            // 用「同步后权威集合」更新各浏览器 last_exports（写回成功的浏览器），
            // 否则快照仍是写回前的残缺导出，用户删除无法被 DetectBrowserDeletions 识别。
            var lastExportsJson2 = await _repository.GetMetaAsync(BrowserSyncMetaKeys.BrowserLastExports, ct);
            var newLast = DeserializeBrowserExports(lastExportsJson2);
            foreach (var kv in exports)
            {
                if (!newLast.ContainsKey(kv.Key))
                    newLast[kv.Key] = kv.Value;
            }
            // apply 结果若有，写回成功的浏览器以 afterItems 为期望状态
            if (applySnapshot != null)
            {
                foreach (var r in applySnapshot.Results)
                {
                    if (string.IsNullOrEmpty(r.Browser)) continue;
                    if (r.Ok || r.Added > 0 || r.Removed > 0)
                    {
                        newLast[r.Browser] = afterItems
                            .Select(i => new FlatItem
                            {
                                Type = i.Type,
                                Id = i.Id,
                                Title = i.Title,
                                Url = i.Url,
                                Path = i.Path?.ToList() ?? new List<string>(),
                                DateAdded = i.DateAdded,
                                DateModified = i.DateModified,
                                Key = i.Key
                            })
                            .ToList();
                    }
                    else if (!newLast.ContainsKey(r.Browser) && exports.TryGetValue(r.Browser, out var exp))
                    {
                        newLast[r.Browser] = exp;
                    }
                }
            }
            await _repository.SetMetaAsync(BrowserSyncMetaKeys.BrowserLastExports, SerializeBrowserExports(newLast), ct);

            result.Success = result.Errors.Count == 0;
            result.Message = $"新增 {result.Added} · 删除 {result.Removed} · 改名 {result.Renamed} · 本机共 {result.LocalTotal}";
            if (result.Errors.Count > 0)
            {
                result.Message += $" · 错误 {result.Errors.Count}";
            }

            Report("同步完成：" + result.Message);
            return result;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "书签同步失败");
            result.Success = false;
            result.Message = $"同步失败：{ex.Message}";
            result.Errors.Add(ex.Message);
            return result;
        }
    }

    /// <summary>将 plan 应用到 DB：upsert merged、删除 toRemove（书签+空文件夹）。</summary>
    private async Task ApplyPlanToDbAsync(MergePlan plan, string sourceBrowser, CancellationToken ct)
    {
        // 删除：他端删的 bookmark + 无内容/已删文件夹
        var deleteKeys = new List<string>();
        foreach (var i in plan.ToRemoveLocal)
        {
            var k = BookmarkMergePlanner.EnsureKey(i);
            if (!string.IsNullOrEmpty(k)) deleteKeys.Add(k);
        }
        foreach (var f in plan.ToRemoveFolders)
        {
            var k = BookmarkMergePlanner.EnsureKey(f);
            if (!string.IsNullOrEmpty(k)) deleteKeys.Add(k);
        }
        if (deleteKeys.Count > 0)
            await _repository.DeleteByKeysAsync(deleteKeys, ct);

        // upsert 合并结果（含 rename 后的标题；不含已 prune 的孤儿文件夹）
        var entities = plan.MergedCloud.Select(i => BrowserBookmarkRepository.ToEntity(i, sourceBrowser)).ToList();
        await _repository.UpsertManyAsync(entities, ct);
    }

    public async Task<BookmarkSyncResult> ResetBaselineAsync(CancellationToken ct = default)
    {
        await _repository.SetBaselineAsync("[]", ct);
        // 保留 BrowserLastExports：浏览器删除检测不依赖全局 baseline
        Log.Information("[BookmarkSync] 基线已重置；下次同步不做「云端相对基线」删除，但仍可检测浏览器侧删除");
        return new BookmarkSyncResult
        {
            Success = true,
            Message = "同步基线已重置。云端基线删除已关闭；浏览器侧删除（相对上次快照）仍会传播。"
        };
    }

    /// <summary>兼容解析：v3 items、带 tree 的旧格式、纯数组。</summary>
    internal static List<FlatItem> ParseCloudPayload(string json)
    {
        return BrowserBookmarkRepository.DeserializeItems(json);
    }

    /// <summary>各浏览器导出快照：{"浏览器名": [item, ...]}，items 为对象数组（勿再 stringify 一层）。</summary>
    private static string SerializeBrowserExports(IReadOnlyDictionary<string, List<FlatItem>> exports)
    {
        if (exports == null || exports.Count == 0) return "{}";
        var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in exports)
        {
            dict[kv.Key] = kv.Value.Select(i => new
            {
                type = i.Type == FlatItemType.Bookmark ? "bookmark" : "folder",
                title = i.Title,
                url = i.Url,
                path = i.Path,
                dateAdded = i.DateAdded,
                dateModified = i.DateModified,
                key = i.Key
            }).ToList();
        }
        return JsonSerializer.Serialize(dict, JsonOptions);
    }

    /// <summary>解析各浏览器导出快照；兼容旧「字符串套 JSON」格式。损坏时返回空字典（本次不判删除，安全）。</summary>
    private static Dictionary<string, List<FlatItem>> DeserializeBrowserExports(string? json)
    {
        var result = new Dictionary<string, List<FlatItem>>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}") return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    // 旧格式：值是 JSON 文本
                    result[prop.Name] = BrowserBookmarkRepository.DeserializeItems(prop.Value.GetString());
                }
                else
                {
                    result[prop.Name] = BrowserBookmarkRepository.DeserializeItems(prop.Value.GetRawText());
                }
            }
            Log.Information("已加载浏览器导出快照: {Browsers}",
                string.Join(", ", result.Select(kv => $"{kv.Key}={kv.Value.Count}")));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "解析浏览器导出快照失败，本次跳过浏览器删除检测");
        }
        return result;
    }
}
