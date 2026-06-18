using System.Runtime.InteropServices;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Helpers;

namespace PChabit.Infrastructure.Monitoring;

public class MouseMonitor : IMouseMonitor
{
    public bool IsRunning { get; private set; }
    public DateTime LastActivityTime { get; private set; } = DateTime.MinValue;
    public event EventHandler<MouseClickEventArgs>? OnMouseClick;
    public event EventHandler<MouseMoveEventArgs>? OnMouseMove;
    public event EventHandler<MouseScrollEventArgs>? OnMouseScroll;
    
    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
    
    private IntPtr _hook = IntPtr.Zero;
    private LowLevelMouseProc? _proc;
    
    private const double MoveThreshold = 5.0;
    private const int MoveThrottleMs = 50;
    private const int TrailSampleInterval = 100;
    
    private double _lastX;
    private double _lastY;
    private DateTime _lastMoveTime = DateTime.MinValue;
    private DateTime _lastTrailTime = DateTime.MinValue;

    private double _totalTrailDistance;
    private string? _currentProcess;
    
    public event EventHandler<MouseTrailEventArgs>? OnTrailCompleted;

    public void Start()
    {
        if (IsRunning) return;

        _proc = HookCallback;
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        using var module = process.MainModule;
        var moduleHandle = Win32Helper.GetModuleHandle(module?.ModuleName ?? string.Empty);
        _hook = Win32Helper.SetWindowsHookEx(
            Win32Helper.WH_MOUSE_LL,
            _proc,
            moduleHandle,
            0);

        Serilog.Log.Debug("[MS-Start] ModuleName={ModuleName} ModuleHandle=0x{Handle:X} HookHandle=0x{Hook:X}",
            module?.ModuleName ?? "null", moduleHandle.ToInt64(), _hook.ToInt64());

        IsRunning = _hook != IntPtr.Zero;
    }
    
    public void Stop()
    {
        if (!IsRunning) return;
        
        if (_hook != IntPtr.Zero)
        {
            Win32Helper.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
        
        CompleteTrail();
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
                var hookStruct = Marshal.PtrToStructure<Win32Helper.MOUSEHOOKSTRUCT>(lParam);
                var wParamInt = wParam.ToInt32();
                var now = DateTime.Now;

                switch (wParamInt)
                {
                    case Win32Helper.WM_LBUTTONDOWN:
                        OnMouseClick?.Invoke(this, new MouseClickEventArgs(
                            MouseButtonType.Left, hookStruct.pt.X, hookStruct.pt.Y, now));
                        break;

                    case Win32Helper.WM_RBUTTONDOWN:
                        OnMouseClick?.Invoke(this, new MouseClickEventArgs(
                            MouseButtonType.Right, hookStruct.pt.X, hookStruct.pt.Y, now));
                        break;

                    case Win32Helper.WM_MBUTTONDOWN:
                        OnMouseClick?.Invoke(this, new MouseClickEventArgs(
                            MouseButtonType.Middle, hookStruct.pt.X, hookStruct.pt.Y, now));
                        break;

                    case Win32Helper.WM_XBUTTONDOWN:
                        var xButton = (hookStruct.mouseData >> 16) switch
                        {
                            1 => MouseButtonType.XButton1,
                            2 => MouseButtonType.XButton2,
                            _ => MouseButtonType.XButton1
                        };
                        OnMouseClick?.Invoke(this, new MouseClickEventArgs(
                            xButton, hookStruct.pt.X, hookStruct.pt.Y, now));
                        break;

                    case Win32Helper.WM_MOUSEWHEEL:
                    case Win32Helper.WM_MOUSEHWHEEL:
                        var delta = (short)(hookStruct.mouseData >> 16);
                        OnMouseScroll?.Invoke(this, new MouseScrollEventArgs(
                            delta, hookStruct.pt.X, hookStruct.pt.Y, now));
                        break;

                    case Win32Helper.WM_MOUSEMOVE:
                        HandleMouseMove(hookStruct.pt.X, hookStruct.pt.Y, now);
                        break;
                }
            }
            catch
            {
                // 吞掉所有异常，防止钩子被 Windows 静默卸载
            }
        }

        return Win32Helper.CallNextHookEx(_hook, nCode, wParam, lParam);
    }
    
    private void HandleMouseMove(double x, double y, DateTime now)
    {
        if (_lastX == 0 && _lastY == 0)
        {
            _lastX = x;
            _lastY = y;
            _lastMoveTime = now;
            _lastTrailTime = now;
            return;
        }

        var dx = x - _lastX;
        var dy = y - _lastY;
        var distance = Math.Sqrt(dx * dx + dy * dy);

        if (distance < MoveThreshold) return;
        if ((now - _lastMoveTime).TotalMilliseconds < MoveThrottleMs) return;

        OnMouseMove?.Invoke(this, new MouseMoveEventArgs(_lastX, _lastY, x, y, now));

        _totalTrailDistance += distance;

        if ((now - _lastTrailTime).TotalMilliseconds >= TrailSampleInterval)
        {
            _lastTrailTime = now;

            if (_totalTrailDistance > 0)
            {
                OnTrailCompleted?.Invoke(this, new MouseTrailEventArgs(_totalTrailDistance));
                _totalTrailDistance = 0;
            }
        }

        _lastX = x;
        _lastY = y;
        _lastMoveTime = now;
    }

    private void CompleteTrail()
    {
        if (_totalTrailDistance > 0)
        {
            OnTrailCompleted?.Invoke(this, new MouseTrailEventArgs(_totalTrailDistance));
            _totalTrailDistance = 0;
        }
    }
}

public class MouseTrailEventArgs : EventArgs
{
    public double TotalDistance { get; }

    public MouseTrailEventArgs(double totalDistance)
    {
        TotalDistance = totalDistance;
    }
}
