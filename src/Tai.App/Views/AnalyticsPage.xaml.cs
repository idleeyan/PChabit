using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Serilog;
using Tai.App.ViewModels;

namespace Tai.App.Views;

public sealed partial class AnalyticsPage : Page
{
    public AnalyticsViewModel ViewModel { get; }
    
    public AnalyticsPage()
    {
        Log.Information("AnalyticsPage: 构造函数开始");
        InitializeComponent();
        ViewModel = App.GetService<AnalyticsViewModel>();
        DataContext = ViewModel;
        Log.Information("AnalyticsPage: 构造函数完成");
    }
    
    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        Log.Information("AnalyticsPage: OnNavigatedTo 开始");
        base.OnNavigatedTo(e);
        
        try
        {
            await ViewModel.LoadDataAsync();
            Log.Information("AnalyticsPage: LoadDataAsync 完成");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "AnalyticsPage: LoadDataAsync 失败");
        }
    }
}
