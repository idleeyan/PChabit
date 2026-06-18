using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Serilog;

namespace PChabit.App.Services;

public class TrayService : IDisposable
{
    private Window? _window;
    private IntPtr _hwnd;
    private uint _taskbarCreatedMessage;
    private NotifyIconData _notifyIconData;
    private bool _isCreated;
    private bool _disposed;
    private WndProcDelegate? _wndProcDelegate;
    private IntPtr _oldWndProc;
    private IntPtr _customIcon;

    // 动态进度环：缓存最近一次状态，避免无意义重绘
    private TrayStatus _lastStatus = (TrayStatus)(-1);
    private double _lastProgress = -1;
    private DateTime _lastUpdateUtc = DateTime.MinValue;
    private const int MinUpdateIntervalMs = 30_000; // 30s 节流（防止密集更新）

    public event EventHandler? ExitRequested;

    public bool IsMinimizedToTray { get; private set; }
    
    private const int WM_USER = 0x0400;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_DESTROY = 0x0002;
    private const int GWL_WNDPROC = -4;
    
    private const int NIM_ADD = 0x00000000;
    private const int NIM_MODIFY = 0x00000001;
    private const int NIM_DELETE = 0x00000002;
    
    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;
    
    private const int MF_STRING = 0x00000000;
    private const int MF_SEPARATOR = 0x00000800;
    private const int TPM_RIGHTALIGN = 0x0008;
    private const int TPM_BOTTOMALIGN = 0x0020;
    private const int TPM_RIGHTBUTTON = 0x0002;
    private const int TPM_RETURNCMD = 0x0100;
    
    private const int IDI_APPLICATION = 32512;
    private const int IMAGE_ICON = 1;
    private const int LR_LOADFROMFILE = 0x00000010;
    
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NotifyIconData lpData);
    
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);
    
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);
    
    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    
    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    
    [DllImport("user32.dll")]
    private static extern uint RegisterWindowMessage(string lpString);
    
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);
    
    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();
    
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, int uFlags, uint uIDNewItem, string lpNewItem);
    
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    
    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(IntPtr hMenu, int uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);
    
    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);
    
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
    
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
    
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, int uFlags);
    
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);
    
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;
    private const int SW_RESTORE = 9;
    
    private static readonly IntPtr HWND_TOP = IntPtr.Zero;
    private const int SWP_NOSIZE = 0x0001;
    private const int SWP_NOMOVE = 0x0002;
    private const int SWP_SHOWWINDOW = 0x0040;
    
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }
    
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    
    public void Initialize(Window window)
    {
        _window = window;
        
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
        
        SubclassWindow();
        
        CreateNotifyIcon();
        
        Log.Information("系统托盘服务初始化完成");
    }
    
    private void SubclassWindow()
    {
        _wndProcDelegate = WndProc;
        _oldWndProc = SetWindowLongPtr(_hwnd, GWL_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));
    }
    
    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == _taskbarCreatedMessage && _taskbarCreatedMessage != 0)
        {
            CreateNotifyIcon();
        }
        else if (msg == WM_USER + 1)
        {
            switch ((uint)lParam)
            {
                case WM_LBUTTONUP:
                case WM_LBUTTONDBLCLK:
                    RestoreWindow();
                    break;
                    
                case WM_RBUTTONUP:
                    ShowContextMenu();
                    break;
            }
        }
        
        return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
    }
    
    private void CreateNotifyIcon()
    {
        if (_isCreated)
        {
            Shell_NotifyIcon(NIM_DELETE, ref _notifyIconData);
        }
        
        IntPtr hIcon = IntPtr.Zero;
        
        var exePath = AppContext.BaseDirectory;
        var iconPath = System.IO.Path.Combine(exePath, "pchabit.ico");
        
        if (System.IO.File.Exists(iconPath))
        {
            hIcon = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
            Log.Information("从文件加载托盘图标: {IconPath}", iconPath);
        }
        
        if (hIcon == IntPtr.Zero)
        {
            var hInstance = GetModuleHandle(null);
            hIcon = LoadIcon(hInstance, (IntPtr)32512);
            Log.Information("从资源加载托盘图标");
        }
        
        if (hIcon == IntPtr.Zero)
        {
            hIcon = LoadIcon(IntPtr.Zero, (IntPtr)IDI_APPLICATION);
            Log.Warning("使用默认应用程序图标");
        }
        
        if (_customIcon != IntPtr.Zero && _customIcon != hIcon)
        {
            DestroyIcon(_customIcon);
        }
        _customIcon = hIcon;
        
        _notifyIconData = new NotifyIconData
        {
            cbSize = Marshal.SizeOf<NotifyIconData>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_USER + 1,
            hIcon = hIcon,
            szTip = "PChabit - 电脑使用习惯追踪"
        };
        
        var result = Shell_NotifyIcon(NIM_ADD, ref _notifyIconData);
        
        if (result)
        {
            _isCreated = true;
            Log.Information("系统托盘图标创建成功");
        }
        else
        {
            Log.Error("系统托盘图标创建失败");
        }
    }
    
    /// <summary>
    /// 刷新托盘图标的进度环与提示文本。
    /// 调用频率由 App.xaml.cs 的 DispatcherTimer 控制（默认 60s）。
    /// 节流策略：进度变化 < 1% 且状态未变 → 跳过；30s 内重复调用 → 跳过。
    /// </summary>
    /// <param name="progress">今日已用时长 / 每日总目标，0.0~1.0；null=不画进度环</param>
    /// <param name="status">运行/暂停/禁用</param>
    public void UpdateProgress(double? progress, TrayStatus status)
    {
        if (_disposed || !_isCreated) return;

        var now = DateTime.UtcNow;
        var p = progress.HasValue ? Math.Clamp(progress.Value, 0.0, 1.0) : (double?)null;
        var progressChanged = !_lastProgress.Equals(p);
        var statusChanged   = _lastStatus != status;
        var throttled       = (now - _lastUpdateUtc).TotalMilliseconds < MinUpdateIntervalMs;

        if (!progressChanged && !statusChanged) return;
        if (throttled && !statusChanged) return;

        _lastProgress = p ?? -1;
        _lastStatus   = status;
        _lastUpdateUtc = now;

        try
        {
            var hIcon = IconRenderer.CreateTrayIcon(p, status, 16);
            if (hIcon == IntPtr.Zero) return;

            // 构建 tooltip：保留核心标语 + 进度百分比
            var tip = status switch
            {
                TrayStatus.Paused   => "PChabit - 已暂停",
                TrayStatus.Disabled => "PChabit - 未配置目标",
                _ => p.HasValue
                    ? $"PChabit - 今日 {(int)(p.Value * 100)}%"
                    : "PChabit - 电脑使用习惯追踪"
            };

            UpdateNotifyIcon(hIcon, tip);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "UpdateProgress 失败: progress={Progress}, status={Status}", p, status);
        }
    }

    /// <summary>
    /// 主动重置节流器（暂停→恢复时立即生效）
    /// </summary>
    public void ForceRefresh()
    {
        _lastUpdateUtc = DateTime.MinValue;
    }

    private void UpdateNotifyIcon(IntPtr hIcon, string tip)
    {
        // 释放上一个由 GetHicon() 创建的 HICON，避免 GDI 句柄泄漏
        if (_customIcon != IntPtr.Zero)
        {
            DestroyIcon(_customIcon);
            _customIcon = IntPtr.Zero;
        }
        _customIcon = hIcon;

        _notifyIconData.hIcon = hIcon;
        _notifyIconData.szTip = tip;

        var result = Shell_NotifyIcon(NIM_MODIFY, ref _notifyIconData);
        if (!result)
        {
            // 极少发生：托盘条目被回收 → 重新注册
            Log.Warning("Shell_NotifyIcon(NIM_MODIFY) 失败，尝试 NIM_ADD 重建");
            Shell_NotifyIcon(NIM_ADD, ref _notifyIconData);
        }
    }

    public void MinimizeToTray()
    {
        if (_window == null) return;
        
        try
        {
            ShowWindow(_hwnd, SW_HIDE);
            IsMinimizedToTray = true;
            
            if (!_isCreated)
            {
                CreateNotifyIcon();
            }
            
            Log.Information("窗口已最小化到系统托盘");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "最小化到系统托盘失败");
        }
    }
    
    public void RestoreWindow()
    {
        if (_window == null) return;
        
        try
        {
            ShowWindow(_hwnd, SW_SHOW);
            SetWindowPos(_hwnd, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            SetForegroundWindow(_hwnd);
            IsMinimizedToTray = false;
            
            Log.Information("窗口已从系统托盘恢复");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "从系统托盘恢复窗口失败");
        }
    }
    
    public bool HandleWindowClosing()
    {
        MinimizeToTray();
        return true;
    }
    
    private void ShowContextMenu()
    {
        if (_window == null) return;
        
        GetCursorPos(out var pt);
        
        var menu = CreatePopupMenu();
        
        AppendMenu(menu, MF_STRING, 1, "显示窗口");
        AppendMenu(menu, MF_SEPARATOR, 0, "");
        AppendMenu(menu, MF_STRING, 2, "退出");
        
        SetForegroundWindow(_hwnd);
        
        var cmd = TrackPopupMenu(
            menu,
            TPM_RIGHTALIGN | TPM_BOTTOMALIGN | TPM_RIGHTBUTTON | TPM_RETURNCMD,
            pt.X,
            pt.Y,
            0,
            _hwnd,
            IntPtr.Zero);
        
        if (cmd != 0)
        {
            switch (cmd)
            {
                case 1:
                    RestoreWindow();
                    break;
                case 2:
                    Log.Information("托盘菜单点击: 退出");
                    ExitRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }
        else
        {
            Log.Debug("托盘菜单未选择任何命令");
        }
        
        DestroyMenu(menu);
    }
    
    private void RemoveNotifyIcon()
    {
        if (_isCreated)
        {
            Shell_NotifyIcon(NIM_DELETE, ref _notifyIconData);
            _isCreated = false;
        }
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        
        RemoveNotifyIcon();
        
        if (_customIcon != IntPtr.Zero)
        {
            DestroyIcon(_customIcon);
            _customIcon = IntPtr.Zero;
        }
        
        _disposed = true;
    }
}
