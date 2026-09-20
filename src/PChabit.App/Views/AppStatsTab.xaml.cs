using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using PChabit.App.ViewModels;
using PChabit.Core.Entities;
using PChabit.Infrastructure.Analysis;
using PChabit.Infrastructure.Data;
using Serilog;

namespace PChabit.App.Views;

public sealed partial class AppStatsTab : UserControl
{
    public AppStatsViewModel ViewModel { get; }
    private bool _isWebViewInitialized;

    public AppStatsTab()
    {
        InitializeComponent();
        var scope = App.Services.CreateScope();
        ViewModel = scope.ServiceProvider.GetRequiredService<AppStatsViewModel>();
        DataContext = ViewModel;

        Loaded += AppStatsTab_Loaded;
        Unloaded += AppStatsTab_Unloaded;
    }

    private void AppStatsTab_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_isWebViewInitialized)
        {
            try
            {
                PieChartWebView.Close();
            }
            catch { }
        }
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
    }

    private async void AppStatsTab_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        // 同步周期下拉初始选中
        if (RangeCombo.SelectedItem == null)
        {
            RangeCombo.SelectedItem = ViewModel.PeriodOptions.FirstOrDefault(p => p.Kind == ViewModel.SelectedRange);
        }

        // 数据加载与 WebView2 初始化并行（陷阱 13：数据加载不依赖 UI 组件生命周期）
        _ = ViewModel.LoadDataAsync();
        await InitializePieChartAsync();
    }

    private async void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.PieChartData))
        {
            await UpdatePieChartAsync();
        }
    }

    private async Task InitializePieChartAsync()
    {
        if (_isWebViewInitialized) return;

        try
        {
            await PieChartWebView.EnsureCoreWebView2Async();

            if (PieChartWebView.CoreWebView2 == null)
            {
                Log.Warning("[AppStatsTab] CoreWebView2 初始化失败");
                return;
            }

            // 图表跟随应用/系统主题（修复 piechart.html 写死白底）
            try
            {
                PieChartWebView.CoreWebView2.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Auto;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[AppStatsTab] 设置 WebView2 配色方案失败，回退默认");
            }

            var htmlPath = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Assets", "PieChart", "piechart.html");

            var fileExists = System.IO.File.Exists(htmlPath);
            Log.Information("[AppStatsTab] 饼图 HTML 路径: {Path}, 存在: {Exists}", htmlPath, fileExists);

            if (!fileExists)
            {
                Log.Warning("[AppStatsTab] 饼图 HTML 文件不存在: {Path}", htmlPath);
                return;
            }

            var fileUri = new System.Uri("file:///" + htmlPath.Replace("\\", "/"));
            PieChartWebView.Source = fileUri;

            _isWebViewInitialized = true;
            Log.Information("[AppStatsTab] 饼图 WebView2 初始化完成");

            await Task.Delay(500);
            await UpdatePieChartAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[AppStatsTab] 饼图 WebView2 初始化失败");
        }
    }

    private async Task UpdatePieChartAsync()
    {
        if (!_isWebViewInitialized || PieChartWebView.CoreWebView2 == null)
        {
            return;
        }

        try
        {
            var data = ViewModel.PieChartData;
            if (string.IsNullOrEmpty(data) || data == "[]")
            {
                var emptyScript = "showEmpty();";
                await PieChartWebView.ExecuteScriptAsync(emptyScript);
                return;
            }

            var escapedJson = data.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
            var script = $"renderPieChart(JSON.parse(\"{escapedJson}\"));";

            var result = await PieChartWebView.ExecuteScriptAsync(script);
            Log.Information("[AppStatsTab] 饼图渲染结果: {Result}", result);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[AppStatsTab] 更新饼图数据失败");
        }
    }

    // === 工具栏 ===

    private void RangeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RangeCombo.SelectedItem is PeriodOption opt && opt.Kind != ViewModel.SelectedRange)
        {
            ViewModel.SelectedRange = opt.Kind;
        }
    }

    private void CategoryChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string category)
        {
            ViewModel.SelectedCategoryFilter = category == "全部分类" ? null : category;
        }
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        ViewModel.SearchText = sender.Text;
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadDataAsync();
    }

    private async void CopySummaryButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.CopySummaryCommand.ExecuteAsync(null);
        CopyFeedback.Visibility = Visibility.Visible;
        await Task.Delay(2000);
        CopyFeedback.Visibility = Visibility.Collapsed;
    }

    // === 排行行交互 ===

    private void BackgroundMode_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggleSwitch && toggleSwitch.DataContext is AppStatItem item)
        {
            ViewModel.ToggleBackgroundModeCommand.Execute(item);
        }
    }

    private void DetailButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: AppStatItem item })
            _ = OpenAppDetailAsync(item);
    }

    private void AppRank_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is AppStatItem item)
            _ = OpenAppDetailAsync(item);
    }

    private async Task OpenAppDetailAsync(AppStatItem item)
    {
        try
        {
            var period = ViewModel.LastPeriod ?? AnalyticsPeriod.FromKind(ViewModel.SelectedRange);
            var scope = App.Services.CreateScope();
            var vm = scope.ServiceProvider.GetRequiredService<AppDetailViewModel>();
            vm.Configure(period, item.ProcessName, item.AppName, item.Category, item.CategoryColorHex, ViewModel.LastTotalMinutes);
            var dialog = new AppDetailDialog(vm, process => { _ = ShowCategoryPickerAsync(process); })
            {
                XamlRoot = XamlRoot
            };
            _ = vm.LoadReportAsync();
            await dialog.ShowAsync();
            // 若详情里改了分类，刷新排行
            await ViewModel.LoadDataAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "打开应用详情失败 {App}", item.AppName);
        }
    }

    private async void CategoryTag_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string processName)
        {
            await ShowCategoryPickerAsync(processName);
        }
    }

    private async Task ShowCategoryPickerAsync(string processName)
    {
        try
        {
            var dbFactory = App.GetService<IDbContextFactory<PChabitDbContext>>();

            await using var dbContext = await dbFactory.CreateDbContextAsync();
            var categories = await dbContext.ProgramCategories
                .Include(c => c.ProgramMappings)
                .Where(c => c.IsActive)
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Name)
                .ToListAsync();

            // 查找当前映射
            var currentMapping = await dbContext.ProgramCategoryMappings
                .FirstOrDefaultAsync(m => m.ProcessName.ToLower() == processName.ToLower());
            var currentCategoryId = currentMapping?.CategoryId ?? 0;

            var dialog = new ContentDialog
            {
                Title = $"修改分类 - {processName}",
                PrimaryButtonText = "确定",
                SecondaryButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };

            var stackPanel = new StackPanel { Spacing = 12 };

            // 当前分类显示
            var currentCategory = categories.FirstOrDefault(c => c.Id == currentCategoryId);
            var currentInfo = new TextBlock
            {
                FontSize = 13,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
                Text = currentCategory != null ? $"当前分类: {currentCategory.Icon} {currentCategory.Name}" : "当前分类: 未分类"
            };
            stackPanel.Children.Add(currentInfo);

            // 分类列表选择
            var radioButtons = new RadioButtons
            {
                Header = "选择新分类"
            };

            foreach (var cat in categories.OrderBy(c => c.SortOrder))
            {
                var rb = new RadioButton
                {
                    Content = $"{cat.Icon} {cat.Name}",
                    Tag = cat.Id,
                    IsChecked = cat.Id == currentCategoryId
                };
                radioButtons.Items.Add(rb);
            }

            stackPanel.Children.Add(radioButtons);
            dialog.Content = stackPanel;

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                var selectedRadioButton = radioButtons.Items.Cast<RadioButton>().FirstOrDefault(rb => rb.IsChecked == true);
                if (selectedRadioButton != null && selectedRadioButton.Tag is int newCategoryId)
                {
                    if (newCategoryId != currentCategoryId)
                    {
                        if (currentMapping != null)
                        {
                            // 更新现有映射
                            currentMapping.CategoryId = newCategoryId;
                            currentMapping.UpdatedAt = DateTime.Now;
                            await dbContext.SaveChangesAsync();
                        }
                        else
                        {
                            // 创建新映射
                            var newMapping = new ProgramCategoryMapping
                            {
                                ProcessName = processName,
                                CategoryId = newCategoryId,
                                CreatedAt = DateTime.Now
                            };
                            dbContext.ProgramCategoryMappings.Add(newMapping);
                            await dbContext.SaveChangesAsync();
                        }

                        Log.Information("已修改应用 {ProcessName} 的分类为 CategoryId={CategoryId}", processName, newCategoryId);

                        // 刷新数据
                        await ViewModel.LoadDataAsync();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "修改应用分类失败: {ProcessName}", processName);
        }
    }
}
