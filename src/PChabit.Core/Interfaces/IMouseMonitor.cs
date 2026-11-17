namespace PChabit.Core.Interfaces;

public interface IMouseMonitor : IMonitor
{
    event EventHandler<MouseClickEventArgs>? OnMouseClick;
    event EventHandler<MouseMoveEventArgs>? OnMouseMove;
    event EventHandler<MouseScrollEventArgs>? OnMouseScroll;

    /// <summary>
    /// 左键拦截（在全局钩子回调内同步调用，实现必须极快且不得抛异常）：
    /// 返回 true 表示该次点击已被本程序消费，钩子会吞掉按下与随后的抬起，
    /// 不再投递给目标窗口。用于任务栏小窗这类被系统层遮挡、收不到真实点击的窗口。
    /// </summary>
    Func<int, int, bool>? ClickInterceptor { get; set; }

    void SetCurrentProcess(string? processName);
}

public class MouseClickEventArgs : EventArgs
{
    public MouseButtonType Button { get; }
    public double X { get; }
    public double Y { get; }
    public DateTime Timestamp { get; }
    
    public MouseClickEventArgs(MouseButtonType button, double x, double y, DateTime timestamp)
    {
        Button = button;
        X = x;
        Y = y;
        Timestamp = timestamp;
    }
}

public class MouseMoveEventArgs : EventArgs
{
    public double FromX { get; }
    public double FromY { get; }
    public double ToX { get; }
    public double ToY { get; }
    public double Distance { get; }
    public DateTime Timestamp { get; }
    
    public MouseMoveEventArgs(double fromX, double fromY, double toX, double toY, DateTime timestamp)
    {
        FromX = fromX;
        FromY = fromY;
        ToX = toX;
        ToY = toY;
        Timestamp = timestamp;
        Distance = Math.Sqrt(Math.Pow(toX - fromX, 2) + Math.Pow(toY - fromY, 2));
    }
}

public class MouseScrollEventArgs : EventArgs
{
    public int Delta { get; }
    public double X { get; }
    public double Y { get; }
    public DateTime Timestamp { get; }
    
    public ScrollDirection Direction => Delta > 0 ? ScrollDirection.Up : ScrollDirection.Down;
    
    public MouseScrollEventArgs(int delta, double x, double y, DateTime timestamp)
    {
        Delta = delta;
        X = x;
        Y = y;
        Timestamp = timestamp;
    }
}

public enum MouseButtonType
{
    Left,
    Right,
    Middle,
    XButton1,
    XButton2
}

public enum ScrollDirection
{
    Up,
    Down
}
