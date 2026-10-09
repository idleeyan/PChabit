using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PChabit.App.ViewModels;

namespace PChabit.App.Views;

public sealed partial class NetworkTrafficTab : UserControl
{
    public NetworkTrafficViewModel ViewModel { get; }

    private bool _loaded;

    public NetworkTrafficTab()
    {
        InitializeComponent();
        var scope = App.Services.CreateScope();
        ViewModel = scope.ServiceProvider.GetRequiredService<NetworkTrafficViewModel>();
        DataContext = ViewModel;
        Loaded += (_, _) => EnsureLoaded();
    }

    /// <summary>
    /// 首次显示时加载一次。由宿主页面在页签切换时调用，避免 Pivot 复用导致的空列表。
    /// </summary>
    public void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        _ = ViewModel.LoadAsync();
    }
}
