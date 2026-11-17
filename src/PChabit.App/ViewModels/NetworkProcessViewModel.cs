using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using PChabit.HardwareMonitor.Hardware;

namespace PChabit.App.ViewModels;

/// <summary>
/// 任务栏小窗点击后的「实时网速 · 进程排行」弹窗数据源。
/// 全部数值来自 <see cref="ProcessNetworkMonitor"/> 快照：系统速率取硬件监控同一口径
/// （LHM NET.Up/Down），与小窗上显示的数字完全一致，避免两处读数打架。
/// </summary>
public partial class NetworkProcessViewModel : ObservableObject
{
    private const int MaxRows = 8;

    /// <summary>上次渲染的进程序列，用于「顺序/成员未变则原地更新」避免每秒重建列表造成闪烁。</summary>
    private string _lastSignature = string.Empty;

    public ObservableCollection<NetworkProcessRow> Rows { get; } = new();

    [ObservableProperty]
    private string _downText = "0";

    [ObservableProperty]
    private string _upText = "0";

    [ObservableProperty]
    private string _updatedText = "--:--:--";

    [ObservableProperty]
    private string _sessionText = string.Empty;

    [ObservableProperty]
    private string _emptyText = "等待采样…";

    [ObservableProperty]
    private Visibility _listVisibility = Visibility.Collapsed;

    [ObservableProperty]
    private Visibility _emptyVisibility = Visibility.Visible;

    /// <summary>当前展示的行数（窗口据此调整高度）。</summary>
    public int RowCount => Rows.Count;

    /// <summary>用一次采样快照刷新界面（UI 线程调用）。</summary>
    public void Apply(ProcessNetworkSnapshot snapshot, float? systemUp, float? systemDown, bool estatsEnabled, bool hasRecentAttribution)
    {
        DownText = FormatSpeed(systemDown);
        UpText = FormatSpeed(systemUp);
        UpdatedText = snapshot.Timestamp.ToString("HH:mm:ss");
        SessionText = $"今日 ↓{FormatBytes(snapshot.SystemTodayDownBytes)} · ↑{FormatBytes(snapshot.SystemTodayUpBytes)}"
                      + $"　本会话 ↓{FormatBytes(snapshot.SystemSessionDownBytes)}";

        var items = snapshot.TopByRateList;
        var count = Math.Min(items.Count, MaxRows);

        var signatureBuilder = new System.Text.StringBuilder();
        for (var i = 0; i < count; i++)
        {
            if (i > 0) signatureBuilder.Append('|');
            signatureBuilder.Append(items[i].ProcessName);
        }

        var signature = signatureBuilder.ToString();
        var maxTotal = 0.0;
        for (var i = 0; i < count; i++)
        {
            if (items[i].TotalBytesPerSec > maxTotal) maxTotal = items[i].TotalBytesPerSec;
        }

        if (signature != _lastSignature || count != Rows.Count)
        {
            _lastSignature = signature;
            Rows.Clear();
            for (var i = 0; i < count; i++)
            {
                var row = new NetworkProcessRow();
                row.UpdateFrom(items[i], i, maxTotal);
                Rows.Add(row);
            }
        }
        else
        {
            for (var i = 0; i < count; i++) Rows[i].UpdateFrom(items[i], i, maxTotal);
        }

        if (count > 0)
        {
            ListVisibility = Visibility.Visible;
            EmptyVisibility = Visibility.Collapsed;
            return;
        }

        ListVisibility = Visibility.Collapsed;
        EmptyVisibility = Visibility.Visible;

        if (!ProcessNetworkMonitor.IsElevated)
        {
            EmptyText = "Windows 只允许管理员读取进程级流量\n（连接级统计须提权启用）\n\n"
                        + "以管理员身份运行 PChabit 后，这里会显示进程排行。";
        }
        else if (!estatsEnabled)
        {
            EmptyText = "已以管理员身份运行，连接级统计正在启用…\n约 2 秒后开始出现进程排行。";
        }
        else if (!hasRecentAttribution)
        {
            EmptyText = "连接级统计已启用，但近 60 秒没有可归因的 TCP 流量。\n"
                        + "UDP / QUIC（浏览器 HTTP/3）与本地回环流量无法按进程归因。";
        }
        else
        {
            EmptyText = "当前没有进程在传输数据";
        }
    }

    private static string FormatSpeed(float? bytesPerSec)
    {
        if (!bytesPerSec.HasValue || float.IsNaN(bytesPerSec.Value) || bytesPerSec.Value < 0) return "0 B/s";
        var v = bytesPerSec.Value;
        if (v < 1024) return $"{v:F0} B/s";
        if (v < 1024 * 1024) return $"{v / 1024:F0} KB/s";
        if (v < 1024.0 * 1024 * 1024) return $"{v / 1024 / 1024:F1} MB/s";
        return $"{v / 1024 / 1024 / 1024:F2} GB/s";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        double v = bytes;
        if (v < 1024) return $"{v:F0} B";
        if (v < 1024 * 1024) return $"{v / 1024:F0} KB";
        if (v < 1024.0 * 1024 * 1024) return $"{v / 1024 / 1024:F1} MB";
        return $"{v / 1024 / 1024 / 1024:F2} GB";
    }
}

/// <summary>进程排行单行（原地更新，避免每秒重建导致列表闪烁）。</summary>
public partial class NetworkProcessRow : ObservableObject
{
    [ObservableProperty]
    private string _rankText = string.Empty;

    [ObservableProperty]
    private string _processName = string.Empty;

    [ObservableProperty]
    private string _downText = string.Empty;

    [ObservableProperty]
    private string _upText = string.Empty;

    [ObservableProperty]
    private string _connText = string.Empty;

    [ObservableProperty]
    private double _sharePercent;

    public void UpdateFrom(ProcessNetworkItem item, int index, double maxTotal)
    {
        RankText = (index + 1).ToString();
        ProcessName = item.ProcessName;
        DownText = FormatSpeed(item.DownBytesPerSec);
        UpText = FormatSpeed(item.UpBytesPerSec);
        ConnText = item.ConnectionCount > 0 ? $"{item.ConnectionCount} 连接" : string.Empty;
        SharePercent = maxTotal > 0 ? Math.Clamp(item.TotalBytesPerSec / maxTotal * 100.0, 1, 100) : 0;
    }

    private static string FormatSpeed(double bytesPerSec)
    {
        if (bytesPerSec <= 0) return "—";
        if (bytesPerSec < 1024) return $"{bytesPerSec:F0} B/s";
        if (bytesPerSec < 1024 * 1024) return $"{bytesPerSec / 1024:F0} KB/s";
        if (bytesPerSec < 1024.0 * 1024 * 1024) return $"{bytesPerSec / 1024 / 1024:F1} MB/s";
        return $"{bytesPerSec / 1024 / 1024 / 1024:F2} GB/s";
    }
}
