namespace PChabit.Core.Interfaces;

public interface IAppMonitor : IMonitor<AppActiveEventArgs>
{
    event EventHandler<WindowTitleChangedEventArgs>? OnWindowTitleChanged;
}

public class AppActiveEventArgs : EventArgs
{
    public string ProcessName { get; }
    public string WindowTitle { get; }
    public string ExecutablePath { get; }
    public DateTime Timestamp { get; }
    
    public string AppName { get; init; } = string.Empty;
    public string? AppVersion { get; init; }
    public string? Publisher { get; init; }
    public string? WindowClass { get; init; }
    public double WindowX { get; init; }
    public double WindowY { get; init; }
    public double WindowWidth { get; init; }
    public double WindowHeight { get; init; }
    public bool IsMaximized { get; init; }
    public string? Category { get; init; }
    
    public AppActiveEventArgs(string processName, string windowTitle, string executablePath, DateTime timestamp)
    {
        ProcessName = processName;
        WindowTitle = windowTitle;
        ExecutablePath = executablePath;
        Timestamp = timestamp;
    }
}

public class WindowTitleChangedEventArgs : EventArgs
{
    public string ProcessName { get; }
    public string WindowTitle { get; }
    public DateTime Timestamp { get; }
    
    public WindowTitleChangedEventArgs(string processName, string windowTitle, DateTime timestamp)
    {
        ProcessName = processName;
        WindowTitle = windowTitle;
        Timestamp = timestamp;
    }
}
