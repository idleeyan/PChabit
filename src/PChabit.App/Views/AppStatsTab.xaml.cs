using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PChabit.App.ViewModels;
using PChabit.Core.Entities;
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
        await InitializePieChartAsync();
        await ViewModel.LoadDataAsync();
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

    private void TodayButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedDate = DateTime.Today;
    }

    private void YesterdayButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedDate = DateTime.Today.AddDays(-1);
    }

    private void WeekButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedDate = DateTime.Today.AddDays(-7);
    }

    private void BackgroundMode_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggleSwitch && toggleSwitch.DataContext is AppStatItem item)
        {
            ViewModel.ToggleBackgroundModeCommand.Execute(item);
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
                        await ViewModel.LoadInBackgroundAsync(async () =>
                        {
                            await ViewModel.LoadDataAsync();
                        });
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
