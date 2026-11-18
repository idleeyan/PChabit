using System.Text.Json;
using PChabit.Core.Runtime;
using Serilog;
namespace PChabit.HardwareMonitor.Hardware;

/// <summary>
/// GPU 传感器接入 Rust 内核（NVML 直读）。
///
/// 背景：LHM 的 <c>GpuNvidia.Update()</c> 实测 85.7 ms/拍，占整个硬件监控 86.5 ms 的 99%，
/// 而 PChabit 实际只用 6 个指标。内核里 <c>pchabit-netd gpu</c> 用 NVML 直读同一口径，
/// 热指标每拍、冷指标每 5 拍，实测 2.7 ms/拍。
///
/// 边界：只接管 NVIDIA。内核报 <c>avail=false</c>（无 N 卡/无 nvml.dll/读失败）时本类立即失效，
/// 调用方原样退回 LHM 的 <c>hw.Update()</c>，不做半截数据。
///
/// 开关：环境变量 PCHABIT_RUST_GPU=（默认启用），"0"/"false"/"off" 关闭。
/// </summary>
internal sealed class RustGpuProbe
{
    private const string EnvVar = "PCHABIT_RUST_GPU";

    private static readonly string[] SupportedKeys =
    {
        "GPU.Load", "GPU.Temp", "GPU.VRAM", "GPU.Fan", "GPU.Clock", "GPU.Power"
    };

    private readonly object _gate = new();
    private readonly Dictionary<string, float> _values = new();
    private bool _active;
    private DateTime _lastReplyUtc = DateTime.MinValue;

    /// <summary>内核通道是否可用（可用时调用方应跳过 LHM 的 GPU 更新）。</summary>
    public bool Active
    {
        get { lock (_gate) return _active; }
    }

    private static bool Enabled
    {
        get
        {
            var v = Environment.GetEnvironmentVariable(EnvVar);
            if (string.IsNullOrWhiteSpace(v)) return true;
            return !(v.Trim() == "0"
                     || v.Trim().Equals("false", StringComparison.OrdinalIgnoreCase)
                     || v.Trim().Equals("off", StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>每拍调用一次：向内核取 GPU 快照并刷新缓存。</summary>
    public void Tick()
    {
        if (!Enabled)
        {
            lock (_gate) _active = false;
            return;
        }

        var reply = DaemonPipe.Shared.Send("GPU");
        if (reply == null)
        {
            // 内核不在线：本拍失效，让调用方退回 LHM。内核恢复后会自动重新接管。
            lock (_gate) _active = false;
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(reply);
            var root = doc.RootElement;
            var avail = root.TryGetProperty("avail", out var a) && a.ValueKind == JsonValueKind.True;
            if (!avail)
            {
                lock (_gate)
                {
                    _active = false;
                    _values.Clear();
                }
                return;
            }

            var vramUsed = ReadF(root, "vramUsed");
            var vramTotal = ReadF(root, "vramTotal");

            lock (_gate)
            {
                _values["GPU.Load"] = ReadF(root, "load");
                _values["GPU.Temp"] = ReadF(root, "temp");
                // 口径对齐：LHM 的 GPU.VRAM 是「已用/总量 × 100」的百分比，不是 MB
                _values["GPU.VRAM"] = vramTotal > 0 ? vramUsed / vramTotal * 100f : 0f;
                _values["GPU.Fan"] = ReadF(root, "fan");
                _values["GPU.Clock"] = ReadF(root, "clock");
                _values["GPU.Power"] = ReadF(root, "power");
                _active = true;
                _lastReplyUtc = DateTime.UtcNow;
            }

            if (vramTotal > 0 && HardwareMonitorOptions.DetectedGpuVramTotalGB <= 0)
            {
                HardwareMonitorOptions.DetectedGpuVramTotalGB = vramTotal / 1024f;
            }
        }
        catch (Exception ex)
        {
            lock (_gate) _active = false;
            Log.Debug(ex, "解析 GPU 内核快照失败，本拍退回 LHM");
        }
    }

    /// <summary>命中则返回内核值；未接管或键不在支持列表返回 false。</summary>
    public bool TryGet(string key, out float value)
    {
        value = 0f;
        if (Array.IndexOf(SupportedKeys, key) < 0) return false;

        lock (_gate)
        {
            if (!_active) return false;
            // 快照过期（内核卡死/断开超过 3 拍）视同失效，避免读到陈旧读数
            if ((DateTime.UtcNow - _lastReplyUtc).TotalSeconds > 3) return false;
            if (!_values.TryGetValue(key, out value)) return false;
            return true;
        }
    }

    private static float ReadF(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number
            ? (float)e.GetDouble()
            : 0f;
    }
}
