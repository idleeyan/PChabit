using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;
using PChabit.App.ViewModels;
using PChabit.Core.Entities;
using PChabit.Infrastructure.Data;

namespace PChabit.App.Views;

public sealed partial class WebsiteCategoryTab : UserControl
{
    public WebsiteCategoryManagementViewModel ViewModel { get; }

    public WebsiteCategoryTab()
    {
        ViewModel = App.GetService<WebsiteCategoryManagementViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += WebsiteCategoryTab_Loaded;
    }

    private async void WebsiteCategoryTab_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await ViewModel.InitializeAsync();
            CategoryList.ItemsSource = ViewModel.Categories;
            CategoryList.SelectedItem = ViewModel.SelectedCategory;
            MappingList.ItemsSource = ViewModel.FilteredMappings;
            UpdateCategoryDetailUI();
            TotalCategoriesText.Text = $"共 {ViewModel.Categories.Count} 个分类";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "WebsiteCategoryTab: 初始化失败");
        }
    }

    private void UpdateCategoryDetailUI()
    {
        var isSelected = ViewModel.SelectedCategory != null;

        SelectedCategoryNameText.Text = isSelected ? ViewModel.SelectedCategory!.Name : "选择分类查看映射";
        SelectedCategoryDescText.Text = isSelected ? ViewModel.SelectedCategory!.Description ?? "" : "";
        CategoryActionsPanel.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
        MappingSearchPanel.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
        MappingList.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
        EmptyStatePanel.Visibility = isSelected ? Visibility.Collapsed : Visibility.Visible;

        if (isSelected && ViewModel.SelectedCategory!.IsSystem)
        {
            DeleteCategoryButton.IsEnabled = false;
        }
        else
        {
            DeleteCategoryButton.IsEnabled = true;
        }
    }

    private void OnCategorySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListView listView)
        {
            if (listView.SelectedItem is WebsiteCategory category)
                ViewModel.SelectCategoryCommand.Execute(category);
            else if (listView.SelectedItem == null)
                ViewModel.SelectCategoryCommand.Execute(null);

            MappingList.ItemsSource = ViewModel.FilteredMappings;
            UpdateCategoryDetailUI();
        }
    }

    private async void OnAddCategoryClick(object sender, RoutedEventArgs e)
    {
        var dialog = App.GetService<WebsiteCategoryEditDialog>();
        dialog.ViewModel.InitializeForAdd();
        dialog.XamlRoot = XamlRoot;

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary && dialog.Success && dialog.ResultCategory != null)
        {
            await ViewModel.AddCategoryAsync(dialog.ResultCategory);
            CategoryList.ItemsSource = ViewModel.Categories;
            TotalCategoriesText.Text = $"共 {ViewModel.Categories.Count} 个分类";
        }
    }

    private async void OnEditCategoryClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedCategory == null) return;

        Log.Information("编辑网站分类: {CategoryName}", ViewModel.SelectedCategory.Name);

        var dialog = App.GetService<WebsiteCategoryEditDialog>();
        dialog.ViewModel.InitializeForEdit(ViewModel.SelectedCategory);
        dialog.XamlRoot = XamlRoot;

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary && dialog.Success && dialog.ResultCategory != null)
        {
            await ViewModel.UpdateCategoryAsync(dialog.ResultCategory);
            CategoryList.ItemsSource = ViewModel.Categories;
            UpdateCategoryDetailUI();
        }
    }

    private async void OnDeleteCategoryClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedCategory == null) return;
        if (ViewModel.SelectedCategory.IsSystem) return;

        var dialog = new ContentDialog
        {
            Title = "确认删除",
            Content = $"确定要删除分类 \"{ViewModel.SelectedCategory.Name}\" 吗？\n该分类下的域名映射将一起删除。",
            PrimaryButtonText = "删除",
            SecondaryButtonText = "取消",
            DefaultButton = ContentDialogButton.Secondary,
            XamlRoot = XamlRoot
        };

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            await ViewModel.DeleteCategoryAsync(ViewModel.SelectedCategory.Id);
            CategoryList.ItemsSource = ViewModel.Categories;
            MappingList.ItemsSource = ViewModel.FilteredMappings;
            UpdateCategoryDetailUI();
            TotalCategoriesText.Text = $"共 {ViewModel.Categories.Count} 个分类";
        }
    }

    private async void OnAddMappingClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedCategory == null) return;

        var dialog = new ContentDialog
        {
            Title = "添加域名映射",
            PrimaryButtonText = "添加",
            SecondaryButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        var stackPanel = new StackPanel { Spacing = 12 };

        var modeSelector = new RadioButtons
        {
            Header = "选择方式",
            Items = { "从最近的访问中添加", "手动输入域名" },
            SelectedIndex = 0
        };
        stackPanel.Children.Add(modeSelector);

        // 模式1：最近访问的域名列表
        var recentDomains = await LoadRecentDomainsAsync();
        var domainComboBox = new ComboBox
        {
            Header = "最近访问的域名",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Microsoft.UI.Xaml.Thickness(0, 8, 0, 0),
            ItemsSource = recentDomains,
            IsEnabled = true
        };
        stackPanel.Children.Add(domainComboBox);

        // 模式2：手动输入面板
        var manualInputPanel = new StackPanel
        {
            Spacing = 8,
            Visibility = Visibility.Collapsed,
            Margin = new Microsoft.UI.Xaml.Thickness(0, 8, 0, 0)
        };
        var domainBox = new TextBox
        {
            Header = "域名模式",
            PlaceholderText = "例如: github.com 或 *.github.com"
        };
        manualInputPanel.Children.Add(domainBox);

        var hintText = new TextBlock
        {
            Text = "支持通配符匹配，如 *.github.com 会匹配 github.com 和所有子域名",
            FontSize = 12,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
            Margin = new Microsoft.UI.Xaml.Thickness(0, 8, 0, 0)
        };
        manualInputPanel.Children.Add(hintText);
        stackPanel.Children.Add(manualInputPanel);

        modeSelector.SelectionChanged += (s, args) =>
        {
            if (modeSelector.SelectedIndex == 0)
            {
                domainComboBox.Visibility = Visibility.Visible;
                manualInputPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                domainComboBox.Visibility = Visibility.Collapsed;
                manualInputPanel.Visibility = Visibility.Visible;
            }
        };

        dialog.Content = stackPanel;

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            string? domainPattern = null;

            if (modeSelector.SelectedIndex == 0)
            {
                domainPattern = domainComboBox.SelectedItem as string;
            }
            else
            {
                domainPattern = domainBox.Text?.Trim();
            }

            if (!string.IsNullOrWhiteSpace(domainPattern))
            {
                var mapping = new WebsiteDomainMapping
                {
                    DomainPattern = domainPattern,
                    CategoryId = ViewModel.SelectedCategory.Id
                };

                await ViewModel.AddMappingAsync(mapping);
                MappingList.ItemsSource = ViewModel.FilteredMappings;
            }
        }
    }

    private async Task<List<string>> LoadRecentDomainsAsync()
    {
        var domains = new List<string>();
        try
        {
            using var scope = App.GetService<IServiceScopeFactory>().CreateScope();
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<PChabitDbContext>>();
            await using var dbContext = await dbFactory.CreateDbContextAsync();

            domains = await dbContext.WebSessions
                .AsNoTracking()
                .Where(s => !string.IsNullOrEmpty(s.Domain))
                .Select(s => s.Domain)
                .Distinct()
                .OrderByDescending(d => dbContext.WebSessions
                    .Where(s => s.Domain == d)
                    .Max(s => s.StartTime))
                .Take(50)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "加载最近访问域名失败");
        }
        return domains;
    }

    private async void OnDeleteMappingClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is int mappingId)
        {
            var dialog = new ContentDialog
            {
                Title = "确认删除",
                Content = "确定要删除此域名映射吗？",
                PrimaryButtonText = "删除",
                SecondaryButtonText = "取消",
                DefaultButton = ContentDialogButton.Secondary,
                XamlRoot = XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await ViewModel.DeleteMappingAsync(mappingId);
                MappingList.ItemsSource = ViewModel.FilteredMappings;
            }
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ViewModel.SearchText = SearchBox.Text;
        MappingList.ItemsSource = ViewModel.FilteredMappings;
    }
}
