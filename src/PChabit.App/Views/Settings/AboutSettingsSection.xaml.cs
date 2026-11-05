using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;

namespace PChabit.App.Views.Settings;

/// <summary>「关于」设置分区（3.64.0 从 SettingsPage 拆出）。
/// 只读展示版本 / 技术栈 / 更新日志，无双向绑定，不与页面共享设置加载逻辑。</summary>
public sealed partial class AboutSettingsSection : UserControl
{
    private string _fullChangelog = string.Empty;

    public AboutSettingsSection()
    {
        InitializeComponent();
        ViewChangelogButton.Click += OnViewChangelogClick;
    }

    /// <summary>由宿主 SettingsPage 在构造与 OnNavigatedTo 时调用（等价原 LoadAboutInfo 调用点）。</summary>
    public void Refresh() => LoadAboutInfo();

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
                    catch (Exception ex)
                    {
                        // 预期：个别动态程序集名不可读，跳过
                        Log.Debug(ex, "读取程序集名失败，跳过");
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
        catch (Exception ex)
        {
            // 预期：依赖程序集未加载且 DLL 探测失败，「关于」页显示"未知"
            Log.Debug(ex, "解析 {Assembly} 版本失败", assemblyName);
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
}
