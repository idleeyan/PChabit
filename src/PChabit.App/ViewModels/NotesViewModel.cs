using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Serilog;
using PChabit.Core.Entities;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Services;
using Windows.UI;

namespace PChabit.App.ViewModels;

/// <summary>便签卡片（UI 包装：颜色键 → 画刷）。</summary>
public sealed partial class NoteItemViewModel : ObservableObject
{
    public StickyNote Source { get; }

    public NoteItemViewModel(StickyNote note) => Source = note;

    public Guid Id => Source.Id;
    public string Content => Source.Content;

    /// <summary>是否处于回收站视图（决定卡片按钮组：管理操作 ↔ 还原/彻底删除）。</summary>
    public bool IsTrashItem { get; init; }
    public string Preview => Source.Content.Length > 240 ? Source.Content[..240] + "…" : Source.Content;
    public bool IsPinned => Source.IsPinned;
    public string ColorKey => Source.Color;
    public string UpdatedText => RelativeTime(Source.UpdatedAt);
    public string DeletedText => Source.DeletedAt != null ? "删除于 " + Source.DeletedAt.Value.ToString("MM-dd HH:mm") : "";
    public int Lines => Math.Min(8, Math.Max(2, Source.Content.Count(c => c == '\n') + 1));

    public SolidColorBrush CardBrush => new(NoteColors.Get(Source.Color));
    public SolidColorBrush AccentBrush => new(NoteColors.GetAccent(Source.Color));

    [ObservableProperty]
    private bool _isSelected;

    private static string RelativeTime(DateTime t)
    {
        var d = DateTime.Now - t;
        if (d.TotalSeconds < 60) return "刚刚";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes} 分钟前";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours} 小时前";
        if (d.TotalDays < 30) return $"{(int)d.TotalDays} 天前";
        return t.ToString("yyyy-MM-dd");
    }
}

/// <summary>五色便签调色板（浅/深主题均使用浅色卡片，便签的纸质隐喻）。</summary>
public static class NoteColors
{
    public static readonly IReadOnlyList<string> Keys = new[] { "yellow", "pink", "blue", "green", "gray" };

    public static Color Get(string key) => key switch
    {
        "pink" => Color.FromArgb(0xFF, 0xFC, 0xE4, 0xEC),
        "blue" => Color.FromArgb(0xFF, 0xDD, 0xEC, 0xFB),
        "green" => Color.FromArgb(0xFF, 0xE3, 0xF4, 0xE3),
        "gray" => Color.FromArgb(0xFF, 0xEE, 0xEE, 0xEE),
        _ => Color.FromArgb(0xFF, 0xFE, 0xF3, 0xC4) // yellow
    };

    public static Color GetAccent(string key) => key switch
    {
        "pink" => Color.FromArgb(0xFF, 0xD6, 0x6A, 0x8E),
        "blue" => Color.FromArgb(0xFF, 0x4A, 0x86, 0xC8),
        "green" => Color.FromArgb(0xFF, 0x4E, 0x9A, 0x57),
        "gray" => Color.FromArgb(0xFF, 0x8A, 0x8A, 0x8A),
        _ => Color.FromArgb(0xFF, 0xD9, 0xA8, 0x2E) // yellow
    };
}

public partial class NotesViewModel : ViewModelBase
{
    private readonly IStickyNoteService _notes;
    private readonly StickyNoteSyncService _sync;
    private readonly DispatcherQueueTimer? _searchTimer;
    private bool _subscribed;

    public ObservableCollection<NoteItemViewModel> Notes { get; } = new();
    public IReadOnlyList<string> ColorKeys => NoteColors.Keys;

    public NotesViewModel(IStickyNoteService notes, StickyNoteSyncService sync)
    {
        _notes = notes;
        _sync = sync;
        _selectedFilter = FilterAll;
        try { _searchTimer = DispatcherQueue.GetForCurrentThread().CreateTimer(); _searchTimer.Interval = TimeSpan.FromMilliseconds(250); _searchTimer.IsRepeating = false; _searchTimer.Tick += (_, _) => _ = ReloadAsync(); }
        catch { }
    }

    public const string FilterAll = "全部";
    public const string FilterPinned = "置顶";
    public const string FilterArchived = "归档";
    public const string FilterTrash = "回收站";
    public IReadOnlyList<string> Filters { get; } = new[] { FilterAll, FilterPinned, FilterArchived, FilterTrash };

    [ObservableProperty] private string _selectedFilter;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private string _emptyText = "还没有便签，按 Ctrl+Alt+N 立刻新建";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "";

    // 当前编辑中的便签（编辑 Dialog 绑定）
    [ObservableProperty] private NoteItemViewModel? _editingNote;
    [ObservableProperty] private string _editContent = "";
    [ObservableProperty] private string _editColor = "yellow";
    [ObservableProperty] private bool _editPinned;

    public bool IsTrashView => SelectedFilter == FilterTrash;
    partial void OnSelectedFilterChanged(string value)
    {
        OnPropertyChanged(nameof(IsTrashView));
        _ = ReloadAsync();
    }

    public void OnSearchTextChanged()
    {
        _searchTimer?.Stop();
        _searchTimer?.Start();
    }

    public void Subscribe()
    {
        if (_subscribed) return;
        _subscribed = true;
        _notes.Changed += OnNotesChanged;
        _ = ReloadAsync();
    }

    public void Unsubscribe()
    {
        if (!_subscribed) return;
        _subscribed = false;
        _notes.Changed -= OnNotesChanged;
    }

    private void OnNotesChanged(object? s, StickyNoteChangedEventArgs e) => _ = ReloadAsync();

    private StickyNoteFilter CurrentFilter => SelectedFilter switch
    {
        FilterPinned => StickyNoteFilter.Pinned,
        FilterArchived => StickyNoteFilter.Archived,
        FilterTrash => StickyNoteFilter.Trash,
        _ => StickyNoteFilter.Active
    };
    public async Task ReloadAsync()
    {
        try
        {
            IsBusy = true;
            var items = await _notes.QueryAsync(CurrentFilter, SearchText, take: 300);
            bool inTrash = SelectedFilter == FilterTrash;
            Notes.Clear();
            foreach (var n in items) Notes.Add(new NoteItemViewModel(n) { IsTrashItem = inTrash });
            IsEmpty = items.Count == 0;
            EmptyText = string.IsNullOrWhiteSpace(SearchText)
                ? (SelectedFilter == FilterTrash ? "回收站是空的" : "还没有便签，按 Ctrl+Alt+N 立刻新建")
                : "没有匹配的便签";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Notes] 加载失败");
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task NewAsync()
    {
        var note = await _notes.CreateAsync("", "yellow");
        await BeginEditAsync(note.Id);
    }

    [RelayCommand]
    private async Task CycleColor(Guid id)
    {
        var existing = Notes.FirstOrDefault(n => n.Id == id);
        string? current = existing?.ColorKey ?? (await _notes.GetAsync(id))?.Color;
        if (current == null) return;
        var idx = NoteColors.Keys.ToList().IndexOf(current);
        var next = NoteColors.Keys[(idx + 1) % NoteColors.Keys.Count];
        await _notes.SetColorAsync(id, next);
    }

    [RelayCommand] private Task TogglePin(Guid id) => _notes.SetPinnedAsync(id, !Notes.First(n => n.Id == id).IsPinned);

    [RelayCommand]
    private async Task ArchiveOrUnarchive(Guid id)
    {
        var item = Notes.FirstOrDefault(n => n.Id == id);
        bool archived = SelectedFilter == FilterArchived ? false : (item?.Source.IsArchived ?? false) == false;
        await _notes.SetArchivedAsync(id, archived);
    }

    [RelayCommand] private Task Delete(Guid id) => _notes.MoveToTrashAsync(id);
    [RelayCommand] private Task Restore(Guid id) => _notes.RestoreAsync(id);
    [RelayCommand] private Task Purge(Guid id) => _notes.PurgeAsync(id);

    [RelayCommand]
    private async Task EmptyTrash()
    {
        var n = await _notes.EmptyTrashAsync();
        StatusText = n > 0 ? $"已清空 {n} 条" : "";
    }

    public async Task BeginEditAsync(Guid id)
    {
        var note = await _notes.GetAsync(id);
        if (note == null) return;
        EditingNote = new NoteItemViewModel(note);
        EditContent = note.Content;
        EditColor = note.Color;
        EditPinned = note.IsPinned;
    }

    public void CancelEdit() => EditingNote = null;

    [RelayCommand]
    private async Task SaveEditAsync()
    {
        if (EditingNote == null) return;
        var id = EditingNote.Id;
        await _notes.UpdateContentAsync(id, EditContent);
        await _notes.SetColorAsync(id, EditColor);
        await _notes.SetPinnedAsync(id, EditPinned);
        EditingNote = null;
    }

    /// <summary>空内容便签在编辑取消时清理（新建但什么都没写）。</summary>
    [RelayCommand]
    private async Task DiscardEmptyAsync()
    {
        if (EditingNote != null && string.IsNullOrWhiteSpace(EditContent) && EditingNote.Source.Version <= 1)
            await _notes.PurgeAsync(EditingNote.Id);
        EditingNote = null;
    }

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        IsBusy = true;
        StatusText = "正在同步…";
        var r = await _sync.SyncAsync();
        StatusText = r.Message;
        IsBusy = false;
        if (r.Ok) await ReloadAsync();
    }
}
