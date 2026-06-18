using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PChabit.App.ViewModels;
using Serilog;

namespace PChabit.App.Views;

public sealed partial class AppStatsPage : Page
{
    private AppStatsTab? _rankingTab;
    private CategoryManagementTab? _categoryTab;

    public AppStatsPage()
    {
        InitializeComponent();
        Loaded += AppStatsPage_Loaded;
    }

    private void AppStatsPage_Loaded(object sender, RoutedEventArgs e)
    {
        // 延迟设置选中项，避免在 Loaded 事件中同步创建子控件导致 UI 线程死锁
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
                case "Ranking":
                    ShowRankingTab();
                    break;
                case "Category":
                    ShowCategoryTab();
                    break;
            }
        }
    }

    private void ShowRankingTab()
    {
        if (_rankingTab == null)
        {
            _rankingTab = new AppStatsTab();
        }
        TabContent.Content = _rankingTab;
    }

    private void ShowCategoryTab()
    {
        if (_categoryTab == null)
        {
            _categoryTab = new CategoryManagementTab();
        }
        TabContent.Content = _categoryTab;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
    }
}
