using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;
using PChabit.App.ViewModels;

namespace PChabit.App.Views.Settings;

/// <summary>「常规」设置分区（3.67.0 从 SettingsPage 拆出）。</summary>
public sealed partial class GeneralSettingsSection : UserControl
{
    private SettingsViewModel? _viewModel;
    private bool _isLoading = true;

    public GeneralSettingsSection()
    {
        InitializeComponent();
        BindEvents();
    }

    /// <summary>宿主注入 ViewModel 并加载当前值（构造期调用）。</summary>
    public void Initialize(SettingsViewModel viewModel)
    {
        _viewModel = viewModel;
        LoadFromViewModel();
    }

    /// <summary>宿主重新进入设置页时刷新。</summary>
    public void Reload() => LoadFromViewModel();

    private void BindEvents()
    {
        StartWithWindowsSwitch.Toggled += (s, e) => RaiseChanged("StartWithWindows", StartWithWindowsSwitch.IsOn);
        MinimizeToTraySwitch.Toggled += (s, e) => RaiseChanged("MinimizeToTray", MinimizeToTraySwitch.IsOn);
        ShowNotificationsSwitch.Toggled += (s, e) => RaiseChanged("ShowNotifications", ShowNotificationsSwitch.IsOn);
        AutoStartMonitoringSwitch.Toggled += (s, e) => RaiseChanged("AutoStartMonitoring", AutoStartMonitoringSwitch.IsOn);
    }

    private void LoadFromViewModel()
    {
        if (_viewModel is null) return;
        _isLoading = true;
        try
        {
        StartWithWindowsSwitch.IsOn = _viewModel.StartWithWindows;
        MinimizeToTraySwitch.IsOn = _viewModel.MinimizeToTray;
        ShowNotificationsSwitch.IsOn = _viewModel.ShowNotifications;
        AutoStartMonitoringSwitch.IsOn = _viewModel.AutoStartMonitoring;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void RaiseChanged(string propertyName, object value)
    {
        if (_isLoading) return;
        Log.Information("SettingsPage: 设置变更 {PropertyName} = {Value}", propertyName, value);
        if (_viewModel is null) return;

        switch (propertyName)
        {
            case "StartWithWindows":
                _viewModel.StartWithWindows = (bool)value;
                break;
            case "MinimizeToTray":
                _viewModel.MinimizeToTray = (bool)value;
                break;
            case "ShowNotifications":
                _viewModel.ShowNotifications = (bool)value;
                break;
            case "AutoStartMonitoring":
                _viewModel.AutoStartMonitoring = (bool)value;
                break;
        }

        // 必须落盘：只改 ViewModel 不会写 settings.json
        _viewModel.SaveSetting(propertyName);
    }
}
