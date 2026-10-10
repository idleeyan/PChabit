using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;
using PChabit.App.ViewModels;

namespace PChabit.App.Views;

/// <summary>
/// 浏览历史区块（3.36.0）。
///
/// 替代原「网页浏览历史」独立页签（内嵌 WebView2 加载 report.html）。
/// </summary>
public sealed partial class WebHistorySection : UserControl
{
    public WebHistorySectionViewModel ViewModel { get; }

    private bool _loaded;

    public WebHistorySection()
    {
        ViewModel = App.GetService<WebHistorySectionViewModel>();
        InitializeComponent();
        DataContext = ViewModel;
        Loaded += async (_, _) => await EnsureLoadedAsync();
    }

    /// <summary>懒加载：宿主页面可见时才查库（本机历史库 2.5 万+条，不宜在构造时查）。</summary>
    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            await ViewModel.LoadAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "WebHistorySection: 加载失败");
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _loaded = false;
        await EnsureLoadedAsync();
    }
}