using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PChabit.Core.Entities;
using PChabit.Core.Interfaces;
using PChabit.Core.Sync;
using PChabit.Infrastructure.Data;
using Serilog;

namespace PChabit.Infrastructure.Services;

/// <summary>
/// BrowserBookmark / 基线 / 元数据仓储。DB 是书签同步的 source of truth。
/// </summary>
public class BrowserBookmarkRepository : IBrowserBookmarkRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static int _tablesEnsured;
    private static readonly SemaphoreSlim _ensureLock = new(1, 1);
    private static readonly SemaphoreSlim _upsertLock = new(1, 1);
    private readonly IDbContextFactory<PChabitDbContext> _contextFactory;

    public BrowserBookmarkRepository(IDbContextFactory<PChabitDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <summary>启动迁移可能失败（旧进程锁库等），首次访问时兜底建表。并发安全、失败不重试风暴。</summary>
    private static async Task EnsureTablesAsync(PChabitDbContext context, CancellationToken ct)
    {
        if (Volatile.Read(ref _tablesEnsured) == 1) return;

        await _ensureLock.WaitAsync(ct);
        try
        {
            if (Volatile.Read(ref _tablesEnsured) == 1) return;

            // 单独短超时连接，避免锁库时拖死调用方
            await context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS BrowserBookmarks (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Type TEXT NOT NULL,
                    Title TEXT NOT NULL,
                    Url TEXT,
                    PathJson TEXT NOT NULL DEFAULT '[]',
                    DateAdded INTEGER NOT NULL DEFAULT 0,
                    DateModified INTEGER NOT NULL DEFAULT 0,
                    SourceBrowser TEXT NOT NULL DEFAULT '',
                    IsDeleted INTEGER NOT NULL DEFAULT 0,
                    UpdatedAt INTEGER NOT NULL DEFAULT 0
                );
                CREATE INDEX IF NOT EXISTS IX_BrowserBookmarks_Url ON BrowserBookmarks (Url);
                CREATE INDEX IF NOT EXISTS IX_BrowserBookmarks_IsDeleted ON BrowserBookmarks (IsDeleted);
                CREATE INDEX IF NOT EXISTS IX_BrowserBookmarks_Type_Title ON BrowserBookmarks (Type, Title);
                CREATE TABLE IF NOT EXISTS BookmarkSyncBaselines (
                    Id INTEGER NOT NULL PRIMARY KEY,
                    SavedAt INTEGER NOT NULL DEFAULT 0,
                    ItemsJson TEXT NOT NULL DEFAULT '[]'
                );
                CREATE TABLE IF NOT EXISTS BrowserSyncMetas (
                    Key TEXT NOT NULL PRIMARY KEY,
                    Value TEXT NOT NULL DEFAULT ''
                );
                CREATE TABLE IF NOT EXISTS BrowserHistoryItems (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Url TEXT NOT NULL,
                    Title TEXT NOT NULL DEFAULT '',
                    VisitTime INTEGER NOT NULL DEFAULT 0,
                    VisitCount INTEGER NOT NULL DEFAULT 0,
                    SourceBrowser TEXT NOT NULL DEFAULT '',
                    IngestedAt INTEGER NOT NULL DEFAULT 0
                );
                CREATE INDEX IF NOT EXISTS IX_BrowserHistoryItems_VisitTime ON BrowserHistoryItems (VisitTime);
                CREATE INDEX IF NOT EXISTS IX_BrowserHistoryItems_Url ON BrowserHistoryItems (Url);
            ", ct);

            Volatile.Write(ref _tablesEnsured, 1);
            Log.Information("BrowserBookmark 相关表已确保存在");
        }
        catch (Exception ex)
        {
            // 标记已尝试，避免锁库时每个请求都重试导致线程池耗尽
            Volatile.Write(ref _tablesEnsured, 1);
            Log.Warning(ex, "BrowserBookmark 建表失败（可能数据库被占用），本次跳过");
        }
        finally
        {
            _ensureLock.Release();
        }
    }

    public async Task UpsertManyAsync(IEnumerable<BrowserBookmark> items, CancellationToken ct = default)
    {
        if (items == null) return;
        // 批内去重：Chrome/Edge 可能导出相同 key
        var list = items
            .Where(i => !string.IsNullOrEmpty(i.Id))
            .GroupBy(i => i.Id)
            .Select(g => g.OrderBy(x => x.DateModified).Last())
            .ToList();
        if (list.Count == 0) return;

        await _upsertLock.WaitAsync(ct);
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);
            await EnsureTablesAsync(context, ct);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var ids = list.Select(i => i.Id).ToList();
            var existingMap = await context.BrowserBookmarks
                .AsNoTracking()
                .Where(x => ids.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, ct);

            foreach (var item in list)
            {
                item.UpdatedAt = now;
                if (existingMap.TryGetValue(item.Id, out var existing))
                {
                    context.BrowserBookmarks.Attach(existing);

                    // 改名保护：DateModified 更旧的导出不得覆盖更新后的标题。
                    // 否则 Chrome/Edge/豆包交错 upsert 会把用户刚改的名字写回旧值。
                    var incomingIsStaleRename =
                        string.Equals(item.Type, BrowserBookmarkTypes.Bookmark, StringComparison.OrdinalIgnoreCase)
                        && existing.DateModified > item.DateModified
                        && !string.Equals(existing.Title, item.Title, StringComparison.Ordinal)
                        && !string.IsNullOrEmpty(existing.Title);

                    if (incomingIsStaleRename)
                    {
                        // 仍刷新路径/来源等元数据，但保留较新的 Title/DateModified
                        existing.Url = item.Url;
                        existing.PathJson = item.PathJson;
                        if (item.DateAdded > 0) existing.DateAdded = item.DateAdded;
                        existing.SourceBrowser = item.SourceBrowser;
                        existing.IsDeleted = item.IsDeleted;
                        existing.UpdatedAt = now;
                        context.Entry(existing).State = EntityState.Modified;
                    }
                    else
                    {
                        existing.Type = item.Type;
                        existing.Title = item.Title;
                        existing.Url = item.Url;
                        existing.PathJson = item.PathJson;
                        existing.DateAdded = item.DateAdded;
                        existing.DateModified = item.DateModified;
                        existing.SourceBrowser = item.SourceBrowser;
                        existing.IsDeleted = item.IsDeleted;
                        existing.UpdatedAt = now;
                        context.Entry(existing).State = EntityState.Modified;
                    }
                }
                else
                {
                    context.BrowserBookmarks.Add(item);
                    // 防止同一批次后续相同 key 再次 Add
                    existingMap[item.Id] = item;
                }
            }

            await context.SaveChangesAsync(ct);
            Log.Information("BrowserBookmark upsert {Count} 条", list.Count);
        }
        finally
        {
            _upsertLock.Release();
        }
    }

    public async Task DeleteByKeysAsync(IEnumerable<string> keys, CancellationToken ct = default)
    {
        var keyList = keys?.Where(k => !string.IsNullOrEmpty(k)).Distinct().ToList();
        if (keyList == null || keyList.Count == 0) return;

        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        await EnsureTablesAsync(context, ct);
        var toDelete = await context.BrowserBookmarks
            .Where(x => keyList.Contains(x.Id))
            .ToListAsync(ct);

        if (toDelete.Count == 0) return;

        context.BrowserBookmarks.RemoveRange(toDelete);
        await context.SaveChangesAsync(ct);
        Log.Debug("BrowserBookmark 物理删除 {Count} 条", toDelete.Count);
    }

    public async Task<List<BrowserBookmark>> GetAllActiveAsync(CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        await EnsureTablesAsync(context, ct);
        return await context.BrowserBookmarks
            .Where(x => !x.IsDeleted)
            .ToListAsync(ct);
    }

    public async Task<List<BrowserBookmark>> GetAllAsync(CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        await EnsureTablesAsync(context, ct);
        return await context.BrowserBookmarks.ToListAsync(ct);
    }

    public async Task SetBaselineAsync(string itemsJson, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        await EnsureTablesAsync(context, ct);
        var baseline = await context.BookmarkSyncBaselines.FirstOrDefaultAsync(ct);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (baseline == null)
        {
            context.BookmarkSyncBaselines.Add(new BookmarkSyncBaseline
            {
                Id = 1,
                SavedAt = now,
                ItemsJson = itemsJson ?? "[]"
            });
        }
        else
        {
            baseline.SavedAt = now;
            baseline.ItemsJson = itemsJson ?? "[]";
        }

        await context.SaveChangesAsync(ct);
    }

    public async Task<string?> GetBaselineAsync(CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        await EnsureTablesAsync(context, ct);
        var baseline = await context.BookmarkSyncBaselines.FirstOrDefaultAsync(ct);
        return baseline?.ItemsJson;
    }

    public async Task SetMetaAsync(string key, string value, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(key)) return;

        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        await EnsureTablesAsync(context, ct);
        var meta = await context.BrowserSyncMetas.FirstOrDefaultAsync(x => x.Key == key, ct);

        if (meta == null)
        {
            context.BrowserSyncMetas.Add(new BrowserSyncMeta { Key = key, Value = value ?? string.Empty });
        }
        else
        {
            meta.Value = value ?? string.Empty;
        }

        await context.SaveChangesAsync(ct);
    }

    public async Task<string?> GetMetaAsync(string key, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(key)) return null;
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var meta = await context.BrowserSyncMetas.FirstOrDefaultAsync(x => x.Key == key, ct);
        return meta?.Value;
    }

    public async Task<int> GetActiveCountAsync(CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        await EnsureTablesAsync(context, ct);
        return await context.BrowserBookmarks.CountAsync(x => !x.IsDeleted, ct);
    }

    public async Task<Dictionary<string, int>> GetCountsBySourceAsync(CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        await EnsureTablesAsync(context, ct);
        return await context.BrowserBookmarks
            .Where(x => !x.IsDeleted)
            .GroupBy(x => x.SourceBrowser)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => string.IsNullOrEmpty(x.Key) ? "未知" : x.Key, x => x.Count, ct);
    }

    public async Task<Dictionary<string, int>> GetCountsByTypeAsync(CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        await EnsureTablesAsync(context, ct);
        return await context.BrowserBookmarks
            .Where(x => !x.IsDeleted)
            .GroupBy(x => x.Type)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
    }

    public async Task<Dictionary<string, int>> GetTopFoldersAsync(int top = 8, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        await EnsureTablesAsync(context, ct);
        // PathJson 形如 ["书签栏","Work"]，取第一段作根文件夹统计
        var folders = await context.BrowserBookmarks
            .Where(x => !x.IsDeleted && x.Type == BrowserBookmarkTypes.Bookmark)
            .Select(x => x.PathJson)
            .ToListAsync(ct);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var pj in folders)
        {
            var root = ParseFirstPathSegment(pj);
            if (string.IsNullOrEmpty(root)) root = "(根目录)";
            counts[root] = counts.TryGetValue(root, out var c) ? c + 1 : 1;
        }

        return counts
            .OrderByDescending(kv => kv.Value)
            .Take(top)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    private static string ParseFirstPathSegment(string? pathJson)
    {
        if (string.IsNullOrWhiteSpace(pathJson)) return "";
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(pathJson);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (el.ValueKind == System.Text.Json.JsonValueKind.String)
                        return el.GetString() ?? "";
                }
            }
        }
        catch { }
        return "";
    }

    public static string SerializeItems(IEnumerable<FlatItem> items)
    {
        var dto = items.Select(i => new
        {
            type = i.Type == FlatItemType.Bookmark ? "bookmark" : "folder",
            title = i.Title,
            url = i.Url,
            path = i.Path,
            dateAdded = i.DateAdded,
            dateModified = i.DateModified,
            key = i.Key
        });
        return JsonSerializer.Serialize(dto, JsonOptions);
    }

    public static List<FlatItem> DeserializeItems(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<FlatItem>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var array = root.ValueKind == JsonValueKind.Array
                ? root
                : root.TryGetProperty("items", out var itemsEl) ? itemsEl : default;

            if (array.ValueKind != JsonValueKind.Array)
                return new List<FlatItem>();

            var result = new List<FlatItem>();
            foreach (var el in array.EnumerateArray())
            {
                var typeStr = el.TryGetProperty("type", out var t) ? t.GetString() : "bookmark";
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
                    Type = string.Equals(typeStr, "folder", StringComparison.OrdinalIgnoreCase)
                        ? FlatItemType.Folder
                        : FlatItemType.Bookmark,
                    Title = title,
                    Url = url,
                    Path = path,
                    DateAdded = el.TryGetProperty("dateAdded", out var da) ? da.GetInt64() : 0,
                    DateModified = el.TryGetProperty("dateModified", out var dm) ? dm.GetInt64() : 0,
                    Key = el.TryGetProperty("key", out var k) ? k.GetString() ?? "" : ""
                };

                if (string.IsNullOrEmpty(item.Key))
                    item.Key = BookmarkMergePlanner.ItemKey(item);

                result.Add(item);
            }

            return result;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "解析书签基线/云端 items JSON 失败");
            return new List<FlatItem>();
        }
    }

    public static BrowserBookmark ToEntity(FlatItem item, string sourceBrowser)
    {
        // 入库即规范化：Edge「收藏夹栏」= Chrome「书签栏」，避免 UI 出现重复文件夹
        var type = item.Type == FlatItemType.Folder
            ? BrowserBookmarkTypes.Folder
            : BrowserBookmarkTypes.Bookmark;
        var title = item.Title ?? "";
        var rawPath = item.Path?.ToList() ?? new List<string>();
        List<string> parent;
        string id;
        if (item.Type == FlatItemType.Folder)
        {
            var full = BookmarkMergePlanner.CanonicalFullPath("folder", title, rawPath);
            if (full.Count == 0)
            {
                parent = new List<string>();
                title = title.Trim();
                id = BookmarkMergePlanner.EnsureKey(item);
            }
            else
            {
                parent = full.Take(full.Count - 1).ToList();
                title = full[^1];
                id = "f:" + string.Join("/", parent.Append(title));
            }
        }
        else
        {
            parent = BookmarkMergePlanner.CanonicalizePath(rawPath);
            id = string.IsNullOrEmpty(item.Url)
                ? BookmarkMergePlanner.EnsureKey(item)
                : "b:" + (item.Url ?? "").Trim();
        }

        return new BrowserBookmark
        {
            Id = id,
            Type = type,
            Title = title,
            Url = item.Url,
            PathJson = JsonSerializer.Serialize(parent),
            DateAdded = item.DateAdded,
            DateModified = item.DateModified,
            SourceBrowser = sourceBrowser,
            IsDeleted = false,
            UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    public static FlatItem ToFlatItem(BrowserBookmark entity)
    {
        var path = new List<string>();
        if (!string.IsNullOrEmpty(entity.PathJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(entity.PathJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var s in doc.RootElement.EnumerateArray())
                    {
                        if (s.ValueKind == JsonValueKind.String)
                            path.Add(s.GetString() ?? "");
                    }
                }
            }
            catch
            {
                // PathJson 损坏时视为空路径
            }
        }

        return new FlatItem
        {
            Type = entity.Type == BrowserBookmarkTypes.Folder
                ? FlatItemType.Folder
                : FlatItemType.Bookmark,
            Id = entity.Id,
            Title = entity.Title,
            Url = entity.Url,
            Path = path,
            DateAdded = entity.DateAdded,
            DateModified = entity.DateModified,
            Key = entity.Id
        };
    }
}
