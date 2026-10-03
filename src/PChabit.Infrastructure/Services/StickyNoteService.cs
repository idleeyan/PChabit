using Microsoft.EntityFrameworkCore;
using Serilog;
using PChabit.Core.Entities;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Data;

namespace PChabit.Infrastructure.Services;

/// <summary>
/// 便签领域服务（3.24.0）：短 DbContext 读写 SQLite，软删除+墓碑，事件通知。
/// 事件触发于线程池/调用线程，UI 订阅方必须自行 DispatcherQueue 派发。
/// </summary>
public sealed class StickyNoteService : IStickyNoteService
{
    public static readonly IReadOnlySet<string> ValidColors =
        new HashSet<string>(new[] { "yellow", "pink", "blue", "green", "gray" }, StringComparer.OrdinalIgnoreCase);

    private readonly IDbContextFactory<PChabitDbContext> _dbFactory;
    private readonly ISettingsService _settings;

    public event EventHandler<StickyNoteChangedEventArgs>? Changed;

    public StickyNoteService(IDbContextFactory<PChabitDbContext> dbFactory, ISettingsService settings)
    {
        _dbFactory = dbFactory;
        _settings = settings;
    }

    public async Task<StickyNote> CreateAsync(string content, string color = "yellow", bool pinned = false)
    {
        content = NormalizeContent(content);
        var now = DateTime.Now;
        var note = new StickyNote
        {
            Id = Guid.NewGuid(),
            Content = content,
            Color = NormalizeColor(color),
            IsPinned = pinned,
            CreatedAt = now,
            UpdatedAt = now,
            DeviceId = DeviceId,
            Version = 1
        };
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        ctx.StickyNotes.Add(note);
        await ctx.SaveChangesAsync();
        Emit(note.Id, "created");
        Log.Information("[StickyNote] 新建 {Id} 长度={Len}", note.Id, content.Length);
        return note;
    }

    public async Task UpdateContentAsync(Guid id, string content)
    {
        content = NormalizeContent(content);
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        var note = await ctx.StickyNotes.FindAsync(id);
        if (note == null || note.Content == content) return;
        note.Content = content;
        Touch(note);
        await ctx.SaveChangesAsync();
        Emit(id, "updated");
    }

    public Task SetColorAsync(Guid id, string color) =>
        MutateAsync(id, n => n.Color = NormalizeColor(color));

    public Task SetPinnedAsync(Guid id, bool pinned) =>
        MutateAsync(id, n => n.IsPinned = pinned);

    public Task SetArchivedAsync(Guid id, bool archived) =>
        MutateAsync(id, n => n.IsArchived = archived);

    public async Task MoveToTrashAsync(Guid id)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        var note = await ctx.StickyNotes.FindAsync(id);
        if (note == null || note.IsDeleted) return;
        note.IsDeleted = true;
        note.IsPinned = false;
        note.IsOnDesktop = false; // 删除即撤下桌面窗
        note.DeletedAt = DateTime.Now;
        Touch(note);
        await ctx.SaveChangesAsync();
        Emit(id, "deleted");
    }

    public async Task RestoreAsync(Guid id)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        var note = await ctx.StickyNotes.FindAsync(id);
        if (note == null || !note.IsDeleted) return;
        note.IsDeleted = false;
        note.DeletedAt = null;
        Touch(note);
        await ctx.SaveChangesAsync();
        Emit(id, "restored");
    }

    public async Task PurgeAsync(Guid id)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        var note = await ctx.StickyNotes.FindAsync(id);
        if (note == null) return;
        ctx.StickyNotes.Remove(note);
        await ctx.SaveChangesAsync();
        Emit(id, "purged");
    }

    public async Task<int> EmptyTrashAsync()
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        var rows = await ctx.StickyNotes.Where(n => n.IsDeleted).ExecuteDeleteAsync();
        if (rows > 0) Emit(Guid.Empty, "purged");
        return rows;
    }

    public async Task<int> PurgeExpiredAsync(int retentionDays)
    {
        var cutoff = DateTime.Now.AddDays(-Math.Max(1, retentionDays));
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        var rows = await ctx.StickyNotes
            .Where(n => n.IsDeleted && n.DeletedAt != null && n.DeletedAt < cutoff)
            .ExecuteDeleteAsync();
        if (rows > 0) Log.Information("[StickyNote] 清理过期墓碑 {Count} 条", rows);
        return rows;
    }

    public async Task<List<StickyNote>> QueryAsync(StickyNoteFilter filter, string? search = null, int take = 0)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        IQueryable<StickyNote> q = ctx.StickyNotes.AsNoTracking().AsQueryable();

        q = filter switch
        {
            StickyNoteFilter.Active => q.Where(n => !n.IsDeleted && !n.IsArchived),
            StickyNoteFilter.Pinned => q.Where(n => !n.IsDeleted && !n.IsArchived && n.IsPinned),
            StickyNoteFilter.Archived => q.Where(n => !n.IsDeleted && n.IsArchived),
            StickyNoteFilter.Trash => q.Where(n => n.IsDeleted),
            _ => q
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var kw = search.Trim();
            q = q.Where(n => EF.Functions.Like(n.Content, "%" + kw + "%"));
        }

        q = filter == StickyNoteFilter.Trash
            ? q.OrderByDescending(n => n.DeletedAt)
            : q.OrderByDescending(n => n.IsPinned).ThenByDescending(n => n.UpdatedAt);

        if (take > 0) q = q.Take(take);
        return await q.ToListAsync();
    }

    public async Task<StickyNote?> GetAsync(Guid id)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        return await ctx.StickyNotes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id);
    }

    public async Task<List<StickyNote>> GetAllForSyncAsync()
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        // 同步集只含近 100 天活动记录（更早的墓碑无传播价值）
        var since = DateTime.Now.AddDays(-100);
        return await ctx.StickyNotes.AsNoTracking()
            .Where(n => n.UpdatedAt >= since || !n.IsDeleted)
            .ToListAsync();
    }

    public async Task ReplaceAllForSyncAsync(IReadOnlyCollection<StickyNote> notes)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        await using var tx = await ctx.Database.BeginTransactionAsync();

        var local = await ctx.StickyNotes.ToDictionaryAsync(n => n.Id);
        foreach (var incoming in notes)
        {
            if (local.TryGetValue(incoming.Id, out var existing))
            {
                // 本机私有字段（桌面窗布局）不参与同步，合并时保留
                incoming.IsOnDesktop = existing.IsOnDesktop;
                incoming.WindowLeft = existing.WindowLeft;
                incoming.WindowTop = existing.WindowTop;
                incoming.WindowWidth = existing.WindowWidth;
                incoming.WindowHeight = existing.WindowHeight;
                ctx.Entry(existing).CurrentValues.SetValues(incoming);
            }
            else
            {
                ctx.StickyNotes.Add(incoming);
            }
        }
        // 注意：不删除本地独有便签（离线设备新建但尚未上传的内容受保护，删除只经墓碑传播）
        await ctx.SaveChangesAsync();
        await tx.CommitAsync();
        Emit(Guid.Empty, "updated");
    }

    private async Task MutateAsync(Guid id, Action<StickyNote> change)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        var note = await ctx.StickyNotes.FindAsync(id);
        if (note == null) return;
        change(note);
        Touch(note);
        await ctx.SaveChangesAsync();
        Emit(id, "updated");
    }

    private void Touch(StickyNote note)
    {
        note.UpdatedAt = DateTime.Now;
        note.Version += 1;
        if (string.IsNullOrEmpty(note.DeviceId)) note.DeviceId = DeviceId;
    }

    private void Emit(Guid id, string kind)
    {
        try { Changed?.Invoke(this, new StickyNoteChangedEventArgs { NoteId = id, Kind = kind }); }
        catch (Exception ex) { Log.Warning(ex, "[StickyNote] 变更事件订阅者异常"); }
    }

    private string DeviceId
    {
        get
        {
            var id = _settings.StickyNotesDeviceId;
            if (!string.IsNullOrWhiteSpace(id)) return id;
            id = Guid.NewGuid().ToString("N")[..8];
            _settings.StickyNotesDeviceId = id;
            try { _settings.Save(); } catch { /* 下次重试 */ }
            return id;
        }
    }

    private static string NormalizeContent(string? content)
    {
        content ??= string.Empty;
        content = content.Replace("\r\n", "\n").TrimEnd();
        return content.Length > IStickyNoteService.MaxContentLength
            ? content[..IStickyNoteService.MaxContentLength]
            : content;
    }

    private static string NormalizeColor(string? color) =>
        !string.IsNullOrWhiteSpace(color) && ValidColors.Contains(color) ? color.ToLowerInvariant() : "yellow";
}
