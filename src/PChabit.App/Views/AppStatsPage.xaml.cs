using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PChabit.App.ViewModels;

namespace PChabit.App.Views;

public sealed partial class AppStatsPage : Page
{
    public AppStatsViewModel ViewModel { get; }
    
    public AppStatsPage()
    {
        InitializeComponent();
        var scope = App.Services.CreateScope();
        ViewModel = scope.ServiceProvider.GetRequiredService<AppStatsViewModel>();
        DataContext = ViewModel;
    }
    
    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        try
        {
            await ViewModel.LoadDataAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "加载应用统计数据失败");
        }
    }
    
    private void TodayButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedDate = DateTime.Today;
    }
    
    private void YesterdayButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedDate = DateTime.Today.AddDays(-1);
    }
    
    private void WeekButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedDate = DateTime.Today.AddDays(-7);
    }
    
    private void BackgroundMode_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggleSwitch && toggleSwitch.DataContext is AppStatItem item)
        {
            ViewModel.ToggleBackgroundModeCommand.Execute(item);
        }
    }
}
