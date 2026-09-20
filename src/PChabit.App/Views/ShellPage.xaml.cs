using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Serilog;
using PChabit.App.Services;

namespace PChabit.App.Views;

public sealed partial class ShellPage : Page
{
    private readonly NavigationService _navigationService;
    private bool _isMonitoring;
    private readonly ObservableCollection<string> _globalLogLines = new();
    private bool _globalLogVisible = true;

    public ShellPage()
    {
        InitializeComponent();

        // 必须使用 DI 单例：分析页/硬件页等通过 App.GetService<NavigationService>() 跳转。
        _navigationService = App.GetService<NavigationService>();
        _navigationService.Initialize(ContentFrame);

        GlobalLogList.ItemsSource = _globalLogLines;
        LoadGlobalLogSnapshot();
        GlobalOpLog.Logged += OnGlobalOpLogged;
        Unloaded += (_, _) => GlobalOpLog.Logged -= OnGlobalOpLogged;

        Loaded += ShellPage_Loaded;
    }

    private void LoadGlobalLogSnapshot()
    {
        try
        {
            foreach (var item in GlobalOpLog.Snapshot())
                _globalLogLines.Insert(0, item.Display);
            if (_globalLogLines.Count > 120)
                _globalLogLines.RemoveAt(_globalLogLines.Count - 1);
        }
        catch { }
    }

    private void OnGlobalOpLogged(GlobalOpLog.OpLogItem item)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _globalLogLines.Insert(0, item.Display);
            while (_globalLogLines.Count > 120)
                _globalLogLines.RemoveAt(_globalLogLines.Count - 1);
        });
    }

    private void ToggleGlobalLog_Click(object sender, RoutedEventArgs e)
    {
        _globalLogVisible = !_globalLogVisible;
        GlobalLogList.Visibility = _globalLogVisible ? Visibility.Visible : Visibility.Collapsed;
        if (ToggleGlobalLogButton != null)
            ToggleGlobalLogButton.Content = _globalLogVisible ? "收起" : "展开";
    }

    private void ClearGlobalLog_Click(object sender, RoutedEventArgs e)
    {
        _globalLogLines.Clear();
        GlobalOpLog.Action("Shell", "已清空侧栏全局日志显示");
    }

    private void ShellPage_Loaded(object sender, RoutedEventArgs e)
    {
        GlobalOpLog.Info("Shell", "应用主界面已加载");
        _navigationService.NavigateTo("Dashboard");
        GlobalOpLog.Info("Shell", "导航到仪表盘");
    }

    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        Log.Information("NavView_ItemInvoked: IsSettingsInvoked={IsSettings}", args.IsSettingsInvoked);

        if (args.IsSettingsInvoked)
        {
            Log.Information("NavView_ItemInvoked: 导航到设置页面");
            GlobalOpLog.Action("导航", "打开设置");
            _navigationService.NavigateTo("Settings");
        }
        else if (args.InvokedItemContainer != null)
        {
            var tag = args.InvokedItemContainer.Tag?.ToString();
            Log.Information("NavView_ItemInvoked: Tag={Tag}", tag);
            if (!string.IsNullOrEmpty(tag))
            {
                GlobalOpLog.Action("导航", $"打开 {tag}");
                _navigationService.NavigateTo(tag);
            }
        }
    }

    private void ToggleMonitorButton_Click(object sender, RoutedEventArgs e)
    {
        _isMonitoring = !_isMonitoring;

        if (_isMonitoring)
        {
            ToggleMonitorButton.Content = "暂停监控";
            StatusText.Text = "监控中...";
            StatusIndicator.Fill = new SolidColorBrush(Microsoft.UI.Colors.Green);
            GlobalOpLog.Action("监控", "开始监控");
        }
        else
        {
            ToggleMonitorButton.Content = "开始监控";
            StatusText.Text = "已暂停";
            StatusIndicator.Fill = new SolidColorBrush(Microsoft.UI.Colors.Gray);
            GlobalOpLog.Action("监控", "暂停监控");
        }
    }
}
