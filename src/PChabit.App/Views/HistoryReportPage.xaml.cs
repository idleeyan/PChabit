using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;
using PChabit.App.ViewModels;

namespace PChabit.App.Views;

public sealed partial class HistoryReportPage : Page
{
    public HistoryReportViewModel ViewModel { get; }
    private bool _isWebViewInitialized;
    private bool _isLoading;
    private bool _isNavigating;
    private bool _hasNavigatedOnce;

    public HistoryReportPage()
    {
        InitializeComponent();
        var scope = App.Services.CreateScope();
        ViewModel = scope.ServiceProvider.GetRequiredService<HistoryReportViewModel>();
        DataContext = ViewModel;

        Loaded += HistoryReportPage_Loaded;
        Unloaded += HistoryReportPage_Unloaded;
    }

    private void HistoryReportPage_Unloaded(object sender, RoutedEventArgs e)
    {
        ReportWebView.Close();
    }

    private async void HistoryReportPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_isWebViewInitialized) return;

        try
        {
            await ReportWebView.EnsureCoreWebView2Async();
            if (ReportWebView.CoreWebView2 == null)
            {
                Log.Error("[HistoryReport] CoreWebView2 初始化后仍为 null");
                return;
            }

            ReportWebView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;

            var reportDir = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Assets", "HistoryReport");
            var htmlPath = System.IO.Path.Combine(reportDir, "report.html");

            if (!System.IO.File.Exists(htmlPath))
            {
                Log.Error("[HistoryReport] 模板不存在: {Path}", htmlPath);
                return;
            }

            // 虚拟主机映射：供页面内的 chart.js / report.css 相对资源加载
            const string host = "pchabit.report";
            try
            {
                ReportWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    host, reportDir,
                    Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
            }
            catch (Exception mapEx)
            {
                Log.Warning(mapEx, "[HistoryReport] 虚拟主机映射失败，继续");
            }

            // 先加载数据，再在文档创建时注入（避免 DOMContentLoaded 时无数据触发空状态）
            await LoadAndInjectAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[HistoryReport] WebView2 初始化失败");
        }
    }

    private async void CoreWebView2_NavigationCompleted(
        Microsoft.Web.WebView2.Core.CoreWebView2 sender,
        Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs args)
    {
        if (!_isNavigating) return;
        _isNavigating = false;

        if (!args.IsSuccess)
        {
            Log.Error("[HistoryReport] 页面导航失败 Uri={Uri} WebError={Error} Http={Http}",
                ReportWebView.Source, args.WebErrorStatus, args.HttpStatusCode);
            return;
        }

        _isWebViewInitialized = true;
        Log.Information("[HistoryReport] 页面加载完成，DocumentTitle: {Title}",
            ReportWebView.CoreWebView2?.DocumentTitle ?? "(null)");
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (ReportWebView.CoreWebView2 == null) return;
        await LoadAndInjectAsync();
    }

    private async Task LoadAndInjectAsync()
    {
        if (ReportWebView.CoreWebView2 == null) return;
        if (_isLoading)
        {
            Log.Warning("[HistoryReport] 正在加载中，忽略重复请求");
            return;
        }

        _isLoading = true;
        try
        {
            Log.Information("[HistoryReport] 开始加载历史数据...");
            await ViewModel.LoadDataAsync();

            var json = ViewModel.DataJson;
            if (string.IsNullOrEmpty(json))
            {
                Log.Warning("[HistoryReport] 数据为空");
                return;
            }

            // 已加载过：直接重设数据并重跑 JS，避免重复导航触发 ConnectionAborted
            if (_hasNavigatedOnce)
            {
                var script = $"window.__PCHABIT_HISTORY__ = {json}; loadHistoryData();";
                await ReportWebView.CoreWebView2.ExecuteScriptAsync(script);
                Log.Information("[HistoryReport] 刷新完成，JSON 长度: {Len}", json.Length);
                return;
            }

            // 文档创建时注入：确保 DOMContentLoaded 时全局数据已就绪
            var injectScript = $"window.__PCHABIT_HISTORY__ = {json};";
            await ReportWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(injectScript);
            Log.Information("[HistoryReport] 注入脚本就绪，JSON 长度: {Len}", json.Length);

            // 用 file:// 直接导航（与 SankeyView 同款方案，相对资源可直接加载）
            var htmlPath = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Assets", "HistoryReport", "report.html");
            var fileUri = new System.Uri(htmlPath);
            Log.Information("[HistoryReport] 导航到: {Uri}", fileUri);
            _isNavigating = true;
            _hasNavigatedOnce = true;
            ReportWebView.Source = fileUri;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[HistoryReport] 加载/注入失败");
        }
        finally
        {
            _isLoading = false;
        }
    }
}
