using Microsoft.EntityFrameworkCore;
using PChabit.Core.Entities;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Data;
using Serilog;

namespace PChabit.Infrastructure.Services;

public class GoalService : IGoalService
{
    private readonly IDbContextFactory<PChabitDbContext> _dbContextFactory;

    public GoalService(IDbContextFactory<PChabitDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<IEnumerable<UserGoal>> GetActiveGoalsAsync()
    {
        Log.Information("GoalService: GetActiveGoalsAsync 开始");
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        Log.Information("GoalService: DbContext 创建完成");
        var result = await dbContext.UserGoals
            .Where(g => g.IsActive)
            .OrderBy(g => g.TargetType)
            .ThenBy(g => g.TargetId)
            .ToListAsync();
        Log.Information("GoalService: GetActiveGoalsAsync 完成，返回 {Count} 条记录", result.Count);
        return result;
    }

    public async Task<UserGoal> CreateGoalAsync(UserGoal goal)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        
        goal.CreatedAt = DateTime.Now;
        goal.IsActive = true;
        
        dbContext.UserGoals.Add(goal);
        await dbContext.SaveChangesAsync();
        
        Log.Information("创建用户目标: Type={Type}, TargetId={TargetId}", goal.TargetType, goal.TargetId);
        return goal;
    }

    public async Task UpdateGoalAsync(UserGoal goal)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        
        var existing = await dbContext.UserGoals.FindAsync(goal.Id);
        if (existing == null)
        {
            Log.Warning("目标不存在: Id={Id}", goal.Id);
            return;
        }

        existing.TargetType = goal.TargetType;
        existing.TargetId = goal.TargetId;
        existing.DailyLimitMinutes = goal.DailyLimitMinutes;
        existing.DailyTargetMinutes = goal.DailyTargetMinutes;
        existing.IsActive = goal.IsActive;
        existing.UpdatedAt = DateTime.Now;

        await dbContext.SaveChangesAsync();
        Log.Information("更新用户目标: Id={Id}", goal.Id);
    }

    public async Task DeleteGoalAsync(Guid goalId)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        
        var goal = await dbContext.UserGoals.FindAsync(goalId);
        if (goal != null)
        {
            dbContext.UserGoals.Remove(goal);
            await dbContext.SaveChangesAsync();
            Log.Information("删除用户目标: Id={Id}", goalId);
        }
    }

    public async Task<Dictionary<Guid, double>> GetGoalProgressAsync(DateTime date)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        
        var goals = await dbContext.UserGoals
            .Where(g => g.IsActive)
            .ToListAsync();

        var progress = new Dictionary<Guid, double>();

        foreach (var goal in goals)
        {
            var minutes = await GetGoalMinutesAsync(dbContext, goal, date);
            
            double progressPercent = 0;
            if (goal.DailyLimitMinutes.HasValue && goal.DailyLimitMinutes.Value > 0)
            {
                progressPercent = Math.Min(minutes / goal.DailyLimitMinutes.Value * 100, 100);
            }
            else if (goal.DailyTargetMinutes.HasValue && goal.DailyTargetMinutes.Value > 0)
            {
                progressPercent = Math.Min(minutes / goal.DailyTargetMinutes.Value * 100, 100);
            }

            progress[goal.Id] = progressPercent;
        }

        return progress;
    }

    public async Task<bool> CheckGoalViolationAsync(Guid goalId, DateTime date)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        
        var goal = await dbContext.UserGoals.FindAsync(goalId);
        if (goal == null || !goal.IsActive || !goal.DailyLimitMinutes.HasValue)
            return false;

        var minutes = await GetGoalMinutesAsync(dbContext, goal, date);
        return minutes > goal.DailyLimitMinutes.Value;
    }

    private static async Task<double> GetGoalMinutesAsync(PChabitDbContext dbContext, UserGoal goal, DateTime date)
    {
        var sessions = dbContext.AppSessions
            .Where(s => s.StartTime.Date == date.Date);

        if (goal.TargetType == nameof(GoalTargetType.Application))
        {
            return await sessions
                .Where(s => s.ProcessName.Equals(goal.TargetId, StringComparison.OrdinalIgnoreCase))
                .SumAsync(s => s.Duration.TotalMinutes);
        }
        else if (goal.TargetType == nameof(GoalTargetType.Category))
        {
            return await sessions
                .Where(s => !string.IsNullOrEmpty(s.Category) && 
                            s.Category!.Equals(goal.TargetId, StringComparison.OrdinalIgnoreCase))
                .SumAsync(s => s.Duration.TotalMinutes);
        }
        else if (goal.TargetType == nameof(GoalTargetType.TotalTime))
        {
            return await sessions.SumAsync(s => s.Duration.TotalMinutes);
        }

        return 0;
    }
}
