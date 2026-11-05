using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;
using PChabit.App.ViewModels;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Platform;
using PChabit.Infrastructure.Services;

namespace PChabit.App.Views.Settings;

/// <summary>「便签」设置分区（3.65.0 从 SettingsPage 拆出）。
/// 自治加载/保存（注入同一 SettingsViewModel），含热键校验与立即同步。</summary>
public sealed partial class StickyNotesSection : UserControl
{
    private SettingsViewModel? _viewModel;
    private bool _isLoading = true;

    public StickyNotesSection()
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
        // 便签（3.24.0）
        StickyEnabledSwitch.Toggled += (s, e) => RaiseChanged("StickyNotesEnabled", StickyEnabledSwitch.IsOn);
        StickyQuickDismissSwitch.Toggled += (s, e) => RaiseChanged("StickyNotesQuickDismissOnFocusLost", StickyQuickDismissSwitch.IsOn);
        StickyRetentionBox.ValueChanged += (s, e) => RaiseChanged("StickyNotesRetentionDays", (int)e.NewValue);
        StickySyncSwitch.Toggled += (s, e) => RaiseChanged("StickyNotesSyncEnabled", StickySyncSwitch.IsOn);
    }

    private void LoadFromViewModel()
    {
        if (_viewModel is null) return;
        _isLoading = true;
        try
        {
        // 便签（3.24.0）
        StickyEnabledSwitch.IsOn = _viewModel.StickyNotesEnabled;
        StickyHotkeyNewBox.Text = _viewModel.StickyNotesHotkeyNew;
        StickyHotkeyBoardBox.Text = _viewModel.StickyNotesHotkeyBoard;
        StickyQuickDismissSwitch.IsOn = _viewModel.StickyNotesQuickDismissOnFocusLost;
        StickyRetentionBox.Value = _viewModel.StickyNotesRetentionDays;
        StickySyncSwitch.IsOn = _viewModel.StickyNotesSyncEnabled;
        StickyLastSyncText.Text = _viewModel.StickyNotesLastSyncText;
        }
        finally
        {
            _isLoading = false;
        }
    }

    /// <summary>控件事件 → ViewModel + 落盘（等价原 OnSettingChanged 的便签分支）。</summary>
    private void RaiseChanged(string propertyName, object value)
    {
        if (_isLoading) return;
        Log.Information("SettingsPage: 设置变更 {PropertyName} = {Value}", propertyName, value);
        if (_viewModel is null) return;

        switch (propertyName)
        {
            case "StickyNotesEnabled":
                _viewModel.StickyNotesEnabled = (bool)value;
                break;
            case "StickyNotesQuickDismissOnFocusLost":
                _viewModel.StickyNotesQuickDismissOnFocusLost = (bool)value;
                break;
            case "StickyNotesRetentionDays":
                _viewModel.StickyNotesRetentionDays = (int)value;
                break;
            case "StickyNotesSyncEnabled":
                _viewModel.StickyNotesSyncEnabled = (bool)value;
                break;
        }

        // 必须落盘：只改 ViewModel 不会写 settings.json，也不会执行 ApplyStartupSetting
        _viewModel.SaveSetting(propertyName);
    }

    // ===== 便签（3.24.0）=====

    /// <summary>热键失焦校验：解析 + 真实注册探测；失败还原旧值并红字提示，成功规范化落盘。</summary>
    private void StickyHotkeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        if (_viewModel is null) return;
        bool isNew = ReferenceEquals(sender, StickyHotkeyNewBox);
        var box = isNew ? StickyHotkeyNewBox : StickyHotkeyBoardBox;
        var error = isNew ? StickyHotkeyNewError : StickyHotkeyBoardError;
        var propName = isNew ? "StickyNotesHotkeyNew" : "StickyNotesHotkeyBoard";
        var saved = isNew ? _viewModel.StickyNotesHotkeyNew : _viewModel.StickyNotesHotkeyBoard;
        var gesture = box.Text.Trim();

        void ShowError(string msg)
        {
            error.Text = msg;
            error.Visibility = Visibility.Visible;
        }

        // 空手势：注销并保存空值（总开关关闭时仅保存文本）
        if (string.IsNullOrEmpty(gesture))
        {
            error.Visibility = Visibility.Collapsed;
            if (_viewModel.StickyNotesEnabled)
                App.GetService<GlobalHotkeyService>()?.Unregister(isNew ? App.HotkeyActionNew : App.HotkeyActionBoard);
            if (isNew) _viewModel.StickyNotesHotkeyNew = "";
            else _viewModel.StickyNotesHotkeyBoard = "";
            _viewModel.SaveSetting(propName);
            return;
        }

        if (!GlobalHotkeyService.TryParse(gesture, out _, out _, out string normalized))
        {
            ShowError("格式错误：需 Ctrl/Alt/Win 之一 + 字母/数字/F1-F12");
            box.Text = saved;
            return;
        }

        if (_viewModel.StickyNotesEnabled)
        {
            bool ok;
            try
            {
                var hk = App.GetService<GlobalHotkeyService>();
                hk.Start();
                ok = hk.Register(isNew ? App.HotkeyActionNew : App.HotkeyActionBoard, normalized);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Notes] 设置页热键注册异常");
                ok = false;
            }
            if (!ok)
            {
                ShowError("热键被其他程序占用，请更换");
                box.Text = saved;
                return;
            }
        }

        error.Visibility = Visibility.Collapsed;
        box.Text = normalized;
        if (isNew) _viewModel.StickyNotesHotkeyNew = normalized;
        else _viewModel.StickyNotesHotkeyBoard = normalized;
        _viewModel.SaveSetting(propName);
    }

    /// <summary>立即同步：未配置 WebDAV 直接提示；执行期间禁用按钮防重入。</summary>
    private async void StickySyncNow_Click(object sender, RoutedEventArgs e)
    {
        var settings = App.GetService<ISettingsService>();
        if (settings == null || !settings.WebDAVEnabled)
        {
            StickyLastSyncText.Text = "请先在「数据同步」中开启并配置 WebDAV";
            return;
        }

        StickySyncNowButton.IsEnabled = false;
        try
        {
            var sync = App.GetService<StickyNoteSyncService>();
            var result = await sync.SyncAsync(CancellationToken.None);
            if (result.Ok)
            {
                settings.StickyNotesLastSync = DateTime.UtcNow;
                settings.Save();
                StickyLastSyncText.Text = $"已同步 {DateTime.Now:HH:mm}（本地 {result.LocalCount} / 云端 {result.CloudCount}）";
            }
            else
            {
                StickyLastSyncText.Text = "同步失败：" + result.Message;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Notes] 设置页立即同步异常");
            StickyLastSyncText.Text = "同步失败：" + ex.Message;
        }
        finally
        {
            StickySyncNowButton.IsEnabled = true;
        }
    }
}
