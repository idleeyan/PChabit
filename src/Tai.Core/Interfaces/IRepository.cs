using System.Linq.Expressions;
using Tai.Core.Entities;

namespace Tai.Core.Interfaces;

public interface IRepository<TEntity> where TEntity : class, IEntity
{
    Task<TEntity?> GetByIdAsync(Guid id);
    Task<IEnumerable<TEntity>> GetAllAsync();
    Task<List<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate);
    Task AddAsync(TEntity entity);
    Task AddRangeAsync(IEnumerable<TEntity> entities);
    Task UpdateAsync(TEntity entity);
    Task DeleteAsync(Guid id);
    Task<int> SaveChangesAsync();
}

public interface IAppSessionRepository : IRepository<AppSession>
{
    Task<IEnumerable<AppSession>> GetByDateRangeAsync(DateTime start, DateTime end);
    Task<IEnumerable<AppSession>> GetByProcessNameAsync(string processName);
    Task<Dictionary<string, TimeSpan>> GetUsageByProcessAsync(DateTime date);
}

public interface IKeyboardSessionRepository : IRepository<KeyboardSession>
{
    Task<KeyboardSession?> GetByDateAndHourAsync(DateTime date, int hour);
    Task<IEnumerable<KeyboardSession>> GetByDateRangeAsync(DateTime start, DateTime end);
}

public interface IMouseSessionRepository : IRepository<MouseSession>
{
    Task<MouseSession?> GetByDateAndHourAsync(DateTime date, int hour);
    Task<IEnumerable<MouseSession>> GetByDateRangeAsync(DateTime start, DateTime end);
}

public interface IWebSessionRepository : IRepository<WebSession>
{
    Task<IEnumerable<WebSession>> GetByDateRangeAsync(DateTime start, DateTime end);
    Task<IEnumerable<WebSession>> GetByDomainAsync(string domain);
}

public interface IDailyPatternRepository : IRepository<DailyPattern>
{
    Task<DailyPattern?> GetByDateAsync(DateTime date);
}
