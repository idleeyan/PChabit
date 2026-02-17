using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Serilog;
using PChabit.App.ViewModels;
using PChabit.Core.Entities;

namespace PChabit.App.Views;

public sealed partial class CategoryManagementPage : Page
{
    public CategoryManagementViewModel ViewModel { get; }

    public CategoryManagementPage()
    {
        Log.Information("CategoryManagementPage: 构造函数开始");
        
        ViewModel = App.GetService<CategoryManagementViewModel>();
        
        InitializeComponent();
        
        Log.Information("CategoryManagementPage: 构造函数完成");
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        Log.Information("CategoryManagementPage: OnNavigatedTo 开始");
        base.OnNavigatedTo(e);
        
        try
        {
            await ViewModel.InitializeAsync();
            Log.Information("CategoryManagementPage: 初始化完成");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CategoryManagementPage: 初始化失败");
        }
    }

    private void OnCategorySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListView listView)
        {
            if (listView.SelectedItem is ProgramCategory category)
            {
                ViewModel.SelectCategoryCommand.Execute(category);
            }
            else if (listView.SelectedItem == null)
            {
                ViewModel.SelectCategoryCommand.Execute(null);
            }
        }
    }

    private async void OnEditCategoryClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedCategory == null) return;

        Log.Information("编辑分类: {CategoryName}", ViewModel.SelectedCategory.Name);

        var dialog = App.GetService<CategoryEditDialog>();
        dialog.ViewModel.InitializeForEdit(ViewModel.SelectedCategory);
        
        var result = await dialog.ShowAsync();
        
        if (result == ContentDialogResult.Primary)
        {
            var category = dialog.ViewModel.GetCategory();
            await ViewModel.UpdateCategoryAsync(category);
        }
    }

    private async void OnDeleteCategoryClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedCategory == null) return;
        if (ViewModel.SelectedCategory.IsSystem) return;

        var dialog = new ContentDialog
        {
            Title = "确认删除",
            Content = $"确定要删除分类 \"{ViewModel.SelectedCategory.Name}\" 吗？\n该分类下的程序映射将保留但不再关联。",
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

        var runningProcesses = ViewModel.GetRunningProcesses();
        
        var dialog = new ContentDialog
        {
            Title = "添加程序映射",
            PrimaryButtonText = "添加",
            SecondaryButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        var stackPanel = new StackPanel { Spacing = 12 };
        
        var modeSelector = new RadioButtons
        {
            Header = "选择方式",
            Items = { "从运行中的程序选择", "手动输入程序名" },
            SelectedIndex = 0
        };
        stackPanel.Children.Add(modeSelector);

        var processComboBox = new ComboBox
        {
            Header = "运行中的程序",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Microsoft.UI.Xaml.Thickness(0, 8, 0, 0),
            ItemsSource = runningProcesses,
            DisplayMemberPath = "ProcessName",
            IsEnabled = true
        };
        stackPanel.Children.Add(processComboBox);

        var manualInputPanel = new StackPanel
        {
            Spacing = 8,
            Visibility = Visibility.Collapsed,
            Margin = new Microsoft.UI.Xaml.Thickness(0, 8, 0, 0)
        };
        
        var processNameBox = new TextBox
        {
            Header = "程序名称",
            PlaceholderText = "例如: notepad.exe"
        };
        manualInputPanel.Children.Add(processNameBox);
        stackPanel.Children.Add(manualInputPanel);

        var aliasBox = new TextBox
        {
            Header = "显示名称（可选）",
            PlaceholderText = "例如: 记事本",
            Margin = new Microsoft.UI.Xaml.Thickness(0, 8, 0, 0)
        };
        stackPanel.Children.Add(aliasBox);

        modeSelector.SelectionChanged += (s, args) =>
        {
            if (modeSelector.SelectedIndex == 0)
            {
                processComboBox.Visibility = Visibility.Visible;
                manualInputPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                processComboBox.Visibility = Visibility.Collapsed;
                manualInputPanel.Visibility = Visibility.Visible;
            }
        };

        dialog.Content = stackPanel;

        var result = await dialog.ShowAsync();
        
        if (result == ContentDialogResult.Primary)
        {
            string? processName = null;
            string? processPath = null;
            
            if (modeSelector.SelectedIndex == 0)
            {
                if (processComboBox.SelectedItem is RunningProcessItem selectedProcess)
                {
                    processName = selectedProcess.ProcessName;
                    processPath = selectedProcess.ProcessPath;
                }
            }
            else
            {
                processName = processNameBox.Text?.Trim();
            }
            
            if (!string.IsNullOrWhiteSpace(processName))
            {
                var mapping = new ProgramCategoryMapping
                {
                    ProcessName = processName,
                    ProcessAlias = aliasBox.Text?.Trim(),
                    ProcessPath = processPath,
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
                Content = "确定要删除此程序映射吗？",
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
