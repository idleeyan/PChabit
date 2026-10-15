using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Serilog;

namespace PChabit.App.Views;

/// <summary>
/// 分析-构成：应用分类 + 网页分类双饼图（WebView2 + Assets/PieChart/composition.html）。
/// ChartJson 格式：{"app":[{"name","value"}],"web":[{"name","value"}]}，value 单位分钟。
/// </summary>
public sealed partial class CompositionPieView : UserControl
{
    private bool _initialized;
    private bool _navigating;

    public CompositionPieView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += (_, _) =>
        {
            try { ChartWebView.Close(); } catch { /* ignore */ }
            _initialized = false;
        };
    }

    public static readonly DependencyProperty ChartJsonProperty = DependencyProperty.Register(
        nameof(ChartJson), typeof(string), typeof(CompositionPieView),
        new PropertyMetadata("{}", OnChartJsonChanged));

    public string ChartJson
    {
        get => (string)GetValue(ChartJsonProperty);
        set => SetValue(ChartJsonProperty, value);
    }

    private static void OnChartJsonChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CompositionPieView view)
            view.PushJson(e.NewValue as string ?? "{}");
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        try
        {
            await ChartWebView.EnsureCoreWebView2Async();
            if (ChartWebView.CoreWebView2 == null) return;

            // 图表跟随应用/系统深浅色主题
            try
            {
                ChartWebView.CoreWebView2.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Auto;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[CompositionPieView] 设置 WebView2 配色方案失败，回退默认");
            }

            ChartWebView.CoreWebView2.NavigationCompleted += (_, _) =>
            {
                _navigating = false;
                PushJson(ChartJson);
            };

            var htmlPath = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Assets", "PieChart", "composition.html");
            if (!System.IO.File.Exists(htmlPath))
            {
                Log.Warning("[CompositionPieView] 模板不存在: {Path}", htmlPath);
                return;
            }

            _navigating = true;
            _initialized = true;
            ChartWebView.CoreWebView2.Navigate(new Uri("file:///" + htmlPath.Replace("\\", "/")).ToString());
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[CompositionPieView] WebView 初始化失败");
        }
    }

    private void PushJson(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        // 导航未完成时无需缓存：NavigationCompleted 回调会主动推送当前 ChartJson
        if (!_initialized || _navigating || ChartWebView.CoreWebView2 == null) return;
        try
        {
            var script = "try{renderComposition(" + json + ");}catch(e){}";
            _ = ChartWebView.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[CompositionPieView] 注入数据失败");
        }
    }
}
