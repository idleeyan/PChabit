using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PChabit.Core.Entities;
using PChabit.Core.Interfaces;

namespace PChabit.Infrastructure.Data;

public class MouseSessionRepository : IMouseSessionRepository
{
    private readonly PChabitDbContext _context;
    
    public MouseSessionRepository(PChabitDbContext context)
    {
        _context = context;
    }
    
    public async Task<MouseSession?> GetByIdAsync(Guid id)
    {
        return await _context.MouseSessions.FindAsync(id);
    }
    
    public async Task<IEnumerable<MouseSession>> GetAllAsync()
    {
        return await _context.MouseSessions.ToListAsync();
    }
    
    public async Task<List<MouseSession>> FindAsync(Expression<Func<MouseSession, bool>> predicate)
    {
        return await _context.MouseSessions.Where(predicate).ToListAsync();
    }
    
    public async Task AddAsync(MouseSession entity)
    {
        await _context.MouseSessions.AddAsync(entity);
    }
    
    public async Task AddRangeAsync(IEnumerable<MouseSession> entities)
    {
        await _context.MouseSessions.AddRangeAsync(entities);
    }
    
    public async Task UpdateAsync(MouseSession entity)
    {
        _context.MouseSessions.Update(entity);
        await Task.CompletedTask;
    }
    
    public async Task DeleteAsync(Guid id)
    {
        var entity = await GetByIdAsync(id);
        if (entity != null)
        {
            _context.MouseSessions.Remove(entity);
        }
    }
    
    public async Task<int> SaveChangesAsync()
    {
        return await _context.SaveChangesAsync();
    }
    
    public async Task<MouseSession?> GetByDateAndHourAsync(DateTime date, int hour)
    {
        return await _context.MouseSessions
            .FirstOrDefaultAsync(s => s.Date == date.Date && s.Hour == hour);
    }
    
    public async Task<IEnumerable<MouseSession>> GetByDateRangeAsync(DateTime start, DateTime end)
    {
        return await _context.MouseSessions
            .Where(s => s.Date >= start.Date && s.Date <= end.Date)
            .OrderBy(s => s.Date)
            .ThenBy(s => s.Hour)
            .ToListAsync();
    }
}
