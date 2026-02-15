using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Serilog;
using Tai.App.Services;
using Tai.Core.Interfaces;
using Tai.Infrastructure.Data;

namespace Tai.App.ViewModels;

public partial class TimelineViewModel : ViewModelBase
{
    private readonly TaiDbContext _dbContext;
    private readonly IAppIconService _iconService;
    
    [ObservableProperty]
    private DateTime _selectedDate = DateTime.Today;
    
    [ObservableProperty]
    private string _viewMode = "day";
    
    [ObservableProperty]
    private bool _hasData;
    
    public ObservableCollection<TimelineGroup> TimelineGroups { get; } = new();
    public ObservableCollection<DateSummary> RecentDates { get; } = new();
    
    public TimelineViewModel(TaiDbContext dbContext, IAppIconService iconService) : base()
    {
        _dbContext = dbContext;
        _iconService = iconService;
        Title = "时间线";
    }
    
    public async Task LoadDataAsync()
    {
        IsLoading = true;
        
        try
        {
            var selectedDate = SelectedDate.Date;
            var nextDay = selectedDate.AddDays(1);
            
            try { _dbContext.ChangeTracker.Clear(); } catch { }
            
            List<Core.Entities.AppSession> appSessions;
            
            try
            {
                appSessions = await _dbContext.AppSessions
                    .Where(s => s.StartTime >= selectedDate && s.StartTime < nextDay)
                    .OrderBy(s => s.StartTime)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "加载 AppSessions 失败");
                appSessions = new List<Core.Entities.AppSession>();
            }
            
            TimelineGroups.Clear();
            
            var morningSessions = appSessions.Where(s => s.StartTime.Hour < 12).ToList();
            var afternoonSessions = appSessions.Where(s => s.StartTime.Hour >= 12 && s.StartTime.Hour < 18).ToList();
            var eveningSessions = appSessions.Where(s => s.StartTime.Hour >= 18).ToList();
            
            if (morningSessions.Any())
            {
                var group = CreateTimelineGroup("上午", morningSessions);
                TimelineGroups.Add(group);
            }
            
            if (afternoonSessions.Any())
            {
                var group = CreateTimelineGroup("下午", afternoonSessions);
                TimelineGroups.Add(group);
            }
            
            if (eveningSessions.Any())
            {
                var group = CreateTimelineGroup("晚上", eveningSessions);
                TimelineGroups.Add(group);
            }
            
            HasData = TimelineGroups.Any();
            
            RecentDates.Clear();
            for (int i = 0; i < 7; i++)
            {
                var date = DateTime.Today.AddDays(-i);
                var dayStart = date.Date;
                var dayEnd = dayStart.AddDays(1);
                
                var daySessions = appSessions.Where(s => s.StartTime >= dayStart && s.StartTime < dayEnd).ToList();
                var totalMinutes = daySessions
                    .Where(s => s.EndTime.HasValue)
                    .Sum(s => (s.EndTime!.Value - s.StartTime).TotalMinutes);
                
                RecentDates.Add(new DateSummary
                {
                    Date = date,
                    DayName = date.ToString("ddd"),
                    DayNumber = date.Day.ToString(),
                    TotalTime = $"{(int)(totalMinutes / 60)}h {(int)(totalMinutes % 60)}m",
                    IsToday = i == 0,
                    ProductivityScore = CalculateProductivityScore(daySessions)
                });
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "加载时间线数据失败");
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    private TimelineGroup CreateTimelineGroup(string period, List<Core.Entities.AppSession> sessions)
    {
        var group = new TimelineGroup
        {
            Period = period,
            StartTime = sessions.Min(s => s.StartTime).ToString("HH:mm"),
            EndTime = sessions.Max(s => s.EndTime ?? DateTime.Now).ToString("HH:mm"),
            TotalDuration = CalculateTotalDuration(sessions)
        };
        
        foreach (var session in sessions)
        {
            var duration = session.EndTime.HasValue 
                ? (session.EndTime.Value - session.StartTime).TotalMinutes 
                : 0;
            
            var activity = new TimelineActivity
            {
                Time = session.StartTime.ToString("HH:mm"),
                Duration = $"{(int)(duration / 60)}h {(int)(duration % 60)}m",
                Title = session.AppName ?? session.ProcessName,
                Subtitle = session.WindowTitle ?? "",
                Category = session.Category ?? "其他",
                CategoryColor = GetCategoryColor(session.Category),
                ProcessName = session.ProcessName ?? ""
            };
            
            group.Activities.Add(activity);
            _ = LoadActivityIconAsync(activity);
        }
        
        return group;
    }
    
    private async Task LoadActivityIconAsync(TimelineActivity activity)
    {
        try
        {
            var icon = await _iconService.GetAppIconAsync(activity.ProcessName, 20);
            if (icon != null)
            {
                activity.Icon = icon;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "加载应用图标失败: {ProcessName}", activity.ProcessName);
        }
    }
    
    private static string CalculateTotalDuration(List<Core.Entities.AppSession> sessions)
    {
        var totalMinutes = sessions
            .Where(s => s.EndTime.HasValue)
            .Sum(s => (s.EndTime!.Value - s.StartTime).TotalMinutes);
        
        return $"{(int)(totalMinutes / 60)}小时 {(int)(totalMinutes % 60)}分钟";
    }
    
    private static int CalculateProductivityScore(List<Core.Entities.AppSession> sessions)
    {
        if (!sessions.Any()) return 0;
        
        var totalMinutes = sessions
            .Where(s => s.EndTime.HasValue)
            .Sum(s => (s.EndTime!.Value - s.StartTime).TotalMinutes);
        
        var productiveMinutes = sessions
            .Where(s => s.EndTime.HasValue && (s.Category == "开发" || s.Category == "办公"))
            .Sum(s => (s.EndTime!.Value - s.StartTime).TotalMinutes);
        
        return totalMinutes > 0 ? (int)(productiveMinutes / totalMinutes * 100) : 0;
    }
    
    private static string GetCategoryColor(string? category)
    {
        return category switch
        {
            "开发" => "#512BD4",
            "浏览" => "#0078D4",
            "沟通" => "#107C10",
            "娱乐" => "#FF8C00",
            _ => "#6B7280"
        };
    }
    
    partial void OnSelectedDateChanged(DateTime value)
    {
        _ = LoadDataAsync();
    }
}

public class TimelineGroup
{
    public string Period { get; init; } = string.Empty;
    public string StartTime { get; init; } = string.Empty;
    public string EndTime { get; init; } = string.Empty;
    public string TotalDuration { get; init; } = string.Empty;
    public ObservableCollection<TimelineActivity> Activities { get; } = new();
}

public class TimelineActivity : ObservableObject
{
    public string Time { get; init; } = string.Empty;
    public string Duration { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string CategoryColor { get; init; } = string.Empty;
    public string ProcessName { get; init; } = string.Empty;
    
    private ImageSource? _icon;
    public ImageSource? Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }
}

public class DateSummary
{
    public DateTime Date { get; init; }
    public string DayName { get; init; } = string.Empty;
    public string DayNumber { get; init; } = string.Empty;
    public string TotalTime { get; init; } = string.Empty;
    public bool IsToday { get; init; }
    public int ProductivityScore { get; init; }
}
