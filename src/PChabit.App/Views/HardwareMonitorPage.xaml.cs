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
            Log.Information("硬件页→分析（硬件页签）: {Ok}", ok);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "硬件页跳转分析失败");
        }
    }

    /// <summary>
    /// 切到「流量」页签时才加载网络流量数据（该页数据量大，避免进页面就查）。
    /// </summary>
    private void MainPivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 用 sender 而非 MainPivot 字段：XAML 解析期若触发，字段可能尚未赋值
        if (sender is Pivot { SelectedItem: PivotItem { Header: string header } } && header == "流量")
        {
            TrafficTab?.EnsureLoaded();
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
