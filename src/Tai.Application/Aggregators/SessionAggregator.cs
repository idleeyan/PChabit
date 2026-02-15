using Tai.Core.Entities;
using Tai.Core.Interfaces;

namespace Tai.Application.Aggregators;

public class SessionAggregator
{
    private readonly IRepository<AppSession> _appSessionRepo;
    
    public SessionAggregator(IRepository<AppSession> appSessionRepo)
    {
        _appSessionRepo = appSessionRepo;
    }
    
    public async Task<AppSession?> GetOrCreateCurrentSessionAsync(
        string processName, 
        string windowTitle, 
        string executablePath,
        DateTime timestamp)
    {
        var activeSession = await _appSessionRepo.FindAsync(
            s => s.ProcessName == processName && s.EndTime == null);
        
        var session = activeSession.FirstOrDefault();
        
        if (session == null)
        {
            session = new AppSession
            {
                ProcessName = processName,
                WindowTitle = windowTitle,
                ExecutablePath = executablePath,
                StartTime = timestamp,
                EndTime = null,
                Duration = TimeSpan.Zero
            };
            
            await _appSessionRepo.AddAsync(session);
        }
        
        return session;
    }
    
    public async Task EndSessionAsync(AppSession session, DateTime endTime)
    {
        session.EndTime = endTime;
        session.Duration = endTime - session.StartTime;
        await _appSessionRepo.UpdateAsync(session);
    }
    
    public async Task<List<AppSession>> GetSessionsForPeriodAsync(DateTime start, DateTime end)
    {
        return await _appSessionRepo.FindAsync(
            s => s.StartTime >= start && s.StartTime < end);
    }
    
    public async Task<Dictionary<string, TimeSpan>> GetAppTimeDistributionAsync(DateTime date)
    {
        var sessions = await _appSessionRepo.FindAsync(
            s => s.StartTime.Date == date.Date);
        
        return sessions
            .GroupBy(s => s.ProcessName)
            .ToDictionary(
                g => g.Key,
                g => TimeSpan.FromSeconds(g.Sum(s => s.Duration.TotalSeconds)));
    }
    
    public async Task<List<SessionTransition>> GetSessionTransitionsAsync(DateTime date)
    {
        var sessions = await _appSessionRepo.FindAsync(
            s => s.StartTime.Date == date.Date);
        
        var orderedSessions = sessions.OrderBy(s => s.StartTime).ToList();
        var transitions = new List<SessionTransition>();
        
        for (var i = 1; i < orderedSessions.Count; i++)
        {
            var previous = orderedSessions[i - 1];
            var current = orderedSessions[i];
            
            var previousEndTime = previous.EndTime ?? previous.StartTime;
            
            transitions.Add(new SessionTransition
            {
                FromProcess = previous.ProcessName,
                ToProcess = current.ProcessName,
                Timestamp = current.StartTime,
                GapDuration = current.StartTime - previousEndTime
            });
        }
        
        return transitions;
    }
}

public class SessionTransition
{
    public string FromProcess { get; set; } = string.Empty;
    public string ToProcess { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public TimeSpan GapDuration { get; set; }
}
