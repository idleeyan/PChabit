using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PChabit.Core.Entities;
using PChabit.Core.Interfaces;
using Serilog;

namespace PChabit.App.ViewModels;

public partial class GoalsViewModel : ViewModelBase
{
    private readonly IGoalService _goalService;
    private readonly INotificationService _notificationService;

    public IGoalService GoalService => _goalService;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isAddingGoal;

    [ObservableProperty]
    private string _newGoalTargetType = "Application";

    [ObservableProperty]
    private string _newGoalTargetId = string.Empty;

    [ObservableProperty]
    private int? _newGoalDailyLimit;

    [ObservableProperty]
    private int? _newGoalDailyTarget;

    public ObservableCollection<UserGoal> Goals { get; } = new();
    public ObservableCollection<GoalProgressItem> GoalProgress { get; } = new();
    public ObservableCollection<string> TargetTypes { get; } = new() { "Application", "Category", "TotalTime" };

    public GoalsViewModel(
        IGoalService goalService,
        INotificationService notificationService) : base()
    {
        _goalService = goalService;
        _notificationService = notificationService;
        Title = "目标管理";
    }

    public async Task LoadGoalsAsync()
    {
        IsLoading = true;
        try
        {
            Goals.Clear();
            var goals = await _goalService.GetActiveGoalsAsync();
            foreach (var goal in goals)
            {
                Goals.Add(goal);
            }

            await LoadGoalProgressAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "加载目标失败");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadGoalProgressAsync()
    {
        GoalProgress.Clear();
        var progress = await _goalService.GetGoalProgressAsync(DateTime.Today);
        
        foreach (var goal in Goals)
        {
            if (progress.TryGetValue(goal.Id, out var progressValue))
            {
                GoalProgress.Add(new GoalProgressItem
                {
                    GoalId = goal.Id,
                    Progress = progressValue,
                    IsOverLimit = goal.DailyLimitMinutes.HasValue && progressValue >= 100
                });
            }
        }
    }

    [RelayCommand]
    private void ShowAddGoalDialog()
    {
        IsAddingGoal = true;
        NewGoalTargetType = "Application";
        NewGoalTargetId = string.Empty;
        NewGoalDailyLimit = null;
        NewGoalDailyTarget = null;
    }

    [RelayCommand]
    private void CancelAddGoal()
    {
        IsAddingGoal = false;
    }

    [RelayCommand]
    private async Task AddGoalAsync()
    {
        if (string.IsNullOrWhiteSpace(NewGoalTargetId) && NewGoalTargetType != "TotalTime")
        {
            return;
        }

        try
        {
            var goal = new UserGoal
            {
                TargetType = NewGoalTargetType,
                TargetId = NewGoalTargetType == "TotalTime" ? "total" : NewGoalTargetId,
                DailyLimitMinutes = NewGoalDailyLimit,
                DailyTargetMinutes = NewGoalDailyTarget,
                IsActive = true
            };

            await _goalService.CreateGoalAsync(goal);
            Goals.Add(goal);
            IsAddingGoal = false;

            Log.Information("创建目标成功: {Type} - {Target}", goal.TargetType, goal.TargetId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "创建目标失败");
        }
    }

    [RelayCommand]
    private async Task DeleteGoalAsync(UserGoal goal)
    {
        try
        {
            await _goalService.DeleteGoalAsync(goal.Id);
            Goals.Remove(goal);
            
            var progressItem = GoalProgress.FirstOrDefault(p => p.GoalId == goal.Id);
            if (progressItem != null)
            {
                GoalProgress.Remove(progressItem);
            }

            Log.Information("删除目标成功: {Id}", goal.Id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "删除目标失败");
        }
    }

    [RelayCommand]
    private async Task ToggleGoalAsync(UserGoal goal)
    {
        try
        {
            goal.IsActive = !goal.IsActive;
            await _goalService.UpdateGoalAsync(goal);
            Log.Information("切换目标状态: {Id} - {IsActive}", goal.Id, goal.IsActive);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "更新目标状态失败");
            goal.IsActive = !goal.IsActive;
        }
    }
}

public class GoalProgressItem
{
    public Guid GoalId { get; set; }
    public double Progress { get; set; }
    public bool IsOverLimit { get; set; }
}
