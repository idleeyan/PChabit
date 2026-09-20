using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace PChabit.App.Views;

public sealed partial class AppStatsPage : Page
{
    public AppStatsPage()
    {
        InitializeComponent();
    }

    private void TabPivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 页签切换即内容切换；AppStatsTab 在 Loaded 时自行加载数据
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
    }
}
