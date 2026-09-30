using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using PChabit.App.Services;
using PChabit.HardwareMonitor.Hardware;
using PChabit.Infrastructure.Data;
using Serilog;

namespace PChabit.App.ViewModels;

public partial class NetworkTrafficViewModel : ViewModelBase
{
    public const string PeriodToday = "今日";
    public const string Period7 = "近 7 天";
    public const string Period30 = "近 30 天";

    private readonly IDbContextFactory<PChabitDbContext> _dbFactory;
    private readonly ProcessNetworkMonitor _monitor;
    private readonly NetworkTrafficPersistenceService _persistence;

    public NetworkTrafficViewModel(
        IDbContextFactory<PChabitDbContext> dbFactory,
        ProcessNetworkMonitor monitor,
        NetworkTrafficPersistenceService persistence)
    {
        _dbFactory = dbFactory;
        _monitor = monitor;
        _persistence = persistence;
        _selectedPeriod = PeriodToday;
    }

    public IReadOnlyList<string> PeriodOptions { get; } = new[] { PeriodToday, Period7, Period30 };

    [ObservableProperty]
    private string _selectedPeriod;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "加载中...";

    [ObservableProperty]
    private string _rangeText = "--";

    [ObservableProperty]
    private string _totalTrafficText = "--";

    [ObservableProperty]
    private string _upTrafficText = "--";

    [ObservableProperty]
    private string _downTrafficText = "--";

    [ObservableProperty]
    private string _dailyAvgText = "--";

    [ObservableProperty]
    private string _processCountText = "--";

    [ObservableProperty]
    private string _sampleCountText = "--";

    [ObservableProperty]
    private string _peakText = "--";

    [ObservableProperty]
    private string _liveTopText = "等待采样...";

    [ObservableProperty]
    private string _liveSystemText = "--";

    [ObservableProperty]
    private string _processEmptyText = "暂无进程流量记录";

    public ObservableCollection<DayTrafficBar> DayBars { get; } = new();
    public ObservableCollection<HourTrafficBar> HourBars { get; } = new();
    public ObservableCollection<ProcessTrafficRow> ProcessRows { get; } = new();

    partial void OnSelectedPeriodChanged(string value) => _ = LoadAsync();

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = "加载中...";
        try
        {
            try { _persistence.FlushNow(); } catch { /* ignore */ }

            var range = ResolveRange();
            var fromDate = range.From;
            var toDate = range.To;
            var days = range.Days;
            RangeText = fromDate + " ~ " + toDate + " (" + days + " days)";

            var toExclusive = DateTime.ParseExact(toDate, "yyyy-MM-dd", null).AddDays(1).ToString("yyyy-MM-dd");

            await using var db = await _dbFactory.CreateDbContextAsync();

            var samples = await db.NetworkTrafficSamples.AsNoTracking()
                .Where(x => string.Compare(x.Timestamp, fromDate) >= 0 && string.Compare(x.Timestamp, toExclusive) < 0)
                .ToListAsync();

            var procRows = await db.ProcessNetworkUsages.AsNoTracking()
                .Where(x => string.Compare(x.Date, fromDate) >= 0 && string.Compare(x.Date, toDate) <= 0)
                .ToListAsync();

            long up = 0;
            long down = 0;
            double peakUp = 0;
            double peakDown = 0;
            foreach (var s in samples)
            {
                up += s.BytesUp;
                down += s.BytesDown;
                if (s.PeakUpBps > peakUp) peakUp = s.PeakUpBps;
                if (s.PeakDownBps > peakDown) peakDown = s.PeakDownBps;
            }

            TotalTrafficText = FormatBytes(up + down);
            UpTrafficText = FormatBytes(up);
            DownTrafficText = FormatBytes(down);
            DailyAvgText = days > 0 ? FormatBytes((up + down) / days) : FormatBytes(up + down);
            ProcessCountText = procRows.Select(x => x.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).Count().ToString();
            SampleCountText = samples.Count.ToString();
            PeakText = "Up " + FormatSpeed(peakUp) + " / Down " + FormatSpeed(peakDown);

            BuildDayBars(samples);
            BuildProcessRows(procRows);
            BuildHourBars(procRows);

            RefreshLive();
            StatusText = "Updated " + DateTime.Now.ToString("HH:mm:ss")
                + " | samples " + samples.Count
                + " | process-hour rows " + procRows.Count;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Network traffic stats load failed");
            StatusText = "Load failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void RefreshLive()
    {
        var snap = _monitor.GetSnapshot(8);
        var lines = new List<string>();
        for (var i = 0; i < snap.TopByRateList.Count; i++)
        {
            var p = snap.TopByRateList[i];
            lines.Add((i + 1) + ". " + p.ProcessName + "  down " + FormatSpeed(p.DownBytesPerSec) + " up " + FormatSpeed(p.UpBytesPerSec));
        }

        LiveTopText = lines.Count > 0 ? string.Join(Environment.NewLine, lines) : "No active network process";
        LiveSystemText = "Session up " + FormatBytes(snap.SystemSessionUpBytes) + " / down " + FormatBytes(snap.SystemSessionDownBytes)
            + "  |  Today up " + FormatBytes(snap.SystemTodayUpBytes) + " / down " + FormatBytes(snap.SystemTodayDownBytes);
    }

    private void BuildDayBars(List<PChabit.Core.Entities.NetworkTrafficSample> samples)
    {
        DayBars.Clear();
        var map = new SortedDictionary<string, long[]>(StringComparer.Ordinal);
        foreach (var s in samples)
        {
            var day = s.Timestamp.Length >= 10 ? s.Timestamp.Substring(0, 10) : s.Timestamp;
            if (!map.TryGetValue(day, out var acc))
            {
                acc = new long[2];
                map[day] = acc;
            }

            acc[0] += s.BytesUp;
            acc[1] += s.BytesDown;
        }

        if (map.Count == 0) return;

        long maxTotal = 1;
        foreach (var kv in map)
        {
            var t = kv.Value[0] + kv.Value[1];
            if (t > maxTotal) maxTotal = t;
        }

        foreach (var kv in map)
        {
            var up = kv.Value[0];
            var down = kv.Value[1];
            var total = up + down;
            var label = kv.Key.Length >= 10 ? kv.Key.Substring(5) : kv.Key;
            DayBars.Add(new DayTrafficBar
            {
                DayLabel = label,
                FullDay = kv.Key,
                UpText = FormatBytes(up),
                DownText = FormatBytes(down),
                TotalText = FormatBytes(total),
                UpBarHeight = Math.Max(2, 120.0 * up / maxTotal),
                DownBarHeight = Math.Max(2, 120.0 * down / maxTotal)
            });
        }
    }

    private void BuildProcessRows(List<PChabit.Core.Entities.ProcessNetworkUsage> procRows)
    {
        ProcessRows.Clear();
        var map = new Dictionary<string, long[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in procRows)
        {
            if (!map.TryGetValue(r.ProcessName, out var acc))
            {
                acc = new long[3];
                map[r.ProcessName] = acc;
            }

            acc[0] += r.BytesUp;
            acc[1] += r.BytesDown;
            if (r.PeakBytesPerSec > acc[2]) acc[2] = r.PeakBytesPerSec;
        }

        var ranked = map
            .Select(kv => new { Name = kv.Key, Up = kv.Value[0], Down = kv.Value[1], Peak = kv.Value[2] })
            .OrderByDescending(x => x.Up + x.Down)
            .ToList();

        long maxTotal = ranked.Count > 0 ? ranked.Max(x => x.Up + x.Down) : 1;
        if (maxTotal <= 0) maxTotal = 1;

        var rank = 0;
        foreach (var p in ranked)
        {
            rank++;
            var total = p.Up + p.Down;
            ProcessRows.Add(new ProcessTrafficRow
            {
                Rank = rank,
                ProcessName = p.Name,
                UpText = FormatBytes(p.Up),
                DownText = FormatBytes(p.Down),
                TotalText = FormatBytes(total),
                PeakText = FormatSpeed(p.Peak),
                SharePercent = Math.Round(100.0 * total / maxTotal, 1),
                BarWidth = Math.Max(4, 240.0 * total / maxTotal)
            });
            if (ProcessRows.Count >= 50) break;
        }

        ProcessEmptyText = ProcessRows.Count > 0
            ? string.Empty
            : "No process traffic in selected range (keep app running to sample)";
    }

    private void BuildHourBars(List<PChabit.Core.Entities.ProcessNetworkUsage> procRows)
    {
        HourBars.Clear();
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        var hourMap = new Dictionary<int, long[]>();
        foreach (var r in procRows)
        {
            if (!string.Equals(r.Date, today, StringComparison.Ordinal)) continue;
            if (!hourMap.TryGetValue(r.Hour, out var acc))
            {
                acc = new long[2];
                hourMap[r.Hour] = acc;
            }

            acc[0] += r.BytesUp;
            acc[1] += r.BytesDown;
        }

        if (hourMap.Count == 0) return;

        long maxTotal = 1;
        foreach (var kv in hourMap)
        {
            var t = kv.Value[0] + kv.Value[1];
            if (t > maxTotal) maxTotal = t;
        }

        for (var h = 0; h < 24; h++)
        {
            hourMap.TryGetValue(h, out var item);
            var up = item?[0] ?? 0;
            var down = item?[1] ?? 0;
            var total = up + down;
            HourBars.Add(new HourTrafficBar
            {
                HourLabel = h.ToString("00"),
                UpText = FormatBytes(up),
                DownText = FormatBytes(down),
                TotalText = FormatBytes(total),
                BarHeight = Math.Max(2, 80.0 * total / maxTotal),
                HasData = total > 0,
                BarOpacity = total > 0 ? 1.0 : 0.15
            });
        }
    }

    private (string From, string To, int Days) ResolveRange()
    {
        var today = DateTime.Today;
        if (SelectedPeriod == Period7)
        {
            var from = today.AddDays(-6).ToString("yyyy-MM-dd");
            return (from, today.ToString("yyyy-MM-dd"), 7);
        }

        if (SelectedPeriod == Period30)
        {
            var from = today.AddDays(-29).ToString("yyyy-MM-dd");
            return (from, today.ToString("yyyy-MM-dd"), 30);
        }

        var t = today.ToString("yyyy-MM-dd");
        return (t, t, 1);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "--";
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("F1") + " KB";
        if (bytes < 1024L * 1024 * 1024) return (bytes / 1024.0 / 1024).ToString("F2") + " MB";
        return (bytes / 1024.0 / 1024 / 1024).ToString("F2") + " GB";
    }

    private static string FormatSpeed(double bps)
    {
        if (bps < 0) return "--";
        if (bps < 1024) return bps.ToString("F0") + " B/s";
        if (bps < 1024 * 1024) return (bps / 1024).ToString("F1") + " KB/s";
        return (bps / 1024 / 1024).ToString("F2") + " MB/s";
    }
}

public class DayTrafficBar
{
    public string DayLabel { get; set; } = string.Empty;
    public string FullDay { get; set; } = string.Empty;
    public string UpText { get; set; } = string.Empty;
    public string DownText { get; set; } = string.Empty;
    public string TotalText { get; set; } = string.Empty;
    public double UpBarHeight { get; set; }
    public double DownBarHeight { get; set; }
}

public class HourTrafficBar
{
    public string HourLabel { get; set; } = string.Empty;
    public string UpText { get; set; } = string.Empty;
    public string DownText { get; set; } = string.Empty;
    public string TotalText { get; set; } = string.Empty;
    public double BarHeight { get; set; }
    public bool HasData { get; set; }
    public double BarOpacity { get; set; } = 1.0;
}

public class ProcessTrafficRow
{
    public int Rank { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string UpText { get; set; } = string.Empty;
    public string DownText { get; set; } = string.Empty;
    public string TotalText { get; set; } = string.Empty;
    public string PeakText { get; set; } = string.Empty;
    public double SharePercent { get; set; }
    public double BarWidth { get; set; }
}
