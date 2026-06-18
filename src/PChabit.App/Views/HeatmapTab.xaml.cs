using Microsoft.UI.Xaml.Controls;
using Serilog;
using PChabit.App.ViewModels;

namespace PChabit.App.Views;

public sealed partial class HeatmapTab : UserControl
{
    public HeatmapViewModel ViewModel { get; }
    private bool _initialized;

    public HeatmapTab()
    {
        ViewModel = App.GetService<HeatmapViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    public async Task LoadDataAsync()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            await ViewModel.LoadDataAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[HeatmapTab] 加载数据失败");
        }
    }
}
