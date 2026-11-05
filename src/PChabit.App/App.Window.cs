using System.Runtime.InteropServices;
using Serilog;
using Microsoft.UI.Xaml;

namespace PChabit.App;

public partial class App : Microsoft.UI.Xaml.Application
{
    private const int WM_SETICON = 0x0080;
    private const int IMAGE_ICON = 1;
    private const int LR_LOADFROMFILE = 0x00000010;
    
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hFile, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);

[DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    private const uint LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x1000;
    private const uint LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR = 0x0100;

    private void SetWindowIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "pchabit.ico");
            if (File.Exists(iconPath))
            {
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(_window!);
                var hIcon = LoadImage(
                    IntPtr.Zero,
                    iconPath,
                    IMAGE_ICON,
                    32, 32,
                    LR_LOADFROMFILE);
                
                if (hIcon != IntPtr.Zero)
                {
                    SendMessage(hWnd, WM_SETICON, IntPtr.Zero, hIcon);
                    SendMessage(hWnd, WM_SETICON, (IntPtr)1, hIcon);
                    Log.Information("窗口图标设置成功");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "设置窗口图标失败");
        }
    }

    /// <summary>
    /// 仅当窗口几乎完全离开主屏、或尺寸异常时才校正。
    /// 禁止用「75% 屏幕」当边界——最大化/靠右窗口会被误判并拽到左上角。
    /// </summary>
    private void CorrectWindowBoundsIfOffscreen()
    {
        try
        {
            var hWnd = FindMainWindowHandle();
            if (hWnd == IntPtr.Zero || !GetWindowRect(hWnd, out var rect)) return;

            var w = rect.Right - rect.Left;
            var h = rect.Bottom - rect.Top;
            if (w < 80 || h < 80) return;

            // 最大化/全屏窗口不干预
            if (IsZoomed(hWnd)) return;

            var screenW = GetSystemMetrics(0);
            var screenH = GetSystemMetrics(1);
            if (screenW <= 0 || screenH <= 0) return;

            // 窗口与主屏几乎无交集（露头 < 48px）或尺寸离谱才校正
            var visibleW = Math.Max(0, Math.Min(rect.Right, screenW) - Math.Max(rect.Left, 0));
            var visibleH = Math.Max(0, Math.Min(rect.Bottom, screenH) - Math.Max(rect.Top, 0));
            var mostlyOff = visibleW < 48 || visibleH < 48;
            var absurdSize = w > screenW * 1.25 || h > screenH * 1.25;
            if (!mostlyOff && !absurdSize) return;

            if (w > screenW * 0.95) w = (int)(screenW * 0.9);
            if (h > screenH * 0.95) h = (int)(screenH * 0.85);
            var x = Math.Max(0, (screenW - w) / 2);
            var y = Math.Max(0, (screenH - h) / 2);
            SetWindowPos(hWnd, IntPtr.Zero, x, y, w, h, 0x0040);
            Log.Information("窗口离屏/尺寸异常，已校正到 ({X},{Y}) {W}x{H}（原 {OL},{OT},{OR},{OB}）",
                x, y, w, h, rect.Left, rect.Top, rect.Right, rect.Bottom);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "校正窗口位置失败");
        }
    }
    private static IntPtr FindMainWindowHandle()
    {
        var pid = (uint)Environment.ProcessId;
        IntPtr result = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out uint wndPid);
            if (wndPid != pid) return true;
            GetWindowRect(hWnd, out var r);
            if ((r.Right - r.Left) > 100 && (r.Bottom - r.Top) > 100)
            {
                result = hWnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }
}
