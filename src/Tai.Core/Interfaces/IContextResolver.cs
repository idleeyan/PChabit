using Tai.Core.Entities;

namespace Tai.Core.Interfaces;

public interface IContextResolver
{
    Task<ActivityContext> ResolveContextAsync(AppSession session);
    Task<ActivityContext> ResolveContextAsync(string processName, string? windowTitle, DateTime timestamp);
    Task<List<ContextTag>> GetContextTagsAsync(DateTime startTime, DateTime endTime);
}

public class ActivityContext
{
    public ContextType Type { get; set; }
    public string Category { get; set; } = "Other";
    public string? Project { get; set; }
    public string? Task { get; set; }
    public double ProductivityScore { get; set; }
    public double FocusScore { get; set; }
    public List<string> Tags { get; set; } = [];
    public string? Description { get; set; }
}

public class ContextTag
{
    public string Name { get; set; } = string.Empty;
    public int OccurrenceCount { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public double RelevanceScore { get; set; }
}

public enum ContextType
{
    Development,
    Design,
    Writing,
    Communication,
    Research,
    Entertainment,
    Social,
    Productivity,
    System,
    Other
}
