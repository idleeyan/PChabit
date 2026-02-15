using Tai.Core.Interfaces;

namespace Tai.Core.Entities;

public class AppSession : EntityBase, IAggregateRoot
{
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    
    private TimeSpan? _duration;
    public TimeSpan Duration 
    { 
        get => _duration ?? (EndTime.HasValue ? EndTime.Value - StartTime : TimeSpan.Zero);
        set => _duration = value;
    }
    
    public string ProcessName { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public string? AppVersion { get; set; }
    public string? Publisher { get; set; }
    
    public string WindowTitle { get; set; } = string.Empty;
    public string? WindowClass { get; set; }
    public double WindowX { get; set; }
    public double WindowY { get; set; }
    public double WindowWidth { get; set; }
    public double WindowHeight { get; set; }
    public bool IsMaximized { get; set; }
    
    public int InputEventCount { get; set; }
    
    private TimeSpan? _activeDuration;
    public TimeSpan ActiveDuration 
    { 
        get => _activeDuration ?? Duration;
        set => _activeDuration = value;
    }
    
    public string? ProjectContext { get; set; }
    public string? Category { get; set; }
}
