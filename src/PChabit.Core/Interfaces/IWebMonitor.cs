using PChabit.Core.Entities;

namespace PChabit.Core.Interfaces;

public interface IWebMonitor : IMonitor
{
    event EventHandler<WebActivityEventArgs>? WebActivityReceived;
    event EventHandler<WebClientDisconnectedEventArgs>? ClientDisconnected;
    
    IReadOnlyList<WebSession> GetCurrentSessions();
    Task<IEnumerable<WebSession>> GetSessionsAsync(DateTime startTime, DateTime endTime);
}

public class WebActivityEventArgs : EventArgs
{
    public string Url { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Domain { get; init; } = string.Empty;
    public string Browser { get; init; } = string.Empty;
    public string ClientId { get; init; } = string.Empty;
    public int TabId { get; init; }
    public WebActivityType ActivityType { get; init; }
    public DateTime Timestamp { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = new();
}

public class WebClientDisconnectedEventArgs : EventArgs
{
    public string ClientId { get; init; } = string.Empty;
}

public enum WebActivityType
{
    PageView,
    PageClose,
    TabSwitch,
    TabClose,
    Search,
    Click,
    Scroll,
    FormSubmit,
    Navigation
}
