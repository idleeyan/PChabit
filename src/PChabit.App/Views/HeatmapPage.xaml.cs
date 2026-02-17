using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Serilog;
using PChabit.App.ViewModels;

namespace PChabit.App.Views;

public sealed partial class HeatmapPage : Page
{
    public HeatmapViewModel ViewModel { get; }

    public HeatmapPage()
    {
        Log.Information("[HeatmapPage] ===== 构造函数开始 =====");
        
        try
        {
            Log.Information("[HeatmapPage] 步骤 1: InitializeComponent 开始");
            InitializeComponent();
            Log.Information("[HeatmapPage] 步骤 1: InitializeComponent 完成");
            
            Log.Information("[HeatmapPage] 步骤 2: 获取 ViewModel 开始");
            ViewModel = App.GetService<HeatmapViewModel>();
            Log.Information("[HeatmapPage] 步骤 2: 获取 ViewModel 完成");
            
            Log.Information("[HeatmapPage] 步骤 3: 设置 DataContext 开始");
            DataContext = ViewModel;
            Log.Information("[HeatmapPage] 步骤 3: 设置 DataContext 完成");
            
            Log.Information("[HeatmapPage] ===== 构造函数完成 =====");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[HeatmapPage] 构造函数异常");
            throw;
        }
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Log.Information("[HeatmapPage] OnNavigatedTo 开始");
        
        try
        {
            await ViewModel.InitializeAsync();
            Log.Information("[HeatmapPage] ViewModel.InitializeAsync 完成");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[HeatmapPage] OnNavigatedTo 异常");
        }
    }
}
