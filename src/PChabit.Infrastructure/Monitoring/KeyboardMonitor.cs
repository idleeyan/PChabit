using System.Runtime.InteropServices;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Helpers;

namespace PChabit.Infrastructure.Monitoring;

public class KeyboardMonitor : IKeyboardMonitor, IDisposable
{
    public bool IsRunning { get; private set; }
    public DateTime LastActivityTime { get; private set; } = DateTime.MinValue;
    public event EventHandler<KeyboardEventArgs>? OnDataCollected;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private IntPtr _hook = IntPtr.Zero;
    private LowLevelKeyboardProc? _proc;

    private bool _isShiftPressed;
    private bool _isCtrlPressed;
    private bool _isAltPressed;
    private bool _isWinPressed;

    private string? _currentProcess;

    // 修饰键 VK Code 集合，单独按下时不计入按键统计
    private static readonly HashSet<int> ModifierVkCodes = new()
    {
        Win32Helper.VK_SHIFT, Win32Helper.VK_LSHIFT, Win32Helper.VK_RSHIFT,
        Win32Helper.VK_CONTROL, Win32Helper.VK_LCONTROL, Win32Helper.VK_RCONTROL,
        Win32Helper.VK_MENU, Win32Helper.VK_LMENU, Win32Helper.VK_RMENU,
        Win32Helper.VK_LWIN, Win32Helper.VK_RWIN
    };

    public void Start()
    {
        if (IsRunning) return;

        _proc = HookCallback;
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        using var module = process.MainModule;
        var moduleHandle = Win32Helper.GetModuleHandle(module?.ModuleName ?? string.Empty);
        _hook = Win32Helper.SetWindowsHookEx(
            Win32Helper.WH_KEYBOARD_LL,
            _proc,
            moduleHandle,
            0);

        Serilog.Log.Debug("[KB-Start] ModuleName={ModuleName} ModuleHandle=0x{Handle:X} HookHandle=0x{Hook:X}",
            module?.ModuleName ?? "null", moduleHandle.ToInt64(), _hook.ToInt64());

        if (_hook != IntPtr.Zero)
        {
            IsRunning = true;
        }
    }

    public void Stop()
    {
        if (!IsRunning) return;

        if (_hook != IntPtr.Zero)
        {
            Win32Helper.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        IsRunning = false;
    }

    public void SetCurrentProcess(string? processName)
    {
        _currentProcess = processName;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                LastActivityTime = DateTime.Now;
                var vkCode = Marshal.ReadInt32(lParam);
                var isKeyDown = wParam == (IntPtr)Win32Helper.WM_KEYDOWN ||
                               wParam == (IntPtr)Win32Helper.WM_SYSKEYDOWN;
                var isKeyUp = wParam == (IntPtr)Win32Helper.WM_KEYUP ||
                             wParam == (IntPtr)Win32Helper.WM_SYSKEYUP;

                UpdateModifierState(vkCode, isKeyDown, isKeyUp);

                // 只在 KeyDown 时处理，且排除单独的修饰键
                if (isKeyDown && !ModifierVkCodes.Contains(vkCode))
                {
                    var keyName = GetKeyName(vkCode);

                    // 直接从系统获取当前前台进程，避免定时器延迟导致的进程归属错误
                    var activeProcess = ResolveForegroundProcess();

                    var args = new KeyboardEventArgs(vkCode, keyName, true, DateTime.Now)
                    {
                        IsShiftPressed = _isShiftPressed,
                        IsCtrlPressed = _isCtrlPressed,
                        IsAltPressed = _isAltPressed,
                        IsWinPressed = _isWinPressed,
                        ActiveProcess = activeProcess
                    };

                    OnDataCollected?.Invoke(this, args);
                }
            }
            catch
            {
                // 吞掉所有异常，防止钩子被 Windows 静默卸载
            }
        }

        return Win32Helper.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    /// <summary>
    /// 直接从系统获取前台窗口的进程名，避免依赖定时器同步造成的延迟
    /// </summary>
    private static string? ResolveForegroundProcess()
    {
        try
        {
            var hwnd = Win32Helper.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;
            var pid = Win32Helper.GetProcessIdFromWindow(hwnd);
            if (pid == 0) return null;
            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    private void UpdateModifierState(int vkCode, bool isKeyDown, bool isKeyUp)
    {
        switch (vkCode)
        {
            case Win32Helper.VK_SHIFT:
            case Win32Helper.VK_LSHIFT:
            case Win32Helper.VK_RSHIFT:
                _isShiftPressed = isKeyDown;
                break;
            case Win32Helper.VK_CONTROL:
            case Win32Helper.VK_LCONTROL:
            case Win32Helper.VK_RCONTROL:
                _isCtrlPressed = isKeyDown;
                break;
            case Win32Helper.VK_MENU:
            case Win32Helper.VK_LMENU:
            case Win32Helper.VK_RMENU:
                _isAltPressed = isKeyDown;
                break;
            case Win32Helper.VK_LWIN:
            case Win32Helper.VK_RWIN:
                _isWinPressed = isKeyDown;
                break;
        }
    }

    private static string GetKeyName(int vkCode)
    {
        return vkCode switch
        {
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x10 => "Shift",
            0x11 => "Ctrl",
            0x12 => "Alt",
            0x13 => "Pause",
            0x14 => "CapsLock",
            0x1B => "Esc",
            0x20 => "Space",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2C => "PrintScreen",
            0x2D => "Insert",
            0x2E => "Delete",
            >= 0x30 and <= 0x39 => ((char)('0' + vkCode - 0x30)).ToString(),
            >= 0x41 and <= 0x5A => ((char)('A' + vkCode - 0x41)).ToString(),
            >= 0x70 and <= 0x87 => $"F{vkCode - 0x6F}",
            _ => $"Key{vkCode}"
        };
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
