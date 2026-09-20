using Microsoft.EntityFrameworkCore;
using PChabit.Core.Entities;
using PChabit.Core.Sync;
using PChabit.Infrastructure.Data;
using PChabit.Infrastructure.Monitoring;
using Serilog;

namespace PChabit.Infrastructure.Services;

/// <summary>
/// 书签中枢推送已停用（模块下线）。保留类型以免 DI/编译断裂；
/// 所有同步/推送入口返回空结果，不再修改浏览器或本地书签库。
/// </summary>
public class BookmarkHubService
{
    private readonly BrowserSyncWebSocketHandler _ws;

    public BookmarkHubService(
        IDbContextFactory<PChabitDbContext> dbFactory,
        BrowserSyncWebSocketHandler ws,
        BookmarkLibraryService library)
    {
        _ = dbFactory;
        _ = library;
        _ws = ws;
    }

    public bool AutoPushEnabled => false;

    public Task<(List<FlatItem> applyItems, int total)> BuildPushPayloadAsync(CancellationToken ct = default)
        => Task.FromResult((new List<FlatItem>(), 0));

    public Task<(ApplyAggregateDto? Result, string Message, int ReadyCount, int PayloadCount)> PushLibraryToBrowsersDetailedAsync(CancellationToken ct = default)
        => Task.FromResult<(ApplyAggregateDto?, string, int, int)>(
            (null, "书签推送模块已停用（请使用浏览器自带同步）", _ws.ReadyBrowsers.Count, 0));

    public Task<ApplyAggregateDto?> PushLibraryToBrowsersAsync(CancellationToken ct = default)
        => Task.FromResult<ApplyAggregateDto?>(null);

    public Task<List<PendingBookmarkChange>> HarvestPendingFromBrowsersAsync(CancellationToken ct = default)
        => Task.FromResult(new List<PendingBookmarkChange>());

    public Task<List<PendingBookmarkChange>> GetPendingAsync(CancellationToken ct = default)
        => Task.FromResult(new List<PendingBookmarkChange>());

    public Task<int> CountPendingAsync(CancellationToken ct = default)
        => Task.FromResult(0);

    public Task ResolvePendingAsync(int id, string action, CancellationToken ct = default)
        => Task.CompletedTask;
}
