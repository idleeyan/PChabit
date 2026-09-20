using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Services;
using Serilog;

namespace PChabit.App.ViewModels;

/// <summary>
/// 书签同步模块已下线（3.15.11+）。保留命令壳以免 XAML 断裂；不再访问书签库。
/// </summary>
public partial class DataManagementViewModel
{
    private readonly IBookmarkSyncService? _bookmarkSyncService;
    private readonly BookmarkTidyService? _tidyService;
    private readonly HistorySyncService? _historySyncService;

    [ObservableProperty]
    private bool _browserSyncEnabled = false;

    [ObservableProperty]
    private bool _browserBookmarkSyncEnabled = false;

    [ObservableProperty]
    private int _browserSyncIntervalMinutes = 60;

    [ObservableProperty]
    private bool _isBookmarkSyncing;

    [ObservableProperty]
    private bool _isTidying;

    [ObservableProperty]
    private string _bookmarkSyncStatus = "书签模块已停用";

    [ObservableProperty]
    private string _bookmarkSyncProgressText = "";

    [ObservableProperty]
    private string _connectedBrowsersText = "书签同步已停用";

    [ObservableProperty]
    private string _bookmarkStatsText = "书签模块已停用（本机历史数据可能仍在 SQLite）";

    [ObservableProperty]
    private string _bookmarkSourceText = "";

    [ObservableProperty]
    private string _bookmarkFolderText = "";

    [ObservableProperty]
    private string _lastSyncText = "书签同步已停用";

    public ObservableCollection<string> TidyPreviewLines { get; } = new();

    [ObservableProperty]
    private bool _hasTidyPreview;

    [ObservableProperty]
    private bool _browserHistoryIngestEnabled = true;

    [ObservableProperty]
    private bool _isHistorySyncing;

    [ObservableProperty]
    private string _historyStatusText = "尚未同步历史";

    [ObservableProperty]
    private string _historyStatsText = "";

    private const string ModuleDisabledMsg =
        "书签库/书签同步模块已停用。请使用浏览器自带书签同步；本机历史数据保留在 SQLite，不会自动删除。";

    partial void OnBrowserHistoryIngestEnabledChanged(bool value)
    {
        try { _settingsService.BrowserHistoryIngestEnabled = value; } catch { }
    }

    [RelayCommand]
    private Task RefreshHistoryStatsAsync() => LoadHistoryStatsAsync();

    [RelayCommand]
    private async Task SyncHistoryAsync()
    {
        if (_historySyncService == null)
        {
            HistoryStatusText = "历史同步服务未就绪";
            return;
        }
        if (IsHistorySyncing)
        {
            HistoryStatusText = "历史同步进行中…";
            return;
        }

        IsHistorySyncing = true;
        HistoryStatusText = "正在同步浏览历史…";

        void OnProgress(object? s, string step)
        {
            Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() =>
            {
                HistoryStatusText = step;
            });
        }

        _historySyncService.ProgressChanged += OnProgress;
        try
        {
            var (ok, msg, _, _) = await _historySyncService.SyncAsync(30);
            HistoryStatusText = msg;
            AddLog(ok ? "成功" : "错误", "历史同步：" + msg);
            await LoadHistoryStatsAsync();
        }
        catch (Exception ex)
        {
            HistoryStatusText = $"历史同步失败：{ex.Message}";
            AddLog("错误", $"历史同步异常：{ex.Message}");
        }
        finally
        {
            _historySyncService.ProgressChanged -= OnProgress;
            IsHistorySyncing = false;
        }
    }

    [RelayCommand]
    private async Task SyncHistoryFullAsync()
    {
        if (_historySyncService == null) { HistoryStatusText = "历史同步服务未就绪"; return; }
        if (IsHistorySyncing) { HistoryStatusText = "历史同步进行中…"; return; }

        IsHistorySyncing = true;
        HistoryStatusText = "正在全量同步浏览历史（近 365 天）…";
        try
        {
            var (ok, msg, _, _) = await _historySyncService.SyncAsync(365);
            HistoryStatusText = msg;
            AddLog(ok ? "成功" : "错误", "历史全量同步：" + msg);
            await LoadHistoryStatsAsync();
        }
        catch (Exception ex)
        {
            HistoryStatusText = $"历史全量同步失败：{ex.Message}";
            AddLog("错误", $"历史全量同步异常：{ex.Message}");
        }
        finally { IsHistorySyncing = false; }
    }

    public async Task LoadHistoryStatsAsync()
    {
        try
        {
            var ingest = App.GetService<IHistoryIngestService>();
            var count = await ingest.GetCountAsync();
            var bySource = await ingest.GetCountBySourceAsync();
            var (min, max) = await ingest.GetTimeRangeAsync();

            var src = bySource.Count == 0
                ? "暂无"
                : string.Join(" · ", bySource.Select(kv => $"{kv.Key} {kv.Value}"));
            var range = min > 0
                ? $"{DateTimeOffset.FromUnixTimeMilliseconds(min).ToLocalTime():yyyy-MM-dd} ~ {DateTimeOffset.FromUnixTimeMilliseconds(max).ToLocalTime():yyyy-MM-dd}"
                : "—";

            HistoryStatsText = $"共 {count:N0} 条 · 来源 {src} · 范围 {range} · 实时写入中";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "加载历史统计失败");
            HistoryStatsText = "统计加载失败";
        }
    }

    partial void OnBrowserSyncEnabledChanged(bool value)
    {
        // 模块停用：强制写回 false
        if (value)
        {
            BrowserSyncEnabled = false;
            return;
        }
        try { _settingsService.BrowserSyncEnabled = false; } catch { }
    }

    partial void OnBrowserBookmarkSyncEnabledChanged(bool value)
    {
        if (value)
        {
            BrowserBookmarkSyncEnabled = false;
            return;
        }
        try { _settingsService.BrowserBookmarkSyncEnabled = false; } catch { }
    }

    partial void OnBrowserSyncIntervalMinutesChanged(int value)
    {
        try { _settingsService.BrowserSyncIntervalMinutes = value; } catch { }
    }

    public void UpdateConnectedBrowsers(IEnumerable<string> browsers)
    {
        _ = browsers;
        ConnectedBrowsersText = ModuleDisabledMsg;
    }

    [RelayCommand]
    private Task LoadBookmarkStatsCommandAsync() => LoadBookmarkStatsAsync();

    [RelayCommand]
    private async Task RefreshBrowsersAsync()
    {
        BookmarkSyncStatus = ModuleDisabledMsg;
        ConnectedBrowsersText = ModuleDisabledMsg;
        await LoadBookmarkStatsAsync();
    }

    public async Task LoadBookmarkStatsAsync()
    {
        BookmarkStatsText = ModuleDisabledMsg;
        BookmarkSourceText = "";
        BookmarkFolderText = "";
        LastSyncText = "书签同步已停用";
        BookmarkSyncStatus = ModuleDisabledMsg;
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task SyncBookmarksAsync()
    {
        BookmarkSyncStatus = ModuleDisabledMsg;
        AddLog("警告", "书签同步模块已停用，本次未执行");
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task PreviewTidyAsync()
    {
        BookmarkSyncStatus = ModuleDisabledMsg;
        TidyPreviewLines.Clear();
        HasTidyPreview = false;
        AddLog("警告", "书签整理模块已停用");
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ApplyTidyAsync()
    {
        BookmarkSyncStatus = ModuleDisabledMsg;
        AddLog("警告", "书签整理模块已停用");
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ResetBookmarkBaselineAsync()
    {
        BookmarkSyncStatus = ModuleDisabledMsg;
        AddLog("警告", "书签基线重置已停用");
        await Task.CompletedTask;
    }

    public List<string> GetBookmarkSyncErrorList() => new();

    [RelayCommand]
    private async Task CopyBookmarkSyncErrorsAsync()
    {
        AddLog("信息", "书签模块已停用，无错误可复制");
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task LoadBookmarkCountAsync()
    {
        await LoadBookmarkStatsAsync();
    }
}