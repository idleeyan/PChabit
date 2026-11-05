using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;
using PChabit.App.ViewModels;

namespace PChabit.App.Views.Settings;

/// <summary>「AI」设置分区（3.64.0 从 SettingsPage 拆出）。
/// 自治加载/保存：宿主注入同一 SettingsViewModel 实例，
/// 事件写回 ViewModel 并 SaveSetting（等价原页面集中式 OnSettingChanged 的 AI 分支）。</summary>
public sealed partial class AiSettingsSection : UserControl
{
    private SettingsViewModel? _viewModel;
    private bool _isLoading = true;

    public AiSettingsSection()
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

    /// <summary>宿主重新进入设置页时刷新（等价原 LoadSettingsToUI 的 AI 段）。</summary>
    public void Reload() => LoadFromViewModel();

    private void BindEvents()
    {
        AiInsightsEnabledSwitch.Toggled += (s, e) => RaiseChanged("AiInsightsEnabled", AiInsightsEnabledSwitch.IsOn);
        // 输入即保存（不依赖 LostFocus，避免点按钮时未落盘 → 401）
        AiBaseUrlBox.TextChanged += (s, e) => RaiseChanged("AiBaseUrl", AiBaseUrlBox.Text);
        AiApiKeyBox.TextChanged += (s, e) => RaiseChanged("AiApiKey", AiApiKeyBox.Text);
        AiModelBox.TextChanged += (s, e) => RaiseChanged("AiModel", AiModelBox.Text);
        AiModelFastBox.TextChanged += (s, e) => RaiseChanged("AiModelFast", AiModelFastBox.Text);
        AiTimeoutSecondsBox.ValueChanged += (s, e) => RaiseChanged("AiTimeoutSeconds", e.NewValue);
        AiStrictPrivacySwitch.Toggled += (s, e) => RaiseChanged("AiStrictPrivacy", AiStrictPrivacySwitch.IsOn);
        AiAutoWeeklyInsightSwitch.Toggled += (s, e) => RaiseChanged("AiAutoWeeklyInsight", AiAutoWeeklyInsightSwitch.IsOn);
        AiModeCloudRadio.Checked += (_, _) => OnEndpointModePicked("cloud");
        AiModeLocalRadio.Checked += (_, _) => OnEndpointModePicked("local");
        AiModeDualRadio.Checked += (_, _) => OnEndpointModePicked("dual");
        AiLocalBaseUrlBox.TextChanged += (s, e) => RaiseChanged("AiLocalBaseUrl", AiLocalBaseUrlBox.Text);
        AiLocalModelBox.TextChanged += (s, e) => RaiseChanged("AiLocalModel", AiLocalModelBox.Text);
    }

    private void LoadFromViewModel()
    {
        if (_viewModel is null) return;
        _isLoading = true;
        try
        {
        AiInsightsEnabledSwitch.IsOn = _viewModel.AiInsightsEnabled;
        AiBaseUrlBox.Text = _viewModel.AiBaseUrl ?? "";
        AiApiKeyBox.Text = _viewModel.AiApiKey ?? "";
        AiModelBox.Text = _viewModel.AiModel ?? "";
        AiModelFastBox.Text = _viewModel.AiModelFast ?? "";
        AiLocalBaseUrlBox.Text = _viewModel.AiLocalBaseUrl ?? "";
        AiLocalModelBox.Text = _viewModel.AiLocalModel ?? "";
        var mode = (_viewModel.AiEndpointMode ?? "cloud").ToLowerInvariant();
        // 注意：这里绝不能改 _isLoading，否则外层保护会失效导致 Provider 覆盖配置
        AiModeCloudRadio.IsChecked = mode == "cloud";
        AiModeLocalRadio.IsChecked = mode == "local";
        AiModeDualRadio.IsChecked = mode == "dual";
        ApplyEndpointModeHint(mode);

        AiTimeoutSecondsBox.Value = _viewModel.AiTimeoutSeconds;
        AiStrictPrivacySwitch.IsOn = _viewModel.AiStrictPrivacy;
        AiAutoWeeklyInsightSwitch.IsOn = _viewModel.AiAutoWeeklyInsight;
        }
        finally
        {
            _isLoading = false;
        }
    }

    /// <summary>控件事件 → ViewModel + 落盘（等价原 OnSettingChanged 的 AI 分支）。</summary>
    private void RaiseChanged(string propertyName, object value)
    {
        if (_isLoading) return;
        Log.Information("SettingsPage: 设置变更 {PropertyName} = {Value}", propertyName, value);
        if (_viewModel is null) return;

        switch (propertyName)
        {
            case "AiInsightsEnabled":
                _viewModel.AiInsightsEnabled = (bool)value;
                break;
            case "AiBaseUrl":
                _viewModel.AiBaseUrl = value?.ToString() ?? "";
                break;
            case "AiApiKey":
                _viewModel.AiApiKey = value?.ToString() ?? "";
                break;
            case "AiModel":
                _viewModel.AiModel = value?.ToString() ?? "";
                break;
            case "AiModelFast":
                _viewModel.AiModelFast = value?.ToString() ?? "";
                break;
            case "AiEndpointMode":
                _viewModel.AiEndpointMode = value?.ToString() ?? "cloud";
                break;
            case "AiCloudBaseUrl":
                _viewModel.AiCloudBaseUrl = value?.ToString() ?? "";
                break;
            case "AiCloudApiKey":
                _viewModel.AiCloudApiKey = value?.ToString() ?? "";
                break;
            case "AiCloudModel":
                _viewModel.AiCloudModel = value?.ToString() ?? "";
                break;
            case "AiLocalBaseUrl":
                _viewModel.AiLocalBaseUrl = value?.ToString() ?? "";
                break;
            case "AiLocalModel":
                _viewModel.AiLocalModel = value?.ToString() ?? "";
                break;
            case "AiTimeoutSeconds":
                _viewModel.AiTimeoutSeconds = value is double d ? d : 300;
                break;
            case "AiStrictPrivacy":
                _viewModel.AiStrictPrivacy = (bool)value;
                break;
            case "AiAutoWeeklyInsight":
                _viewModel.AiAutoWeeklyInsight = (bool)value;
                break;
        }

        // 必须落盘：只改 ViewModel 不会写 settings.json，也不会执行 ApplyStartupSetting
        _viewModel.SaveSetting(propertyName);
    }

    private void OnEndpointModePicked(string mode)
    {
        if (_isLoading) return;
        ApplyEndpointModeHint(mode);
        RaiseChanged("AiEndpointMode", mode);
    }

    private void ApplyEndpointModeHint(string mode)
    {
        AiEndpointModeHint.Text = mode switch
        {
            "local" => "当前：本地 — 解读与追问都走 LM Studio",
            "dual" => "当前：双端点 — 解读云端 · 追问本地（推荐）",
            _ => "当前：云端 — 解读与追问都走云端 API"
        };
    }

    private void AiCopyTestResult_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = AiTestResult.Text ?? "";
            if (string.IsNullOrWhiteSpace(text))
            {
                AiTestResult.Text = "（暂无测试结果）";
                return;
            }
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            AiTestResult.Text = text + "\n（已复制到剪贴板）";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "AI 测试结果复制到剪贴板失败");
            AiTestResult.Text = $"复制失败：{ex.Message}";
        }
    }

    /// <summary>一键把界面上 AI 字段全部落盘，并探测实际请求端点。</summary>
    private async void AiTestConnection_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        try
        {
            AiTestResult.Text = "已保存，正在测试…";
            // 强制把当前控件值写入设置
            _viewModel.AiBaseUrl = AiBaseUrlBox.Text ?? "";
            _viewModel.AiApiKey = AiApiKeyBox.Text ?? "";
            _viewModel.AiModel = AiModelBox.Text ?? "";
            _viewModel.AiModelFast = AiModelFastBox.Text ?? "";
            _viewModel.AiLocalBaseUrl = AiLocalBaseUrlBox.Text ?? "";
            _viewModel.AiLocalModel = AiLocalModelBox.Text ?? "";
            _viewModel.SaveSetting("AiBaseUrl");
            _viewModel.SaveSetting("AiApiKey");
            _viewModel.SaveSetting("AiModel");
            _viewModel.SaveSetting("AiModelFast");
            _viewModel.SaveSetting("AiLocalBaseUrl");
            _viewModel.SaveSetting("AiLocalModel");

            var key = _viewModel.AiApiKey ?? "";
            var svc = App.GetService<PChabit.Infrastructure.Services.IAnalyticsAiService>();
            var cfgInfo =
                $"云端：{_viewModel.AiBaseUrl}\n" +
                $"模型：{_viewModel.AiModel}\n" +
                $"Key：{(key.Length == 0 ? "（空）" : key)}\n" +
                $"本地：{_viewModel.AiLocalBaseUrl} · {_viewModel.AiLocalModel}";
            AiTestResult.Text = cfgInfo + "\n探测中…";

            var reply = await svc.ChatFastAsync("请只回复两个字：正常", "ping");
            AiTestResult.Text = cfgInfo + $"\n✅ 连接成功。模型回复：{reply.Trim()}";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "AI 连接测试失败");
            AiTestResult.Text = (AiTestResult.Text ?? "").Split('\n').FirstOrDefault()
                                + "\n❌ 测试失败：" + ex.Message;
        }
    }
}
