using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Services;
using Serilog;

namespace PChabit.App.ViewModels;

/// <summary>
/// 浏览历史同步（书签库/书签同步模块已下线）。
/// </summary>
public partial class DataManagementViewModel
{
    private readonly HistorySyncService? _historySyncService;

    [ObservableProperty]
    private bool _browserSyncEnabled = false;

    [ObservableProperty]
    private int _browserSyncIntervalMinutes = 60;

    [ObservableProperty]
    private bool _browserHistoryIngestEnabled = true;

    [ObservableProperty]
    private bool _isHistorySyncing;

    [ObservableProperty]
    private string _historyStatusText = "尚未同步历史";

    [ObservableProperty]
    private string _historyStatsText = "";

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
        try { _settingsService.BrowserSyncEnabled = value; } catch { }
    }

    partial void OnBrowserSyncIntervalMinutesChanged(int value)
    {
        try { _settingsService.BrowserSyncIntervalMinutes = value; } catch { }
    }
}
