using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Serilog;
using PChabit.App.Services;

namespace PChabit.App.Views;

public sealed partial class WebAccessPage : Page
{
    public WebAccessPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // 跨页落点：NavigateTo("WebAccess", "history") 直接打开网页浏览历史页签
        if (e.Parameter is string tag &&
            string.Equals(tag, "history", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var item in RootPivot.Items)
            {
                if (item is PivotItem { Tag: string t } pivot &&
                    string.Equals(t, tag, StringComparison.OrdinalIgnoreCase))
                {
                    RootPivot.SelectedItem = pivot;
                    break;
                }
            }
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
