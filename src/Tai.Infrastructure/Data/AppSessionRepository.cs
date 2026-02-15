using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Tai.Core.Entities;
using Tai.Core.Interfaces;

namespace Tai.Infrastructure.Data;

public class AppSessionRepository : IAppSessionRepository
{
    private readonly TaiDbContext _context;
    
    public AppSessionRepository(TaiDbContext context)
    {
        _context = context;
    }
    
    public async Task<AppSession?> GetByIdAsync(Guid id)
    {
        return await _context.AppSessions.FindAsync(id);
    }
    
    public async Task<IEnumerable<AppSession>> GetAllAsync()
    {
        return await _context.AppSessions.ToListAsync();
    }
    
    public async Task<List<AppSession>> FindAsync(Expression<Func<AppSession, bool>> predicate)
    {
        return await _context.AppSessions.Where(predicate).ToListAsync();
    }
    
    public async Task AddAsync(AppSession entity)
    {
        await _context.AppSessions.AddAsync(entity);
    }
    
    public async Task AddRangeAsync(IEnumerable<AppSession> entities)
    {
        await _context.AppSessions.AddRangeAsync(entities);
    }
    
    public async Task UpdateAsync(AppSession entity)
    {
        _context.AppSessions.Update(entity);
        await Task.CompletedTask;
    }
    
    public async Task DeleteAsync(Guid id)
    {
        var entity = await GetByIdAsync(id);
        if (entity != null)
        {
            _context.AppSessions.Remove(entity);
        }
    }
    
    public async Task<int> SaveChangesAsync()
    {
        return await _context.SaveChangesAsync();
    }
    
    public async Task<IEnumerable<AppSession>> GetByDateRangeAsync(DateTime start, DateTime end)
    {
        return await _context.AppSessions
            .Where(s => s.StartTime >= start && s.StartTime <= end)
            .OrderBy(s => s.StartTime)
            .ToListAsync();
    }
    
    public async Task<IEnumerable<AppSession>> GetByProcessNameAsync(string processName)
    {
        return await _context.AppSessions
            .Where(s => s.ProcessName == processName)
            .OrderByDescending(s => s.StartTime)
            .ToListAsync();
    }
    
    public async Task<Dictionary<string, TimeSpan>> GetUsageByProcessAsync(DateTime date)
    {
        var start = date.Date;
        var end = start.AddDays(1);
        
        var sessions = await _context.AppSessions
            .Where(s => s.StartTime >= start && s.StartTime < end)
            .ToListAsync();
        
        return sessions
            .GroupBy(s => s.ProcessName)
            .ToDictionary(
                g => g.Key,
                g => TimeSpan.FromTicks(g.Sum(s => s.Duration.Ticks))
            );
    }
}
