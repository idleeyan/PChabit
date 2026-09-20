using Microsoft.EntityFrameworkCore;
using Serilog;
using PChabit.Core.Entities;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Data;

namespace PChabit.Infrastructure.Services;

/// <summary>
/// 浏览器 History API 条目入库。与 WebSessions（停留时长会话）语义不同，勿混用。
/// </summary>
public class HistoryIngestService : IHistoryIngestService
{
    private readonly IDbContextFactory<PChabitDbContext> _contextFactory;
    private readonly ISettingsService _settings;
    // 扩展推送批次与云端合并可能并发入库，串行化避免同 Id 同时写入触发 UNIQUE 冲突
    private static readonly SemaphoreSlim _ingestLock = new(1, 1);

    public HistoryIngestService(
        IDbContextFactory<PChabitDbContext> contextFactory,
        ISettingsService settings)
    {
        _contextFactory = contextFactory;
        _settings = settings;
    }

    public async Task<int> IngestAsync(string browser, IReadOnlyList<HistoryEntry> entries, CancellationToken ct = default)
    {
        if (!_settings.BrowserSyncEnabled || !_settings.BrowserHistoryIngestEnabled)
        {
            Log.Debug("历史入库已关闭，跳过 {N} 条", entries?.Count ?? 0);
            return 0;
        }

        if (entries == null || entries.Count == 0) return 0;

        await _ingestLock.WaitAsync(ct);
        try
        {
            return await IngestCoreAsync(browser, entries, ct);
        }
        finally
        {
            _ingestLock.Release();
        }
    }

    private async Task<int> IngestCoreAsync(string browser, IReadOnlyList<HistoryEntry> entries, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var items = new List<BrowserHistoryItem>(entries.Count);

        foreach (var e in entries)
        {
            if (string.IsNullOrWhiteSpace(e.Url)) continue;
            // 跳过特殊协议
            var lower = e.Url.Trim().ToLowerInvariant();
            if (lower.StartsWith("javascript:") || lower.StartsWith("chrome://")
                || lower.StartsWith("edge://") || lower.StartsWith("about:")
                || lower.StartsWith("chrome-extension://")) continue;

            var id = $"{e.Url}|{e.VisitTime}";
            items.Add(new BrowserHistoryItem
            {
                Id = id.Length > 500 ? id[..500] : id,
                Url = e.Url,
                Title = e.Title ?? "",
                VisitTime = e.VisitTime,
                VisitCount = Math.Max(1, e.VisitCount),
                SourceBrowser = browser,
                IngestedAt = now
            });
        }

        if (items.Count == 0) return 0;

        // 列表内去重：扩展按天分段导出时同一访问可能跨天重复（同 Url|VisitTime），
        // 若不去重，同一批次 AddRange 会因主键重复导致 EF Core 跟踪冲突。
        var byId = new Dictionary<string, BrowserHistoryItem>(StringComparer.Ordinal);
        foreach (var it in items)
        {
            if (byId.TryGetValue(it.Id, out var cur))
            {
                cur.VisitCount += Math.Max(1, it.VisitCount);
            }
            else
            {
                byId[it.Id] = it;
            }
        }
        items = byId.Values.ToList();

        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        // 兜底建表（启动迁移可能失败）
        await context.Database.ExecuteSqlRawAsync(@"
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
            CREATE TABLE IF NOT EXISTS BrowserSyncMetas (
                Key TEXT NOT NULL PRIMARY KEY,
                Value TEXT NOT NULL DEFAULT ''
            );
        ", ct);
        var ids = items.Select(i => i.Id).ToList();
        var existing = await context.BrowserHistoryItems
            .Where(x => ids.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync(ct);
        var existingSet = existing.ToHashSet();

        var toAdd = items.Where(i => !existingSet.Contains(i.Id)).ToList();
        var added = toAdd.Count;
        if (toAdd.Count > 0)
        {
            try
            {
                context.BrowserHistoryItems.AddRange(toAdd);
                await context.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                // 并发/跨批次主键冲突兜底：逐条检查后插入，跳过已存在
                Log.Warning(ex, "历史入库主键冲突，逐条重试 {N} 条 from {Browser}", toAdd.Count, browser);
                added = 0;
                foreach (var item in toAdd)
                {
                    try
                    {
                        await using var c2 = await _contextFactory.CreateDbContextAsync(ct);
                        if (await c2.BrowserHistoryItems.AnyAsync(x => x.Id == item.Id, ct)) continue;
                        c2.BrowserHistoryItems.Add(item);
                        await c2.SaveChangesAsync(ct);
                        added++;
                    }
                    catch (DbUpdateException)
                    {
                        // 并发下仍可能冲突，忽略该条
                    }
                }
                Log.Information("历史入库（重试后）：新增 {Added} from {Browser}", added, browser);
                return added;
            }
        }

        await context.SetMetaIfPossibleAsync(BrowserSyncMetaKeys.HistoryLastSyncAt, now.ToString(), ct);
        Log.Information("历史入库：新增 {Added} / 总计 {Total} from {Browser}", added, items.Count, browser);
        return added;
    }

    public async Task<int> GetCountAsync(CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        return await context.BrowserHistoryItems.CountAsync(ct);
    }

    public async Task<List<BrowserHistoryItem>> QueryRecentAsync(int limit = 100, CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        return await context.BrowserHistoryItems
            .OrderByDescending(x => x.VisitTime)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<Dictionary<string, int>> GetCountBySourceAsync(CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        return await context.BrowserHistoryItems
            .GroupBy(x => x.SourceBrowser)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => string.IsNullOrEmpty(x.Key) ? "未知" : x.Key, x => x.Count, ct);
    }

    public async Task<(long Min, long Max)> GetTimeRangeAsync(CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        if (!await context.BrowserHistoryItems.AnyAsync(ct)) return (0, 0);
        var min = await context.BrowserHistoryItems.MinAsync(x => x.VisitTime, ct);
        var max = await context.BrowserHistoryItems.MaxAsync(x => x.VisitTime, ct);
        return (min, max);
    }
}

internal static class HistoryIngestDbExtensions
{
    public static async Task SetMetaIfPossibleAsync(this PChabitDbContext context, string key, string value, CancellationToken ct)
    {
        var meta = await context.BrowserSyncMetas.FirstOrDefaultAsync(x => x.Key == key, ct);
        if (meta == null)
        {
            context.BrowserSyncMetas.Add(new BrowserSyncMeta { Key = key, Value = value });
        }
        else
        {
            meta.Value = value;
        }
        await context.SaveChangesAsync(ct);
    }
}
