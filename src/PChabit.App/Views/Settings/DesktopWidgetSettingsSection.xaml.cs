using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;
using PChabit.App.ViewModels;

namespace PChabit.App.Views.Settings;

/// <summary>「桌面悬浮插件」设置分区（3.67.0 从 SettingsPage 拆出）。</summary>
public sealed partial class DesktopWidgetSettingsSection : UserControl
{
    private SettingsViewModel? _viewModel;
    private bool _isLoading = true;

    public DesktopWidgetSettingsSection()
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
        // 桌面悬浮插件（3.23.0）
        DesktopWidgetEnabledSwitch.Toggled += (s, e) => RaiseChanged("DesktopWidgetEnabled", DesktopWidgetEnabledSwitch.IsOn);
        DesktopCpuSwitch.Toggled += (s, e) => RaiseChanged("DesktopWidgetShowCpu", DesktopCpuSwitch.IsOn);
        DesktopMemorySwitch.Toggled += (s, e) => RaiseChanged("DesktopWidgetShowMemory", DesktopMemorySwitch.IsOn);
        DesktopGpuSwitch.Toggled += (s, e) => RaiseChanged("DesktopWidgetShowGpu", DesktopGpuSwitch.IsOn);
        DesktopVramSwitch.Toggled += (s, e) => RaiseChanged("DesktopWidgetShowVram", DesktopVramSwitch.IsOn);
        DesktopNetSwitch.Toggled += (s, e) => RaiseChanged("DesktopWidgetShowNet", DesktopNetSwitch.IsOn);
        DesktopDiskSwitch.Toggled += (s, e) => RaiseChanged("DesktopWidgetShowDisk", DesktopDiskSwitch.IsOn);
        DesktopUsageSwitch.Toggled += (s, e) => RaiseChanged("DesktopWidgetShowUsage", DesktopUsageSwitch.IsOn);
        DesktopCurrentAppSwitch.Toggled += (s, e) => RaiseChanged("DesktopWidgetShowCurrentApp", DesktopCurrentAppSwitch.IsOn);
        DesktopTopmostSwitch.Toggled += (s, e) => RaiseChanged("DesktopWidgetTopmost", DesktopTopmostSwitch.IsOn);
        DesktopClickThroughSwitch.Toggled += (s, e) => RaiseChanged("DesktopWidgetClickThrough", DesktopClickThroughSwitch.IsOn);
        DesktopClampSwitch.Toggled += (s, e) => RaiseChanged("DesktopWidgetClampToScreen", DesktopClampSwitch.IsOn);
    }

    private void LoadFromViewModel()
    {
        if (_viewModel is null) return;
        _isLoading = true;
        try
        {
        // 桌面悬浮插件（3.23.0）
        DesktopWidgetEnabledSwitch.IsOn = _viewModel.DesktopWidgetEnabled;
        DesktopCpuSwitch.IsOn = _viewModel.DesktopWidgetShowCpu;
        DesktopMemorySwitch.IsOn = _viewModel.DesktopWidgetShowMemory;
        DesktopGpuSwitch.IsOn = _viewModel.DesktopWidgetShowGpu;
        DesktopVramSwitch.IsOn = _viewModel.DesktopWidgetShowVram;
        DesktopNetSwitch.IsOn = _viewModel.DesktopWidgetShowNet;
        DesktopDiskSwitch.IsOn = _viewModel.DesktopWidgetShowDisk;
        DesktopUsageSwitch.IsOn = _viewModel.DesktopWidgetShowUsage;
        DesktopCurrentAppSwitch.IsOn = _viewModel.DesktopWidgetShowCurrentApp;
        DesktopTopmostSwitch.IsOn = _viewModel.DesktopWidgetTopmost;
        DesktopClickThroughSwitch.IsOn = _viewModel.DesktopWidgetClickThrough;
        DesktopClampSwitch.IsOn = _viewModel.DesktopWidgetClampToScreen;
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
            case "DesktopWidgetEnabled":
                _viewModel.DesktopWidgetEnabled = (bool)value;
                break;
            case "DesktopWidgetShowCpu":
                _viewModel.DesktopWidgetShowCpu = (bool)value;
                break;
            case "DesktopWidgetShowMemory":
                _viewModel.DesktopWidgetShowMemory = (bool)value;
                break;
            case "DesktopWidgetShowGpu":
                _viewModel.DesktopWidgetShowGpu = (bool)value;
                break;
            case "DesktopWidgetShowVram":
                _viewModel.DesktopWidgetShowVram = (bool)value;
                break;
            case "DesktopWidgetShowNet":
                _viewModel.DesktopWidgetShowNet = (bool)value;
                break;
            case "DesktopWidgetShowDisk":
                _viewModel.DesktopWidgetShowDisk = (bool)value;
                break;
            case "DesktopWidgetShowUsage":
                _viewModel.DesktopWidgetShowUsage = (bool)value;
                break;
            case "DesktopWidgetTopmost":
                _viewModel.DesktopWidgetTopmost = (bool)value;
                break;
            case "DesktopWidgetClickThrough":
                _viewModel.DesktopWidgetClickThrough = (bool)value;
                break;
            case "DesktopWidgetClampToScreen":
                _viewModel.DesktopWidgetClampToScreen = (bool)value;
                break;
        }

        // 必须落盘：只改 ViewModel 不会写 settings.json
        _viewModel.SaveSetting(propertyName);
    }
}
