using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;
using PChabit.App.Services;

namespace PChabit.App.Views;

public sealed partial class AppStatsPage : Page
{
    public AppStatsPage()
    {
        InitializeComponent();
    }

    private void OpenCategoryManager_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var ok = App.GetService<NavigationService>().NavigateTo("CategoryManagement");
            Log.Information("应用统计页→分类管理: {Ok}", ok);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "应用统计页跳转分类管理失败");
        }
    }
}
