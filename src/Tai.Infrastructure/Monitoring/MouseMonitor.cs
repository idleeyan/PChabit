using System.Runtime.InteropServices;
using Tai.Core.Interfaces;
using Tai.Infrastructure.Helpers;

namespace Tai.Infrastructure.Monitoring;

public class MouseMonitor : IMouseMonitor
{
    public bool IsRunning { get; private set; }
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
    
    private readonly List<MouseTrailPoint> _currentTrail = [];
    private double _totalTrailDistance;
    
    public event EventHandler<MouseTrailEventArgs>? OnTrailCompleted;
    
    public void Start()
    {
        if (IsRunning) return;
        
        _proc = HookCallback;
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        using var module = process.MainModule;
        _hook = Win32Helper.SetWindowsHookEx(
            Win32Helper.WH_MOUSE_LL, 
            _proc, 
            Win32Helper.GetModuleHandle(module?.ModuleName ?? string.Empty), 
            0);
        
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
    
    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
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
            _currentTrail.Add(new MouseTrailPoint(x, y, now));
            return;
        }
        
        var distance = Math.Sqrt(Math.Pow(x - _lastX, 2) + Math.Pow(y - _lastY, 2));
        
        if (distance < MoveThreshold) return;
        if ((now - _lastMoveTime).TotalMilliseconds < MoveThrottleMs) return;
        
        OnMouseMove?.Invoke(this, new MouseMoveEventArgs(_lastX, _lastY, x, y, now));
        
        _totalTrailDistance += distance;
        
        if ((now - _lastTrailTime).TotalMilliseconds >= TrailSampleInterval)
        {
            _currentTrail.Add(new MouseTrailPoint(x, y, now));
            _lastTrailTime = now;
            
            if (_currentTrail.Count >= 50)
            {
                CompleteTrail();
            }
        }
        
        _lastX = x;
        _lastY = y;
        _lastMoveTime = now;
    }
    
    private void CompleteTrail()
    {
        if (_currentTrail.Count < 2)
        {
            _currentTrail.Clear();
            _totalTrailDistance = 0;
            return;
        }
        
        var trail = _currentTrail.ToList();
        var totalDistance = _totalTrailDistance;
        
        OnTrailCompleted?.Invoke(this, new MouseTrailEventArgs(trail, totalDistance));
        
        _currentTrail.Clear();
        _totalTrailDistance = 0;
    }
}

public class MouseTrailPoint
{
    public double X { get; }
    public double Y { get; }
    public DateTime Timestamp { get; }
    
    public MouseTrailPoint(double x, double y, DateTime timestamp)
    {
        X = x;
        Y = y;
        Timestamp = timestamp;
    }
}

public class MouseTrailEventArgs : EventArgs
{
    public List<MouseTrailPoint> Trail { get; }
    public double TotalDistance { get; }
    
    public MouseTrailEventArgs(List<MouseTrailPoint> trail, double totalDistance)
    {
        Trail = trail;
        TotalDistance = totalDistance;
    }
}
