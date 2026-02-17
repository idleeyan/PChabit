using System.Diagnostics;
using System.Runtime;
using Serilog;

namespace PChabit.Infrastructure.Services;

public interface IPerformanceMonitor
{
    PerformanceMetrics GetCurrentMetrics();
    void StartMonitoring();
    void StopMonitoring();
    event EventHandler<PerformanceMetricsEventArgs>? MetricsUpdated;
}

public class PerformanceMonitor : IPerformanceMonitor, IDisposable
{
    private readonly Timer _monitoringTimer;
    private readonly Process _currentProcess;
    private bool _isMonitoring;
    private DateTime _lastCpuCheck;
    private TimeSpan _lastCpuTime;
    
    private const int MonitoringIntervalMs = 1000;
    
    public event EventHandler<PerformanceMetricsEventArgs>? MetricsUpdated;
    
    public PerformanceMonitor()
    {
        _currentProcess = Process.GetCurrentProcess();
        _monitoringTimer = new Timer(OnMonitoringTimer, null, Timeout.Infinite, MonitoringIntervalMs);
        _lastCpuCheck = DateTime.UtcNow;
        _lastCpuTime = _currentProcess.TotalProcessorTime;
    }
    
    public bool IsMonitoring => _isMonitoring;
    
    public void StartMonitoring()
    {
        if (_isMonitoring) return;
        
        _isMonitoring = true;
        _monitoringTimer.Change(0, MonitoringIntervalMs);
        Log.Information("性能监控已启动");
    }
    
    public void StopMonitoring()
    {
        if (!_isMonitoring) return;
        
        _isMonitoring = false;
        _monitoringTimer.Change(Timeout.Infinite, MonitoringIntervalMs);
        Log.Information("性能监控已停止");
    }
    
    public PerformanceMetrics GetCurrentMetrics()
    {
        _currentProcess.Refresh();
        
        var cpuUsage = CalculateCpuUsage();
        var memoryMB = _currentProcess.WorkingSet64 / (1024.0 * 1024.0);
        var threadCount = _currentProcess.Threads.Count;
        var handleCount = _currentProcess.HandleCount;
        var gcMemory = GC.GetTotalMemory(false) / (1024.0 * 1024.0);
        
        return new PerformanceMetrics
        {
            Timestamp = DateTime.UtcNow,
            CpuUsagePercent = cpuUsage,
            MemoryMB = memoryMB,
            GcMemoryMB = gcMemory,
            ThreadCount = threadCount,
            HandleCount = handleCount,
            Gen0Collections = GC.CollectionCount(0),
            Gen1Collections = GC.CollectionCount(1),
            Gen2Collections = GC.CollectionCount(2)
        };
    }
    
    private double CalculateCpuUsage()
    {
        var currentTime = DateTime.UtcNow;
        var currentCpuTime = _currentProcess.TotalProcessorTime;
        
        var cpuUsedMs = (currentCpuTime - _lastCpuTime).TotalMilliseconds;
        var totalMsPassed = (currentTime - _lastCpuCheck).TotalMilliseconds;
        
        var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed) * 100;
        
        _lastCpuCheck = currentTime;
        _lastCpuTime = currentCpuTime;
        
        return Math.Max(0, Math.Min(100, cpuUsageTotal));
    }
    
    private void OnMonitoringTimer(object? state)
    {
        try
        {
            var metrics = GetCurrentMetrics();
            MetricsUpdated?.Invoke(this, new PerformanceMetricsEventArgs(metrics));
            
            if (metrics.MemoryMB > 100)
            {
                Log.Warning("内存使用超过 100MB: {MemoryMB:F2}MB", metrics.MemoryMB);
            }
            
            if (metrics.CpuUsagePercent > 5)
            {
                Log.Warning("CPU 使用超过 5%: {CpuUsage:F2}%", metrics.CpuUsagePercent);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "性能监控出错");
        }
    }
    
    public void Dispose()
    {
        StopMonitoring();
        _monitoringTimer.Dispose();
        _currentProcess.Dispose();
    }
}

public class PerformanceMetrics
{
    public DateTime Timestamp { get; set; }
    public double CpuUsagePercent { get; set; }
    public double MemoryMB { get; set; }
    public double GcMemoryMB { get; set; }
    public int ThreadCount { get; set; }
    public int HandleCount { get; set; }
    public int Gen0Collections { get; set; }
    public int Gen1Collections { get; set; }
    public int Gen2Collections { get; set; }
}

public class PerformanceMetricsEventArgs : EventArgs
{
    public PerformanceMetrics Metrics { get; }
    
    public PerformanceMetricsEventArgs(PerformanceMetrics metrics)
    {
        Metrics = metrics;
    }
}

public static class PerformanceOptimizer
{
    public static void OptimizeForProduction()
    {
        GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
        
        ThreadPool.SetMinThreads(Environment.ProcessorCount * 2, Environment.ProcessorCount * 2);
        ThreadPool.SetMaxThreads(Environment.ProcessorCount * 4, Environment.ProcessorCount * 4);
        
        Log.Information("已应用生产环境性能优化设置");
    }
    
    public static void OptimizeForDevelopment()
    {
        GCSettings.LatencyMode = GCLatencyMode.Interactive;
        
        Log.Information("已应用开发环境性能优化设置");
    }
    
    public static void ForceGarbageCollection()
    {
        var beforeMemory = GC.GetTotalMemory(false) / (1024.0 * 1024.0);
        
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        
        var afterMemory = GC.GetTotalMemory(true) / (1024.0 * 1024.0);
        
        Log.Information("GC 完成: {Before:F2}MB -> {After:F2}MB (释放 {Freed:F2}MB)", 
            beforeMemory, afterMemory, beforeMemory - afterMemory);
    }
}
