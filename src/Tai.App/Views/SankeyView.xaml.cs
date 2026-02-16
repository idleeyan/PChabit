using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;
using Tai.App.ViewModels;

namespace Tai.App.Views;

public sealed partial class SankeyView : UserControl
{
    public SankeyViewModel ViewModel { get; }
    private bool _isWebViewInitialized;
    private bool _isLoading;
    private bool _isNavigating;
    private int _initCount;
    
    public SankeyView()
    {
        InitializeComponent();
        var scope = App.Services.CreateScope();
        ViewModel = scope.ServiceProvider.GetRequiredService<SankeyViewModel>();
        DataContext = ViewModel;
        
        Loaded += SankeyView_Loaded;
        Unloaded += SankeyView_Unloaded;
    }
    
    private void SankeyView_Unloaded(object sender, RoutedEventArgs e)
    {
        ChartWebView.Close();
    }
    
    private async void SankeyView_Loaded(object sender, RoutedEventArgs e)
    {
        _initCount++;
        Log.Information("[SankeyView] Loaded 事件触发，次数: {Count}, 已初始化: {Initialized}", _initCount, _isWebViewInitialized);
        
        if (_isWebViewInitialized) return;
        
        try
        {
            Log.Information("[SankeyView] 开始初始化 WebView2...");
            
            await ChartWebView.EnsureCoreWebView2Async();
            
            if (ChartWebView.CoreWebView2 == null)
            {
                Log.Error("[SankeyView] CoreWebView2 初始化后仍为 null");
                ViewModel.SummaryText = "图表组件初始化失败";
                return;
            }
            
            Log.Information("[SankeyView] WebView2 初始化完成");
            
            ChartWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
            ChartWebView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
            
            var htmlPath = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Assets", "Sankey", "sankey.html");
            
            var fileExists = System.IO.File.Exists(htmlPath);
            Log.Information("[SankeyView] HTML 路径: {Path}, 存在: {Exists}", htmlPath, fileExists);
            
            if (!fileExists)
            {
                ViewModel.SummaryText = "图表模板文件不存在";
                return;
            }
            
            var fileUri = new System.Uri("file:///" + htmlPath.Replace("\\", "/"));
            Log.Information("[SankeyView] 导航到: {Uri}", fileUri);
            
            _isNavigating = true;
            ChartWebView.Source = fileUri;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[SankeyView] WebView2 初始化失败");
            ViewModel.SummaryText = "图表组件初始化失败: " + ex.Message;
        }
    }
    
    private async void CoreWebView2_NavigationCompleted(Microsoft.Web.WebView2.Core.CoreWebView2 sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs args)
    {
        if (!_isNavigating) return;
        _isNavigating = false;
        
        Log.Information("[SankeyView] NavigationCompleted，IsSuccess: {IsSuccess}, HttpStatusCode: {StatusCode}", 
            args.IsSuccess, args.HttpStatusCode);
        
        if (!args.IsSuccess)
        {
            Log.Error("[SankeyView] 导航失败");
            ViewModel.SummaryText = "图表页面加载失败";
            return;
        }
        
        _isWebViewInitialized = true;
        Log.Information("[SankeyView] 页面加载完成，开始加载数据");
        
        await Task.Delay(500);
        await RefreshDataAsync();
    }
    
    private void CoreWebView2_WebMessageReceived(Microsoft.Web.WebView2.Core.CoreWebView2 sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            var message = args.TryGetWebMessageAsString();
            Log.Information("[SankeyView] 收到 WebView 消息: {Message}", message);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[SankeyView] 处理 WebView 消息失败");
        }
    }
    
    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            Log.Warning("[SankeyView] 正在加载中，忽略重复请求");
            return;
        }
        
        await RefreshDataAsync();
    }
    
    private async Task RefreshDataAsync()
    {
        Log.Information("[SankeyView] RefreshDataAsync 开始，已初始化: {Initialized}", _isWebViewInitialized);
        
        if (!_isWebViewInitialized)
        {
            Log.Warning("[SankeyView] WebView 未初始化，跳过刷新");
            return;
        }
        
        if (ChartWebView.CoreWebView2 == null)
        {
            Log.Warning("[SankeyView] CoreWebView2 为 null，跳过刷新");
            return;
        }
        
        if (_isLoading)
        {
            Log.Warning("[SankeyView] 正在加载中，跳过重复请求");
            return;
        }
        
        _isLoading = true;
        ViewModel.IsLoading = true;
        
        try
        {
            Log.Information("[SankeyView] 开始加载数据...");
            
            var data = await ViewModel.LoadDataAsync();
            
            if (data == null)
            {
                Log.Warning("[SankeyView] 数据加载返回 null");
                return;
            }
            
            if (data.Nodes.Count == 0)
            {
                Log.Information("[SankeyView] 无数据，显示空状态");
                ViewModel.SummaryText = "所选时间范围内没有活动数据";
                var emptyResult = await ChartWebView.ExecuteScriptAsync("showEmpty();");
                Log.Information("[SankeyView] showEmpty 结果: {Result}", emptyResult);
                return;
            }
            
            var json = ViewModel.ToJson(data);
            Log.Information("[SankeyView] 数据序列化完成，节点数: {NodeCount}, 链接数: {LinkCount}, JSON长度: {JsonLength}", 
                data.Nodes.Count, data.Links.Count, json.Length);
            Log.Information("[SankeyView] JSON 内容: {Json}", json);
            
            var escapedJson = json.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
            var script = $"renderSankey(JSON.parse(\"{escapedJson}\"));";
            
            Log.Information("[SankeyView] 执行渲染脚本");
            
            var result = await ChartWebView.ExecuteScriptAsync(script);
            Log.Information("[SankeyView] JavaScript 执行结果: {Result}", result);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[SankeyView] 刷新数据失败");
            ViewModel.SummaryText = "加载数据时发生错误: " + ex.Message;
        }
        finally
        {
            _isLoading = false;
            ViewModel.IsLoading = false;
        }
    }
}
