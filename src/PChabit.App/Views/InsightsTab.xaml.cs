using Microsoft.UI.Xaml.Controls;
using Serilog;
using PChabit.App.ViewModels;

namespace PChabit.App.Views;

public sealed partial class InsightsTab : UserControl
{
    public InsightsViewModel ViewModel { get; }
    private bool _initialized;

    public InsightsTab()
    {
        ViewModel = App.GetService<InsightsViewModel>();
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
            Log.Error(ex, "[InsightsTab] 加载数据失败");
        }
    }
}
