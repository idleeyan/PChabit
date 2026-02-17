namespace PChabit.Core.Interfaces;

public interface IMouseMonitor : IMonitor
{
    event EventHandler<MouseClickEventArgs>? OnMouseClick;
    event EventHandler<MouseMoveEventArgs>? OnMouseMove;
    event EventHandler<MouseScrollEventArgs>? OnMouseScroll;
}

public class MouseEventArgs : EventArgs
{
    public MouseAction Action { get; }
    public double X { get; }
    public double Y { get; }
    public double Distance { get; }
    public DateTime Timestamp { get; }
    public string? KeyCode { get; set; }
    public int KeyCount { get; set; }
    
    public MouseEventArgs(MouseAction action, double x, double y, DateTime timestamp, double distance = 0)
    {
        Action = action;
        X = x;
        Y = y;
        Distance = distance;
        Timestamp = timestamp;
    }
}

public enum MouseAction
{
    LeftClick,
    RightClick,
    MiddleClick,
    Scroll,
    Move
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
