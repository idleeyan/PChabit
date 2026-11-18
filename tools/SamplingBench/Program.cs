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
        if (mode is "lhm-detail") BenchmarkLhmDetail();
        if (mode is "netadapter") BenchmarkAdapter();
        if (mode is "nvml") BenchmarkNvml();
        if (mode is "nvml-percall") BenchmarkNvmlPerCall();
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

    private static void BenchmarkLhmDetail()
    {
        Console.WriteLine();
        Console.WriteLine("[硬件监控·分解] 逐个硬件节点计时，看 86 ms 到底花在哪 ← 单例 + 内核驱动");

        var computer = new LibreHardwareMonitor.Hardware.Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsStorageEnabled = true,
            IsNetworkEnabled = true,
            IsBatteryEnabled = true,
            IsControllerEnabled = false,
            IsPsuEnabled = false
        };

        var open = Stopwatch.StartNew();
        computer.Open();
        Console.WriteLine($"  Computer.Open(): {open.Elapsed.TotalMilliseconds:F1} ms");
        Console.WriteLine("  硬件树: " + string.Join(" / ", computer.Hardware.Select(h => $"{h.HardwareType}:{h.Name}")));

        // 预热：首拍含驱动连接、性能计数器实例创建
        var warm = Stopwatch.StartNew();
        foreach (var hw in computer.Hardware) { try { hw.Update(); } catch { } }
        Console.WriteLine($"  预热拍: {warm.Elapsed.TotalMilliseconds:F1} ms");
        Thread.Sleep(1500);

        var perType = new Dictionary<string, List<double>>();
        var perNode = new Dictionary<string, List<double>>();
        const int ticks = 15;

        for (var t = 0; t < ticks; t++)
        {
            foreach (var hw in computer.Hardware)
            {
                var type = hw.HardwareType.ToString();
                var node = $"{type}:{hw.Name}";
                var sw = Stopwatch.StartNew();
                try
                {
                    hw.Update();
                    foreach (var sub in hw.SubHardware) sub.Update();
                }
                catch { }
                sw.Stop();

                var ms = sw.Elapsed.TotalMilliseconds;
                if (!perType.ContainsKey(type)) perType[type] = new List<double>();
                perType[type].Add(ms);
                if (!perNode.ContainsKey(node)) perNode[node] = new List<double>();
                perNode[node].Add(ms);
            }
            Thread.Sleep(1000);
        }

        Console.WriteLine();
        Console.WriteLine("  按类型（每拍一次，含子硬件）:");
        foreach (var kv in perType.OrderByDescending(k => k.Value.Average()))
        {
            var avg = kv.Value.Average();
            var sorted = kv.Value.OrderBy(v => v).ToList();
            Console.WriteLine($"    {kv.Key,-14} 平均 {avg,7:F2} ms | p50 {sorted[sorted.Count / 2],7:F2} | max {sorted[^1],7:F2}");
        }
        Console.WriteLine($"    合计平均 {perType.Values.Sum(v => v.Average()):F2} ms/拍");

        Console.WriteLine();
        Console.WriteLine("  按节点 Top 8:");
        foreach (var kv in perNode.OrderByDescending(k => k.Value.Average()).Take(8))
        {
            Console.WriteLine($"    {kv.Key,-46} {kv.Value.Average(),7:F2} ms/拍");
        }

        computer.Close();
    }

    /// <summary>网卡统计读取成本（生产里每拍都要读一次 IP Statistics）。</summary>
    private static void BenchmarkAdapter()
    {
        Console.WriteLine();
        Console.WriteLine("[网卡] NetworkInterface.GetAllNetworkInterfaces() + GetIPStatistics()");

        var samples = new List<double>();
        for (var i = 0; i < 20; i++)
        {
            var sw = Stopwatch.StartNew();
            var nics = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
            long total = 0;
            foreach (var nic in nics)
            {
                try
                {
                    var stats = nic.GetIPStatistics();
                    total += stats.BytesSent + stats.BytesReceived;
                }
                catch { }
            }
            sw.Stop();
            samples.Add(sw.Elapsed.TotalMilliseconds);
            if (i == 0) Console.WriteLine($"  网卡数 {nics.Length}，字节合计 {total}");
            Thread.Sleep(200);
        }
        Report(samples);
    }

    /// <summary>对照组：NVML 直接读 6 个 GPU 指标的成本（PChabit 只需 Load/Temp/VRAM/Fan/Clock/Power）。</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NvmlUtilization { public uint Gpu; public uint Memory; }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NvmlMemory { public ulong Total; public ulong Free; public ulong Used; }

    [System.Runtime.InteropServices.DllImport("nvml.dll", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
    private static extern int nvmlInit_v2();
    [System.Runtime.InteropServices.DllImport("nvml.dll", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);
    [System.Runtime.InteropServices.DllImport("nvml.dll", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out NvmlUtilization util);
    [System.Runtime.InteropServices.DllImport("nvml.dll", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetTemperature(IntPtr device, uint sensorType, out uint temp);
    [System.Runtime.InteropServices.DllImport("nvml.dll", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetMemoryInfo(IntPtr device, out NvmlMemory mem);
    [System.Runtime.InteropServices.DllImport("nvml.dll", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetFanSpeed(IntPtr device, out uint speed);
    [System.Runtime.InteropServices.DllImport("nvml.dll", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetClockInfo(IntPtr device, uint clockType, out uint clock);
    [System.Runtime.InteropServices.DllImport("nvml.dll", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetPowerUsage(IntPtr device, out uint watts);

    private static void BenchmarkNvml()
    {
        Console.WriteLine();
        Console.WriteLine("[GPU·NVML 直读] 6 个指标：利用率/温度/显存/风扇/频率/功耗");

        var t0 = Stopwatch.StartNew();
        var rc = nvmlInit_v2();
        IntPtr dev = IntPtr.Zero;
        nvmlDeviceGetHandleByIndex_v2(0, out dev);
        t0.Stop();
        Console.WriteLine($"  nvmlInit + 取句柄：{t0.Elapsed.TotalMilliseconds:F2} ms（rc={rc}）");

        uint temp = 0, fan = 0, clk = 0, pw = 0;
        NvmlUtilization util = default;
        NvmlMemory mem = default;

        // 预热
        for (var i = 0; i < 5; i++)
        {
            nvmlDeviceGetUtilizationRates(dev, out util);
            nvmlDeviceGetTemperature(dev, 0, out temp);
            nvmlDeviceGetMemoryInfo(dev, out mem);
            nvmlDeviceGetFanSpeed(dev, out fan);
            nvmlDeviceGetClockInfo(dev, 1, out clk);
            nvmlDeviceGetPowerUsage(dev, out pw);
        }

        var samples = new List<double>();
        for (var i = 0; i < 60; i++)
        {
            var sw = Stopwatch.StartNew();
            nvmlDeviceGetUtilizationRates(dev, out util);
            nvmlDeviceGetTemperature(dev, 0, out temp);
            nvmlDeviceGetMemoryInfo(dev, out mem);
            nvmlDeviceGetFanSpeed(dev, out fan);
            nvmlDeviceGetClockInfo(dev, 1, out clk);
            nvmlDeviceGetPowerUsage(dev, out pw);
            sw.Stop();
            samples.Add(sw.Elapsed.TotalMilliseconds);
            Thread.Sleep(100);
        }

        nvmlDeviceGetUtilizationRates(dev, out util);
        nvmlDeviceGetTemperature(dev, 0, out temp);
        nvmlDeviceGetMemoryInfo(dev, out mem);
        nvmlDeviceGetFanSpeed(dev, out fan);
        nvmlDeviceGetClockInfo(dev, 1, out clk);
        nvmlDeviceGetPowerUsage(dev, out pw);
        Console.WriteLine($"  读数：占用 {util.Gpu}% | 温度 {temp}C | 显存 {mem.Used / 1024 / 1024}MB/{mem.Total / 1024 / 1024}MB | 风扇 {fan}% | 频率 {clk}MHz | 功耗 {pw / 1000.0:F0}W");
        Report(samples);
    }

    private static void BenchmarkNvmlPerCall()
    {
        Console.WriteLine();
        Console.WriteLine("[GPU·NVML 单调用成本] 决定哪些指标可以降频取");

        nvmlInit_v2();
        IntPtr dev = IntPtr.Zero;
        nvmlDeviceGetHandleByIndex_v2(0, out dev);

        var calls = new (string Name, Action Fn)[]
        {
            ("利用率 nvmlDeviceGetUtilizationRates", () => { NvmlUtilization u; nvmlDeviceGetUtilizationRates(dev, out u); }),
            ("温度   nvmlDeviceGetTemperature", () => { uint v; nvmlDeviceGetTemperature(dev, 0, out v); }),
            ("显存   nvmlDeviceGetMemoryInfo", () => { NvmlMemory m; nvmlDeviceGetMemoryInfo(dev, out m); }),
            ("风扇   nvmlDeviceGetFanSpeed", () => { uint v; nvmlDeviceGetFanSpeed(dev, out v); }),
            ("频率   nvmlDeviceGetClockInfo", () => { uint v; nvmlDeviceGetClockInfo(dev, 1, out v); }),
            ("功耗   nvmlDeviceGetPowerUsage", () => { uint v; nvmlDeviceGetPowerUsage(dev, out v); })
        };

        foreach (var c in calls)
        {
            for (var i = 0; i < 3; i++) c.Fn(); // 预热
            var samples = new List<double>();
            for (var i = 0; i < 40; i++)
            {
                var sw = Stopwatch.StartNew();
                c.Fn();
                sw.Stop();
                samples.Add(sw.Elapsed.TotalMicroseconds / 1000.0);
                Thread.Sleep(50);
            }
            var sorted = samples.OrderBy(v => v).ToList();
            Console.WriteLine($"    {c.Name,-38} 平均 {samples.Average(),6:F3} ms | p50 {sorted[sorted.Count / 2],6:F3} | max {sorted[^1],6:F3}");
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
