using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PChabit.Core.Entities;
using PChabit.Infrastructure.Data;
using Serilog;

namespace PChabit.Infrastructure.Services;

/// <summary>书签库树节点。FullPath 由 Path+Title 推导；请用 FromRow 保证与 Id 一致。</summary>
public record BookmarkLibraryNode(
    string Id,
    string Type,
    string Title,
    string? Url,
    IReadOnlyList<string> Path,
    string SourceBrowser,
    long DateModified)
{
    public string FullPath => Type == BrowserBookmarkTypes.Folder
        ? (Path.Count == 0 ? Title : string.Join("/", Path) + "/" + Title)
        : string.Join("/", Path);
    public string PathDisplay => Path.Count == 0 ? "（根）" : string.Join(" / ", Path);
    public bool IsFolder => Type == BrowserBookmarkTypes.Folder;
}

public record BookmarkLibraryStats(
    int Bookmarks,
    int Folders,
    int Deleted,
    int FoldersInTree);

/// <summary>
/// 书签库中枢：唯一用户编辑面。整树 CRUD + 软删回收站。
/// 删除默认软删（IsDeleted=1，UpdatedAt=删除时间），30 天内可恢复。
/// </summary>
public class BookmarkLibraryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public const int RecycleDays = 30;

    private readonly IDbContextFactory<PChabitDbContext> _dbFactory;

    public BookmarkLibraryService(IDbContextFactory<PChabitDbContext> dbFactory)
        => _dbFactory = dbFactory;

    private static List<string> ParsePath(string? pathJson)
    {
        if (string.IsNullOrWhiteSpace(pathJson)) return new List<string>();
        try
        {
            var arr = JsonSerializer.Deserialize<List<string>>(pathJson, JsonOptions);
            return PChabit.Core.Sync.BookmarkMergePlanner.CanonicalizePath(
                arr?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList());
        }
        catch
        {
            return new List<string>();
        }
    }

    private static string SerializePath(IReadOnlyList<string> path)
        => JsonSerializer.Serialize(
            PChabit.Core.Sync.BookmarkMergePlanner.CanonicalizePath(path),
            JsonOptions);

    public static string FolderId(IReadOnlyList<string> path, string title)
    {
        var canon = PChabit.Core.Sync.BookmarkMergePlanner.CanonicalizePath(path);
        var segs = PChabit.Core.Sync.BookmarkMergePlanner.SplitPathString(title);
        var t = segs.Count > 0 ? segs[^1] : (title ?? "").Trim();
        var parent = segs.Count > 1 ? canon.Concat(segs.Take(segs.Count - 1)).ToList() : canon;
        return "f:" + string.Join("/", parent.Append(t ?? ""));
    }

    public static string BookmarkId(string url)
        => "b:" + (url?.Trim() ?? "");

    public async Task<List<BookmarkLibraryNode>> GetActiveAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.BrowserBookmarks.AsNoTracking()
            .Where(x => !x.IsDeleted)
            .ToListAsync(ct);
        var nodes = rows.Select(ToNode).ToList();
        // 同一规范路径只显示一个文件夹（Chrome 书签栏/工具 与 Edge 收藏夹栏/工具）
        var seenFolder = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenBookmark = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dedup = new List<BookmarkLibraryNode>();
        foreach (var n in nodes)
        {
            if (n.IsFolder)
            {
                if (!seenFolder.Add(n.FullPath)) continue;
            }
            else
            {
                var key = n.Url ?? n.Id;
                if (!seenBookmark.Add(key)) continue;
            }
            dedup.Add(n);
        }
        return dedup.OrderBy(n => n.IsFolder ? 0 : 1)
            .ThenBy(n => n.FullPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(n => n.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<List<BookmarkLibraryNode>> GetChildrenAsync(string? folderPath, CancellationToken ct = default)
    {
        var all = await GetActiveAsync(ct);
        var path = string.IsNullOrEmpty(folderPath)
            ? new List<string>()
            : folderPath.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();

        return all.Where(n =>
        {
            if (path.Count == 0)
                return n.Path.Count == 0 || (n.IsFolder && n.Path.Count == 0);
            // 文件夹：Path == path；书签：Path == path
            return n.Path.Count == path.Count
                   && n.Path.Select((s, i) => string.Equals(s, path[i], StringComparison.OrdinalIgnoreCase))
                       .All(x => x);
        }).ToList();
    }

    public async Task<List<BookmarkLibraryNode>> SearchAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return await GetActiveAsync(ct);
        var q = query.Trim();
        var all = await GetActiveAsync(ct);
        return all.Where(n =>
            n.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
            || (n.Url != null && n.Url.Contains(q, StringComparison.OrdinalIgnoreCase))
            || n.FullPath.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public async Task<List<BookmarkLibraryNode>> GetRecycleBinAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-RecycleDays).ToUnixTimeMilliseconds();
        var rows = await db.BrowserBookmarks.AsNoTracking()
            .Where(x => x.IsDeleted && x.UpdatedAt >= cutoff)
            .ToListAsync(ct);
        return rows.Select(ToNode).OrderByDescending(n => n.DateModified).ToList();
    }

    public async Task<BookmarkLibraryStats> GetStatsAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var bm = await db.BrowserBookmarks.CountAsync(x => !x.IsDeleted && x.Type == BrowserBookmarkTypes.Bookmark, ct);
        var fd = await db.BrowserBookmarks.CountAsync(x => !x.IsDeleted && x.Type == BrowserBookmarkTypes.Folder, ct);
        var del = await db.BrowserBookmarks.CountAsync(x => x.IsDeleted, ct);
        var foldersInTree = (await db.BrowserBookmarks.AsNoTracking()
            .Where(x => !x.IsDeleted && x.Type == BrowserBookmarkTypes.Folder)
            .Select(x => x.PathJson)
            .ToListAsync(ct))
            .Select(ParsePath)
            .Select(p => string.Join("/", p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        return new BookmarkLibraryStats(bm, fd, del, foldersInTree);
    }

    public async Task EnsureFolderAsync(IReadOnlyList<string> path, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await EnsureFolderInDbAsync(db, path, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>按需创建整条文件夹链（含根级）。</summary>
    private static async Task EnsureFolderInDbAsync(PChabitDbContext db, IReadOnlyList<string> path, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var acc = new List<string>();
        foreach (var seg in path)
        {
            var title = seg.Trim();
            if (title.Length == 0) continue;
            var parent = acc.ToList();
            var id = FolderId(parent, title);
            var exists = await db.BrowserBookmarks.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id, ct);
            if (exists == null)
            {
                db.BrowserBookmarks.Add(new BrowserBookmark
                {
                    Id = id,
                    Type = BrowserBookmarkTypes.Folder,
                    Title = title,
                    Url = null,
                    PathJson = SerializePath(parent),
                    DateAdded = now,
                    DateModified = now,
                    SourceBrowser = "library",
                    IsDeleted = false,
                    UpdatedAt = now
                });
            }
            else if (exists.IsDeleted)
            {
                exists.IsDeleted = false;
                exists.UpdatedAt = now;
                db.BrowserBookmarks.Update(exists);
            }
            acc.Add(title);
        }
    }

    public async Task<BookmarkLibraryNode> CreateFolderAsync(IReadOnlyList<string> parentPath, string title, CancellationToken ct = default)
    {
        var segs = PChabit.Core.Sync.BookmarkMergePlanner.SplitPathString(title);
        if (segs.Count == 0) throw new ArgumentException("文件夹名不能为空");
        var path = PChabit.Core.Sync.BookmarkMergePlanner.CanonicalizePath(parentPath);
        // 若标题本身是「书签栏/工具」则最后一段为文件夹名，前面段并入父路径
        if (segs.Count > 1)
        {
            foreach (var s in segs.Take(segs.Count - 1)) path.Add(s);
            title = segs[^1];
        }
        else
        {
            title = segs[0];
        }
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await EnsureFolderInDbAsync(db, path, ct);
        var id = FolderId(path, title);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var existing = await db.BrowserBookmarks.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (existing != null)
        {
            existing.IsDeleted = false;
            existing.UpdatedAt = now;
            existing.DateModified = now;
        }
        else
        {
            db.BrowserBookmarks.Add(new BrowserBookmark
            {
                Id = id,
                Type = BrowserBookmarkTypes.Folder,
                Title = title,
                Url = null,
                PathJson = SerializePath(path),
                DateAdded = now,
                DateModified = now,
                SourceBrowser = "library",
                IsDeleted = false,
                UpdatedAt = now
            });
        }
        await db.SaveChangesAsync(ct);
        return new BookmarkLibraryNode(id, BrowserBookmarkTypes.Folder, title, null, path, "library", now);
    }

    public async Task<BookmarkLibraryNode> CreateBookmarkAsync(IReadOnlyList<string> path, string title, string url, CancellationToken ct = default)
    {
        url = (url ?? "").Trim();
        title = string.IsNullOrWhiteSpace(title) ? url : title.Trim();
        if (url.Length == 0) throw new ArgumentException("URL 不能为空");
        // 分类若是「书签栏/工具」则拆成路径，避免建成「书签栏 / 工具」单个文件夹名
        var parent = PChabit.Core.Sync.BookmarkMergePlanner.CanonicalizePath(path);
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await EnsureFolderInDbAsync(db, parent, ct);
        var id = BookmarkId(url);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var row = await db.BrowserBookmarks.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row != null)
        {
            row.Title = title;
            row.PathJson = SerializePath(parent);
            row.IsDeleted = false;
            row.DateModified = now;
            row.UpdatedAt = now;
        }
        else
        {
            db.BrowserBookmarks.Add(new BrowserBookmark
            {
                Id = id,
                Type = BrowserBookmarkTypes.Bookmark,
                Title = title,
                Url = url,
                PathJson = SerializePath(parent),
                DateAdded = now,
                DateModified = now,
                SourceBrowser = "library",
                IsDeleted = false,
                UpdatedAt = now
            });
        }
        await db.SaveChangesAsync(ct);
        return new BookmarkLibraryNode(id, BrowserBookmarkTypes.Bookmark, title, url, parent, "library", now);
    }

    /// <summary>重命名：书签改标题；文件夹改标题并级联更新子路径。</summary>
    public async Task RenameAsync(string id, string newTitle, CancellationToken ct = default)
    {
        newTitle = (newTitle ?? "").Trim();
        if (newTitle.Length == 0) throw new ArgumentException("名称不能为空");
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.BrowserBookmarks.FirstOrDefaultAsync(x => x.Id == id, ct)
                  ?? throw new InvalidOperationException("条目不存在");
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (row.Type == BrowserBookmarkTypes.Bookmark)
        {
            row.Title = newTitle;
            row.DateModified = now;
            row.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            return;
        }

        var oldTitle = row.Title;
        var path = ParsePath(row.PathJson);
        var oldFull = string.Join("/", path.Append(oldTitle));
        var newFull = string.Join("/", path.Append(newTitle));
        var oldId = FolderId(path, oldTitle);
        var newId = FolderId(path, newTitle);

        // 更新自身 Id 需删旧插新（主键）
        db.BrowserBookmarks.Remove(row);
        await db.SaveChangesAsync(ct);
        db.BrowserBookmarks.Add(new BrowserBookmark
        {
            Id = newId,
            Type = BrowserBookmarkTypes.Folder,
            Title = newTitle,
            Url = null,
            PathJson = SerializePath(path),
            DateAdded = row.DateAdded,
            DateModified = now,
            SourceBrowser = row.SourceBrowser,
            IsDeleted = false,
            UpdatedAt = now
        });
        await db.SaveChangesAsync(ct);
        await CascadeUpdatePathsAsync(db, oldFull, newFull, now, ct);
    }

    /// <summary>移动到新父路径（书签/文件夹）。文件夹级联改子路径。</summary>
    public async Task MoveAsync(string id, IReadOnlyList<string> newParentPath, CancellationToken ct = default)
    {
        var newParent = newParentPath?.ToList() ?? new List<string>();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.BrowserBookmarks.FirstOrDefaultAsync(x => x.Id == id, ct)
                  ?? throw new InvalidOperationException("条目不存在");
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await EnsureFolderInDbAsync(db, newParent, ct);

        if (row.Type == BrowserBookmarkTypes.Bookmark)
        {
            row.PathJson = SerializePath(newParent);
            row.DateModified = now;
            row.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            return;
        }

        var oldPath = ParsePath(row.PathJson);
        var oldFull = string.Join("/", oldPath.Append(row.Title));
        var newFull = string.Join("/", newParent.Append(row.Title));
        if (string.Equals(oldFull, newFull, StringComparison.OrdinalIgnoreCase))
        {
            await db.SaveChangesAsync(ct);
            return;
        }

        db.BrowserBookmarks.Remove(row);
        await db.SaveChangesAsync(ct);
        db.BrowserBookmarks.Add(new BrowserBookmark
        {
            Id = FolderId(newParent, row.Title),
            Type = BrowserBookmarkTypes.Folder,
            Title = row.Title,
            Url = null,
            PathJson = SerializePath(newParent),
            DateAdded = row.DateAdded,
            DateModified = now,
            SourceBrowser = row.SourceBrowser,
            IsDeleted = false,
            UpdatedAt = now
        });
        await db.SaveChangesAsync(ct);
        await CascadeUpdatePathsAsync(db, oldFull, newFull, now, ct);
    }

    /// <summary>文件夹改名/移动后，更新所有子孙 PathJson / Id。</summary>
    private static async Task CascadeUpdatePathsAsync(
        PChabitDbContext db, string oldFolderFull, string newFolderFull, long now, CancellationToken ct)
    {
        var oldPrefix = oldFolderFull + "/";
        var rows = await db.BrowserBookmarks.ToListAsync(ct);
        var changed = new List<BrowserBookmark>();
        foreach (var r in rows)
        {
            var path = ParsePath(r.PathJson);
            string full;
            if (r.Type == BrowserBookmarkTypes.Folder)
                full = string.Join("/", path.Append(r.Title));
            else
                full = string.Join("/", path);

            // 子文件夹：full 以 oldPrefix 开头；书签：path 前缀匹配
            if (r.Type == BrowserBookmarkTypes.Folder)
            {
                if (!full.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(full, oldFolderFull, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.Equals(full, oldFolderFull, StringComparison.OrdinalIgnoreCase))
                    continue; // 自身已在别处处理
                var newChildFull = newFolderFull + full[oldFolderFull.Length..];
                var parts = newChildFull.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
                var newTitle = parts[^1];
                var newParent = parts.Take(parts.Count - 1).ToList();
                db.BrowserBookmarks.Remove(r);
                changed.Add(new BrowserBookmark
                {
                    Id = FolderId(newParent, newTitle),
                    Type = BrowserBookmarkTypes.Folder,
                    Title = newTitle,
                    Url = r.Url,
                    PathJson = SerializePath(newParent),
                    DateAdded = r.DateAdded,
                    DateModified = now,
                    SourceBrowser = r.SourceBrowser,
                    IsDeleted = r.IsDeleted,
                    UpdatedAt = now
                });
            }
            else
            {
                var pathPrefix = string.Join("/", path);
                if (pathPrefix.Length == 0) continue;
                if (!pathPrefix.StartsWith(oldFolderFull, StringComparison.OrdinalIgnoreCase))
                    continue;
                var newPrefix = newFolderFull + pathPrefix[oldFolderFull.Length..];
                var newParent = newPrefix.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
                r.PathJson = SerializePath(newParent);
                r.DateModified = now;
                r.UpdatedAt = now;
                db.BrowserBookmarks.Update(r);
            }
        }

        foreach (var rem in rows.Where(r => r.Type == BrowserBookmarkTypes.Folder
                     && (string.Join("/", ParsePath(r.PathJson).Append(r.Title))
                             .StartsWith(oldFolderFull + "/", StringComparison.OrdinalIgnoreCase))))
        {
            // 已在上面 Remove；避免重复
        }

        await db.SaveChangesAsync(ct);
        foreach (var add in changed)
        {
            var exists = await db.BrowserBookmarks.AsNoTracking().FirstOrDefaultAsync(x => x.Id == add.Id, ct);
            if (exists == null)
                db.BrowserBookmarks.Add(add);
            else
            {
                exists.IsDeleted = add.IsDeleted;
                exists.PathJson = add.PathJson;
                exists.UpdatedAt = now;
                exists.DateModified = now;
            }
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>软删；文件夹同时软删子孙（按路径前缀）。</summary>
    public async Task SoftDeleteAsync(IEnumerable<string> ids, CancellationToken ct = default)
    {
        var idList = ids?.Distinct().ToList() ?? new List<string>();
        if (idList.Count == 0) return;
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        foreach (var id in idList)
        {
            var row = await db.BrowserBookmarks.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (row == null) continue;
            row.IsDeleted = true;
            row.UpdatedAt = now;
            db.BrowserBookmarks.Update(row);

            if (row.Type != BrowserBookmarkTypes.Folder) continue;
            var path = ParsePath(row.PathJson);
            var folderFull = string.Join("/", path.Append(row.Title));
            var prefix = folderFull + "/";
            var all = await db.BrowserBookmarks.Where(x => !x.IsDeleted).ToListAsync(ct);
            foreach (var child in all)
            {
                var cp = ParsePath(child.PathJson);
                var childFull = child.Type == BrowserBookmarkTypes.Folder
                    ? string.Join("/", cp.Append(child.Title))
                    : string.Join("/", cp);
                if (childFull.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(childFull, folderFull, StringComparison.OrdinalIgnoreCase))
                {
                    child.IsDeleted = true;
                    child.UpdatedAt = now;
                    db.BrowserBookmarks.Update(child);
                }
            }
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task RestoreAsync(IEnumerable<string> ids, CancellationToken ct = default)
    {
        var idList = ids?.Distinct().ToList() ?? new List<string>();
        if (idList.Count == 0) return;
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var id in idList)
        {
            var row = await db.BrowserBookmarks.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (row == null) continue;
            row.IsDeleted = false;
            row.UpdatedAt = now;
            db.BrowserBookmarks.Update(row);
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task PurgeAsync(IEnumerable<string> ids, CancellationToken ct = default)
    {
        var idList = ids?.Distinct().ToList() ?? new List<string>();
        if (idList.Count == 0) return;
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.BrowserBookmarks.Where(x => idList.Contains(x.Id)).ToListAsync(ct);
        db.BrowserBookmarks.RemoveRange(rows);
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> EmptyRecycleBinAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-RecycleDays).ToUnixTimeMilliseconds();
        var rows = await db.BrowserBookmarks.Where(x => x.IsDeleted && x.UpdatedAt < cutoff).ToListAsync(ct);
        db.BrowserBookmarks.RemoveRange(rows);
        await db.SaveChangesAsync(ct);
        return rows.Count;
    }

    /// <summary>清理超过回收期的软删记录。</summary>
    public Task<int> PurgeExpiredRecycleAsync(CancellationToken ct = default)
        => EmptyRecycleBinAsync(ct);

    private static BookmarkLibraryNode ToNode(BrowserBookmark r)
    {
        // Id 是权威键：f:path/title 或 b:url。Title/PathJson 历史数据可能不一致，以 Id 为准。
        var path = ParsePath(r.PathJson);
        var title = r.Title;
        if (r.Type == BrowserBookmarkTypes.Folder && r.Id.StartsWith("f:", StringComparison.Ordinal))
        {
            var segs = r.Id[2..].Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segs.Length > 0)
            {
                title = segs[^1];
                path = segs.Take(segs.Length - 1).ToList();
            }
        }
        else if (r.Type == BrowserBookmarkTypes.Bookmark)
        {
            path = PChabit.Core.Sync.BookmarkMergePlanner.CanonicalizePath(path);
        }

        return new BookmarkLibraryNode(
            r.Id,
            r.Type,
            title,
            r.Url,
            path,
            r.SourceBrowser,
            r.DateModified);
    }
}
