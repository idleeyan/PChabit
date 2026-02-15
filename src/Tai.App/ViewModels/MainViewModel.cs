using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tai.App.Services;

namespace Tai.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private NavigationService? _navigationService;
    
    [ObservableProperty]
    private NavigationViewItem? _selectedMenuItem;
    
    [ObservableProperty]
    private bool _isMonitoring;
    
    [ObservableProperty]
    private string _statusMessage = "就绪";
    
    public ObservableCollection<NavigationItem> MenuItems { get; } = new()
    {
        new NavigationItem { Icon = "\uE80F", Label = "仪表盘", Key = "Dashboard" },
        new NavigationItem { Icon = "\uE823", Label = "时间线", Key = "Timeline" },
        new NavigationItem { Icon = "\uE9D9", Label = "分析", Key = "Analytics" },
        new NavigationItem { Icon = "\uE896", Label = "导出", Key = "Export" },
        new NavigationItem { Icon = "\uE713", Label = "设置", Key = "Settings" }
    };
    
    public MainViewModel()
    {
    }
    
    public MainViewModel(NavigationService navigationService)
    {
        _navigationService = navigationService;
        _navigationService.Navigated += OnNavigated;
    }
    
    public void SetNavigationService(NavigationService navigationService)
    {
        if (_navigationService != null)
        {
            _navigationService.Navigated -= OnNavigated;
        }
        _navigationService = navigationService;
        _navigationService.Navigated += OnNavigated;
    }
    
    private void OnNavigated(object? sender, NavigatedEventArgs e)
    {
        var item = MenuItems.FirstOrDefault(m => m.Key == e.PageKey);
        if (item != null)
        {
            SelectedMenuItem = null;
        }
    }
    
    [RelayCommand]
    private void Navigate(string key)
    {
        _navigationService?.NavigateTo(key);
    }
    
    [RelayCommand]
    private void ToggleMonitoring()
    {
        IsMonitoring = !IsMonitoring;
        StatusMessage = IsMonitoring ? "监控中..." : "已暂停";
    }
    
    partial void OnSelectedMenuItemChanged(NavigationViewItem? value)
    {
        if (value?.Tag is string key)
        {
            Navigate(key);
        }
    }
}

public class NavigationItem
{
    public string Icon { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
}
