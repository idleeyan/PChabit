using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;
using PChabit.App.ViewModels;

namespace PChabit.App.Views.Settings;

/// <summary>「任务栏与托盘」设置分区（3.67.0 从 SettingsPage 拆出）。</summary>
public sealed partial class TaskbarSettingsSection : UserControl
{
    private SettingsViewModel? _viewModel;
    private bool _isLoading = true;

    public TaskbarSettingsSection()
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
        TaskbarEnabledSwitch.Toggled += (s, e) => RaiseChanged("TaskbarEnabled", TaskbarEnabledSwitch.IsOn);

        DailyUsageGoalHoursBox.ValueChanged += (s, e) => RaiseChanged("DailyUsageGoalHours", e.NewValue);

        TaskbarCpuSwitch.Toggled += (s, e) => RaiseChanged("TaskbarShowCpu", TaskbarCpuSwitch.IsOn);
        TaskbarMemorySwitch.Toggled += (s, e) => RaiseChanged("TaskbarShowMemory", TaskbarMemorySwitch.IsOn);
        TaskbarGpuSwitch.Toggled += (s, e) => RaiseChanged("TaskbarShowGpu", TaskbarGpuSwitch.IsOn);
        TaskbarNetSwitch.Toggled += (s, e) => RaiseChanged("TaskbarShowNet", TaskbarNetSwitch.IsOn);
        TaskbarDiskSwitch.Toggled += (s, e) => RaiseChanged("TaskbarShowDisk", TaskbarDiskSwitch.IsOn);
        TaskbarTempSwitch.Toggled += (s, e) => RaiseChanged("TaskbarShowTemp", TaskbarTempSwitch.IsOn);
        TaskbarUsageSwitch.Toggled += (s, e) => RaiseChanged("TaskbarShowUsage", TaskbarUsageSwitch.IsOn);
    }

    private void LoadFromViewModel()
    {
        if (_viewModel is null) return;
        _isLoading = true;
        try
        {
        TaskbarEnabledSwitch.IsOn = _viewModel.TaskbarEnabled;

        DailyUsageGoalHoursBox.Value = _viewModel.DailyUsageGoalHours;

        TaskbarCpuSwitch.IsOn = _viewModel.TaskbarShowCpu;
        TaskbarMemorySwitch.IsOn = _viewModel.TaskbarShowMemory;
        TaskbarGpuSwitch.IsOn = _viewModel.TaskbarShowGpu;
        TaskbarNetSwitch.IsOn = _viewModel.TaskbarShowNet;
        TaskbarDiskSwitch.IsOn = _viewModel.TaskbarShowDisk;
        TaskbarTempSwitch.IsOn = _viewModel.TaskbarShowTemp;
        TaskbarUsageSwitch.IsOn = _viewModel.TaskbarShowUsage;
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
            case "TaskbarEnabled":
                _viewModel.TaskbarEnabled = (bool)value;
                break;
            case "DailyUsageGoalHours":
                _viewModel.DailyUsageGoalHours = (double)value;
                break;
            case "TaskbarShowCpu":
                _viewModel.TaskbarShowCpu = (bool)value;
                break;
            case "TaskbarShowMemory":
                _viewModel.TaskbarShowMemory = (bool)value;
                break;
            case "TaskbarShowGpu":
                _viewModel.TaskbarShowGpu = (bool)value;
                break;
            case "TaskbarShowNet":
                _viewModel.TaskbarShowNet = (bool)value;
                break;
            case "TaskbarShowDisk":
                _viewModel.TaskbarShowDisk = (bool)value;
                break;
            case "TaskbarShowTemp":
                _viewModel.TaskbarShowTemp = (bool)value;
                break;
            case "TaskbarShowUsage":
                _viewModel.TaskbarShowUsage = (bool)value;
                break;
        }

        // 必须落盘：只改 ViewModel 不会写 settings.json
        _viewModel.SaveSetting(propertyName);
    }
}
