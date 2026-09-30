using Microsoft.EntityFrameworkCore;
using PChabit.Core.Entities;
using PChabit.HardwareMonitor.Hardware;
using PChabit.Infrastructure.Data;
using Serilog;

namespace PChabit.App.Services;

/// <summary>
/// 网络流量落库：系统分钟样本 + 进程日/小时聚合。
/// 从 ProcessNetworkMonitor 读增量写入 SQLite，供「网络流量」独立页统计。
/// </summary>
public sealed class NetworkTrafficPersistenceService : IDisposable
{
    private const int FlushIntervalMs = 15_000;
    private const int SampleRetainDays = 30;
    private const int ProcessRetainDays = 90;

    private readonly ProcessNetworkMonitor _monitor;
    private readonly IDbContextFactory<PChabitDbContext> _dbFactory;

    private System.Threading.Timer? _timer;
    private readonly object _sync = new();
    private bool _running;

    // 系统累计（用于算增量）
    private long _lastSysUp;
    private long _lastSysDown;
    private double _minuteUp;
    private double _minuteDown;
    private double _minutePeakUp;
    private double _minutePeakDown;
    private int _minuteSampleCount;
    private string _minuteKey = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
    private DateTime _lastCleanupUtc = DateTime.MinValue;

    // 进程今日累计已落库水位
    private readonly Dictionary<string, (long Up, long Down)> _flushedToday =
        new(StringComparer.OrdinalIgnoreCase);

    public NetworkTrafficPersistenceService(
        ProcessNetworkMonitor monitor,
        IDbContextFactory<PChabitDbContext> dbFactory)
    {
        _monitor = monitor;
        _dbFactory = dbFactory;
    }

    public bool IsRunning
    {
        get { lock (_sync) return _running; }
    }

    public void Start()
    {
        lock (_sync)
        {
            if (_running) return;
            _running = true;
            _timer = new System.Threading.Timer(_ => SafeFlush(), null, 2_000, FlushIntervalMs);
        }

        _ = SeedFromDatabaseAsync();
        Log.Information("网络流量落库服务已启动");
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (!_running) return;
            _running = false;
            _timer?.Dispose();
            _timer = null;
        }

        try
        {
            FlushNow();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "网络流量停止落库失败");
        }
    }

    public void Dispose() => Stop();

    private async Task SeedFromDatabaseAsync()
    {
        try
        {
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            await using var db = await _dbFactory.CreateDbContextAsync();

            var procRows = await db.ProcessNetworkUsages
                .AsNoTracking()
                .Where(x => x.Date == today)
                .ToListAsync();

            var byName = new Dictionary<string, (long Up, long Down)>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in procRows)
            {
                var key = r.ProcessName;
                byName.TryGetValue(key, out var acc);
                byName[key] = (acc.Up + r.BytesUp, acc.Down + r.BytesDown);
            }

            foreach (var kv in byName)
            {
                _flushedToday[kv.Key] = (kv.Value.Up, kv.Value.Down);
            }

            _monitor.SeedTodayFrom(byName.Select(kv => (kv.Key, kv.Value.Up, kv.Value.Down)));

            var sampleRows = await db.NetworkTrafficSamples
                .AsNoTracking()
                .Where(x => x.Timestamp.StartsWith(today))
                .ToListAsync();

            long seedUp = sampleRows.Sum(x => x.BytesUp);
            long seedDown = sampleRows.Sum(x => x.BytesDown);
            _monitor.SeedSystemToday(seedUp, seedDown);
            _lastSysUp = _monitor.GetSystemTotals().TodayUp;
            _lastSysDown = _monitor.GetSystemTotals().TodayDown;

            Log.Information("网络流量历史种子加载完成: 进程 {ProcCount} 项, 系统今日 ↑{Up} ↓{Down}",
                byName.Count, seedUp, seedDown);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "网络流量历史种子加载失败");
        }
    }

    private void SafeFlush()
    {
        try
        {
            FlushNow();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "网络流量落库失败");
        }
    }

    /// <summary>立即把增量写入数据库（15s 定时与退出时调用）。</summary>
    public void FlushNow()
    {
        var snap = _monitor.GetSnapshot(3);
        var totals = _monitor.GetSystemTotals();
        var procs = _monitor.GetAllTodayProcesses();
        var rates = snap.TopByRateList;

        long sysUpDelta;
        long sysDownDelta;
        double peakUp = 0;
        double peakDown = 0;

        lock (_sync)
        {
            sysUpDelta = Math.Max(0, totals.TodayUp - _lastSysUp);
            sysDownDelta = Math.Max(0, totals.TodayDown - _lastSysDown);
            _lastSysUp = totals.TodayUp;
            _lastSysDown = totals.TodayDown;

            // 用当前速率估峰值
            foreach (var p in rates)
            {
                if (p.UpBytesPerSec > peakUp) peakUp = p.UpBytesPerSec;
                if (p.DownBytesPerSec > peakDown) peakDown = p.DownBytesPerSec;
            }

            // 系统速率也可由硬件注入估算：优先用监视器系统差/间隔近似
            var now = DateTime.Now;
            var minuteKey = now.ToString("yyyy-MM-dd HH:mm");
            if (!string.Equals(minuteKey, _minuteKey, StringComparison.Ordinal))
            {
                WriteMinuteBucket(_minuteKey, (long)_minuteUp, (long)_minuteDown, _minutePeakUp, _minutePeakDown, _minuteSampleCount);
                _minuteKey = minuteKey;
                _minuteUp = 0;
                _minuteDown = 0;
                _minutePeakUp = 0;
                _minutePeakDown = 0;
                _minuteSampleCount = 0;
            }

            _minuteUp += sysUpDelta;
            _minuteDown += sysDownDelta;
            var estInterval = FlushIntervalMs / 1000.0;
            var upBps = sysUpDelta / estInterval;
            var downBps = sysDownDelta / estInterval;
            if (upBps > _minutePeakUp) _minutePeakUp = upBps;
            if (downBps > _minutePeakDown) _minutePeakDown = downBps;
            _minuteSampleCount++;

            MaybeCleanup(now);
        }

        WriteProcessHourDeltas(procs);
        WriteMinuteBucketIfForced(sysUpDelta, sysDownDelta, peakUp, peakDown);
    }

    private void WriteMinuteBucketIfForced(long up, long down, double peakUp, double peakDown)
    {
        // 空闲时不写 0 样本，避免刷屏；有流量或分钟切换时由 WriteMinuteBucket 处理
        _ = up;
        _ = down;
        _ = peakUp;
        _ = peakDown;
    }

    private void WriteMinuteBucket(string minuteKey, long up, long down, double peakUp, double peakDown, int sampleCount)
    {
        if (sampleCount <= 0 && up <= 0 && down <= 0) return;
        if (string.IsNullOrEmpty(minuteKey)) return;

        try
        {
            using var db = _dbFactory.CreateDbContext();
            var row = db.NetworkTrafficSamples.FirstOrDefault(x => x.Timestamp == minuteKey);
            if (row == null)
            {
                row = new NetworkTrafficSample
                {
                    Timestamp = minuteKey,
                    BytesUp = up,
                    BytesDown = down,
                    PeakUpBps = peakUp,
                    PeakDownBps = peakDown,
                    SampleCount = Math.Max(1, sampleCount),
                    LastUpdated = DateTime.Now
                };
                db.NetworkTrafficSamples.Add(row);
            }
            else
            {
                row.BytesUp += up;
                row.BytesDown += down;
                row.PeakUpBps = Math.Max(row.PeakUpBps, peakUp);
                row.PeakDownBps = Math.Max(row.PeakDownBps, peakDown);
                row.SampleCount += Math.Max(1, sampleCount);
                row.LastUpdated = DateTime.Now;
            }

            db.SaveChanges();

            TryUpdateDailySummary(db, minuteKey[..10]);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "写入 NetworkTrafficSamples 失败: {Key}", minuteKey);
        }
    }

    private void WriteProcessHourDeltas(List<ProcessNetworkItem> procs)
    {
        if (procs.Count == 0) return;

        var now = DateTime.Now;
        var date = now.ToString("yyyy-MM-dd");
        var hour = now.Hour;
        var deltas = new List<(string Name, long Up, long Down, long Peak)>();

        lock (_sync)
        {
            foreach (var p in procs)
            {
                if (string.IsNullOrWhiteSpace(p.ProcessName)) continue;
                var todayUp = p.TodayUpBytes;
                var todayDown = p.TodayDownBytes;
                _flushedToday.TryGetValue(p.ProcessName, out var flushed);
                var dUp = todayUp - flushed.Up;
                var dDown = todayDown - flushed.Down;
                if (dUp <= 0 && dDown <= 0) continue;

                _flushedToday[p.ProcessName] = (todayUp, todayDown);
                var peak = (long)Math.Max(p.UpBytesPerSec + p.DownBytesPerSec, 0);
                deltas.Add((p.ProcessName, Math.Max(0, dUp), Math.Max(0, dDown), peak));
            }
        }

        if (deltas.Count == 0) return;

        try
        {
            using var db = _dbFactory.CreateDbContext();
            foreach (var (name, dUp, dDown, peak) in deltas)
            {
                var row = db.ProcessNetworkUsages.Find(date, hour, name);
                if (row == null)
                {
                    db.ProcessNetworkUsages.Add(new ProcessNetworkUsage
                    {
                        Date = date,
                        Hour = hour,
                        ProcessName = name,
                        BytesUp = dUp,
                        BytesDown = dDown,
                        PeakBytesPerSec = peak,
                        LastUpdated = now
                    });
                }
                else
                {
                    row.BytesUp += dUp;
                    row.BytesDown += dDown;
                    if (peak > row.PeakBytesPerSec) row.PeakBytesPerSec = peak;
                    row.LastUpdated = now;
                }
            }

            db.SaveChanges();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "写入 ProcessNetworkUsages 失败");
        }
    }

    private static void TryUpdateDailySummary(PChabitDbContext db, string date)
    {
        try
        {
            var up = db.NetworkTrafficSamples
                .Where(x => x.Timestamp.StartsWith(date))
                .Sum(x => (long?)x.BytesUp) ?? 0;
            var down = db.NetworkTrafficSamples
                .Where(x => x.Timestamp.StartsWith(date))
                .Sum(x => (long?)x.BytesDown) ?? 0;

            var summary = db.DailySummaries.FirstOrDefault(x => x.Date == date);
            if (summary == null)
            {
                db.DailySummaries.Add(new DailySummary
                {
                    Date = date,
                    NetBytesUp = up,
                    NetBytesDown = down,
                    LastUpdated = DateTime.Now,
                    TopApps = "[]",
                    HourlyKeyDistribution = "[]"
                });
            }
            else
            {
                summary.NetBytesUp = up;
                summary.NetBytesDown = down;
                summary.LastUpdated = DateTime.Now;
            }

            db.SaveChanges();
        }
        catch
        {
            // DailySummary 为附属信息，失败不影响主落库
        }
    }

    private void MaybeCleanup(DateTime now)
    {
        if ((now - _lastCleanupUtc).TotalHours < 20) return;
        _lastCleanupUtc = now;

        var sampleCut = now.AddDays(-SampleRetainDays).ToString("yyyy-MM-dd");
        var procCut = now.AddDays(-ProcessRetainDays).ToString("yyyy-MM-dd");
        try
        {
            using var db = _dbFactory.CreateDbContext();
            db.Database.ExecuteSqlRaw(
                "DELETE FROM NetworkTrafficSamples WHERE Timestamp < {0}", sampleCut);
            db.Database.ExecuteSqlRaw(
                "DELETE FROM ProcessNetworkUsages WHERE Date < {0}", procCut);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "网络流量历史清理跳过");
        }
    }
}
