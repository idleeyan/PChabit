using Tai.Core.Interfaces;

namespace Tai.Core.Entities;

public class WebSession : EntityBase, IAggregateRoot
{
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    
    private TimeSpan? _duration;
    public TimeSpan Duration 
    { 
        get => _duration ?? (EndTime.HasValue ? EndTime.Value - StartTime : TimeSpan.Zero);
        set => _duration = value;
    }
    
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string? Favicon { get; set; }
    
    public string Browser { get; set; } = string.Empty;
    public int TabId { get; set; }
    
    public int ScrollDepth { get; set; }
    public int ClickCount { get; set; }
    public bool HasFormInteraction { get; set; }
    public List<string> InteractedElements { get; set; } = [];
    
    public string? Referrer { get; set; }
    public string? Source { get; set; }
    public List<string> SearchQueries { get; set; } = [];
    
    public bool IsActiveTab { get; set; }
    
    private TimeSpan? _activeDuration;
    public TimeSpan ActiveDuration 
    { 
        get => _activeDuration ?? Duration;
        set => _activeDuration = value;
    }
    
    public List<TabSwitch> TabSwitches { get; set; } = [];
}

public class TabSwitch
{
    public DateTime Timestamp { get; set; }
    public string? FromUrl { get; set; }
    public string ToUrl { get; set; } = string.Empty;
    public TimeSpan TimeOnPreviousTab { get; set; }
}
