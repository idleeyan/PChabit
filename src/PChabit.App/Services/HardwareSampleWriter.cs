using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Serilog;
using PChabit.Core.Entities;
using PChabit.HardwareMonitor;
using PChabit.Infrastructure.Data;

namespace PChabit.App.Services;

/// <summary>
/// 硬件分钟样本写入器（分析升级 P0）：
/// 订阅 <see cref="HardwareMonitorService.ValuesUpdated"/>（1s，线程池线程），
/// 在内存分钟桶内聚合均值/峰值，每分钟翻转一次，经 Channel 串行 Upsert 到 HardwareSamples。
/// 休眠/关机缺分钟无行；停监控时提交残余分钟（普通事务，不做 WAL checkpoint）。
/// 聚合逻辑在 <see cref="MinuteBucket"/>（无 IO，可单测）。
/// </summary>
public sealed class HardwareSampleWriter : IDisposable
{
    private readonly HardwareMonitorService _hardware;
    private readonly IDbContextFactory<PChabitDbContext> _dbFactory;
    private readonly object _bucketLock = new();
    private readonly Channel<HardwareSample> _channel =
        Channel.CreateUnbounded<HardwareSample>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

    private MinuteBucket? _current;
    private CancellationTokenSource? _cts;
    private Task? _consumerTask;
    private bool _started;
    private bool _disposed;

    public HardwareSampleWriter(HardwareMonitorService hardware, IDbContextFactory<PChabitDbContext> dbFactory)
    {
        _hardware = hardware;
        _dbFactory = dbFactory;
    }

    /// <summary>订阅采集事件并启动串行写库消费者（幂等）。</summary>
    public void Start()
    {
        if (_started) return;
        _started = true;
        _cts = new CancellationTokenSource();
        _consumerTask = Task.Run(ConsumeAsync);
        _hardware.ValuesUpdated += OnValuesUpdated;
        Log.Information("HardwareSampleWriter 已启动（分钟样本落库）");
    }

    private void OnValuesUpdated()
    {
        try
        {
            var now = DateTime.Now;
            var key = now.ToString("yyyy-MM-dd HH:mm");
            HardwareSample? finished = null;

            lock (_bucketLock)
            {
                if (_current == null || _current.Key != key)
                {
                    if (_current != null) finished = _current.Build();
                    _current = new MinuteBucket(key);
                }
                _current.Add(_hardware.Get);
            }

            if (finished != null) _channel.Writer.TryWrite(finished);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "HardwareSampleWriter 采样聚合失败");
        }
    }

    /// <summary>停止订阅并提交残余分钟桶；等待在写样本落库（最多 3 秒）。</summary>
    public async Task StopAsync()
    {
        if (!_started) return;
        _started = false;
        _hardware.ValuesUpdated -= OnValuesUpdated;

        lock (_bucketLock)
        {
            if (_current != null)
            {
                _channel.Writer.TryWrite(_current.Build());
                _current = null;
            }
        }
        _channel.Writer.TryComplete();

        try
        {
            if (_consumerTask != null)
                await Task.WhenAny(_consumerTask, Task.Delay(3000));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "HardwareSampleWriter 停止等待异常");
        }
        _cts?.Cancel();
    }

    private async Task ConsumeAsync()
    {
        try
        {
            await foreach (var sample in _channel.Reader.ReadAllAsync(_cts?.Token ?? CancellationToken.None))
            {
                try
                {
                    await UpsertAsync(sample);
                }
                catch (Exception ex)
                {
                    // 单行失败不杀死消费者（锁竞争等下个分钟自然恢复）
                    Log.Warning(ex, "HardwareSample 写入失败: {Timestamp}", sample.Timestamp);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Warning(ex, "HardwareSampleWriter 消费者退出");
        }
    }

    /// <summary>按 Timestamp 唯一键 Upsert（原子 SQL，处理残余分钟与正常翻转的竞争）。</summary>
    private async Task UpsertAsync(HardwareSample s)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        s.Id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO HardwareSamples
  (Id, Timestamp, CpuLoadAvg, CpuLoadMax, CpuTempMax, GpuLoadAvg, GpuLoadMax, GpuTempMax,
   VramUsedMax, VramTotal, MemLoadAvg, MemLoadMax, MemUsedMax,
   DiskActivityAvg, DiskReadMax, DiskWriteMax, NetUpAvg, NetDownAvg, SampleCount, SensorFlags)
VALUES
  ({s.Id.ToString()}, {s.Timestamp}, {s.CpuLoadAvg}, {s.CpuLoadMax}, {s.CpuTempMax},
   {s.GpuLoadAvg}, {s.GpuLoadMax}, {s.GpuTempMax}, {s.VramUsedMax}, {s.VramTotal},
   {s.MemLoadAvg}, {s.MemLoadMax}, {s.MemUsedMax}, {s.DiskActivityAvg}, {s.DiskReadMax},
   {s.DiskWriteMax}, {s.NetUpAvg}, {s.NetDownAvg}, {s.SampleCount}, {s.SensorFlags})
ON CONFLICT(Timestamp) DO UPDATE SET
   CpuLoadAvg=excluded.CpuLoadAvg, CpuLoadMax=excluded.CpuLoadMax, CpuTempMax=excluded.CpuTempMax,
   GpuLoadAvg=excluded.GpuLoadAvg, GpuLoadMax=excluded.GpuLoadMax, GpuTempMax=excluded.GpuTempMax,
   VramUsedMax=excluded.VramUsedMax, VramTotal=excluded.VramTotal,
   MemLoadAvg=excluded.MemLoadAvg, MemLoadMax=excluded.MemLoadMax, MemUsedMax=excluded.MemUsedMax,
   DiskActivityAvg=excluded.DiskActivityAvg, DiskReadMax=excluded.DiskReadMax, DiskWriteMax=excluded.DiskWriteMax,
   NetUpAvg=excluded.NetUpAvg, NetDownAvg=excluded.NetDownAvg,
   SampleCount=excluded.SampleCount, SensorFlags=excluded.SensorFlags;");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { StopAsync().GetAwaiter().GetResult(); } catch { }
        _cts?.Dispose();
    }
}

/// <summary>
/// 单分钟聚合桶（纯数值逻辑，无 IO/时钟依赖，可单测）。
/// 均值类维护 sum/count，峰值类维护 max；传感器缺失或 NaN 不计入。
/// </summary>
internal sealed class MinuteBucket
{
    public string Key { get; }

    // 均值累加器：sum/count
    private double _cpuLoadSum, _gpuLoadSum, _memLoadSum, _diskActSum, _netUpSum, _netDownSum;
    private int _cpuLoadN, _gpuLoadN, _memLoadN, _diskActN, _netUpN, _netDownN;

    // 峰值
    private double? _cpuLoadMax, _cpuTempMax, _gpuLoadMax, _gpuTempMax, _vramUsedMax, _vramTotal;
    private double? _memLoadMax, _memUsedMax, _diskReadMax, _diskWriteMax;

    private int _sampleCount, _sensorFlags;

    public MinuteBucket(string key) => Key = key;

    /// <summary>摄入一次 1s 采样快照（getter 对接 HardwareMonitorService.Get）。</summary>
    public void Add(Func<string, float?> get)
    {
        _sampleCount++;

        AccumAvg("CPU.Load", get, ref _cpuLoadSum, ref _cpuLoadN, ref _cpuLoadMax, HardwareSensorFlags.CpuLoad);
        AccumMax("CPU.Temp", get, ref _cpuTempMax, HardwareSensorFlags.CpuTemp);
        AccumAvg("GPU.Load", get, ref _gpuLoadSum, ref _gpuLoadN, ref _gpuLoadMax, HardwareSensorFlags.GpuLoad);
        AccumMax("GPU.Temp", get, ref _gpuTempMax, HardwareSensorFlags.GpuTemp);

        var vramUsed = Valid(get("GPU.VRAM.Used"));
        if (vramUsed.HasValue)
        {
            if (!_vramUsedMax.HasValue || vramUsed.Value > _vramUsedMax.Value) _vramUsedMax = vramUsed.Value;
            _sensorFlags |= (int)HardwareSensorFlags.VramUsed;
        }
        var vramTotal = Valid(get("GPU.VRAM.Total"));
        if (vramTotal.HasValue)
        {
            _vramTotal ??= vramTotal.Value; // 总量常量快照：取首次有效值
            _sensorFlags |= (int)HardwareSensorFlags.VramTotal;
        }

        AccumAvg("MEM.Load", get, ref _memLoadSum, ref _memLoadN, ref _memLoadMax, HardwareSensorFlags.MemLoad);
        AccumMax("MEM.Used", get, ref _memUsedMax, HardwareSensorFlags.MemUsed);

        AccumAvgOnly("DISK.Activity", get, ref _diskActSum, ref _diskActN, HardwareSensorFlags.DiskActivity);
        AccumMax("DISK.Read", get, ref _diskReadMax, HardwareSensorFlags.DiskRead);
        AccumMax("DISK.Write", get, ref _diskWriteMax, HardwareSensorFlags.DiskWrite);

        AccumAvgOnly("NET.Up", get, ref _netUpSum, ref _netUpN, HardwareSensorFlags.NetUp);
        AccumAvgOnly("NET.Down", get, ref _netDownSum, ref _netDownN, HardwareSensorFlags.NetDown);
    }

    private void AccumAvg(string key, Func<string, float?> get,
        ref double sum, ref int n, ref double? max, HardwareSensorFlags flag)
    {
        var v = Valid(get(key));
        if (!v.HasValue) return;
        sum += v.Value;
        n++;
        if (!max.HasValue || v.Value > max.Value) max = v.Value;
        _sensorFlags |= (int)flag;
    }

    private void AccumAvgOnly(string key, Func<string, float?> get,
        ref double sum, ref int n, HardwareSensorFlags flag)
    {
        var v = Valid(get(key));
        if (!v.HasValue) return;
        sum += v.Value;
        n++;
        _sensorFlags |= (int)flag;
    }

    private void AccumMax(string key, Func<string, float?> get, ref double? max, HardwareSensorFlags flag)
    {
        var v = Valid(get(key));
        if (!v.HasValue) return;
        if (!max.HasValue || v.Value > max.Value) max = v.Value;
        _sensorFlags |= (int)flag;
    }

    /// <summary>NaN/无穷大视为传感器缺失。</summary>
    private static double? Valid(float? v) =>
        v.HasValue && !float.IsNaN(v.Value) && !float.IsInfinity(v.Value) ? (double?)v.Value : null;

    /// <summary>产出实体（均值 = sum/count；无样本列为 null）。</summary>
    public HardwareSample Build() => new()
    {
        Timestamp = Key,
        CpuLoadAvg = Avg(_cpuLoadSum, _cpuLoadN),
        CpuLoadMax = _cpuLoadMax,
        CpuTempMax = _cpuTempMax,
        GpuLoadAvg = Avg(_gpuLoadSum, _gpuLoadN),
        GpuLoadMax = _gpuLoadMax,
        GpuTempMax = _gpuTempMax,
        VramUsedMax = _vramUsedMax,
        VramTotal = _vramTotal,
        MemLoadAvg = Avg(_memLoadSum, _memLoadN),
        MemLoadMax = _memLoadMax,
        MemUsedMax = _memUsedMax,
        DiskActivityAvg = Avg(_diskActSum, _diskActN),
        DiskReadMax = _diskReadMax,
        DiskWriteMax = _diskWriteMax,
        NetUpAvg = Avg(_netUpSum, _netUpN),
        NetDownAvg = Avg(_netDownSum, _netDownN),
        SampleCount = _sampleCount,
        SensorFlags = _sensorFlags
    };

    private static double? Avg(double sum, int n) => n > 0 ? Math.Round(sum / n, 2) : null;
}
