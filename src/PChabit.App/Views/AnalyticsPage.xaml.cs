using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Serilog;
using PChabit.App.Services;
using PChabit.App.ViewModels;

namespace PChabit.App.Views;

public sealed partial class AnalyticsPage : Page
{
    public AnalyticsViewModel ViewModel { get; }

    public AnalyticsPage()
    {
        Log.Information("AnalyticsPage: 构造函数开始");
        InitializeComponent();
        ViewModel = App.GetService<AnalyticsViewModel>();
        DataContext = ViewModel;
        ViewModel.PivotNavigationRequested += ApplyPivot;
        Unloaded += (_, _) => ViewModel.PivotNavigationRequested -= ApplyPivot;
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

        // 跨页导航参数：NavigateTo("Analytics", new AnalyticsNavArgs("compose"))
        if (e.Parameter is AnalyticsNavArgs navArgs)
        {
            // 低优先级入队，等周期数据驱动的布局完成后再滚动/高亮
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => ApplyPivot(navArgs.Pivot));
        }
    }

    private void Refresh_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => _ = ViewModel.LoadDataAsync();

    private void DayHeat_Tapped(object sender, TappedRoutedEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement fe || fe.Tag is not DateTime date)
                return;

            var nav = App.GetService<NavigationService>();
            // 时间线页若支持参数可传 date；当前仅导航
            nav.NavigateTo("Timeline");
            Log.Information("热力图跳转时间线: {Date:yyyy-MM-dd}", date);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "热力图跳转失败");
        }
    }

    /// <summary>
    /// 3.57.0 单页化后：导航参数由页签切换改为「锚点滚动定位」。
    /// history → 网页访问页；rhythm/compose → 滚动到节奏卡并高亮；flow → 滚动到底部应用流向。
    /// </summary>
    private void ApplyPivot(string key)
    {
        if (string.IsNullOrEmpty(key)) return;

        // 网页浏览历史已转移到「网页访问」页：整页导航并直接落到历史页签
        if (string.Equals(key, "history", StringComparison.OrdinalIgnoreCase))
        {
            App.GetService<NavigationService>().NavigateTo("WebAccess", "history");
            Log.Information("分析页→网页访问-网页浏览历史");
            return;
        }
        if (string.Equals(key, "flow", StringComparison.OrdinalIgnoreCase))
        {
            RhythmCard.StartBringIntoView();
            Log.Information("分析页导航 flow：滚到节奏区");
            return;
        }

        // rhythm / compose 及未知 key → 滚动并高亮节奏/构成卡
        RhythmCard.StartBringIntoView();
        _ = HighlightCardAsync(RhythmCard);
        Log.Information("分析页导航 {Key} 定位节奏/构成卡片", key);
    }

    /// <summary>卡片强调色描边 1.6 秒后恢复，作为导航落点反馈。</summary>
    private static async Task HighlightCardAsync(FrameworkElement card)
    {
        if (card is not Border border) return;

        Brush? accent = null;
        Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue(
            "SystemControlBackgroundAccentBrush", out var resource);
        accent = resource as Brush;
        if (accent == null) return;

        var originalBrush = border.BorderBrush;
        var originalThickness = border.BorderThickness;
        border.BorderBrush = accent;
        border.BorderThickness = new Thickness(2);

        await Task.Delay(1600);

        border.BorderBrush = originalBrush;
        border.BorderThickness = originalThickness;
    }
}
