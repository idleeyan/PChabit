namespace Tai.Core.Interfaces;

public interface IUnitOfWork : IDisposable
{
    IAppSessionRepository AppSessions { get; }
    IKeyboardSessionRepository KeyboardSessions { get; }
    IMouseSessionRepository MouseSessions { get; }
    IWebSessionRepository WebSessions { get; }
    IDailyPatternRepository DailyPatterns { get; }
    
    Task<int> SaveChangesAsync();
    Task BeginTransactionAsync();
    Task CommitAsync();
    Task RollbackAsync();
}
