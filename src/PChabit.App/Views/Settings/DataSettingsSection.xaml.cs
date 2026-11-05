using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;
using PChabit.App.ViewModels;

namespace PChabit.App.Views.Settings;

/// <summary>「数据」设置分区（3.67.0 从 SettingsPage 拆出）。
/// 原页面用 `x:Bind ViewModel.X, Mode=TwoWay` 直接绑页面 ViewModel（不经
/// OnSettingChanged/LoadSettingsToUI）。拆分后统一为与其它分区一致的自治模式
/// （注入同一 SettingsViewModel + RaiseChanged/SaveSetting），顺带修正
/// 原 x:Bind TwoWay「改动只写 ViewModel、不调 SaveSetting」的落盘隐患。</summary>
public sealed partial class DataSettingsSection : UserControl
{
    private SettingsViewModel? _viewModel;
    private bool _isLoading = true;

    public DataSettingsSection()
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
        AutoCleanupSwitch.Toggled += (s, e) => RaiseChanged("AutoCleanupEnabled", AutoCleanupSwitch.IsOn);
        DataRetentionBox.ValueChanged += (s, e) => RaiseChanged("DataRetentionDays", (int)e.NewValue);
    }

    private void LoadFromViewModel()
    {
        if (_viewModel is null) return;
        _isLoading = true;
        try
        {
            AutoCleanupSwitch.IsOn = _viewModel.AutoCleanupEnabled;
            DataRetentionBox.Value = _viewModel.DataRetentionDays;
        }
        finally
        {
            _isLoading = false;
        }
    }

    /// <summary>控件事件 → ViewModel + 落盘。</summary>
    private void RaiseChanged(string propertyName, object value)
    {
        if (_isLoading) return;
        Log.Information("SettingsPage: 设置变更 {PropertyName} = {Value}", propertyName, value);
        if (_viewModel is null) return;

        switch (propertyName)
        {
            case "AutoCleanupEnabled":
                _viewModel.AutoCleanupEnabled = (bool)value;
                break;
            case "DataRetentionDays":
                _viewModel.DataRetentionDays = (int)value;
                break;
        }

        // 必须落盘：只改 ViewModel 不会写 settings.json，也不会执行 ApplyStartupSetting
        _viewModel.SaveSetting(propertyName);
    }
}
