using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PChabit.Core.Entities;
using PChabit.Core.Interfaces;

namespace PChabit.Infrastructure.Data;

public class KeyboardSessionRepository : IKeyboardSessionRepository
{
    private readonly PChabitDbContext _context;
    
    public KeyboardSessionRepository(PChabitDbContext context)
    {
        _context = context;
    }
    
    public async Task<KeyboardSession?> GetByIdAsync(Guid id)
    {
        return await _context.KeyboardSessions.FindAsync(id);
    }
    
    public async Task<IEnumerable<KeyboardSession>> GetAllAsync()
    {
        return await _context.KeyboardSessions.ToListAsync();
    }
    
    public async Task<List<KeyboardSession>> FindAsync(Expression<Func<KeyboardSession, bool>> predicate)
    {
        return await _context.KeyboardSessions.Where(predicate).ToListAsync();
    }
    
    public async Task AddAsync(KeyboardSession entity)
    {
        await _context.KeyboardSessions.AddAsync(entity);
    }
    
    public async Task AddRangeAsync(IEnumerable<KeyboardSession> entities)
    {
        await _context.KeyboardSessions.AddRangeAsync(entities);
    }
    
    public async Task UpdateAsync(KeyboardSession entity)
    {
        _context.KeyboardSessions.Update(entity);
        await Task.CompletedTask;
    }
    
    public async Task DeleteAsync(Guid id)
    {
        var entity = await GetByIdAsync(id);
        if (entity != null)
        {
            _context.KeyboardSessions.Remove(entity);
        }
    }
    
    public async Task<int> SaveChangesAsync()
    {
        return await _context.SaveChangesAsync();
    }
    
    public async Task<KeyboardSession?> GetByDateAndHourAsync(DateTime date, int hour)
    {
        return await _context.KeyboardSessions
            .FirstOrDefaultAsync(s => s.Date == date.Date && s.Hour == hour);
    }
    
    public async Task<IEnumerable<KeyboardSession>> GetByDateRangeAsync(DateTime start, DateTime end)
    {
        return await _context.KeyboardSessions
            .Where(s => s.Date >= start.Date && s.Date <= end.Date)
            .OrderBy(s => s.Date)
            .ThenBy(s => s.Hour)
            .ToListAsync();
    }
}
