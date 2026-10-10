using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Serilog;
using PChabit.App.Services;

namespace PChabit.App.Views;

/// <summary>
/// 应用使用页（3.34.0）。
///
/// 由原「应用统计」页与「键鼠详情」页合并：侧栏两项收敛为一项，
/// 内部以页签区分「应用排行」与「键鼠详情」。
/// 旧导航 key（AppStats / KeyboardDetails）保留为别名，见 <see cref="NavigationService"/>。
/// </summary>
public sealed partial class AppUsagePage : Page
{
    public AppUsagePage()
    {
        InitializeComponent();
        Log.Information("AppUsagePage: 构造完成");
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // 兼容旧调用：NavigateTo("KeyboardDetails") 直接落到「键鼠详情」页签
        if (e.Parameter is AppUsageNavArgs args && !string.IsNullOrEmpty(args.PivotTag))
        {
            RootPivot.SelectedIndex = FindPivotIndex(args.PivotTag);
            Log.Information("AppUsagePage: 按参数落页签 {Tag}", args.PivotTag);
        }
    }

    private int FindPivotIndex(string tag)
    {
        for (int i = 0; i < RootPivot.Items.Count; i++)
        {
            if (RootPivot.Items[i] is PivotItem item &&
                string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// 切到键鼠页签时补一次数据加载——Pivot 会缓存页签内容，
    /// 首次显示时 Loaded 可能已触发过，这里兜底。
    /// </summary>
    private void RootPivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 用 sender 而非 RootPivot 字段：XAML 解析期 Pivot 默认选中第一项，
        // 此时字段可能仍为 null（陷阱 31）
        if (sender is Pivot { SelectedItem: PivotItem { Tag: "keyboard" } })
        {
            _ = KeyboardTab.EnsureLoadedAsync();
        }
    }

    private void OpenCategoryManager_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var ok = App.GetService<NavigationService>().NavigateTo("CategoryManagement");
            Log.Information("应用使用页→分类管理: {Ok}", ok);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "应用使用页跳转分类管理失败");
        }
    }
}

/// <summary>
/// 应用使用页导航参数（<c>NavigationService.NavigateTo("AppUsage", new AppUsageNavArgs("keyboard"))</c>）。
/// Pivot 取值：ranking / keyboard。
/// </summary>
public sealed record AppUsageNavArgs(string PivotTag);