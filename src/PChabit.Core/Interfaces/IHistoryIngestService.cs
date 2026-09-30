using PChabit.Core.Entities;

namespace PChabit.Core.Interfaces;

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
