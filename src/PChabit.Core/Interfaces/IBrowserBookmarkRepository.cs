using PChabit.Core.Entities;
using PChabit.Core.Sync;

namespace PChabit.Core.Interfaces;

public interface IBrowserBookmarkRepository
{
    Task UpsertManyAsync(IEnumerable<BrowserBookmark> items, CancellationToken ct = default);
    Task DeleteByKeysAsync(IEnumerable<string> keys, CancellationToken ct = default);
    Task<List<BrowserBookmark>> GetAllActiveAsync(CancellationToken ct = default);
    Task<List<BrowserBookmark>> GetAllAsync(CancellationToken ct = default);
    Task SetBaselineAsync(string itemsJson, CancellationToken ct = default);
    Task<string?> GetBaselineAsync(CancellationToken ct = default);
    Task SetMetaAsync(string key, string value, CancellationToken ct = default);
    Task<string?> GetMetaAsync(string key, CancellationToken ct = default);
    Task<int> GetActiveCountAsync(CancellationToken ct = default);
    Task<Dictionary<string, int>> GetCountsBySourceAsync(CancellationToken ct = default);
    Task<Dictionary<string, int>> GetCountsByTypeAsync(CancellationToken ct = default);
    Task<Dictionary<string, int>> GetTopFoldersAsync(int top = 8, CancellationToken ct = default);
}

public class BookmarkLibraryStats
{
    public int Total { get; set; }
    public int Bookmarks { get; set; }
    public int Folders { get; set; }
    public Dictionary<string, int> BySource { get; set; } = new();
    public Dictionary<string, int> TopFolders { get; set; } = new();
    public long? LastSyncAt { get; set; }
    public int? BaselineCount { get; set; }
    public List<string> ReadyBrowsers { get; set; } = new();
}

public interface IHistoryIngestService
{
    Task<int> IngestAsync(string browser, IReadOnlyList<HistoryEntry> entries, CancellationToken ct = default);
    Task<int> GetCountAsync(CancellationToken ct = default);
    Task<List<BrowserHistoryItem>> QueryRecentAsync(int limit = 100, CancellationToken ct = default);
    Task<Dictionary<string, int>> GetCountBySourceAsync(CancellationToken ct = default);
    Task<(long Min, long Max)> GetTimeRangeAsync(CancellationToken ct = default);
}

public class HistoryEntry
{
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public long VisitTime { get; set; }
    public int VisitCount { get; set; }
}

public interface IBookmarkSyncService
{
    event EventHandler<BookmarkSyncProgressEventArgs>? ProgressChanged;
    Task<BookmarkSyncResult> SyncAsync(CancellationToken ct = default);
    Task<BookmarkSyncResult> ResetBaselineAsync(CancellationToken ct = default);
    Task<int> GetLocalCountAsync(CancellationToken ct = default);
}

public class BookmarkSyncProgressEventArgs : EventArgs
{
    public string Step { get; }
    public BookmarkSyncProgressEventArgs(string step) => Step = step;
}
