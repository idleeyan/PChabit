using Microsoft.Extensions.DependencyInjection;
using PChabit.App.Views;
using PChabit.HardwareMonitor.Hardware;
using Serilog;

namespace PChabit.App;

/// <summary>
/// 任务栏小窗点击 → 实时网速 / 进程排行浮窗（3.86.0）。
/// 数据面复用已常驻的 <see cref="ProcessNetworkMonitor"/>，不新增采样线程；
/// 进程级归因受 Windows 权限限制（仅管理员可启用连接级统计），
/// 普通权限下只展示系统速率与权限说明，不伪造进程数据。
/// </summary>
public partial class App : Microsoft.UI.Xaml.Application
{
    private NetworkProcessWindow? _networkProcessWindow;

    /// <summary>
    /// 任务栏小窗被点击。返回 true 表示已被本程序消费（钩子会吞掉该次点击）。
    /// 可能来自全局鼠标钩子线程，必须立刻回到 UI 队列，钩子内不得做重活。
    /// </summary>
    private bool OnTaskbarWidgetClicked()
    {
        var dq = _window?.DispatcherQueue;
        if (dq == null) return false;
        return dq.TryEnqueue(ToggleNetworkProcessWindow);
    }

    private void ToggleNetworkProcessWindow()
    {
        try
        {
            if (_networkProcessWindow is { IsVisible: true })
            {
                _networkProcessWindow.Hide();
                Log.Debug("进程网速浮窗：收起");
                return;
            }

            // 同一次点击里「失焦收起 → 点击回调」会连着发生，500ms 内视为已处理过
            if (_networkProcessWindow?.HiddenWithin(TimeSpan.FromMilliseconds(500)) == true) return;

            var monitor = _serviceProvider?.GetService<ProcessNetworkMonitor>();
            if (monitor == null)
            {
                Log.Warning("进程网络监视器不可用，无法打开进程网速浮窗");
                return;
            }

            _networkProcessWindow ??= new NetworkProcessWindow(
                monitor,
                () => _hardwareMonitorService?.Get("NET.Up"),
                () => _hardwareMonitorService?.Get("NET.Down"));

            _networkProcessWindow.Show();
            Log.Debug("进程网速浮窗：展开");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "打开进程网速浮窗失败");
        }
    }

    /// <summary>关闭并释放浮窗（禁用任务栏小窗、应用退出时调用）。</summary>
    private void CloseNetworkProcessWindow()
    {
        try { _networkProcessWindow?.Close(); }
        catch (Exception ex) { Log.Debug(ex, "关闭进程网速浮窗失败"); }
        _networkProcessWindow = null;
    }
}
