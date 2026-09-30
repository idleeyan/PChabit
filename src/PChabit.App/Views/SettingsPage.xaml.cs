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

        AiInsightsEnabledSwitch.Toggled += (s, e) => OnSettingChanged("AiInsightsEnabled", AiInsightsEnabledSwitch.IsOn);
        AiBaseUrlBox.LostFocus += (s, e) => OnSettingChanged("AiBaseUrl", AiBaseUrlBox.Text);
        AiApiKeyBox.LostFocus += (s, e) => OnSettingChanged("AiApiKey", AiApiKeyBox.Password);
        AiModelBox.LostFocus += (s, e) => OnSettingChanged("AiModel", AiModelBox.Text);
        AiModelFastBox.LostFocus += (s, e) => OnSettingChanged("AiModelFast", AiModelFastBox.Text);
        AiProviderBox.SelectionChanged += (s, e) =>
        {
            if (_isLoading) return;
            var tag = (AiProviderBox.SelectedItem as ComboBoxItem)?.Tag?.ToString()
                      ?? AiProviderBox.SelectedItem?.ToString() ?? "zhipu";
            OnSettingChanged("AiProvider", tag);
            // 回填 URL/模型到输入框
            if (Enum.TryParse<PChabit.Infrastructure.Services.AiProviderKind>(tag, true, out var kind))
            {
                var p = PChabit.Infrastructure.Services.AiProviderPresets.Get(kind);
                AiBaseUrlBox.Text = p.DefaultBaseUrl;
                AiModelBox.Text = p.DefaultModel;
                OnSettingChanged("AiBaseUrl", p.DefaultBaseUrl);
                OnSettingChanged("AiModel", p.DefaultModel);
            }
        };
        AiTimeoutSecondsBox.ValueChanged += (s, e) => OnSettingChanged("AiTimeoutSeconds", e.NewValue);
        AiStrictPrivacySwitch.Toggled += (s, e) => OnSettingChanged("AiStrictPrivacy", AiStrictPrivacySwitch.IsOn);
        AiAutoWeeklyInsightSwitch.Toggled += (s, e) => OnSettingChanged("AiAutoWeeklyInsight", AiAutoWeeklyInsightSwitch.IsOn);
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
            // 每次进入设置页刷新软件信息，保证与当前程序集/CHANGELOG 一致
            LoadAboutInfo();

            await ViewModel.InitializeAsync();
            Log.Information("SettingsPage: InitializeAsync 完成");

            LoadSettingsToUI();
            _isLoading = false;
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

        AiInsightsEnabledSwitch.IsOn = ViewModel.AiInsightsEnabled;
        AiBaseUrlBox.Text = ViewModel.AiBaseUrl ?? "";
        AiApiKeyBox.Password = ViewModel.AiApiKey ?? "";
        AiModelBox.Text = ViewModel.AiModel ?? "";
        AiModelFastBox.Text = ViewModel.AiModelFast ?? "";
        AiProviderBox.Items.Clear();
        foreach (var p in PChabit.Infrastructure.Services.AiProviderPresets.All)
            AiProviderBox.Items.Add(new ComboBoxItem { Content = p.Label, Tag = p.Kind.ToString() });
        var sel = AiProviderBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => string.Equals(i.Tag?.ToString(), ViewModel.AiProvider, StringComparison.OrdinalIgnoreCase));
        if (sel != null) AiProviderBox.SelectedItem = sel;
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
}
