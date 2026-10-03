using Microsoft.Extensions.DependencyInjection;
using Serilog;
using PChabit.App.Services;
using PChabit.Core.Interfaces;

namespace PChabit.App;

/// <summary>
/// App 的桌面硬件悬浮插件驱动分部（3.23.0 从 App.xaml.cs 拆分，避免单文件超千行）。
/// 行为规格照抄 LiteMonitor（MIT）；窗口机制见 Services/DesktopWidget.cs。
/// 所有窗口操作均在 UI 线程：DispatcherTimer tick 驱动，退出时由主关闭流程回 UI 线程销毁。
/// </summary>
public partial class App
{
    // 独立 1s 刷新（任务栏小窗为 5s），窗口必须由 UI 线程创建
    private Microsoft.UI.Xaml.DispatcherTimer? _desktopWidgetTimer;
    private DesktopWidget? _desktopWidget;
    // 网速自适应峰值（bytes/s）：每秒 max(当前值, 峰值*0.97)，回落保证条不会长期满格
    private float _netPeakDown;
    private float _netPeakUp;
    private const float NetMinBaseline = 100f * 1024f; // 空闲/低速基准 100KB/s

    private void StartDesktopWidgetTimer()
    {
        try
        {
            _desktopWidgetTimer?.Stop();
            _desktopWidgetTimer = new Microsoft.UI.Xaml.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1) // LiteMonitor 默认 RefreshMs=1000
            };
            _desktopWidgetTimer.Tick += (_, _) => RefreshDesktopWidget();
            _desktopWidgetTimer.Start();
            Log.Information("桌面悬浮插件定时器已启动（1s 刷新）");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "启动桌面悬浮插件定时器失败");
        }
    }

    /// <summary>退出流程（后台线程）调用：仅停定时器；窗口销毁由 DisposeDesktopWidgetOnUiThread 在 UI 线程完成。</summary>
    private void StopDesktopWidgetTimer()
    {
        _desktopWidgetTimer?.Stop();
        _desktopWidgetTimer = null;
    }

    /// <summary>UI 线程调用：销毁悬浮窗（DestroyWindow 必须同创建线程）。</summary>
    private void DisposeDesktopWidgetOnUiThread()
    {
        _desktopWidget?.Dispose();
        _desktopWidget = null;
    }

    /// <summary>
    /// 驱动桌面悬浮窗（每秒，UI 线程）：设置关闭即销毁；首次开启按保存位置创建；
    /// 置顶/穿透/限屏设置变更即时幂等应用；内容/主题每秒更新。
    /// </summary>
    private void RefreshDesktopWidget()
    {
        try
        {
            var settings = _serviceProvider!.GetRequiredService<ISettingsService>();
            if (!settings.DesktopWidgetEnabled)
            {
                if (_desktopWidget != null)
                {
                    _desktopWidget.Dispose();
                    _desktopWidget = null;
                    Log.Information("桌面悬浮插件已隐藏");
                }
                return;
            }

            if (_desktopWidget == null)
            {
                var widget = new DesktopWidget();
                widget.DoubleClicked += OnDesktopWidgetDoubleClicked;
                widget.LocationSaved += OnDesktopWidgetLocationSaved;
                _desktopWidget = widget;
                widget.Start(new DesktopWidgetOptions(
                    settings.DesktopWidgetTopmost,
                    settings.DesktopWidgetClickThrough,
                    settings.DesktopWidgetScreen,
                    settings.DesktopWidgetLeft,
                    settings.DesktopWidgetTop,
                    settings.DesktopWidgetWidth,
                    settings.DesktopWidgetHeight));
            }
            if (!_desktopWidget.IsRunning) return;

            _desktopWidget.SetClampToScreen(settings.DesktopWidgetClampToScreen);
            _desktopWidget.ApplyTopMost(settings.DesktopWidgetTopmost);
            _desktopWidget.ApplyClickThrough(settings.DesktopWidgetClickThrough);

            double todayMinutes = GetTodayActiveMinutes();
            _desktopWidget.UpdateContent(BuildDesktopWidgetRows(settings, todayMinutes));
            _desktopWidget.SetTheme(IsSystemLightTheme());
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "刷新桌面悬浮插件失败");
        }
    }

    /// <summary>双击悬浮窗：还原主窗口（含从托盘恢复）并跳转硬件监控页。</summary>
    private void OnDesktopWidgetDoubleClicked()
    {
        try
        {
            _window?.DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    _trayService?.RestoreWindow();
                    _serviceProvider?.GetService<NavigationService>()?.NavigateTo("Monitor");
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "桌面悬浮窗双击打开主窗口失败");
                }
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "桌面悬浮窗双击调度失败");
        }
    }

    /// <summary>拖拽/缩放结束：保存显示器 DeviceName + 坐标 + 物理宽高（仅操作结束时触发一次，同步落盘可接受）。</summary>
    private void OnDesktopWidgetLocationSaved(string device, int left, int top, int width, int height)
    {
        try
        {
            var settings = _serviceProvider!.GetRequiredService<ISettingsService>();
            settings.DesktopWidgetScreen = device;
            settings.DesktopWidgetLeft = left;
            settings.DesktopWidgetTop = top;
            settings.DesktopWidgetWidth = width;
            settings.DesktopWidgetHeight = height;
            settings.Save();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "保存桌面悬浮窗位置/尺寸失败");
        }
    }

    /// <summary>
    /// 组装悬浮窗多行（显示项受设置控制；温度并入 CPU/GPU 行不单列）。
    /// </summary>
    private List<DesktopWidgetRow> BuildDesktopWidgetRows(ISettingsService settings, double todayMinutes)
    {
        var hw = _hardwareMonitorService;
        var rows = new List<DesktopWidgetRow>();

        if (settings.DesktopWidgetShowCpu)
        {
            var load = hw?.Get("CPU.Load");
            var temp = SanitizeTemp(hw?.Get("CPU.Temp"));
            rows.Add(new DesktopWidgetRow("CPU",
                new[]
                {
                    new DesktopWidgetSegment(FormatPercent(load), LoadColor(load)),
                    new DesktopWidgetSegment("  " + FormatTempShort(temp), TempColor(temp)),
                },
                load.HasValue && !float.IsNaN(load.Value) ? Math.Clamp(load.Value / 100f, 0f, 1f) : null,
                LoadColor(load)));
        }

        if (settings.DesktopWidgetShowMemory)
        {
            var load = hw?.Get("MEM.Load");
            rows.Add(new DesktopWidgetRow("内存",
                new[] { new DesktopWidgetSegment(FormatPercent(load), LoadColor(load)) },
                load.HasValue && !float.IsNaN(load.Value) ? Math.Clamp(load.Value / 100f, 0f, 1f) : null,
                LoadColor(load)));
        }

        if (settings.DesktopWidgetShowGpu)
        {
            var load = hw?.Get("GPU.Load");
            var temp = SanitizeTemp(hw?.Get("GPU.Temp"));
            rows.Add(new DesktopWidgetRow("GPU",
                new[]
                {
                    new DesktopWidgetSegment(FormatPercent(load), LoadColor(load)),
                    new DesktopWidgetSegment("  " + FormatTempShort(temp), TempColor(temp)),
                },
                load.HasValue && !float.IsNaN(load.Value) ? Math.Clamp(load.Value / 100f, 0f, 1f) : null,
                LoadColor(load)));
        }

        if (settings.DesktopWidgetShowVram)
        {
            var (usedMb, totalMb) = NormalizeVramMb(hw?.Get("GPU.VRAM.Used"), hw?.Get("GPU.VRAM.Total"));
            float? fraction = null;
            TaskbarMetricColor color = TaskbarMetricColor.Safe;
            string text = "--";
            if (!float.IsNaN(totalMb))
            {
                fraction = Math.Clamp(usedMb / totalMb, 0f, 1f);
                color = LoadColor(fraction.Value * 100f);
                text = $"{FormatVramMb(usedMb)}/{FormatVramMb(totalMb)}";
            }
            rows.Add(new DesktopWidgetRow("显存",
                new[] { new DesktopWidgetSegment(text, color) },
                fraction, color));
        }

        if (settings.DesktopWidgetShowNet)
        {
            var down = hw?.Get("NET.Down");
            var up = hw?.Get("NET.Up");
            // 下载/上行分行，各自带自适应峰值进度条（峰值每秒回落 3%，空闲基准 100KB/s）
            rows.Add(BuildNetRow("↓下载", down, ref _netPeakDown));
            rows.Add(BuildNetRow("↑上传", up, ref _netPeakUp));
        }

        if (settings.DesktopWidgetShowDisk)
        {
            var activity = hw?.Get("DISK.Activity");
            rows.Add(new DesktopWidgetRow("磁盘",
                new[] { new DesktopWidgetSegment(FormatPercent(activity), LoadColor(activity)) },
                activity.HasValue && !float.IsNaN(activity.Value) ? Math.Clamp(activity.Value / 100f, 0f, 1f) : null,
                LoadColor(activity)));
        }

        if (settings.DesktopWidgetShowUsage)
        {
            double goalMinutes = Math.Max(0.1, settings.DailyUsageGoalHours) * 60.0;
            float fraction = (float)Math.Clamp(todayMinutes / goalMinutes, 0, 1);
            rows.Add(new DesktopWidgetRow("今日",
                new[] { new DesktopWidgetSegment(FormatUsage(todayMinutes, settings.DailyUsageGoalHours), TaskbarMetricColor.Safe) },
                fraction, TaskbarMetricColor.Safe));
        }

        return rows;
    }

    /// <summary>温度读数规整：无权限/无传感器时常返回 0，超量程视为无效，统一显示 "--"。</summary>
    private static float? SanitizeTemp(float? v) =>
        v.HasValue && !float.IsNaN(v.Value) && v.Value is > 0f and < 130f ? v : null;

    /// <summary>显存归一化：LibreHardwareMonitor 可能返回字节或 MB，&gt;10MB 按字节换算（照抄 HardwareValueProvider）。</summary>
    private static (float usedMb, float totalMb) NormalizeVramMb(float? usedRaw, float? totalRaw)
    {
        if (!usedRaw.HasValue || !totalRaw.HasValue || totalRaw.Value <= 0
            || float.IsNaN(usedRaw.Value) || float.IsNaN(totalRaw.Value))
            return (float.NaN, float.NaN);
        float used = usedRaw.Value;
        float total = totalRaw.Value;
        if (total > 10485760f) { used /= 1048576f; total /= 1048576f; }
        return (used, total);
    }

    /// <summary>显存容量短格式：856M / 1.0G（输入单位 MB）。</summary>
    private static string FormatVramMb(float mb)
    {
        if (float.IsNaN(mb) || mb < 0) return "--";
        return mb >= 1024f ? $"{mb / 1024f:F1}G" : $"{mb:F0}M";
    }

    /// <summary>
    /// 网速行：速率相对近期峰值归一化（自适应基准，无固定满速假设）；
    /// 高网速不是异常，进度条恒为安全绿。无数据时 fraction=null。
    /// </summary>
    private static DesktopWidgetRow BuildNetRow(string label, float? bytesPerSec, ref float peak)
    {
        float? fraction = null;
        if (bytesPerSec.HasValue && !float.IsNaN(bytesPerSec.Value) && bytesPerSec.Value >= 0)
        {
            float v = bytesPerSec.Value;
            peak = MathF.Max(v, peak * 0.97f);
            fraction = Math.Clamp(v / MathF.Max(peak, NetMinBaseline), 0f, 1f);
        }
        else
        {
            peak *= 0.97f;
        }
        return new DesktopWidgetRow(label,
            new[] { new DesktopWidgetSegment(FormatSpeedWidget(bytesPerSec), TaskbarMetricColor.Safe) },
            fraction, TaskbarMetricColor.Safe);
    }

    /// <summary>悬浮窗网速格式（空间充足，保留 /s 后缀）：1.2M/s、85K/s、600B/s。</summary>
    private static string FormatSpeedWidget(float? bytesPerSec)
    {
        if (!bytesPerSec.HasValue || float.IsNaN(bytesPerSec.Value) || bytesPerSec.Value < 0) return "--";
        double v = bytesPerSec.Value;
        if (v < 1024) return $"{v:F0}B/s";
        if (v < 1024 * 1024) return $"{v / 1024:F0}K/s";
        if (v < 1024.0 * 1024 * 1024) return $"{v / 1024 / 1024:F1}M/s";
        return $"{v / 1024 / 1024 / 1024:F1}G/s";
    }
}
