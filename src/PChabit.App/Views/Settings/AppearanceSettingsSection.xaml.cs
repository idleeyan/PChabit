using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;
using PChabit.App.ViewModels;

namespace PChabit.App.Views.Settings;

/// <summary>「外观」设置分区（3.67.0 从 SettingsPage 拆出）。</summary>
public sealed partial class AppearanceSettingsSection : UserControl
{
    private SettingsViewModel? _viewModel;
    private bool _isLoading = true;

    public AppearanceSettingsSection()
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
        ThemeComboBox.SelectionChanged += (s, e) =>
        {
            if (!_isLoading && ThemeComboBox.SelectedItem is ComboBoxItem item)
                RaiseChanged("SelectedThemeKey", item.Tag?.ToString() ?? "system");
        };

        LanguageComboBox.SelectionChanged += (s, e) =>
        {
            if (!_isLoading && LanguageComboBox.SelectedItem is ComboBoxItem item)
                RaiseChanged("SelectedLanguageKey", item.Tag?.ToString() ?? "zh-CN");
        };
    }

    private void LoadFromViewModel()
    {
        if (_viewModel is null) return;
        _isLoading = true;
        try
        {
        ThemeComboBox.Items.Clear();

        ThemeComboBox.Items.Add(new ComboBoxItem { Content = "系统默认", Tag = "system" });
        ThemeComboBox.Items.Add(new ComboBoxItem { Content = "浅色", Tag = "light" });
        ThemeComboBox.Items.Add(new ComboBoxItem { Content = "深色", Tag = "dark" });
        ThemeComboBox.SelectedIndex = _viewModel.SelectedThemeKey switch
        {
            "light" => 1,
            "dark" => 2,
            _ => 0
        };

        LanguageComboBox.Items.Clear();
        LanguageComboBox.Items.Add(new ComboBoxItem { Content = "简体中文", Tag = "zh-CN" });
        LanguageComboBox.Items.Add(new ComboBoxItem { Content = "English", Tag = "en-US" });
        LanguageComboBox.SelectedIndex = _viewModel.SelectedLanguageKey == "en-US" ? 1 : 0;
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
            case "SelectedThemeKey":
                _viewModel.SelectedThemeKey = (string)value;
                break;
            case "SelectedLanguageKey":
                _viewModel.SelectedLanguageKey = (string)value;
                break;
        }

        // 必须落盘：只改 ViewModel 不会写 settings.json
        _viewModel.SaveSetting(propertyName);
    }
}
