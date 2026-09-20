using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Serilog;
using PChabit.App.Services;
using PChabit.App.ViewModels;
using PChabit.Infrastructure.Analysis;

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

    private void AiSuggestion_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AiItemViewModel item })
            ViewModel.NavigateAiActionCommand.Execute(item);
    }

    private async void ExportExcel_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        try
        {
            var bytes = ViewModel.BuildExcelBytes();
            if (bytes is null || bytes.Length == 0)
            {
                Log.Warning("分析 Excel：无数据可导出");
                return;
            }

            var picker = new Windows.Storage.Pickers.FileSavePicker
            {
                SuggestedFileName = $"PChabit分析_{ViewModel.PeriodLabel}_{DateTime.Now:yyyyMMdd}.xlsx"
            };
            picker.FileTypeChoices.Add("Excel", new List<string> { ".xlsx" });
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSaveFileAsync();
            if (file == null) return;
            await Windows.Storage.FileIO.WriteBytesAsync(file, bytes);
            Log.Information("分析 Excel 已导出: {Path}", file.Path);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "分析 Excel 导出失败");
        }
    }

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

    /// <summary>洞察卡片点击：交给 ViewModel 触发 PivotNavigationRequested。</summary>
    private void InsightCard_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AnalyticsInsight insight })
            ViewModel.NavigateInsightCommand.Execute(insight);
    }

    /// <summary>
    /// 应用 ActionKey 导航：优先切到同 Tag 的 PivotItem（P1 页签就位即自动生效）；
    /// compose/rhythm 页签尚未建立时（P0），落总览并定位到对应现有卡片。
    /// </summary>
    private void ApplyPivot(string key)
    {
        if (string.IsNullOrEmpty(key)) return;

        foreach (var item in RootPivot.Items)
        {
            if (item is PivotItem { Tag: string tag } pivot
                && string.Equals(tag, key, StringComparison.OrdinalIgnoreCase))
            {
                RootPivot.SelectedItem = pivot;
                Log.Information("分析页切换到页签: {Key}", key);
                return;
            }
        }

        // P0 回退：总览页内锚点（P1 节奏/构成页签上线后此回退自然不再触发）
        RootPivot.SelectedIndex = 0;
        var anchor = key switch
        {
            "rhythm" => RhythmCard,
            "compose" => ComposeCard,
            _ => null
        };

        if (anchor is null || anchor.Visibility != Visibility.Visible)
        {
            Log.Information("分析页导航 {Key} 暂无对应页签/锚点，停留总览", key);
            return;
        }

        anchor.StartBringIntoView();
        _ = HighlightCardAsync(anchor);
        Log.Information("分析页导航 {Key} 已定位总览锚点卡片", key);
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
