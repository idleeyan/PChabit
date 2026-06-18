using System.Runtime.InteropServices;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Helpers;
using PChabit.Infrastructure.Services;

namespace PChabit.Infrastructure.Monitoring;

public class KeyboardMonitor : IKeyboardMonitor
{
    public bool IsRunning { get; private set; }
    public event EventHandler<KeyboardEventArgs>? OnDataCollected;
    
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    
    private IntPtr _hook = IntPtr.Zero;
    private LowLevelKeyboardProc? _proc;
    
    private bool _isShiftPressed;
    private bool _isCtrlPressed;
    private bool _isAltPressed;
    private bool _isWinPressed;
    
    private string? _currentProcess;
    private readonly TypingBurstDetector _burstDetector;
    private readonly ShortcutDetector _shortcutDetector;
    private readonly System.Timers.Timer _idleCheckTimer;
    
    public KeyboardMonitor()
    {
        _burstDetector = new TypingBurstDetector();
        _shortcutDetector = new ShortcutDetector();
        
        _burstDetector.OnBurstCompleted += (s, e) =>
        {
            BurstCompleted?.Invoke(this, e);
        };
        
        _shortcutDetector.OnShortcutDetected += (s, e) =>
        {
            ShortcutDetected?.Invoke(this, e);
        };
        
        _idleCheckTimer = new System.Timers.Timer(1000);
        _idleCheckTimer.Elapsed += (s, e) =>
        {
            _burstDetector.CheckForCompletion(DateTime.Now);
        };
    }
    
    public event EventHandler<TypingBurstEventArgs>? BurstCompleted;
    public event EventHandler<ShortcutDetectedEventArgs>? ShortcutDetected;
    
    public void Start()
    {
        if (IsRunning) return;
        
        _proc = HookCallback;
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        using var module = process.MainModule;
        _hook = Win32Helper.SetWindowsHookEx(
            Win32Helper.WH_KEYBOARD_LL, 
            _proc, 
            Win32Helper.GetModuleHandle(module?.ModuleName ?? string.Empty), 
            0);
        
        if (_hook != IntPtr.Zero)
        {
            IsRunning = true;
            _idleCheckTimer.Start();
        }
    }
    
    public void Stop()
    {
        if (!IsRunning) return;
        
        _idleCheckTimer.Stop();
        
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
            var vkCode = Marshal.ReadInt32(lParam);
            var isKeyDown = wParam == (IntPtr)Win32Helper.WM_KEYDOWN || 
                           wParam == (IntPtr)Win32Helper.WM_SYSKEYDOWN;
            var isKeyUp = wParam == (IntPtr)Win32Helper.WM_KEYUP || 
                         wParam == (IntPtr)Win32Helper.WM_SYSKEYUP;
            
            UpdateModifierState(vkCode, isKeyDown, isKeyUp);
            
            if (isKeyDown)
            {
                var keyName = GetKeyName(vkCode);
                var shortcut = GetShortcutString(keyName);
                
                var args = new KeyboardEventArgs(vkCode, keyName, true, DateTime.Now)
                {
                    IsShiftPressed = _isShiftPressed,
                    IsCtrlPressed = _isCtrlPressed,
                    IsAltPressed = _isAltPressed,
                    ActiveProcess = _currentProcess
                };
                
                OnDataCollected?.Invoke(this, args);
                
                if (_shortcutDetector.IsKnownShortcut(shortcut))
                {
                    _shortcutDetector.Detect(shortcut, _currentProcess, DateTime.Now);
                }
                else
                {
                    _burstDetector.OnKeyPress(_currentProcess, DateTime.Now);
                }
            }
        }
        
        return Win32Helper.CallNextHookEx(_hook, nCode, wParam, lParam);
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
    
    private string GetShortcutString(string keyName)
    {
        var parts = new List<string>();
        if (_isCtrlPressed) parts.Add("Ctrl");
        if (_isShiftPressed) parts.Add("Shift");
        if (_isAltPressed) parts.Add("Alt");
        if (_isWinPressed) parts.Add("Win");
        parts.Add(keyName);
        return string.Join("+", parts);
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
}
