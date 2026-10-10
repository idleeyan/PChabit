using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;
using PChabit.App.ViewModels;

namespace PChabit.App.Views;

/// <summary>
/// 键鼠详情页签（3.34.0：由 KeyboardDetailsPage 转 UserControl 并入「应用使用」页）。
///
/// 转换要点（陷阱 29）：Page → UserControl 四处同步改；
/// 去掉 OnNavigatedTo（UserControl 没有），改 Loaded + 供宿主调用的 EnsureLoadedAsync()，
/// 避免 Pivot 缓存复用导致首次显示时列表为空。
/// </summary>
public sealed partial class KeyboardDetailsTab : UserControl
{
    public KeyboardDetailsViewModel ViewModel { get; }

    private bool _dataLoaded;

    public KeyboardDetailsTab()
    {
        InitializeComponent();
        ViewModel = App.GetService<KeyboardDetailsViewModel>();
        Loaded += KeyboardDetailsTab_Loaded;
    }

    private async void KeyboardDetailsTab_Loaded(object sender, RoutedEventArgs e)
    {
        await EnsureLoadedAsync();
    }

    /// <summary>
    /// 供宿主在页签被选中时调用，确保数据已加载。
    /// Pivot 会缓存页签内容，Loaded 触发时机与「首次可见」不完全一致，
    /// 故由宿主在 SelectionChanged 里再显式调一次。
    /// </summary>
    public async Task EnsureLoadedAsync()
    {
        if (_dataLoaded) return;
        _dataLoaded = true;
        try
        {
            await ViewModel.LoadDataAsync();
            Log.Information("KeyboardDetailsTab: 数据加载完成");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "KeyboardDetailsTab: 数据加载失败");
        }
    }

    private async void PreviousDay_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ChangeDateAsync(-1);
    }

    private async void NextDay_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ChangeDateAsync(1);
    }
}