using System.Runtime.InteropServices;
using System.Text;
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

    // 前台进程缓存：Process.GetProcessById 在钩子回调中代价过高，会引起输入卡顿
    private uint _cachedPid;
    private string? _cachedProcessName;
    private long _cachedProcessTick;
    private const long ProcessCacheMs = 250;

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

                if (isKeyDown && !ModifierVkCodes.Contains(vkCode))
                {
                    var keyName = GetKeyName(vkCode);
                    var activeProcess = ResolveForegroundProcessFast();

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
    /// 获取前台进程名：短 TTL 缓存 + QueryFullProcessImageName，避免在钩子路径上
    /// 反复 Process.GetProcessById（会打开进程句柄并触发多次系统调用）。
    /// </summary>
    private string? ResolveForegroundProcessFast()
    {
        var hwnd = Win32Helper.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return _currentProcess;

        var pid = Win32Helper.GetProcessIdFromWindow(hwnd);
        if (pid == 0) return _currentProcess;

        var tick = Environment.TickCount64;
        if (pid == _cachedPid && _cachedProcessName != null && tick - _cachedProcessTick < ProcessCacheMs)
        {
            return _cachedProcessName;
        }

        var name = QueryProcessName(pid) ?? _currentProcess;
        if (name != null)
        {
            _cachedPid = pid;
            _cachedProcessName = name;
            _cachedProcessTick = tick;
        }

        return name;
    }

    private static string? QueryProcessName(uint pid)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero) return null;

        try
        {
            var buffer = new StringBuilder(1024);
            var size = (uint)buffer.Capacity;
            if (!QueryFullProcessImageName(handle, 0, buffer, ref size) || size == 0)
            {
                return null;
            }

            var fileName = Path.GetFileNameWithoutExtension(buffer.ToString(0, (int)size));
            return string.IsNullOrEmpty(fileName) ? null : fileName;
        }
        catch
        {
            return null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

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
