using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Serilog;
using PChabit.App.ViewModels;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Platform;
using PChabit.Infrastructure.Services;

namespace PChabit.App.Views;

public sealed partial class SettingsPage : Page
{
    private SettingsViewModel ViewModel { get; }
    private bool _isLoading = true;
    private string _fullChangelog = string.Empty;

    public SettingsPage()
    {
        Log.Information("SettingsPage: 构造函数开始");

        try
        {
            ViewModel = App.GetService<SettingsViewModel>();
            Log.Information("SettingsPage: ViewModel 已获取");

            InitializeComponent();
            Log.Information("SettingsPage: InitializeComponent 完成");

            AddEventHandlers();
            LoadAboutInfo();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SettingsPage: 构造函数失败");
            throw;
        }

        Log.Information("SettingsPage: 构造函数完成");
    }

    /// <summary>
    /// 从程序集/运行时/磁盘读取真实软件信息，避免写死过期文案。
    /// 每次进入设置页都会重新加载。
    /// </summary>
    private void LoadAboutInfo()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var name = asm.GetName();
            var fileVersion = FileVersionInfo.GetVersionInfo(asm.Location).FileVersion;
            var productVersion = FileVersionInfo.GetVersionInfo(asm.Location).ProductVersion;
            var infoVersion = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            var version = name.Version;
            VersionText.Text = version != null
                ? $"{version.Major}.{version.Minor}.{version.Build}"
                : "未知";

            var buildDate = File.Exists(asm.Location)
                ? File.GetLastWriteTime(asm.Location).ToString("yyyy-MM-dd HH:mm")
                : "未知";

            BuildInfoText.Text =
                $"文件版本 {fileVersion} · 程序集 {name.Version} · 构建于 {buildDate}";

            // 技术栈：以实际运行时与引用程序集为准
            var runtime = RuntimeInformation.FrameworkDescription; // e.g. .NET 10.0.12
            var os = RuntimeInformation.OSDescription;
            var arch = RuntimeInformation.ProcessArchitecture;

            var winAppSdk = ResolvePackageVersion("Microsoft.WindowsAppSDK")
                ?? ResolvePackageVersion("Microsoft.WinUI")
                ?? "2.2.0";
            var ef = ResolvePackageVersion("Microsoft.EntityFrameworkCore") ?? "10.0.0";
            var sqlite = ResolvePackageVersion("Microsoft.Data.Sqlite") ?? ResolvePackageVersion("Microsoft.EntityFrameworkCore.Sqlite") ?? "EF Core Sqlite";

            TechStackText.Text =
                $"WinUI 3 (Windows App SDK {winAppSdk}) · {runtime} · EF Core {ef} · SQLite";
            RuntimeInfoText.Text =
                $"{os} · {arch} · Process {Environment.ProcessId}";

            var dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PChabit", "Data");
            DataPathText.Text = dataDir;

            // 最新更新摘要（实时读取 CHANGELOG.md）
            LoadLatestChangelogSummary();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "加载关于信息失败");
            VersionText.Text = "未知";
            TechStackText.Text = "WinUI 3 + .NET + SQLite";
            RuntimeInfoText.Text = Environment.Version.ToString();
        }
    }

    private static string? ResolvePackageVersion(string assemblyName)
    {
        try
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a =>
                {
                    try
                    {
                        return string.Equals(a.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        return false;
                    }
                });

            if (asm == null)
            {
                // 程序集可能尚未加载；尝试从已加载依赖旁的 DLL 读取
                var baseDir = AppContext.BaseDirectory;
                var candidates = Directory.Exists(baseDir)
                    ? Directory.GetFiles(baseDir, assemblyName + ".dll")
                    : Array.Empty<string>();
                foreach (var path in candidates)
                {
                    var vi = FileVersionInfo.GetVersionInfo(path);
                    if (!string.IsNullOrEmpty(vi.FileVersion))
                    {
                        return NormalizeVersion(vi.FileVersion);
                    }
                }
                return null;
            }

            var fvi = FileVersionInfo.GetVersionInfo(asm.Location);
            return NormalizeVersion(fvi.FileVersion ?? asm.GetName().Version?.ToString());
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return "未知";
        // FileVersion 可能是 10.0.0.0，展示为 10.0.0
        var parts = version.Split('.');
        if (parts.Length >= 3)
        {
            return string.Join('.', parts.Take(3));
        }
        return version;
    }

    private void LoadLatestChangelogSummary()
    {
        try
        {
            var changelogPath = Path.Combine(AppContext.BaseDirectory, "CHANGELOG.md");
            if (!File.Exists(changelogPath))
            {
                LatestChangelogTitleText.Text = "更新日志";
                LatestChangelogBodyText.Text = "未找到 CHANGELOG.md。";
                return;
            }

            _fullChangelog = File.ReadAllText(changelogPath);

            // 取第一个正式版本小节（跳过 [Unreleased]）
            var sections = _fullChangelog.Split("## ", StringSplitOptions.None);
            string? title = null;
            string? body = null;

            foreach (var raw in sections.Skip(1))
            {
                var section = raw.Trim();
                if (section.StartsWith("[Unreleased]", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var newline = section.IndexOf('\n');
                if (newline < 0)
                {
                    title = section.Trim();
                    body = string.Empty;
                }
                else
                {
                    title = section[..newline].Trim();
                    body = section[(newline + 1)..].Trim();
                }
                break;
            }

            LatestChangelogTitleText.Text = string.IsNullOrEmpty(title) ? "最新更新" : $"最新更新 · {title}";

            if (string.IsNullOrEmpty(body))
            {
                LatestChangelogBodyText.Text = "暂无更新说明。";
            }
            else
            {
                // 去掉 Markdown 标记，显示摘要
                var plain = System.Text.RegularExpressions.Regex.Replace(body, @"^#+\s*", "", System.Text.RegularExpressions.RegexOptions.Multiline);
                plain = System.Text.RegularExpressions.Regex.Replace(plain, @"[*`\-]+", "");
                plain = System.Text.RegularExpressions.Regex.Replace(plain, @"\n{3,}", "\n\n").Trim();
                if (plain.Length > 400)
                {
                    plain = plain[..400] + "…";
                }
                LatestChangelogBodyText.Text = plain.Replace("\r\n", "\n");
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "读取更新日志摘要失败");
            LatestChangelogTitleText.Text = "更新日志";
            LatestChangelogBodyText.Text = "读取更新日志失败。";
        }
    }

    private void AddEventHandlers()
    {
        StartWithWindowsSwitch.Toggled += (s, e) => OnSettingChanged("StartWithWindows", StartWithWindowsSwitch.IsOn);
        MinimizeToTraySwitch.Toggled += (s, e) => OnSettingChanged("MinimizeToTray", MinimizeToTraySwitch.IsOn);
        ShowNotificationsSwitch.Toggled += (s, e) => OnSettingChanged("ShowNotifications", ShowNotificationsSwitch.IsOn);
        AutoStartMonitoringSwitch.Toggled += (s, e) => OnSettingChanged("AutoStartMonitoring", AutoStartMonitoringSwitch.IsOn);

        MonitoringIntervalBox.ValueChanged += (s, e) => OnSettingChanged("MonitoringInterval", (int)e.NewValue);
        IdleThresholdBox.ValueChanged += (s, e) => OnSettingChanged("IdleThreshold", (int)e.NewValue);

        TrackKeyboardSwitch.Toggled += (s, e) => OnSettingChanged("TrackKeyboard", TrackKeyboardSwitch.IsOn);
        TrackMouseSwitch.Toggled += (s, e) => OnSettingChanged("TrackMouse", TrackMouseSwitch.IsOn);
        TrackWebBrowsingSwitch.Toggled += (s, e) => OnSettingChanged("TrackWebBrowsing", TrackWebBrowsingSwitch.IsOn);

        ThemeComboBox.SelectionChanged += (s, e) =>
        {
            if (!_isLoading && ThemeComboBox.SelectedItem is ComboBoxItem item)
                OnSettingChanged("SelectedThemeKey", item.Tag?.ToString() ?? "system");
        };

        LanguageComboBox.SelectionChanged += (s, e) =>
        {
            if (!_isLoading && LanguageComboBox.SelectedItem is ComboBoxItem item)
                OnSettingChanged("SelectedLanguageKey", item.Tag?.ToString() ?? "zh-CN");
        };

        ViewChangelogButton.Click += OnViewChangelogClick;
        TaskbarEnabledSwitch.Toggled += (s, e) => OnSettingChanged("TaskbarEnabled", TaskbarEnabledSwitch.IsOn);

        DailyUsageGoalHoursBox.ValueChanged += (s, e) => OnSettingChanged("DailyUsageGoalHours", e.NewValue);

        TaskbarCpuSwitch.Toggled += (s, e) => OnSettingChanged("TaskbarShowCpu", TaskbarCpuSwitch.IsOn);
        TaskbarMemorySwitch.Toggled += (s, e) => OnSettingChanged("TaskbarShowMemory", TaskbarMemorySwitch.IsOn);
        TaskbarGpuSwitch.Toggled += (s, e) => OnSettingChanged("TaskbarShowGpu", TaskbarGpuSwitch.IsOn);
        TaskbarNetSwitch.Toggled += (s, e) => OnSettingChanged("TaskbarShowNet", TaskbarNetSwitch.IsOn);
        TaskbarDiskSwitch.Toggled += (s, e) => OnSettingChanged("TaskbarShowDisk", TaskbarDiskSwitch.IsOn);
        TaskbarTempSwitch.Toggled += (s, e) => OnSettingChanged("TaskbarShowTemp", TaskbarTempSwitch.IsOn);
        TaskbarUsageSwitch.Toggled += (s, e) => OnSettingChanged("TaskbarShowUsage", TaskbarUsageSwitch.IsOn);

        // 桌面悬浮插件（3.23.0）
        DesktopWidgetEnabledSwitch.Toggled += (s, e) => OnSettingChanged("DesktopWidgetEnabled", DesktopWidgetEnabledSwitch.IsOn);
        DesktopCpuSwitch.Toggled += (s, e) => OnSettingChanged("DesktopWidgetShowCpu", DesktopCpuSwitch.IsOn);
        DesktopMemorySwitch.Toggled += (s, e) => OnSettingChanged("DesktopWidgetShowMemory", DesktopMemorySwitch.IsOn);
        DesktopGpuSwitch.Toggled += (s, e) => OnSettingChanged("DesktopWidgetShowGpu", DesktopGpuSwitch.IsOn);
        DesktopVramSwitch.Toggled += (s, e) => OnSettingChanged("DesktopWidgetShowVram", DesktopVramSwitch.IsOn);
        DesktopNetSwitch.Toggled += (s, e) => OnSettingChanged("DesktopWidgetShowNet", DesktopNetSwitch.IsOn);
        DesktopDiskSwitch.Toggled += (s, e) => OnSettingChanged("DesktopWidgetShowDisk", DesktopDiskSwitch.IsOn);
        DesktopUsageSwitch.Toggled += (s, e) => OnSettingChanged("DesktopWidgetShowUsage", DesktopUsageSwitch.IsOn);
        DesktopTopmostSwitch.Toggled += (s, e) => OnSettingChanged("DesktopWidgetTopmost", DesktopTopmostSwitch.IsOn);
        DesktopClickThroughSwitch.Toggled += (s, e) => OnSettingChanged("DesktopWidgetClickThrough", DesktopClickThroughSwitch.IsOn);
        DesktopClampSwitch.Toggled += (s, e) => OnSettingChanged("DesktopWidgetClampToScreen", DesktopClampSwitch.IsOn);

        // 便签（3.24.0）
        StickyEnabledSwitch.Toggled += (s, e) => OnSettingChanged("StickyNotesEnabled", StickyEnabledSwitch.IsOn);
        StickyQuickDismissSwitch.Toggled += (s, e) => OnSettingChanged("StickyNotesQuickDismissOnFocusLost", StickyQuickDismissSwitch.IsOn);
        StickyRetentionBox.ValueChanged += (s, e) => OnSettingChanged("StickyNotesRetentionDays", (int)e.NewValue);
        StickySyncSwitch.Toggled += (s, e) => OnSettingChanged("StickyNotesSyncEnabled", StickySyncSwitch.IsOn);

        AiInsightsEnabledSwitch.Toggled += (s, e) => OnSettingChanged("AiInsightsEnabled", AiInsightsEnabledSwitch.IsOn);
        // 输入即保存（不依赖 LostFocus，避免点按钮时未落盘 → 401）
        AiBaseUrlBox.TextChanged += (s, e) => OnSettingChanged("AiBaseUrl", AiBaseUrlBox.Text);
        AiApiKeyBox.TextChanged += (s, e) => OnSettingChanged("AiApiKey", AiApiKeyBox.Text);
        AiModelBox.TextChanged += (s, e) => OnSettingChanged("AiModel", AiModelBox.Text);
        AiModelFastBox.TextChanged += (s, e) => OnSettingChanged("AiModelFast", AiModelFastBox.Text);
        AiTimeoutSecondsBox.ValueChanged += (s, e) => OnSettingChanged("AiTimeoutSeconds", e.NewValue);
        AiStrictPrivacySwitch.Toggled += (s, e) => OnSettingChanged("AiStrictPrivacy", AiStrictPrivacySwitch.IsOn);
        AiAutoWeeklyInsightSwitch.Toggled += (s, e) => OnSettingChanged("AiAutoWeeklyInsight", AiAutoWeeklyInsightSwitch.IsOn);
        AiModeCloudRadio.Checked += (_, _) => OnEndpointModePicked("cloud");
        AiModeLocalRadio.Checked += (_, _) => OnEndpointModePicked("local");
        AiModeDualRadio.Checked += (_, _) => OnEndpointModePicked("dual");
        AiLocalBaseUrlBox.TextChanged += (s, e) => OnSettingChanged("AiLocalBaseUrl", AiLocalBaseUrlBox.Text);
        AiLocalModelBox.TextChanged += (s, e) => OnSettingChanged("AiLocalModel", AiLocalModelBox.Text);
    }

    private void OnEndpointModePicked(string mode)
    {
        if (_isLoading) return;
        ApplyEndpointModeHint(mode);
        OnSettingChanged("AiEndpointMode", mode);
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
            AiTestResult.Text = $"复制失败：{ex.Message}";
        }
    }

    /// <summary>一键把界面上 AI 字段全部落盘，并探测实际请求端点。</summary>
    private async void AiTestConnection_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AiTestResult.Text = "已保存，正在测试…";
            // 强制把当前控件值写入设置
            ViewModel.AiBaseUrl = AiBaseUrlBox.Text ?? "";
            ViewModel.AiApiKey = AiApiKeyBox.Text ?? "";
            ViewModel.AiModel = AiModelBox.Text ?? "";
            ViewModel.AiModelFast = AiModelFastBox.Text ?? "";
            ViewModel.AiLocalBaseUrl = AiLocalBaseUrlBox.Text ?? "";
            ViewModel.AiLocalModel = AiLocalModelBox.Text ?? "";
            ViewModel.SaveSetting("AiBaseUrl");
            ViewModel.SaveSetting("AiApiKey");
            ViewModel.SaveSetting("AiModel");
            ViewModel.SaveSetting("AiModelFast");
            ViewModel.SaveSetting("AiLocalBaseUrl");
            ViewModel.SaveSetting("AiLocalModel");

            var key = ViewModel.AiApiKey ?? "";
            var svc = App.GetService<PChabit.Infrastructure.Services.IAnalyticsAiService>();
            var cfgInfo =
                $"云端：{ViewModel.AiBaseUrl}\n" +
                $"模型：{ViewModel.AiModel}\n" +
                $"Key：{(key.Length == 0 ? "（空）" : key)}\n" +
                $"本地：{ViewModel.AiLocalBaseUrl} · {ViewModel.AiLocalModel}";
            AiTestResult.Text = cfgInfo + "\n探测中…";

            var reply = await svc.ChatFastAsync("请只回复两个字：正常", "ping");
            AiTestResult.Text = cfgInfo + $"\n✅ 连接成功。模型回复：{reply.Trim()}";
        }
        catch (Exception ex)
        {
            AiTestResult.Text = (AiTestResult.Text ?? "").Split('\n').FirstOrDefault()
                                + "\n❌ 测试失败：" + ex.Message;
        }
    }

    private async void OnViewChangelogClick(object sender, RoutedEventArgs e)
    {
        var senderButton = sender as Button;
        var originalContent = senderButton?.Content;
        if (senderButton != null)
        {
            senderButton.IsEnabled = false;
        }

        try
        {
            // 显示加载中的对话框
            var loadingDialog = new ContentDialog
            {
                Title = "更新日志",
                Content = new StackPanel
                {
                    Spacing = 12,
                    HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center,
                    Children =
                    {
                        new ProgressRing
                        {
                            IsActive = true,
                            Width = 32,
                            Height = 32
                        },
                        new TextBlock
                        {
                            Text = "正在加载更新日志...",
                            FontSize = 13,
                            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center
                        }
                    }
                },
                XamlRoot = XamlRoot
            };

            _ = loadingDialog.ShowAsync();

            // 每次打开都从磁盘重读，保证与 CHANGELOG.md 实时一致
            var content = await Task.Run(async () =>
            {
                try
                {
                    var changelogPath = Path.Combine(AppContext.BaseDirectory, "CHANGELOG.md");
                    if (File.Exists(changelogPath))
                    {
                        var text = await File.ReadAllTextAsync(changelogPath);
                        return text;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "读取 CHANGELOG.md 失败");
                }

                return string.IsNullOrEmpty(_fullChangelog)
                    ? "更新日志文件未找到。"
                    : _fullChangelog;
            });

            _fullChangelog = content;
            LoadLatestChangelogSummary();

            loadingDialog.Hide();
            await Task.Yield();

            var contentPanel = new StackPanel { Spacing = 12 };
            var sections = content.Split("## ");
            foreach (var section in sections)
            {
                if (string.IsNullOrWhiteSpace(section)) continue;

                var sectionText = section.StartsWith("#") ? section.TrimEnd() : "## " + section.TrimEnd();
                var textBlock = new TextBlock
                {
                    Text = sectionText,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 13,
                    LineHeight = 20,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas")
                };
                contentPanel.Children.Add(textBlock);
            }

            var dialog = new ContentDialog
            {
                Title = "更新日志",
                Content = new ScrollViewer
                {
                    Content = contentPanel,
                    MaxHeight = 500
                },
                CloseButtonText = "关闭",
                XamlRoot = XamlRoot
            };

            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "显示更新日志失败");
        }
        finally
        {
            if (senderButton != null)
            {
                senderButton.IsEnabled = true;
                if (originalContent != null)
                {
                    senderButton.Content = originalContent;
                }
            }
        }
    }

    private void OnSettingChanged(string propertyName, object value)
    {
        if (_isLoading) return;

        Log.Information("SettingsPage: 设置变更 {PropertyName} = {Value}", propertyName, value);

        switch (propertyName)
        {
            case "StartWithWindows":
                ViewModel.StartWithWindows = (bool)value;
                break;
            case "MinimizeToTray":
                ViewModel.MinimizeToTray = (bool)value;
                break;
            case "ShowNotifications":
                ViewModel.ShowNotifications = (bool)value;
                break;
            case "AutoStartMonitoring":
                ViewModel.AutoStartMonitoring = (bool)value;
                break;
            case "MonitoringInterval":
                ViewModel.MonitoringInterval = (int)value;
                break;
            case "IdleThreshold":
                ViewModel.IdleThreshold = (int)value;
                break;
            case "TrackKeyboard":
                ViewModel.TrackKeyboard = (bool)value;
                break;
            case "TrackMouse":
                ViewModel.TrackMouse = (bool)value;
                break;
            case "TrackWebBrowsing":
                ViewModel.TrackWebBrowsing = (bool)value;
                break;
            case "SelectedThemeKey":
                ViewModel.SelectedThemeKey = (string)value;
                break;
            case "SelectedLanguageKey":
                ViewModel.SelectedLanguageKey = (string)value;
                break;
            case "TaskbarEnabled":
                ViewModel.TaskbarEnabled = (bool)value;
                break;
            case "DailyUsageGoalHours":
                ViewModel.DailyUsageGoalHours = (double)value;
                break;
            case "TaskbarShowCpu":
                ViewModel.TaskbarShowCpu = (bool)value;
                break;
            case "TaskbarShowMemory":
                ViewModel.TaskbarShowMemory = (bool)value;
                break;
            case "TaskbarShowGpu":
                ViewModel.TaskbarShowGpu = (bool)value;
                break;
            case "TaskbarShowNet":
                ViewModel.TaskbarShowNet = (bool)value;
                break;
            case "TaskbarShowDisk":
                ViewModel.TaskbarShowDisk = (bool)value;
                break;
            case "TaskbarShowTemp":
                ViewModel.TaskbarShowTemp = (bool)value;
                break;
            case "TaskbarShowUsage":
                ViewModel.TaskbarShowUsage = (bool)value;
                break;
            case "DesktopWidgetEnabled":
                ViewModel.DesktopWidgetEnabled = (bool)value;
                break;
            case "DesktopWidgetShowCpu":
                ViewModel.DesktopWidgetShowCpu = (bool)value;
                break;
            case "DesktopWidgetShowMemory":
                ViewModel.DesktopWidgetShowMemory = (bool)value;
                break;
            case "DesktopWidgetShowGpu":
                ViewModel.DesktopWidgetShowGpu = (bool)value;
                break;
            case "DesktopWidgetShowVram":
                ViewModel.DesktopWidgetShowVram = (bool)value;
                break;
            case "DesktopWidgetShowNet":
                ViewModel.DesktopWidgetShowNet = (bool)value;
                break;
            case "DesktopWidgetShowDisk":
                ViewModel.DesktopWidgetShowDisk = (bool)value;
                break;
            case "DesktopWidgetShowUsage":
                ViewModel.DesktopWidgetShowUsage = (bool)value;
                break;
            case "DesktopWidgetTopmost":
                ViewModel.DesktopWidgetTopmost = (bool)value;
                break;
            case "DesktopWidgetClickThrough":
                ViewModel.DesktopWidgetClickThrough = (bool)value;
                break;
            case "DesktopWidgetClampToScreen":
                ViewModel.DesktopWidgetClampToScreen = (bool)value;
                break;
            case "StickyNotesEnabled":
                ViewModel.StickyNotesEnabled = (bool)value;
                break;
            case "StickyNotesQuickDismissOnFocusLost":
                ViewModel.StickyNotesQuickDismissOnFocusLost = (bool)value;
                break;
            case "StickyNotesRetentionDays":
                ViewModel.StickyNotesRetentionDays = (int)value;
                break;
            case "StickyNotesSyncEnabled":
                ViewModel.StickyNotesSyncEnabled = (bool)value;
                break;
            case "AiInsightsEnabled":
                ViewModel.AiInsightsEnabled = (bool)value;
                break;
            case "AiBaseUrl":
                ViewModel.AiBaseUrl = value?.ToString() ?? "";
                break;
            case "AiApiKey":
                ViewModel.AiApiKey = value?.ToString() ?? "";
                break;
            case "AiModel":
                ViewModel.AiModel = value?.ToString() ?? "";
                break;
            case "AiModelFast":
                ViewModel.AiModelFast = value?.ToString() ?? "";
                break;
            case "AiEndpointMode":
                ViewModel.AiEndpointMode = value?.ToString() ?? "cloud";
                break;
            case "AiCloudBaseUrl":
                ViewModel.AiCloudBaseUrl = value?.ToString() ?? "";
                break;
            case "AiCloudApiKey":
                ViewModel.AiCloudApiKey = value?.ToString() ?? "";
                break;
            case "AiCloudModel":
                ViewModel.AiCloudModel = value?.ToString() ?? "";
                break;
            case "AiLocalBaseUrl":
                ViewModel.AiLocalBaseUrl = value?.ToString() ?? "";
                break;
            case "AiLocalModel":
                ViewModel.AiLocalModel = value?.ToString() ?? "";
                break;
            case "AiTimeoutSeconds":
                ViewModel.AiTimeoutSeconds = value is double d ? d : 300;
                break;
            case "AiStrictPrivacy":
                ViewModel.AiStrictPrivacy = (bool)value;
                break;
            case "AiAutoWeeklyInsight":
                ViewModel.AiAutoWeeklyInsight = (bool)value;
                break;

        }

        // 必须落盘：只改 ViewModel 不会写 settings.json，也不会执行 ApplyStartupSetting
        ViewModel.SaveSetting(propertyName);
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        Log.Information("SettingsPage: OnNavigatedTo 开始");
        base.OnNavigatedTo(e);

        try
        {
            LoadAboutInfo();

            _isLoading = true;
            try
            {
                await ViewModel.InitializeAsync();
                Log.Information("SettingsPage: InitializeAsync 完成");
                LoadSettingsToUI();
            }
            finally
            {
                _isLoading = false;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SettingsPage: InitializeAsync 失败");
        }
    }

    private void LoadSettingsToUI()
    {
        Log.Information("SettingsPage: 加载设置到 UI");

        StartWithWindowsSwitch.IsOn = ViewModel.StartWithWindows;
        MinimizeToTraySwitch.IsOn = ViewModel.MinimizeToTray;
        ShowNotificationsSwitch.IsOn = ViewModel.ShowNotifications;
        AutoStartMonitoringSwitch.IsOn = ViewModel.AutoStartMonitoring;

        MonitoringIntervalBox.Value = ViewModel.MonitoringInterval;
        IdleThresholdBox.Value = ViewModel.IdleThreshold;

        TrackKeyboardSwitch.IsOn = ViewModel.TrackKeyboard;
        TrackMouseSwitch.IsOn = ViewModel.TrackMouse;
        TrackWebBrowsingSwitch.IsOn = ViewModel.TrackWebBrowsing;

        ThemeComboBox.Items.Clear();
        TaskbarEnabledSwitch.IsOn = ViewModel.TaskbarEnabled;

        DailyUsageGoalHoursBox.Value = ViewModel.DailyUsageGoalHours;

        TaskbarCpuSwitch.IsOn = ViewModel.TaskbarShowCpu;
        TaskbarMemorySwitch.IsOn = ViewModel.TaskbarShowMemory;
        TaskbarGpuSwitch.IsOn = ViewModel.TaskbarShowGpu;
        TaskbarNetSwitch.IsOn = ViewModel.TaskbarShowNet;
        TaskbarDiskSwitch.IsOn = ViewModel.TaskbarShowDisk;
        TaskbarTempSwitch.IsOn = ViewModel.TaskbarShowTemp;
        TaskbarUsageSwitch.IsOn = ViewModel.TaskbarShowUsage;

        // 桌面悬浮插件（3.23.0）
        DesktopWidgetEnabledSwitch.IsOn = ViewModel.DesktopWidgetEnabled;
        DesktopCpuSwitch.IsOn = ViewModel.DesktopWidgetShowCpu;
        DesktopMemorySwitch.IsOn = ViewModel.DesktopWidgetShowMemory;
        DesktopGpuSwitch.IsOn = ViewModel.DesktopWidgetShowGpu;
        DesktopVramSwitch.IsOn = ViewModel.DesktopWidgetShowVram;
        DesktopNetSwitch.IsOn = ViewModel.DesktopWidgetShowNet;
        DesktopDiskSwitch.IsOn = ViewModel.DesktopWidgetShowDisk;
        DesktopUsageSwitch.IsOn = ViewModel.DesktopWidgetShowUsage;
        DesktopTopmostSwitch.IsOn = ViewModel.DesktopWidgetTopmost;
        DesktopClickThroughSwitch.IsOn = ViewModel.DesktopWidgetClickThrough;
        DesktopClampSwitch.IsOn = ViewModel.DesktopWidgetClampToScreen;

        // 便签（3.24.0）
        StickyEnabledSwitch.IsOn = ViewModel.StickyNotesEnabled;
        StickyHotkeyNewBox.Text = ViewModel.StickyNotesHotkeyNew;
        StickyHotkeyBoardBox.Text = ViewModel.StickyNotesHotkeyBoard;
        StickyQuickDismissSwitch.IsOn = ViewModel.StickyNotesQuickDismissOnFocusLost;
        StickyRetentionBox.Value = ViewModel.StickyNotesRetentionDays;
        StickySyncSwitch.IsOn = ViewModel.StickyNotesSyncEnabled;
        StickyLastSyncText.Text = ViewModel.StickyNotesLastSyncText;

        AiInsightsEnabledSwitch.IsOn = ViewModel.AiInsightsEnabled;
        AiBaseUrlBox.Text = ViewModel.AiBaseUrl ?? "";
        AiApiKeyBox.Text = ViewModel.AiApiKey ?? "";
        AiModelBox.Text = ViewModel.AiModel ?? "";
        AiModelFastBox.Text = ViewModel.AiModelFast ?? "";
        AiLocalBaseUrlBox.Text = ViewModel.AiLocalBaseUrl ?? "";
        AiLocalModelBox.Text = ViewModel.AiLocalModel ?? "";
        var mode = (ViewModel.AiEndpointMode ?? "cloud").ToLowerInvariant();
        // 注意：这里绝不能改 _isLoading，否则外层保护会失效导致 Provider 覆盖配置
        AiModeCloudRadio.IsChecked = mode == "cloud";
        AiModeLocalRadio.IsChecked = mode == "local";
        AiModeDualRadio.IsChecked = mode == "dual";
        ApplyEndpointModeHint(mode);

        AiTimeoutSecondsBox.Value = ViewModel.AiTimeoutSeconds;
        AiStrictPrivacySwitch.IsOn = ViewModel.AiStrictPrivacy;
        AiAutoWeeklyInsightSwitch.IsOn = ViewModel.AiAutoWeeklyInsight;


        ThemeComboBox.Items.Add(new ComboBoxItem { Content = "系统默认", Tag = "system" });
        ThemeComboBox.Items.Add(new ComboBoxItem { Content = "浅色", Tag = "light" });
        ThemeComboBox.Items.Add(new ComboBoxItem { Content = "深色", Tag = "dark" });
        ThemeComboBox.SelectedIndex = ViewModel.SelectedThemeKey switch
        {
            "light" => 1,
            "dark" => 2,
            _ => 0
        };

        LanguageComboBox.Items.Clear();
        LanguageComboBox.Items.Add(new ComboBoxItem { Content = "简体中文", Tag = "zh-CN" });
        LanguageComboBox.Items.Add(new ComboBoxItem { Content = "English", Tag = "en-US" });
        LanguageComboBox.SelectedIndex = ViewModel.SelectedLanguageKey == "en-US" ? 1 : 0;

        Log.Information("SettingsPage: 设置已加载到 UI");
    }

    // ===== 便签（3.24.0）=====

    /// <summary>热键失焦校验：解析 + 真实注册探测；失败还原旧值并红字提示，成功规范化落盘。</summary>
    private void StickyHotkeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        bool isNew = ReferenceEquals(sender, StickyHotkeyNewBox);
        var box = isNew ? StickyHotkeyNewBox : StickyHotkeyBoardBox;
        var error = isNew ? StickyHotkeyNewError : StickyHotkeyBoardError;
        var propName = isNew ? "StickyNotesHotkeyNew" : "StickyNotesHotkeyBoard";
        var saved = isNew ? ViewModel.StickyNotesHotkeyNew : ViewModel.StickyNotesHotkeyBoard;
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
            if (ViewModel.StickyNotesEnabled)
                App.GetService<GlobalHotkeyService>()?.Unregister(isNew ? App.HotkeyActionNew : App.HotkeyActionBoard);
            if (isNew) ViewModel.StickyNotesHotkeyNew = "";
            else ViewModel.StickyNotesHotkeyBoard = "";
            ViewModel.SaveSetting(propName);
            return;
        }

        if (!GlobalHotkeyService.TryParse(gesture, out _, out _, out string normalized))
        {
            ShowError("格式错误：需 Ctrl/Alt/Win 之一 + 字母/数字/F1-F12");
            box.Text = saved;
            return;
        }

        if (ViewModel.StickyNotesEnabled)
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
        if (isNew) ViewModel.StickyNotesHotkeyNew = normalized;
        else ViewModel.StickyNotesHotkeyBoard = normalized;
        ViewModel.SaveSetting(propName);
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
