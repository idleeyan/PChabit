namespace PChabit.Core.Interfaces;

public interface IMonitor
{
    bool IsRunning { get; }
    void Start();
    void Stop();
}

public interface IMonitor<TEventArgs> : IMonitor where TEventArgs : EventArgs
{
    event EventHandler<TEventArgs>? OnDataCollected;
}

public class MonitorStatusEventArgs : EventArgs
{
    public bool IsRunning { get; }
    public string Message { get; }
    
    public MonitorStatusEventArgs(bool isRunning, string message = "")
    {
        IsRunning = isRunning;
        Message = message;
    }
}
