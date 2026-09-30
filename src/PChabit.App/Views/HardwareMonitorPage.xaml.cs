using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Serilog;
using PChabit.App.Services;
using PChabit.App.ViewModels;

namespace PChabit.App.Views;

public sealed partial class HardwareMonitorPage : Page
{
    public HardwareMonitorViewModel ViewModel { get; }

    public HardwareMonitorPage()
    {
        InitializeComponent();
        var scope = App.Services.CreateScope();
        ViewModel = scope.ServiceProvider.GetRequiredService<HardwareMonitorViewModel>();
        DataContext = ViewModel;
    }

    private void OpenHistoryAnalysis_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var ok = App.GetService<NavigationService>().NavigateTo("Analytics", new AnalyticsNavArgs("hardware"));
            Log.Information("硬件页→分析历史: {Ok}", ok);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "硬件页跳转分析失败");
        }
    }

    private void OpenNetworkTraffic_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var ok = App.GetService<NavigationService>().NavigateTo("NetworkTraffic");
            Log.Information("硬件页→网络流量: {Ok}", ok);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "硬件页跳转网络流量失败", ex);
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.Subscribe();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Unsubscribe();
    }
}
