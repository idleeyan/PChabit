using PChabit.Core.Entities;

namespace PChabit.Core.Interfaces;

public class ExportData
{
    public DateTime ExportTime { get; init; }
    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; init; }
    public TimeSpan Duration => EndTime - StartTime;
    
    public List<AppSession> AppSessions { get; init; } = [];
    public List<KeyboardSession> KeyboardSessions { get; init; } = [];
    public List<MouseSession> MouseSessions { get; init; } = [];
    public List<WebSession> WebSessions { get; init; } = [];
    public List<DailyPattern> DailyPatterns { get; init; } = [];
    
    public ExportStatistics? Statistics { get; set; }
    public Dictionary<string, object> Metadata { get; init; } = new();
}

public class ExportStatistics
{
    public int TotalAppSessions { get; init; }
    public int TotalKeyboardSessions { get; init; }
    public int TotalMouseSessions { get; init; }
    public int TotalWebSessions { get; init; }
    
    public TimeSpan TotalActiveTime { get; init; }
    public TimeSpan TotalIdleTime { get; init; }
    
    public int TotalKeyPresses { get; init; }
    public int TotalMouseClicks { get; init; }
    public int TotalWebPages { get; init; }
    
    public Dictionary<string, TimeSpan> TopApplications { get; init; } = new();
    public Dictionary<string, TimeSpan> TopWebsites { get; init; } = new();
    public Dictionary<string, int> TopKeyCategories { get; init; } = new();
    
    public double AverageProductivityScore { get; init; }
    public int TotalFocusBlocks { get; init; }
    public TimeSpan TotalDeepWorkTime { get; init; }
}
