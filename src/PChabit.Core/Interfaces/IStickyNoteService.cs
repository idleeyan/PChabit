using PChabit.Core.Entities;

namespace PChabit.Core.Interfaces;

/// <summary>便签列表过滤视图。</summary>
public enum StickyNoteFilter
{
    /// <summary>未删除、未归档（含置顶）。</summary>
    Active,
    /// <summary>仅置顶。</summary>
    Pinned,
    /// <summary>归档区。</summary>
    Archived,
    /// <summary>回收站（软删除）。</summary>
    Trash
}

/// <summary>便签变更事件（桌面窗/列表页刷新用）。</summary>
public sealed class StickyNoteChangedEventArgs : EventArgs
{
    public Guid NoteId { get; init; }
    /// <summary>created | updated | deleted | restored | purged</summary>
    public string Kind { get; init; } = "updated";
}

/// <summary>
/// 便签领域服务：增删改查、归档、回收站（软删除+过期物理清理）、变更通知。
/// 所有方法异步（DbContextFactory 短上下文），事件在后台线程触发，UI 订阅方自行派发。
/// </summary>
public interface IStickyNoteService
{
    /// <summary>正文硬上限（超出截断）。</summary>
    const int MaxContentLength = 20_000;

    Task<StickyNote> CreateAsync(string content, string color = "yellow", bool pinned = false);

    Task UpdateContentAsync(Guid id, string content);

    Task SetColorAsync(Guid id, string color);

    Task SetPinnedAsync(Guid id, bool pinned);

    Task SetArchivedAsync(Guid id, bool archived);

    /// <summary>软删除（进回收站，记 DeletedAt）。</summary>
    Task MoveToTrashAsync(Guid id);

    /// <summary>从回收站还原。</summary>
    Task RestoreAsync(Guid id);

    /// <summary>物理删除单条（回收站"彻底删除"）。</summary>
    Task PurgeAsync(Guid id);

    /// <summary>清空回收站。</summary>
    Task<int> EmptyTrashAsync();

    /// <summary>物理删除删除时间超过保留期的墓碑，返回清理条数（启动时调用）。</summary>
    Task<int> PurgeExpiredAsync(int retentionDays);

    /// <summary>
    /// 查询便签。Active/Pinned/Archived 置顶优先、UpdatedAt 倒序；Trash 按 DeletedAt 倒序。
    /// search 非空时对正文做包含匹配（LIKE）；take 限制条数（0=不限）。
    /// </summary>
    Task<List<StickyNote>> QueryAsync(StickyNoteFilter filter, string? search = null, int take = 0);

    /// <summary>取单条（含回收站）；不存在返回 null。</summary>
    Task<StickyNote?> GetAsync(Guid id);

    /// <summary>同步合并用：读取本机全部便签（含墓碑，供 StickyNoteSyncService）。</summary>
    Task<List<StickyNote>> GetAllForSyncAsync();

    /// <summary>同步合并用：按字典整体覆盖写入（upsert + 远端新增的删除），由同步服务在事务内实现。</summary>
    Task ReplaceAllForSyncAsync(IReadOnlyCollection<StickyNote> notes);

    /// <summary>任意变更后触发（kind 区分创建/修改/删除等）。</summary>
    event EventHandler<StickyNoteChangedEventArgs>? Changed;
}
