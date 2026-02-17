using PChabit.Core.Interfaces;

namespace PChabit.Core.Entities;

public class KeyboardSession : EntityBase, IAggregateRoot
{
    public DateTime Date { get; set; }
    public int Hour { get; set; }
    public string? ProcessName { get; set; }
    
    public int TotalKeyPresses { get; set; }
    public Dictionary<int, int> KeyFrequency { get; set; } = new();
    public Dictionary<string, int> KeyCategoryFrequency { get; set; } = new();
    
    public double AverageTypingSpeed { get; set; }
    public double PeakTypingSpeed { get; set; }
    
    public int UndoCount { get; set; }
    public int DeleteCount { get; set; }
    public int BackspaceCount { get; set; }
    
    public List<TypingBurst> TypingBursts { get; set; } = [];
    public List<ShortcutUsage> Shortcuts { get; set; } = [];
}

public class TypingBurst
{
    public DateTime StartTime { get; set; }
    public TimeSpan Duration { get; set; }
    public int KeyCount { get; set; }
    public string? Context { get; set; }
}

public class ShortcutUsage
{
    public DateTime Timestamp { get; set; }
    public string Shortcut { get; set; } = string.Empty;
    public string? Application { get; set; }
    public string? Context { get; set; }
}
