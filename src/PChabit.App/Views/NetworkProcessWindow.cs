using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using PChabit.App.ViewModels;
using PChabit.HardwareMonitor.Hardware;
using Serilog;
using Windows.Graphics;
using WinRT.Interop;

namespace PChabit.App.Views;

/// <summary>
/// 任务栏小窗点击后弹出的「实时网速 · 进程排行」浮窗（3.86.0）。
/// 单例复用（Hide 而非 Close）：无边框置顶、贴在任务栏小窗正上方，
/// 每秒读一次 <see cref="ProcessNetworkMonitor"/> 快照刷新列表，失焦或 Esc 自动收起。
/// 进程级流量归因受 Windows 权限限制（仅管理员可启用连接级统计），
/// 普通权限下这里显示系统速率 + 权限说明，不会伪造进程数据。
/// </summary>
public sealed class NetworkProcessWindow
{
    private const int LogicW = 404;
    private const int BaseHeight = 146;   // 标题 + 系统速率 + 页脚（实测排版高度，勿随意加大）
    private const int RowHeight = 38;     // 单行实测高度（含 6px 上下内边距与 2px 进度条）
    private const int EmptyHeight = 306;
    private const int MinHeight = 220;
    private const int MaxHeight = 520;

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowLongPtrW(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private readonly Window _window;
    private readonly NetworkProcessView _view;
    private readonly NetworkProcessViewModel _vm = new();
    private readonly ProcessNetworkMonitor _monitor;
    private readonly Func<float?>? _netUpGetter;
    private readonly Func<float?>? _netDownGetter;
    private readonly AppWindow _appWindow;
    private readonly DispatcherQueueTimer _timer;
    private readonly IntPtr _hwnd;
    private int _currentHeight = BaseHeight;
    private DateTime _lastHiddenUtc = DateTime.MinValue;
    private bool _closing;

    public NetworkProcessWindow(
        ProcessNetworkMonitor monitor,
        Func<float?>? netUpGetter,
        Func<float?>? netDownGetter)
    {
        _monitor = monitor;
        _netUpGetter = netUpGetter;
        _netDownGetter = netDownGetter;

        _view = new NetworkProcessView { DataContext = _vm };
        _window = new Window { Content = _view };

        var hwnd = WindowNative.GetWindowHandle(_window);
        _hwnd = hwnd;
        try
        {
            // 工具窗口：不出现在任务栏与 Alt+Tab，符合"点击即弹、点走即收"的浮层语义
            var ex = GetWindowLongPtrW(hwnd, GWL_EXSTYLE).ToInt64();
            SetWindowLongPtrW(hwnd, GWL_EXSTYLE, (IntPtr)(ex | WS_EX_TOOLWINDOW));

            // Win11 系统圆角，与内部 Border CornerRadius=10 视觉一致（Win10 上调用失败即忽略）
            var pref = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
        }
        catch (Exception ex)
        {
            // 预期：个别系统不支持 DWM 属性，仅影响圆角外观
            Log.Debug(ex, "[NetProc] 浮窗样式设置失败");
        }

        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
        try { _appWindow.Title = "实时网速 · 进程排行"; }
        catch (Exception ex) { Log.Debug(ex, "[NetProc] 窗口标题设置失败"); }

        var presenter = OverlappedPresenter.Create();
        presenter.IsAlwaysOnTop = true;
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        _appWindow.SetPresenter(presenter);

        // Alt+F4 / 系统关闭：拦截为隐藏，保证单例可复用
        _appWindow.Closing += (_, args) =>
        {
            if (_closing) return;
            args.Cancel = true;
            Hide();
        };

        _view.CloseRequested += Hide;

        // 失焦即收起：与系统托盘/开始菜单浮层行为一致
        _window.Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated) Hide();
        };

        var dq = _window.DispatcherQueue;
        _timer = dq.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.IsRepeating = true;
        _timer.Tick += (_, _) => Refresh();
    }

    public bool IsVisible => _appWindow.IsVisible;

    /// <summary>
    /// 是否在指定时间窗内刚被收起。用于消解「点小窗时先失焦收起、随后点击回调又把它打开」
    /// 的同一次点击抖动：App 侧据此跳过这次重新显示。
    /// </summary>
    public bool HiddenWithin(TimeSpan span) => DateTime.UtcNow - _lastHiddenUtc < span;

    /// <summary>显示浮窗（已可见则仅刷新与重新定位）。</summary>
    public void Show()
    {
        try
        {
            Refresh();
            PositionAboveTaskbar();
            _appWindow.Show();
            _window.Activate();
            _timer.Start();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[NetProc] 显示进程网速浮窗失败");
        }
    }

    public void Hide()
    {
        _timer.Stop();
        _lastHiddenUtc = DateTime.UtcNow;
        try { _appWindow.Hide(); }
        catch (Exception ex) { Log.Debug(ex, "[NetProc] 隐藏浮窗失败"); }
    }

    /// <summary>应用退出时真正销毁窗口（不再拦截 Closing）。</summary>
    public void Close()
    {
        _closing = true;
        _timer.Stop();
        try { _window.Close(); }
        catch (Exception ex) { Log.Debug(ex, "[NetProc] 关闭浮窗失败"); }
    }

    private void Refresh()
    {
        try
        {
            var snapshot = _monitor.GetSnapshot(8);
            _vm.Apply(
                snapshot,
                _netUpGetter?.Invoke(),
                _netDownGetter?.Invoke(),
                _monitor.EstatsEnabled,
                _monitor.HasRecentAttribution);
            ApplyHeight(_vm.RowCount);
        }
        catch (Exception ex)
        {
            // 预期：采样与 UI 刷新并发时偶发异常，跳过本拍即可（下拍自愈）
            Log.Debug(ex, "[NetProc] 刷新快照失败");
        }
    }

    private void ApplyHeight(int rows)
    {
        var target = rows > 0 ? BaseHeight + rows * RowHeight : EmptyHeight;
        target = Math.Clamp(target, MinHeight, MaxHeight);
        if (target == _currentHeight) return;

        _currentHeight = target;
        var scale = CurrentScale();
        try
        {
            _appWindow.Resize(new SizeInt32((int)(LogicW * scale), (int)(_currentHeight * scale)));
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[NetProc] 调整浮窗尺寸失败");
            return;
        }

        PositionAboveTaskbar();
    }

    private double CurrentScale()
    {
        var dpi = GetDpiForWindow(_hwnd);
        if (dpi == 0) dpi = 96;
        return dpi / 96.0;
    }

    /// <summary>贴在任务栏小窗正上方（任务栏在顶部时改为下方），并夹在显示器工作区内。</summary>
    private void PositionAboveTaskbar()
    {
        try
        {
            var tray = FindWindow("Shell_TrayWnd", null);
            int left, top, right, bottom;
            if (tray != IntPtr.Zero && GetWindowRect(tray, out var rc))
            {
                left = rc.Left; top = rc.Top; right = rc.Right; bottom = rc.Bottom;
            }
            else
            {
                var sw = GetSystemMetrics(0);
                var sh = GetSystemMetrics(1);
                left = 0; right = sw; bottom = sh; top = sh - 48;
            }

            var scale = CurrentScale();
            var w = (int)(LogicW * scale);
            var h = (int)(_currentHeight * scale);

            var area = DisplayArea.GetFromPoint(
                new PointInt32(left + 16, Math.Max(top, bottom - 8)),
                DisplayAreaFallback.Primary);
            var wa = area.WorkArea;

            var taskbarAtBottom = top > wa.Y + wa.Height / 2;
            var x = left + (int)(8 * scale);
            var y = taskbarAtBottom
                ? wa.Y + wa.Height - h - (int)(8 * scale)   // 工作区底部正好是任务栏上沿
                : wa.Y + (int)(8 * scale);

            var maxX = Math.Max(wa.X + 8, wa.X + wa.Width - w - 8);
            x = Math.Clamp(x, wa.X + 8, maxX);
            y = Math.Clamp(y, wa.Y + 8, Math.Max(wa.Y + 8, wa.Y + wa.Height - h - 8));

            _appWindow.Resize(new SizeInt32(w, h));
            _appWindow.Move(new PointInt32(x, y));
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[NetProc] 浮窗定位失败，保留默认位置");
        }
    }
}
