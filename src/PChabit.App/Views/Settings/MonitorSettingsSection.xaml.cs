using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;
using PChabit.App.ViewModels;

namespace PChabit.App.Views.Settings;

/// <summary>「监控」设置分区（3.67.0 从 SettingsPage 拆出）。</summary>
public sealed partial class MonitorSettingsSection : UserControl
{
    private SettingsViewModel? _viewModel;
    private bool _isLoading = true;

    public MonitorSettingsSection()
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
        MonitoringIntervalBox.ValueChanged += (s, e) => RaiseChanged("MonitoringInterval", (int)e.NewValue);
        IdleThresholdBox.ValueChanged += (s, e) => RaiseChanged("IdleThreshold", (int)e.NewValue);

        TrackKeyboardSwitch.Toggled += (s, e) => RaiseChanged("TrackKeyboard", TrackKeyboardSwitch.IsOn);
        TrackMouseSwitch.Toggled += (s, e) => RaiseChanged("TrackMouse", TrackMouseSwitch.IsOn);
        TrackWebBrowsingSwitch.Toggled += (s, e) => RaiseChanged("TrackWebBrowsing", TrackWebBrowsingSwitch.IsOn);
    }

    private void LoadFromViewModel()
    {
        if (_viewModel is null) return;
        _isLoading = true;
        try
        {
        MonitoringIntervalBox.Value = _viewModel.MonitoringInterval;
        IdleThresholdBox.Value = _viewModel.IdleThreshold;

        TrackKeyboardSwitch.IsOn = _viewModel.TrackKeyboard;
        TrackMouseSwitch.IsOn = _viewModel.TrackMouse;
        TrackWebBrowsingSwitch.IsOn = _viewModel.TrackWebBrowsing;
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
            case "MonitoringInterval":
                _viewModel.MonitoringInterval = (int)value;
                break;
            case "IdleThreshold":
                _viewModel.IdleThreshold = (int)value;
                break;
            case "TrackKeyboard":
                _viewModel.TrackKeyboard = (bool)value;
                break;
            case "TrackMouse":
                _viewModel.TrackMouse = (bool)value;
                break;
            case "TrackWebBrowsing":
                _viewModel.TrackWebBrowsing = (bool)value;
                break;
        }

        // 必须落盘：只改 ViewModel 不会写 settings.json
        _viewModel.SaveSetting(propertyName);
    }
}
