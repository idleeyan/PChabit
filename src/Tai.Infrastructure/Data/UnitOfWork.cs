using Microsoft.EntityFrameworkCore.Storage;
using Tai.Core.Interfaces;
using Tai.Infrastructure.Data;

namespace Tai.Infrastructure.Data;

public class UnitOfWork : IUnitOfWork
{
    private readonly TaiDbContext _context;
    private IDbContextTransaction? _transaction;
    
    public IAppSessionRepository AppSessions { get; }
    public IKeyboardSessionRepository KeyboardSessions { get; }
    public IMouseSessionRepository MouseSessions { get; }
    public IWebSessionRepository WebSessions { get; }
    public IDailyPatternRepository DailyPatterns { get; }
    
    public UnitOfWork(
        TaiDbContext context,
        IAppSessionRepository appSessions,
        IKeyboardSessionRepository keyboardSessions,
        IMouseSessionRepository mouseSessions,
        IWebSessionRepository webSessions,
        IDailyPatternRepository dailyPatterns)
    {
        _context = context;
        AppSessions = appSessions;
        KeyboardSessions = keyboardSessions;
        MouseSessions = mouseSessions;
        WebSessions = webSessions;
        DailyPatterns = dailyPatterns;
    }
    
    public async Task<int> SaveChangesAsync()
    {
        return await _context.SaveChangesAsync();
    }
    
    public async Task BeginTransactionAsync()
    {
        _transaction = await _context.Database.BeginTransactionAsync();
    }
    
    public async Task CommitAsync()
    {
        try
        {
            await _context.SaveChangesAsync();
            if (_transaction != null)
            {
                await _transaction.CommitAsync();
            }
        }
        finally
        {
            if (_transaction != null)
            {
                await _transaction.DisposeAsync();
                _transaction = null;
            }
        }
    }
    
    public async Task RollbackAsync()
    {
        if (_transaction != null)
        {
            await _transaction.RollbackAsync();
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }
    
    public void Dispose()
    {
        _transaction?.Dispose();
        _context.Dispose();
    }
}
