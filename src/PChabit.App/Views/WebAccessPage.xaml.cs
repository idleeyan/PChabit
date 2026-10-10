using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Serilog;
using PChabit.App.Services;

namespace PChabit.App.Views;

/// <summary>
/// 网页访问页（3.36.0）。
///
/// 原为「访问统计 / 网页浏览历史」双页签；现已合并为单页——
/// 历史内容原生化后并入统计页底部（<see cref="WebHistorySection"/>）。
/// 导航 key "WebAccess" 保持不变，分析页的跳转入口无需改动。
/// </summary>
public sealed partial class WebAccessPage : Page
{
    public WebAccessPage()
    {
        InitializeComponent();
        Log.Information("WebAccessPage: 构造完成（单页，历史已并入）");
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // 跨页落点：NavigateTo("WebAccess", "history") 滚动到浏览历史区块并加载它。
        // 3.36.0 起本页已无二级页签，历史内容是页面底部的原生区块。
        if (e.Parameter is string tag &&
            string.Equals(tag, "history", StringComparison.OrdinalIgnoreCase))
        {
            await StatsSection.ScrollToHistoryAsync();
            Log.Information("WebAccessPage: 已落点至浏览历史区块");
        }
    }

    private void OpenCategoryManager_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var ok = App.GetService<NavigationService>().NavigateTo("CategoryManagement");
            Log.Information("网页访问页→分类管理: {Ok}", ok);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "网页访问页跳转分类管理失败");
        }
    }
}