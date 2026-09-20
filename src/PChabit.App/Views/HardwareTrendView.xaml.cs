using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;

namespace PChabit.App.Views;

/// <summary>分析页硬件时序：WebView2 + 本地 canvas 模板（Assets/Analytics/hardware-trend.html）。</summary>
public sealed partial class HardwareTrendView : UserControl
{
    private bool _initialized;
    private bool _navigating;
    private string? _pendingJson;

    public HardwareTrendView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += (_, _) =>
        {
            try { ChartWebView.Close(); } catch { /* ignore */ }
        };
    }

    public static readonly DependencyProperty ChartJsonProperty = DependencyProperty.Register(
        nameof(ChartJson), typeof(string), typeof(HardwareTrendView),
        new PropertyMetadata("{}", OnChartJsonChanged));

    public string ChartJson
    {
        get => (string)GetValue(ChartJsonProperty);
        set => SetValue(ChartJsonProperty, value);
    }

    private static void OnChartJsonChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HardwareTrendView view)
            view.PushJson(e.NewValue as string ?? "{}");
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        try
        {
            await ChartWebView.EnsureCoreWebView2Async();
            if (ChartWebView.CoreWebView2 == null) return;

            ChartWebView.CoreWebView2.NavigationCompleted += (_, _) =>
            {
                _navigating = false;
                PushJson(ChartJson);
            };

            var htmlPath = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Assets", "Analytics", "hardware-trend.html");
            if (!System.IO.File.Exists(htmlPath))
            {
                Log.Warning("HardwareTrendView 模板不存在: {Path}", htmlPath);
                return;
            }

            _navigating = true;
            _initialized = true;
            ChartWebView.CoreWebView2.Navigate(new Uri("file:///" + htmlPath.Replace("\\", "/")).ToString());
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "HardwareTrendView WebView 初始化失败");
        }
    }

    private void PushJson(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        if (!_initialized || _navigating || ChartWebView.CoreWebView2 == null)
        {
            _pendingJson = json;
            return;
        }
        try
        {
            // 兼容 window.postMessage 与全局变量
            var script =
                "try{window.__HW_DATA__=" + json + ";if(typeof draw==='function')draw(window.__HW_DATA__);" +
                "window.postMessage(window.__HW_DATA__,'*');}catch(e){}";
            _ = ChartWebView.CoreWebView2.ExecuteScriptAsync(script);
            _pendingJson = null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "HardwareTrendView 注入数据失败");
        }
    }
}
