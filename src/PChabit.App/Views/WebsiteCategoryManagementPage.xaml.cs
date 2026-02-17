using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Serilog;
using PChabit.App.ViewModels;
using PChabit.Core.Entities;

namespace PChabit.App.Views;

public sealed partial class WebsiteCategoryManagementPage : Page
{
    public WebsiteCategoryManagementViewModel ViewModel { get; }

    public WebsiteCategoryManagementPage()
    {
        Log.Information("WebsiteCategoryManagementPage: 构造函数开始");
        
        ViewModel = App.GetService<WebsiteCategoryManagementViewModel>();
        
        InitializeComponent();
        
        Log.Information("WebsiteCategoryManagementPage: 构造函数完成");
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        Log.Information("WebsiteCategoryManagementPage: OnNavigatedTo 开始");
        base.OnNavigatedTo(e);
        
        try
        {
            await ViewModel.InitializeAsync();
            Log.Information("WebsiteCategoryManagementPage: 初始化完成");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "WebsiteCategoryManagementPage: 初始化失败");
        }
    }

    private void OnCategorySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListView listView)
        {
            if (listView.SelectedItem is WebsiteCategory category)
            {
                ViewModel.SelectCategoryCommand.Execute(category);
            }
            else if (listView.SelectedItem == null)
            {
                ViewModel.SelectCategoryCommand.Execute(null);
            }
        }
    }

    private async void OnAddCategoryClick(object sender, RoutedEventArgs e)
    {
        Log.Information("添加网站分类");

        var dialog = App.GetService<WebsiteCategoryEditDialog>();
        dialog.ViewModel.InitializeForAdd();
        dialog.XamlRoot = XamlRoot;
        
        var result = await dialog.ShowAsync();
        
        if (result == ContentDialogResult.Primary && dialog.Success && dialog.ResultCategory != null)
        {
            await ViewModel.AddCategoryAsync(dialog.ResultCategory);
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
        
        var domainBox = new TextBox
        {
            Header = "域名模式",
            PlaceholderText = "例如: github.com 或 *.github.com"
        };
        stackPanel.Children.Add(domainBox);

        var hintText = new TextBlock
        {
            Text = "支持通配符匹配，如 *.github.com 会匹配 github.com 和所有子域名",
            FontSize = 12,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
            Margin = new Microsoft.UI.Xaml.Thickness(0, 8, 0, 0)
        };
        stackPanel.Children.Add(hintText);

        dialog.Content = stackPanel;

        var result = await dialog.ShowAsync();
        
        if (result == ContentDialogResult.Primary)
        {
            var domainPattern = domainBox.Text?.Trim();
            
            if (!string.IsNullOrWhiteSpace(domainPattern))
            {
                var mapping = new WebsiteDomainMapping
                {
                    DomainPattern = domainPattern,
                    CategoryId = ViewModel.SelectedCategory.Id
                };
                
                await ViewModel.AddMappingAsync(mapping);
            }
        }
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

            var result = await dialog.ShowAsync();
            
            if (result == ContentDialogResult.Primary)
            {
                await ViewModel.DeleteMappingAsync(mappingId);
            }
        }
    }
}
