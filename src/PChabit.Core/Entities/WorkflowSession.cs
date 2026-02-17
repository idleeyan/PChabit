using PChabit.Core.Interfaces;

namespace PChabit.Core.Entities;

public class WorkflowSession : EntityBase, IAggregateRoot
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string WorkflowType { get; set; } = string.Empty;
    
    public List<string> ActiveApplications { get; set; } = [];
    public Dictionary<string, TimeSpan> AppTimeDistribution { get; set; } = new();
    
    public int ContextSwitchCount { get; set; }
    public TimeSpan AverageFocusDuration { get; set; }
    
    public int FilesCreated { get; set; }
    public int FilesModified { get; set; }
    public int ClipboardOperations { get; set; }
    public List<string> EditedFilePaths { get; set; } = [];
    
    public List<ContextSwitch> ContextSwitches { get; set; } = [];
}

public class ContextSwitch
{
    public DateTime Timestamp { get; set; }
    public string? FromContext { get; set; }
    public string? ToContext { get; set; }
    public SwitchType Type { get; set; }
    public TimeSpan RecoveryTime { get; set; }
}

public enum SwitchType
{
    Active,
    Passive,
    Interrupted
}
