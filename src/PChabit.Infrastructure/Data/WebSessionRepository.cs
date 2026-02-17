using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PChabit.Core.Entities;
using PChabit.Core.Interfaces;

namespace PChabit.Infrastructure.Data;

public class WebSessionRepository : IWebSessionRepository
{
    private readonly PChabitDbContext _context;
    
    public WebSessionRepository(PChabitDbContext context)
    {
        _context = context;
    }
    
    public async Task<WebSession?> GetByIdAsync(Guid id)
    {
        return await _context.WebSessions.FindAsync(id);
    }
    
    public async Task<IEnumerable<WebSession>> GetAllAsync()
    {
        return await _context.WebSessions.ToListAsync();
    }
    
    public async Task<List<WebSession>> FindAsync(Expression<Func<WebSession, bool>> predicate)
    {
        return await _context.WebSessions.Where(predicate).ToListAsync();
    }
    
    public async Task AddAsync(WebSession entity)
    {
        await _context.WebSessions.AddAsync(entity);
    }
    
    public async Task AddRangeAsync(IEnumerable<WebSession> entities)
    {
        await _context.WebSessions.AddRangeAsync(entities);
    }
    
    public async Task UpdateAsync(WebSession entity)
    {
        _context.WebSessions.Update(entity);
        await Task.CompletedTask;
    }
    
    public async Task DeleteAsync(Guid id)
    {
        var entity = await GetByIdAsync(id);
        if (entity != null)
        {
            _context.WebSessions.Remove(entity);
        }
    }
    
    public async Task<int> SaveChangesAsync()
    {
        return await _context.SaveChangesAsync();
    }
    
    public async Task<IEnumerable<WebSession>> GetByDateRangeAsync(DateTime start, DateTime end)
    {
        return await _context.WebSessions
            .Where(s => s.StartTime >= start && s.StartTime <= end)
            .OrderBy(s => s.StartTime)
            .ToListAsync();
    }
    
    public async Task<IEnumerable<WebSession>> GetByDomainAsync(string domain)
    {
        return await _context.WebSessions
            .Where(s => s.Domain == domain)
            .OrderByDescending(s => s.StartTime)
            .ToListAsync();
    }
}
