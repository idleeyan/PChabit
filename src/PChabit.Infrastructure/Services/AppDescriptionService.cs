using Microsoft.EntityFrameworkCore;
using Serilog;
using PChabit.Core.Entities;
using PChabit.Infrastructure.Analysis;
using PChabit.Infrastructure.Data;

namespace PChabit.Infrastructure.Services;

public interface IAppDescriptionService
{
    /// <summary>按进程名获取说明；无记录返回 null（进程名自动规范化，兼容带/不带 .exe）。</summary>
    Task<string?> GetDescriptionAsync(string processName, CancellationToken cancellationToken = default);

    /// <summary>批量获取多个进程的说明（列表页一次性查询），返回「规范化进程名 → 说明」。</summary>
    Task<Dictionary<string, string>> GetDescriptionsForProcessesAsync(
        IEnumerable<string> processNames, CancellationToken cancellationToken = default);

    /// <summary>保存用户编辑的说明（upsert，置 IsUserEdited=true，预置种子不再覆盖）。</summary>
    Task SaveUserDescriptionAsync(string processName, string description, CancellationToken cancellationToken = default);
}

public class AppDescriptionService : IAppDescriptionService
{
    private readonly IDbContextFactory<PChabitDbContext> _dbContextFactory;

    public AppDescriptionService(IDbContextFactory<PChabitDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    private static string Normalize(string processName)
    {
        return AppStatsEngine.NormalizeProcessName(processName ?? "");
    }

    public async Task<string?> GetDescriptionAsync(string processName, CancellationToken cancellationToken = default)
    {
        var key = Normalize(processName);
        if (string.IsNullOrEmpty(key)) return null;

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.AppDescriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.ProcessName == key, cancellationToken);
        return entity?.Description;
    }

    public async Task<Dictionary<string, string>> GetDescriptionsForProcessesAsync(
        IEnumerable<string> processNames, CancellationToken cancellationToken = default)
    {
        var keys = processNames
            .Select(Normalize)
            .Where(k => !string.IsNullOrEmpty(k))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (keys.Count == 0) return new Dictionary<string, string>(StringComparer.Ordinal);

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await dbContext.AppDescriptions
            .AsNoTracking()
            .Where(d => keys.Contains(d.ProcessName))
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(d => d.ProcessName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Description, StringComparer.Ordinal);
    }

    public async Task SaveUserDescriptionAsync(string processName, string description, CancellationToken cancellationToken = default)
    {
        var key = Normalize(processName);
        if (string.IsNullOrEmpty(key)) return;

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await dbContext.AppDescriptions
            .FirstOrDefaultAsync(d => d.ProcessName == key, cancellationToken);

        var trimmed = (description ?? "").Trim();
        if (existing != null)
        {
            existing.Description = trimmed;
            existing.IsUserEdited = true;
            existing.UpdatedAt = DateTime.Now;
        }
        else
        {
            dbContext.AppDescriptions.Add(new AppDescription
            {
                ProcessName = key,
                Description = trimmed,
                IsUserEdited = true,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        Log.Information("已保存应用说明（用户编辑）: {ProcessName}", key);
    }

    /// <summary>
    /// 启动期种子：把 <see cref="DefaultAppDescriptions"/> 预置库增量写入数据库。
    /// 规则：不覆盖用户编辑过（IsUserEdited=true）的行；已存在且未编辑的行可更新为最新预置文本。
    /// </summary>
    public static async Task EnsureDescriptionsSeededAsync(PChabitDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now;
        var changed = false;

        var existing = await dbContext.AppDescriptions.ToListAsync(cancellationToken);
        var byKey = new Dictionary<string, AppDescription>(StringComparer.Ordinal);
        foreach (var d in existing)
        {
            if (string.IsNullOrEmpty(d.ProcessName)) continue;
            byKey[d.ProcessName] = d;
        }

        foreach (var entry in DefaultAppDescriptions.Entries)
        {
            var key = entry.ProcessKey.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(key)) continue;

            if (byKey.TryGetValue(key, out var row))
            {
                if (row.IsUserEdited) continue;              // 用户编辑过，不覆盖
                if (row.Description == entry.Description) continue;
                row.Description = entry.Description;         // 预置文本更新
                row.UpdatedAt = now;
                changed = true;
            }
            else
            {
                dbContext.AppDescriptions.Add(new AppDescription
                {
                    ProcessName = key,
                    Description = entry.Description,
                    IsUserEdited = false,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                changed = true;
            }
        }

        if (changed)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            Log.Information("已初始化/补齐默认应用说明（DefaultAppDescriptions）");
        }
        else
        {
            Log.Information("默认应用说明已是最新");
        }
    }
}
