// 采样子系统基准：直接调用项目里的真实实现，量出每个子系统每拍的真实耗时。
// 用途：为「哪些部分适合迁到 Rust」提供判据（不猜，只量）。
//
// 运行：dotnet run -c Release --project tools/SamplingBench -- [net|lhm|rust|ab|all]

using System.Diagnostics;
using PChabit.HardwareMonitor;
using PChabit.HardwareMonitor.Hardware;

namespace SamplingBench;

public static class Program
{
    public static void Main(string[] args)
    {
        var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "net";
        Console.WriteLine($"=== PChabit 采样基准 (mode={mode}) ===");

        if (mode is "net" or "all") BenchmarkNet();
        if (mode is "lhm" or "all") BenchmarkLhm();
        if (mode is "rust" or "all") BenchmarkRust(withGetter: true);
        if (mode is "rust-nogetter") BenchmarkRust(withGetter: false);
        if (mode is "ab") BenchmarkAb();
    }

    /// <summary>Rust 通道：同一入口（SampleOnce），采样与聚合在守护进程里完成。</summary>
    private static void BenchmarkRust(bool withGetter)
    {
        Console.WriteLine();
        Console.WriteLine(withGetter
            ? "[进程网络采样·Rust 内核·注入 LHM 速率] ProcessNetworkMonitor.SampleOnce() → pchabit-netd"
            : "[进程网络采样·Rust 内核·无 getter 回退网卡统计] ProcessNetworkMonitor.SampleOnce() → pchabit-netd");

        Environment.SetEnvironmentVariable("PCHABIT_RUST_NETSAMPLER", "1");
        using var monitor = new ProcessNetworkMonitor();
        if (withGetter)
        {
            // 模拟生产：HardwareMonitorViewModel 注入 LHM NET.Up/Down，内核据此积分系统流量
            monitor.NetUpGetter = () => 12_000f;
            monitor.NetDownGetter = () => 48_000f;
        }
        monitor.SeedTodayFrom(Array.Empty<(string, long, long)>()); // 幂等接入内核，且不启动定时器

        monitor.SampleOnce(); // 预热：建基线
        Thread.Sleep(300);

        var samples = new List<double>();
        const int ticks = 20;
        for (var i = 0; i < ticks; i++)
        {
            var sw = Stopwatch.StartNew();
            monitor.SampleOnce();
            sw.Stop();
            samples.Add(sw.Elapsed.TotalMilliseconds);
            Thread.Sleep(1000);
        }

        Report(samples);
        DumpSnapshot(monitor);
    }

    /// <summary>A/B：同进程、同秒、同节奏，两套实现各跑一拍，直接比数与耗时。</summary>
    private static void BenchmarkAb()
    {
        Console.WriteLine();
        Console.WriteLine("[A/B] C# 内置采样 vs Rust 守护进程（同机同秒对照）");

        // 先让 A 落地为纯 C#（此时未开环境变量），再给 B 打开 Rust 通道
        using var csharpMonitor = new ProcessNetworkMonitor();
        csharpMonitor.SampleOnce();
        Environment.SetEnvironmentVariable("PCHABIT_RUST_NETSAMPLER", "1");
        using var rustMonitor = new ProcessNetworkMonitor();
        rustMonitor.SeedTodayFrom(Array.Empty<(string, long, long)>());
        rustMonitor.SampleOnce();
        Thread.Sleep(500);

        const int ticks = 20;
        var cs = new List<double>();
        var rs = new List<double>();
        var rateReport = new List<string>();

        for (var i = 0; i < ticks; i++)
        {
            var sw = Stopwatch.StartNew();
            csharpMonitor.SampleOnce();
            sw.Stop();
            cs.Add(sw.Elapsed.TotalMilliseconds);

            sw.Restart();
            rustMonitor.SampleOnce();
            sw.Stop();
            rs.Add(sw.Elapsed.TotalMilliseconds);

            var a = csharpMonitor.GetSnapshot(8);
            var b = rustMonitor.GetSnapshot(8);
            var aNames = string.Join(",", a.TopByRateList.Select(x => x.ProcessName));
            var bNames = string.Join(",", b.TopByRateList.Select(x => x.ProcessName));
            var aRate = a.TopByRateList.Sum(x => x.TotalBytesPerSec);
            var bRate = b.TopByRateList.Sum(x => x.TotalBytesPerSec);
            var ta = csharpMonitor.GetSystemTotals();
            var tb = rustMonitor.GetSystemTotals();
            rateReport.Add(
                $"#{i,2} C#Top=[{aNames}] Σ={aRate / 1024:F1}K/s  SysΔ=({ta.TodayUp / 1024}/{ta.TodayDown / 1024}K) | " +
                $"RustTop=[{bNames}] Σ={bRate / 1024:F1}K/s  SysΔ=({tb.TodayUp / 1024}/{tb.TodayDown / 1024}K)");
            Thread.Sleep(1000);
        }

        Console.WriteLine();
        Console.WriteLine("  —— C# 内置 ——");
        Report(cs);
        Console.WriteLine("  —— Rust 内核 ——");
        Report(rs);
        var avgC = cs.Average();
        var avgR = rs.Average();
        Console.WriteLine($"  差值：{avgC - avgR:F3} ms/拍 | 百分比 {(avgC - avgR) / avgC * 100:F1}% | 每秒省 {(avgC - avgR) * 1000:F0} us");
        Console.WriteLine();
        Console.WriteLine("  逐拍对照（进程名集合 + 速率和 + 系统今日累计）：");
        foreach (var line in rateReport) Console.WriteLine("  " + line);
    }

    private static void DumpSnapshot(ProcessNetworkMonitor monitor)
    {
        var snap = monitor.GetSnapshot(8);
        var totals = monitor.GetSystemTotals();
        Console.WriteLine($"  规模：TopByRate={snap.TopByRateList.Count} 条，系统今日 上={totals.TodayUp} 下={totals.TodayDown} 字节");
        foreach (var item in snap.TopByRateList.Take(5))
        {
            Console.WriteLine($"  {item.ProcessName,-24} ↓{item.DownBytesPerSec / 1024:F0}K/s ↑{item.UpBytesPerSec / 1024:F0}K/s 连接={item.ConnectionCount}");
        }
    }

    private static void BenchmarkNet()
    {
        Console.WriteLine();
        Console.WriteLine("[进程网络采样] ProcessNetworkMonitor.SampleOnce()");

        using var monitor = new ProcessNetworkMonitor();
        // 预热：首拍建立基线（_adapterPrimed / 首次 ResolveName），不计入样本
        monitor.SampleOnce();
        Thread.Sleep(200);

        var samples = new List<double>();
        const int ticks = 40;
        for (var i = 0; i < ticks; i++)
        {
            var sw = Stopwatch.StartNew();
            monitor.SampleOnce();
            sw.Stop();
            samples.Add(sw.Elapsed.TotalMilliseconds);
            Thread.Sleep(1000); // 贴近真实 1Hz 采样节奏
        }

        Report(samples);
        var snap = monitor.GetSnapshot(5);
        var totals = monitor.GetSystemTotals();
        Console.WriteLine($"  规模：TopByRate={snap.TopByRateList.Count} 条，系统今日 上={totals.TodayUp} 下={totals.TodayDown} 字节");
        foreach (var item in snap.TopByRateList.Take(5))
        {
            Console.WriteLine($"  {item.ProcessName,-24} ↓{item.DownBytesPerSec / 1024:F0}K/s ↑{item.UpBytesPerSec / 1024:F0}K/s 连接={item.ConnectionCount}");
        }
    }

    private static void BenchmarkLhm()
    {
        Console.WriteLine();
        Console.WriteLine("[硬件监控] HardwareMonitor.UpdateAll()  ← 单例 + 内核驱动，注意会与运行中的 App 共用驱动");

        var options = new HardwareMonitorOptions();
        var monitor = new HardwareMonitor(options);
        Console.WriteLine("  打开硬件树…");
        monitor.UpdateAll(); // 首拍含 Computer.Open + 传感器枚举，单独计时
        Thread.Sleep(1500);

        var samples = new List<double>();
        const int ticks = 20;
        for (var i = 0; i < ticks; i++)
        {
            var sw = Stopwatch.StartNew();
            monitor.UpdateAll();
            sw.Stop();
            samples.Add(sw.Elapsed.TotalMilliseconds);
            Thread.Sleep(1000);
        }

        Report(samples);
        monitor.Dispose();
    }

    private static void Report(List<double> samples)
    {
        var sorted = samples.OrderBy(v => v).ToList();
        var avg = samples.Average();
        Console.WriteLine($"  样本 {samples.Count} 拍 | 平均 {avg:F3} ms | p50 {sorted[sorted.Count / 2]:F3} ms | p95 {sorted[(int)(sorted.Count * 0.95)]:F3} ms | 最大 {sorted[^1]:F3} ms");
        Console.WriteLine($"  按 1 Hz：占单核 {avg / 10.0:F2}% | 占 12 核整机 {avg / 10.0 / 12:F3}%");
    }
}
