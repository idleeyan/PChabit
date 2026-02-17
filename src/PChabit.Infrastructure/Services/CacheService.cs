using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace PChabit.Infrastructure.Services;

public interface ICacheService
{
    T? Get<T>(string key);
    Task<T?> GetAsync<T>(string key);
    void Set<T>(string key, T value, TimeSpan? expiration = null);
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null);
    void Remove(string key);
    void RemoveByPrefix(string prefix);
    void Clear();
}

public class MemoryCacheService : ICacheService
{
    private readonly IMemoryCache _cache;
    private readonly ConcurrentDictionary<string, byte> _keys;
    
    private static readonly TimeSpan DefaultExpiration = TimeSpan.FromMinutes(30);
    
    public MemoryCacheService(IMemoryCache cache)
    {
        _cache = cache;
        _keys = new ConcurrentDictionary<string, byte>();
    }
    
    public T? Get<T>(string key)
    {
        return _cache.TryGetValue(key, out T? value) ? value : default;
    }
    
    public Task<T?> GetAsync<T>(string key)
    {
        return Task.FromResult(Get<T>(key));
    }
    
    public void Set<T>(string key, T value, TimeSpan? expiration = null)
    {
        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiration ?? DefaultExpiration,
            SlidingExpiration = TimeSpan.FromMinutes(10)
        };
        
        _cache.Set(key, value, options);
        _keys.TryAdd(key, 0);
    }
    
    public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null)
    {
        Set(key, value, expiration);
        return Task.CompletedTask;
    }
    
    public void Remove(string key)
    {
        _cache.Remove(key);
        _keys.TryRemove(key, out _);
    }
    
    public void RemoveByPrefix(string prefix)
    {
        var keysToRemove = _keys.Keys.Where(k => k.StartsWith(prefix)).ToList();
        
        foreach (var key in keysToRemove)
        {
            Remove(key);
        }
    }
    
    public void Clear()
    {
        foreach (var key in _keys.Keys)
        {
            _cache.Remove(key);
        }
        
        _keys.Clear();
    }
}

public static class CacheKeys
{
    public const string DailyStats = "daily_stats";
    public const string WeeklyStats = "weekly_stats";
    public const string Patterns = "patterns";
    public const string Productivity = "productivity";
    public const string Context = "context";
    public const string TopApps = "top_apps";
    
    public static string DailyStatsKey(DateTime date) => $"{DailyStats}_{date:yyyyMMdd}";
    public static string WeeklyStatsKey(DateTime weekStart) => $"{WeeklyStats}_{weekStart:yyyyMMdd}";
    public static string PatternsKey(DateTime date) => $"{Patterns}_{date:yyyyMMdd}";
    public static string ProductivityKey(DateTime date) => $"{Productivity}_{date:yyyyMMdd}";
    public static string ContextKey(string processName) => $"{Context}_{processName}";
    public static string TopAppsKey(DateTime date) => $"{TopApps}_{date:yyyyMMdd}";
}
