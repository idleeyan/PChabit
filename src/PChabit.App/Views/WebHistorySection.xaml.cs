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

    /// <summary>告警条上的「开启采集」：恢复采集后立即重查一次，看是否已有新数据。</summary>
    private async void EnableIngest_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.EnableIngestCommand.Execute(null);
        _loaded = false;
        await EnsureLoadedAsync();
    }

    /// <summary>
    /// 点击行 → 打开域名详情（3.38.0，还原原 HTML 报告的域名下钻）。
    /// 用 ItemClick 而非 SelectionChanged：后者在「点击后保持选中」时语义不清，
    /// 且会与 ListViewItem 自身的选中高亮叠加成双重高亮。
    /// </summary>
    private async void DomainList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not HistoryDomainRow row) return;

        ViewModel.OpenDetailCommand.Execute(row.Domain);

        try
        {
            // ContentDialog 必须设 XamlRoot，否则 ShowAsync 抛异常（陷阱 2）
            DetailDialog.XamlRoot = XamlRoot;

            // 主按钮「在浏览器中打开」：需要 deferral 才能阻止默认关闭行为。
            // 处理器在 ShowAsync 后统一退订，避免多次打开时不断累积（陷阱 31）。
            void OnPrimaryClick(ContentDialog s, ContentDialogButtonClickEventArgs args)
            {
                args.Cancel = true;
                ViewModel.OpenDomainInBrowserCommand.Execute(row.Domain);
            }
            DetailDialog.PrimaryButtonClick += OnPrimaryClick;

            await DetailDialog.ShowAsync();
            DetailDialog.PrimaryButtonClick -= OnPrimaryClick;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "WebHistorySection: 域名详情弹窗失败 {Domain}", row.Domain);
        }
        finally
        {
            ViewModel.CloseDetailCommand.Execute(null);
        }
    }
}