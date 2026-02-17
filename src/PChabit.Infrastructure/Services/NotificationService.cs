using Serilog;
using PChabit.Core.Interfaces;

namespace PChabit.Infrastructure.Services;

public class NotificationService : INotificationService
{
    private readonly Dictionary<string, ScheduledNotification> _scheduledNotifications = new();

    public async Task ShowReminderAsync(string title, string message, string? action = null)
    {
        try
        {
            Log.Information("显示通知: {Title} - {Message}", title, message);
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "显示通知失败");
        }
    }

    public async Task ShowGoalAlertAsync(string title, string message)
    {
        try
        {
            Log.Information("显示目标警报: {Title} - {Message}", title, message);
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "显示目标警报失败");
        }
    }

    public Task ScheduleReminderAsync(string id, DateTime time, string title, string message)
    {
        try
        {
            var delay = time - DateTime.Now;
            if (delay.TotalSeconds <= 0)
            {
                return ShowReminderAsync(title, message);
            }

            var scheduledNotification = new ScheduledNotification
            {
                Id = id,
                Title = title,
                Message = message,
                ScheduledTime = time
            };

            _scheduledNotifications[id] = scheduledNotification;
            
            _ = Task.Run(async () =>
            {
                await Task.Delay(delay);
                if (_scheduledNotifications.ContainsKey(id))
                {
                    await ShowReminderAsync(title, message);
                    _scheduledNotifications.Remove(id);
                }
            });

            Log.Information("计划提醒: {Id} - {Time}", id, time);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "计划提醒失败");
        }

        return Task.CompletedTask;
    }

    public void CancelReminder(string id)
    {
        if (_scheduledNotifications.Remove(id))
        {
            Log.Information("取消提醒: {Id}", id);
        }
    }
}

internal class ScheduledNotification
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime ScheduledTime { get; set; }
}
