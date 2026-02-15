using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Tai.App.ViewModels;

namespace Tai.App.Views;

public sealed partial class TimelinePage : Page
{
    public TimelineViewModel ViewModel { get; }
    
    public TimelinePage()
    {
        InitializeComponent();
        var scope = App.Services.CreateScope();
        ViewModel = scope.ServiceProvider.GetRequiredService<TimelineViewModel>();
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
            Serilog.Log.Error(ex, "加载时间线数据失败");
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
}
