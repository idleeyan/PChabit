using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Serilog;
using PChabit.App.Services;
using PChabit.Core.Interfaces;
using PChabit.Core.Entities;
using PChabit.Infrastructure.Data;

namespace PChabit.App;

public partial class App : Microsoft.UI.Xaml.Application
{
    private void StartTrayDisplayTimer()
    {
        try
        {
            _trayDisplayTimer?.Stop();
            _trayDisplayTimer = new Microsoft.UI.Xaml.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _trayDisplayTimer.Tick += (_, _) => RefreshTrayDisplay();
            _trayDisplayTimer.Start();
            Log.Information("任务栏/托盘显示定时器已启动（5s 刷新）");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "启动任务栏/托盘显示定时器失败");
        }
    }

    private void RefreshTrayDisplay()
    {
        try
        {
            var settings = _serviceProvider!.GetRequiredService<ISettingsService>();
            RefreshTaskbarWidget(settings);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "刷新任务栏小窗失败");
        }
    }

    /// <summary>驱动任务栏小窗：开关 + 两行文本（AI_MAINTENANCE 3.9.1 记录）。</summary>
    private async void RefreshTaskbarWidget(ISettingsService settings)
    {
        if (!settings.TaskbarEnabled)
        {
            if (_taskbarWidget != null && _taskbarWidget.IsRunning)
            {
                _taskbarWidget.Dispose();
                _taskbarWidget = null;
                Log.Information("任务栏小窗已隐藏");
            }
            return;
        }

        if (_taskbarWidget == null)
        {
            _taskbarWidget = new TaskbarWidget();
        }
        // Start 幂等：失效句柄会重建，父窗口变化会重挂载（Explorer 重启后可恢复）
        _taskbarWidget.Start();
        if (!_taskbarWidget.IsRunning) return;

        double todayMinutes = GetTodayActiveMinutes();
        long todayDownBytes = await GetTodayDownBytesAsync();
        _taskbarWidget.UpdateContent(BuildTaskbarRows(settings, todayMinutes, todayDownBytes));
        _taskbarWidget.SetTheme(IsSystemLightTheme());
    }

    /// <summary>
    /// 今日累计下载量（字节，60s 缓存，来自 NetworkTrafficSamples 分钟样本求和）。
    /// 只取"今天"，昨天及更早的样本不计入。表可能尚未写入当天的样本，
    /// 此时返回 0（小窗显示为 ↓0，不报错）。
    /// </summary>
    private async Task<long> GetTodayDownBytesAsync()
    {
        if ((DateTime.Now - _todayDownCacheTime).TotalSeconds < 60) return _todayDownBytes;
        _todayDownCacheTime = DateTime.Now;

        try
        {
            var today = DateTime.Today;
            // Timestamp 是 "yyyy-MM-dd HH:mm" 定长字符串，前缀匹配即可圈出当天所有分钟样本。
            // 不在表达式树里用 string 的 >= / <（EF 无法稳定翻译）。
            var dayPrefix = today.ToString("yyyy-MM-dd");

            using var scope = _serviceProvider!.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<PChabitDbContext>();
            _todayDownBytes = await dbContext.Set<NetworkTrafficSample>()
                .Where(s => s.Timestamp.StartsWith(dayPrefix))
                .SumAsync(s => (long?)s.BytesDown) ?? 0L;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "查询今日下载量失败");
        }
        return _todayDownBytes;
    }

    /// <summary>
    /// 组装任务栏内容：左侧「下载网速」专属双行区块（当前速率 / 今日累计），
    /// 右侧常规两行——行1 负载大项（CPU/内存/GPU/显存），行2 次指标（上传/磁盘/温度/今日使用）。
    /// 标签尽量短，温度用 C/G 消歧，避免和负载项的 CPU/GPU 撞名。
    /// </summary>
    private TaskbarLayoutItem BuildTaskbarRows(ISettingsService settings, double todayMinutes, long todayDownBytes)
    {
        var hw = _hardwareMonitorService;
        var blockRow1 = new List<TaskbarMetricItem>();
        var blockRow2 = new List<TaskbarMetricItem>();
        var row1 = new List<TaskbarMetricItem>();
        var row2 = new List<TaskbarMetricItem>();

        // 左侧区块（独占上下两行）：上行=当前下载速率（最大字号+速率分级配色），下行=今日累计下载量
        if (settings.TaskbarShowNet)
        {
            var down = hw?.Get("NET.Down");
            blockRow1.Add(new TaskbarMetricItem("", $"↓{FormatSpeedDown(down)}", NetDownColor(down), Emphasize: true));
            blockRow2.Add(new TaskbarMetricItem("", $"今 {FormatBytesShort(todayDownBytes)}", TaskbarMetricColor.NetIdle, Emphasize: true));
        }

        // 右侧主行：使用率大项
        if (settings.TaskbarShowCpu)
            row1.Add(new TaskbarMetricItem("CPU", FormatPercent(hw?.Get("CPU.Load")), LoadColor(hw?.Get("CPU.Load"))));
        if (settings.TaskbarShowMemory)
            row1.Add(new TaskbarMetricItem("内存", FormatPercent(hw?.Get("MEM.Load")), LoadColor(hw?.Get("MEM.Load"))));
        if (settings.TaskbarShowGpu)
            row1.Add(new TaskbarMetricItem("GPU", FormatPercent(hw?.Get("GPU.Load")), LoadColor(hw?.Get("GPU.Load"))));

        var vramUsed = hw?.Get("GPU.VRAM.Used");
        var vramTotal = hw?.Get("GPU.VRAM.Total");
        if (settings.TaskbarShowGpu && vramTotal.HasValue && vramTotal > 0)
        {
            float vramPct = (float)(vramUsed.GetValueOrDefault() / vramTotal.Value * 100.0);
            row1.Add(new TaskbarMetricItem("显存", FormatPercent(vramPct), LoadColor(vramPct)));
        }

        // 次行：次要信息，短前缀。下载已提升到主行，这里只留上传
        if (settings.TaskbarShowNet)
        {
            row2.Add(new TaskbarMetricItem("",
                $"↑{FormatSpeedShort(hw?.Get("NET.Up"))}", TaskbarMetricColor.Safe));
        }
        if (settings.TaskbarShowDisk)
            row2.Add(new TaskbarMetricItem("盘", FormatPercent(hw?.Get("DISK.Activity")), LoadColor(hw?.Get("DISK.Activity"))));
        if (settings.TaskbarShowTemp)
        {
            row2.Add(new TaskbarMetricItem("C", FormatTempShort(hw?.Get("CPU.Temp")), TempColor(hw?.Get("CPU.Temp"))));
            row2.Add(new TaskbarMetricItem("G", FormatTempShort(hw?.Get("GPU.Temp")), TempColor(hw?.Get("GPU.Temp"))));
        }
        if (settings.TaskbarShowUsage)
            row2.Add(new TaskbarMetricItem("今", FormatUsage(todayMinutes, settings.DailyUsageGoalHours), TaskbarMetricColor.Safe));

        return new TaskbarLayoutItem(blockRow1, blockRow2, row1, row2);
    }

    // 桌面硬件悬浮插件（3.23.0）驱动已拆分至 App.DesktopWidget.cs

    private static TaskbarMetricColor LoadColor(float? v) =>
        v.HasValue && !float.IsNaN(v.Value)
            ? v.Value >= 90 ? TaskbarMetricColor.Crit : v.Value >= 70 ? TaskbarMetricColor.Warn : TaskbarMetricColor.Safe
            : TaskbarMetricColor.Safe;

    /// <summary>
    /// 左侧区块下行「今日累计下载量」格式：B/K/M/G，带单位，与速率格式区分。
    /// </summary>
    private static string FormatBytesShort(long bytes)
    {
        if (bytes <= 0) return "0B";
        double v = bytes;
        if (v < 1024) return $"{v:F0}B";
        if (v < 1024 * 1024) return $"{v / 1024:F0}K";
        if (v < 1024.0 * 1024 * 1024) return $"{v / 1024 / 1024:F1}M";
        return $"{v / 1024 / 1024 / 1024:F2}G";
    }

    /// <summary>
    /// 下载速率配色分级：空闲灰 → 活跃蓝（≥100KB/s）→ 高速青（≥1MB/s）。
    /// 阈值按"是否值得看一眼"划分，与负载的黄/红警告语义无关。
    /// </summary>
    private static TaskbarMetricColor NetDownColor(float? bytesPerSec)
    {
        if (!bytesPerSec.HasValue || float.IsNaN(bytesPerSec.Value) || bytesPerSec.Value < 0)
            return TaskbarMetricColor.NetIdle;
        double v = bytesPerSec.Value;
        if (v >= 1024 * 1024) return TaskbarMetricColor.NetFast;
        if (v >= 100 * 1024) return TaskbarMetricColor.NetActive;
        return v > 1024 ? TaskbarMetricColor.NetActive : TaskbarMetricColor.NetIdle;
    }

    /// <summary>
    /// 主行下载速度格式：带 ↓ 前缀，宽度可控（高速时省掉小数），
    /// 保证和后面 4 项百分比挤在同一行时不会把小窗撑太宽。
    /// </summary>
    private static string FormatSpeedDown(float? bytesPerSec)
    {
        if (!bytesPerSec.HasValue || float.IsNaN(bytesPerSec.Value) || bytesPerSec.Value < 0) return "0";
        double v = bytesPerSec.Value;
        if (v < 1024) return "0";                                  // 不足 1K 视为空闲，避免抖动
        if (v < 1024 * 1024) return $"{v / 1024:F0}K";
        if (v < 10 * 1024 * 1024) return $"{v / 1024 / 1024:F1}M";
        return $"{v / 1024 / 1024:F0}M";
    }

    private static TaskbarMetricColor TempColor(float? v) =>
        v.HasValue && !float.IsNaN(v.Value)
            ? v.Value >= 85 ? TaskbarMetricColor.Crit : v.Value >= 70 ? TaskbarMetricColor.Warn : TaskbarMetricColor.Safe
            : TaskbarMetricColor.Safe;

    private static string FormatUsage(double minutes, double goalHours)
    {
        var goal = Math.Max(0.1, goalHours);
        return $"{minutes / 60:F1}/{goal:0.#}h";
    }

    /// <summary>系统深浅主题（任务栏小窗配色自适应）。</summary>
    private static bool IsSystemLightTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int i && i == 1;
        }
        catch (Exception ex)
        {
            // 预期：注册表键偶发不可读，降级为深色主题（返回 false）
            Log.Debug(ex, "读取系统主题设置失败，降级为深色");
            return false;
        }
    }

    /// <summary>今日活跃时长（分钟，60s 缓存，来自 AppSessions 实时查询；DailySummary 仅含昨日）。</summary>
    private double GetTodayActiveMinutes()
    {
        if ((DateTime.Now - _todayUsageCacheTime).TotalSeconds < 60) return _todayActiveMinutes;
        _todayUsageCacheTime = DateTime.Now;

        try
        {
            var today = DateTime.Today;
            using var scope = _serviceProvider!.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<PChabitDbContext>();
            var sessions = dbContext.AppSessions.AsNoTracking()
                .Where(s => s.StartTime >= today && s.StartTime < today.AddDays(1))
                .Select(s => new { s.StartTime, s.EndTime, s.ActiveDuration })
                .ToList();

            double minutes = 0;
            foreach (var s in sessions)
            {
                var d = s.ActiveDuration > TimeSpan.Zero
                    ? s.ActiveDuration
                    : (s.EndTime.HasValue ? s.EndTime.Value - s.StartTime : TimeSpan.Zero);
                if (d > TimeSpan.Zero) minutes += d.TotalMinutes;
            }
            _todayActiveMinutes = minutes;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "查询今日活跃时长失败");
        }
        return _todayActiveMinutes;
    }

    private static string FormatPercent(float? v) =>
        v.HasValue && !float.IsNaN(v.Value) ? $"{Math.Clamp(v.Value, 0, 999):F0}%" : "--";

    /// <summary>任务栏次行网速：去掉 /s 后缀，缩短占位。</summary>
    private static string FormatSpeedShort(float? bytesPerSec)
    {
        if (!bytesPerSec.HasValue || float.IsNaN(bytesPerSec.Value) || bytesPerSec.Value < 0) return "--";
        double v = bytesPerSec.Value;
        if (v < 1024) return $"{v:F0}B";
        if (v < 1024 * 1024) return $"{v / 1024:F0}K";
        if (v < 1024.0 * 1024 * 1024) return $"{v / 1024 / 1024:F1}M";
        return $"{v / 1024 / 1024 / 1024:F1}G";
    }

    /// <summary>任务栏次行温度：仅度数，来源用 C/G 短标签消歧。</summary>
    private static string FormatTempShort(float? v) =>
        v.HasValue && !float.IsNaN(v.Value) ? $"{v.Value:F0}°" : "--";
}
