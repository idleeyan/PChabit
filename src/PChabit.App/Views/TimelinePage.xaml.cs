using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using PChabit.App.ViewModels;

namespace PChabit.App.Views;

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
            await ViewModel.LoadDataAsync(CancellationToken.None);
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
    
    private void DateItem_Click(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is DateTime date)
        {
            ViewModel.SelectedDate = date;
        }
    }
    
    private async void HourGroup_Expanding(Expander sender, ExpanderExpandingEventArgs args)
    {
        if (sender.DataContext is TimelineHourGroup group)
        {
            await ViewModel.ExpandHourGroupAsync(group);
        }
    }
}
