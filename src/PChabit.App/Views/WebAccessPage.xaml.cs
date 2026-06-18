using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Serilog;

namespace PChabit.App.Views;

public sealed partial class WebAccessPage : Page
{
    private WebStatsTab? _statsTab;
    private WebsiteCategoryTab? _categoryTab;

    public WebAccessPage()
    {
        Log.Information("WebAccessPage: 构造函数开始");
        InitializeComponent();
        Loaded += WebAccessPage_Loaded;
        Log.Information("WebAccessPage: 构造函数完成");
    }

    private void WebAccessPage_Loaded(object sender, RoutedEventArgs e)
    {
        // 延迟设置选中项，避免在 Loaded 事件中同步创建子控件导致 UI 线程死锁
        // NavigationView 的 SelectionChanged 会同步创建子 UserControl，
        // 如果在 Loaded 事件中同步执行，可能与 NavigationView 内部状态冲突
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (TabNav.MenuItems.Count > 0 && TabNav.SelectedItem == null)
            {
                TabNav.SelectedItem = TabNav.MenuItems[0];
            }
        });
    }

    private void TabNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item)
        {
            var tag = item.Tag?.ToString();
            switch (tag)
            {
                case "Stats":
                    ShowStatsTab();
                    break;
                case "Category":
                    ShowCategoryTab();
                    break;
            }
        }
    }

    private void ShowStatsTab()
    {
        if (_statsTab == null)
        {
            _statsTab = new WebStatsTab();
        }
        TabContent.Content = _statsTab;
    }

    private void ShowCategoryTab()
    {
        if (_categoryTab == null)
        {
            _categoryTab = new WebsiteCategoryTab();
        }
        TabContent.Content = _categoryTab;
    }
}
