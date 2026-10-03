using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using PChabit.App.ViewModels;
using Serilog;
using System;
using System.IO;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Core;

namespace PChabit.App.Views;

public sealed partial class DashboardPage : Page
{
    public DashboardViewModel ViewModel { get; }

    // WebView2 图表状态
    private bool _chartInitialized;
    private bool _chartNavigating;
    private bool _chartPendingPush;
    private string? _pendingChartJson;

    public DashboardPage()
    {
        InitializeComponent();
        var scope = App.Services.CreateScope();
        ViewModel = scope.ServiceProvider.GetRequiredService<DashboardViewModel>();
        DataContext = ViewModel;

        Loaded += DashboardPage_Loaded;
        Unloaded += DashboardPage_Unloaded;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        try
        {
            // 数据加载不依赖 WebView2（陷阱 #13）：即使图表初始化失败，数字照常呈现
            await ViewModel.LoadDataAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "加载仪表盘数据失败");
        }

        // 规则摘要已就绪后再请求 AI 覆盖（未配置 AI 时内部直接返回，不做任何网络调用）
        try
        {
            await ViewModel.RequestAiSummaryAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "仪表盘 AI 速览请求失败");
        }
    }

    private async void DashboardPage_Loaded(object sender, RoutedEventArgs e)
    {
        await InitChartAsync();
        PushChart();

        // 进入页面才启动实时刷新，离开即停（对齐 3.24.0 的省电思路）
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        ViewModel.StartLiveRefresh();
    }

    private void DashboardPage_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.StopLiveRefresh();
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;

        // 离开页面即释放 WebView2，避免常驻占用内存与 GPU 资源
        try
        {
            if (ActivityChartWebView.CoreWebView2 != null)
            {
                ActivityChartWebView.CoreWebView2.Navigate("about:blank");
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "仪表盘图表释放失败");
        }
    }

    /// <summary>
    /// 监听 ChartJson 变化并推给图表。局部更新而非重建页面，
    /// 因此滚动位置、悬停态都保持不变。
    /// </summary>
    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DashboardViewModel.ChartJson)) return;
        if (_chartInitialized && !_chartNavigating) PushChart();
    }

    /// <summary>
    /// 初始化 WebView2 图表。失败时显示降级提示，不影响页面其余部分。
    /// </summary>
    private async Task InitChartAsync()
    {
        if (_chartInitialized) return;

        try
        {
            await ActivityChartWebView.EnsureCoreWebView2Async();
            if (ActivityChartWebView.CoreWebView2 == null)
            {
                ShowChartFallback("图表组件初始化失败");
                return;
            }

            var htmlPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Assets", "Dashboard", "activity-trend.html");

            if (!File.Exists(htmlPath))
            {
                Log.Warning("仪表盘图表模板不存在: {Path}", htmlPath);
                ShowChartFallback("图表模板文件缺失");
                return;
            }

            ActivityChartWebView.CoreWebView2.NavigationCompleted += (_, _) =>
            {
                _chartNavigating = false;
                _chartInitialized = true;
                // 主题必须先于首次数据注入，否则首帧会用默认深色画一帧再被纠正（可见闪色）
                PushTheme();
                PushChart();
            };

            _chartNavigating = true;
            var uri = new Uri("file:///" + htmlPath.Replace("\\", "/"));
            ActivityChartWebView.CoreWebView2.Navigate(uri.ToString());
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "仪表盘 WebView2 初始化失败");
            ShowChartFallback("图表组件不可用");
        }
    }

    private void ShowChartFallback(string message)
    {
        try
        {
            ChartFallbackPanel.Visibility = Visibility.Visible;
            ActivityChartWebView.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "设置图表降级提示失败: {Message}", message);
        }
    }

    /// <summary>把 ViewModel 生成的图表 JSON 注入 WebView2。</summary>
    private void PushChart()
    {
        var json = ViewModel.ChartJson;
        if (string.IsNullOrEmpty(json)) return;

        if (!_chartInitialized || _chartNavigating || ActivityChartWebView.CoreWebView2 == null)
        {
            _pendingChartJson = json;
            return;
        }

        try
        {
            var script =
                "try{window.__ACT_DATA__=" + json + ";" +
                "if(typeof draw==='function')draw(window.__ACT_DATA__);" +
                "window.postMessage(window.__ACT_DATA__,'*');}catch(e){}";
            _ = ActivityChartWebView.CoreWebView2.ExecuteScriptAsync(script);
            _pendingChartJson = null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "仪表盘图表数据注入失败");
        }
    }

    /// <summary>同步应用深浅色主题到图表（图表在 WebView 内，需显式告知背景/网格/轴的颜色）。</summary>
    private void PushTheme()
    {
        try
        {
            if (ActivityChartWebView.CoreWebView2 == null || !_chartInitialized) return;

            var dark = IsDarkTheme();
            var script = $"try{{if(typeof setTheme==='function')setTheme({(dark ? "true" : "false")});}}catch(e){{}};";
            _ = ActivityChartWebView.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "仪表盘图表主题同步失败");
        }
    }

    private bool IsDarkTheme()
    {
        try
        {
            var settings = new Windows.UI.ViewManagement.UISettings();
            var bg = settings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background);
            // 计算感知亮度：0.5 * 0.299R + 0.5 * 0.587G + 0.5 * 0.114B > 0.5 视为深色
            var luminance = (0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B) / 255.0;
            return luminance < 0.5;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 卡片键盘可达（3.29.0）：Enter 或空格触发与鼠标点击相同的行为。
    /// 此前四张统计卡只能用鼠标点，键盘用户无法访问。
    /// </summary>
    private void Card_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is not (VirtualKey.Enter or VirtualKey.Space)) return;

        // 阻止空格滚动页面 / 回车触发默认焦点移动
        e.Handled = true;

        if (sender is not Border card) return;

        // 按 x:Name 分派到对应行为，避免为四张卡各写一个 KeyDown 处理器。
        // 键盘路径不能构造 TappedRoutedEventArgs（构造函数不可用），故各行为拆成无参方法。
        switch (card.Name)
        {
            case "ActiveTimeCard":
                NavigateToTimeline();
                break;
            case "KeyboardCard":
                _ = ShowKeyboardDetailsAsync();
                break;
            case "MouseCard":
                _ = ShowMouseDetailsAsync();
                break;
            case "WebCard":
                _ = ShowWebDetailsAsync();
                break;
        }
    }

    /// <summary>
    /// 今日活动时间卡片：跳转到时间线页。
    /// 3.25.1 补上此前缺失的点击（4 张卡里只有 3 张可点，而这张恰是最该深挖的）。
    /// </summary>
    private void ActiveTimeCard_Click(object sender, TappedRoutedEventArgs e)
    {
        NavigateToTimeline();
    }

    private void NavigateToTimeline()
    {
        try
        {
            App.GetService<Services.NavigationService>()?.NavigateTo("Timeline");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "跳转到时间线失败");
        }
    }

    private Task ShowKeyboardDetailsAsync() =>
        ShowDetailAsync(vm => vm.LoadKeyboardDetailsAsync(DateTime.Today));

    private Task ShowMouseDetailsAsync() =>
        ShowDetailAsync(vm => vm.LoadMouseDetailsAsync(DateTime.Today));

    private Task ShowWebDetailsAsync() =>
        ShowDetailAsync(vm => vm.LoadWebDetailsAsync(DateTime.Today));

    private async void KeyboardCard_Click(object sender, TappedRoutedEventArgs e) =>
        await ShowKeyboardDetailsAsync();

    private async void MouseCard_Click(object sender, TappedRoutedEventArgs e) =>
        await ShowMouseDetailsAsync();

    private async void WebCard_Click(object sender, TappedRoutedEventArgs e) =>
        await ShowWebDetailsAsync();

    /// <summary>
    /// 打开详情对话框。三张卡的加载逻辑原本重复三遍，抽出此处便于统一加异常处理。
    /// </summary>
    private async Task ShowDetailAsync(Func<DetailDialogViewModel, Task> load)
    {
        try
        {
            var scope = App.Services.CreateScope();
            var dialogViewModel = scope.ServiceProvider.GetRequiredService<DetailDialogViewModel>();
            await load(dialogViewModel);

            // 陷阱 #2：DI 取出的 ContentDialog 必须设 XamlRoot，否则点击无反应或卡死
            var dialog = new DetailDialog(dialogViewModel)
            {
                XamlRoot = XamlRoot
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "打开详情对话框失败");
        }
    }
}
