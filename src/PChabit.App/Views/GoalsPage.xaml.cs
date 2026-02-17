using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PChabit.App.ViewModels;
using PChabit.Core.Entities;
using Serilog;

namespace PChabit.App.Views;

public sealed partial class GoalsPage : Page
{
    public GoalsViewModel ViewModel { get; }

    public GoalsPage()
    {
        Log.Information("GoalsPage: 构造函数开始");
        try
        {
            Log.Information("GoalsPage: 开始解析 GoalsViewModel");
            ViewModel = App.GetService<GoalsViewModel>();
            Log.Information("GoalsPage: GoalsViewModel 解析完成");
            InitializeComponent();
            Log.Information("GoalsPage: InitializeComponent 完成");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "GoalsPage: 构造函数异常");
            throw;
        }
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        Log.Information("GoalsPage: OnNavigatedTo 开始");
        base.OnNavigatedTo(e);
        try
        {
            Log.Information("GoalsPage: 开始加载目标数据");
            await ViewModel.LoadGoalsAsync();
            Log.Information("GoalsPage: 目标数据加载完成");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "GoalsPage: OnNavigatedTo 异常");
        }
    }

    private void AddGoalButton_Click(object sender, RoutedEventArgs e)
    {
        AddGoalOverlay.Visibility = Visibility.Visible;
        TargetTypeComboBox.SelectedIndex = 0;
        TargetIdTextBox.Text = string.Empty;
        DailyLimitNumberBox.Value = 60;
        DailyTargetNumberBox.Value = 0;
    }

    private void CancelAddGoal_Click(object sender, RoutedEventArgs e)
    {
        AddGoalOverlay.Visibility = Visibility.Collapsed;
    }

    private async void ConfirmAddGoal_Click(object sender, RoutedEventArgs e)
    {
        var targetType = TargetTypeComboBox.SelectedIndex switch
        {
            0 => "Application",
            1 => "Category",
            2 => "TotalTime",
            _ => "Application"
        };

        var targetId = targetType == "TotalTime" ? "total" : TargetIdTextBox.Text;

        if (string.IsNullOrWhiteSpace(targetId) && targetType != "TotalTime")
        {
            return;
        }

        var goal = new UserGoal
        {
            TargetType = targetType,
            TargetId = targetId,
            DailyLimitMinutes = (int)DailyLimitNumberBox.Value,
            DailyTargetMinutes = DailyTargetNumberBox.Value > 0 ? (int)DailyTargetNumberBox.Value : null,
            IsActive = true
        };

        await ViewModel.GoalService.CreateGoalAsync(goal);
        ViewModel.Goals.Add(goal);
        
        AddGoalOverlay.Visibility = Visibility.Collapsed;
    }

    private void DeleteGoal_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is UserGoal goal)
        {
            _ = ViewModel.DeleteGoalCommand.ExecuteAsync(goal);
        }
    }

    private void ToggleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggleSwitch && toggleSwitch.DataContext is UserGoal goal)
        {
            _ = ViewModel.ToggleGoalCommand.ExecuteAsync(goal);
        }
    }
}
