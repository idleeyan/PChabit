using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Tai.Core.Entities;
using Tai.Core.Interfaces;

namespace Tai.Infrastructure.Data;

public class DailyPatternRepository : IDailyPatternRepository
{
    private readonly TaiDbContext _context;
    
    public DailyPatternRepository(TaiDbContext context)
    {
        _context = context;
    }
    
    public async Task<DailyPattern?> GetByIdAsync(Guid id)
    {
        return await _context.DailyPatterns.FindAsync(id);
    }
    
    public async Task<IEnumerable<DailyPattern>> GetAllAsync()
    {
        return await _context.DailyPatterns.ToListAsync();
    }
    
    public async Task<List<DailyPattern>> FindAsync(Expression<Func<DailyPattern, bool>> predicate)
    {
        return await _context.DailyPatterns.Where(predicate).ToListAsync();
    }
    
    public async Task AddAsync(DailyPattern entity)
    {
        await _context.DailyPatterns.AddAsync(entity);
    }
    
    public async Task AddRangeAsync(IEnumerable<DailyPattern> entities)
    {
        await _context.DailyPatterns.AddRangeAsync(entities);
    }
    
    public async Task UpdateAsync(DailyPattern entity)
    {
        _context.DailyPatterns.Update(entity);
        await Task.CompletedTask;
    }
    
    public async Task DeleteAsync(Guid id)
    {
        var entity = await GetByIdAsync(id);
        if (entity != null)
        {
            _context.DailyPatterns.Remove(entity);
        }
    }
    
    public async Task<int> SaveChangesAsync()
    {
        return await _context.SaveChangesAsync();
    }
    
    public async Task<DailyPattern?> GetByDateAsync(DateTime date)
    {
        return await _context.DailyPatterns
            .FirstOrDefaultAsync(s => s.Date == date.Date);
    }
}
