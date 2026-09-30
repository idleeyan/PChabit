using FluentAssertions;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Services;
using Xunit;

namespace PChabit.Tests.Analysis;

public class AiEndpointResolveTests
{
    private sealed class FakeSettings : ISettingsService
    {
        public string AiBaseUrl { get; set; } = "https://api.openai.com/v1";
        public string AiApiKey { get; set; } = "sk-cloud";
        public string AiModel { get; set; } = "gpt-4o-mini";
        public string AiModelFast { get; set; } = "";
        public string AiLocalBaseUrl { get; set; } = "http://127.0.0.1:1234/v1";
        public string AiLocalModel { get; set; } = "qwen2.5-7b";
        public string AiCloudBaseUrl { get; set; } = "";
        public string AiCloudApiKey { get; set; } = "";
        public string AiCloudModel { get; set; } = "";
        public string AiEndpointMode { get; set; } = "dual";
        public bool AiStrictPrivacy { get; set; }
        public bool AiAutoWeeklyInsight { get; set; }
        public bool AiInsightsEnabled { get; set; } = true;
        public int AiTimeoutSeconds { get; set; } = 300;
        public string AiProvider { get; set; } = "custom";

        // 其余接口成员用不到
        public bool StartWithWindows { get; set; }
        public bool MinimizeToTray { get; set; }
        public bool ShowNotifications { get; set; }
        public bool AutoStartMonitoring { get; set; }
        public int MonitoringInterval { get; set; }
        public int IdleThreshold { get; set; }
        public bool TrackKeyboard { get; set; }
        public bool TrackMouse { get; set; }
        public bool TrackWebBrowsing { get; set; }
        public bool AnonymizeData { get; set; }
        public int RetentionDays { get; set; }
        public string WebSocketPort { get; set; } = "0";
        public string CurrentTheme { get; set; } = "system";
        public string CurrentLanguage { get; set; } = "zh-CN";
        public string WebDAVUrl { get; set; } = "";
        public string WebDAVUsername { get; set; } = "";
        public string WebDAVPassword { get; set; } = "";
        public bool WebDAVEnabled { get; set; }
        public DateTime? WebDAVLastSync { get; set; }
        public string BackupPath { get; set; } = "";
        public bool AutoBackupEnabled { get; set; }
        public int AutoBackupIntervalHours { get; set; }
        public int MaxBackupCount { get; set; }
        public int DataRetentionDays { get; set; } = 90;
        public bool AutoCleanupEnabled { get; set; }
        public bool ArchiveBeforeCleanup { get; set; }
        public int MaxCloudBackupCount { get; set; }
        public bool BrowserSyncEnabled { get; set; }
        public bool BrowserHistoryIngestEnabled { get; set; }
        public int BrowserSyncIntervalMinutes { get; set; }
        public bool TaskbarEnabled { get; set; }
        public bool TaskbarShowCpu { get; set; }
        public bool TaskbarShowMemory { get; set; }
        public bool TaskbarShowGpu { get; set; }
        public bool TaskbarShowNet { get; set; }
        public bool TaskbarShowDisk { get; set; }
        public bool TaskbarShowTemp { get; set; }
        public bool TaskbarShowUsage { get; set; }
        public double DailyUsageGoalHours { get; set; } = 6;
        public bool BrowserAutoPush { get; set; }
        public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;
        public Task LoadAsync() => Task.CompletedTask;
        public Task SaveAsync() => Task.CompletedTask;
        public void Load() { }
        public void Save() { }
        public void ResetToDefaults() { }
    }

    [Fact]
    public void DualMode_InsightUsesCloud_FollowUpUsesLocal()
    {
        var s = new FakeSettings { AiEndpointMode = "dual" };
        var c = new OpenAiCompatibleChatClient(s);
        var insight = c.Resolve(AiEndpointSlot.Auto, isInsight: true);
        var chat = c.Resolve(AiEndpointSlot.Auto, isInsight: false);
        insight.Label.Should().Be("云端");
        insight.Model.Should().Be("gpt-4o-mini");
        chat.Label.Should().Be("本地");
        chat.Model.Should().Be("qwen2.5-7b");
    }

    [Fact]
    public void LocalMode_UsesLocal()
    {
        var s = new FakeSettings { AiEndpointMode = "local" };
        var c = new OpenAiCompatibleChatClient(s);
        c.Resolve(AiEndpointSlot.Auto, true).Label.Should().Be("本地");
        c.Resolve(AiEndpointSlot.Auto, false).Label.Should().Be("本地");
    }

    [Fact]
    public void CloudOverrides_ReplacePrimary()
    {
        var s = new FakeSettings
        {
            AiCloudBaseUrl = "https://api.deepseek.com",
            AiCloudApiKey = "sk-ds",
            AiCloudModel = "deepseek-chat"
        };
        var c = new OpenAiCompatibleChatClient(s);
        var cloud = c.Resolve(AiEndpointSlot.Cloud, true);
        cloud.Model.Should().Be("deepseek-chat");
        cloud.BaseUrl.Should().Contain("deepseek");
    }

    [Fact]
    public void LocalWithoutKey_IsUsable()
    {
        var cfg = new AiEndpointConfig("http://127.0.0.1:1234/v1", "", "m", "本地");
        cfg.IsUsable.Should().BeTrue();
    }

    [Fact]
    public void CloudWithoutKey_NotUsable()
    {
        var cfg = new AiEndpointConfig("https://api.openai.com/v1", "", "gpt", "云端");
        cfg.IsUsable.Should().BeFalse();
    }

    [Fact]
    public void DualMode_CloudMissing_FallsBackLocal()
    {
        var s = new FakeSettings
        {
            AiEndpointMode = "dual",
            AiBaseUrl = "",
            AiApiKey = "",
            AiModel = "",
            AiCloudBaseUrl = "",
            AiCloudApiKey = "",
            AiCloudModel = ""
        };
        var c = new OpenAiCompatibleChatClient(s);
        c.Resolve(AiEndpointSlot.Auto, true).Label.Should().Be("本地");
    }

    [Fact]
    public void ExtractContent_ReadsMessageContent()
    {
        var json = """{"choices":[{"message":{"role":"assistant","content":"你好"}}]}""";
        OpenAiCompatibleChatClient.ExtractContent(json).Should().Be("你好");
    }

    [Fact]
    public void ExtractContent_FallsBackToReasoning()
    {
        var json = """{"choices":[{"message":{"role":"assistant","content":"","reasoning_content":"思考结果"}}]}""";
        OpenAiCompatibleChatClient.ExtractContent(json).Should().Be("思考结果");
    }

    [Fact]
    public void CloudSlot_IndependentUrl_RequiresOwnModel()
    {
        // 只填云端 URL、删掉云端模型 → 不得拿主配置模型去凑，避免 401/串模型
        var s = new FakeSettings
        {
            AiBaseUrl = "https://api.openai.com/v1",
            AiApiKey = "sk-primary",
            AiModel = "gpt-4o-mini",
            AiCloudBaseUrl = "https://api.deepseek.com",
            AiCloudApiKey = "sk-ds",
            AiCloudModel = "" // 用户删了云端模型
        };
        var c = new OpenAiCompatibleChatClient(s);
        var cloud = c.Resolve(AiEndpointSlot.Cloud, true);
        cloud.IsUsable.Should().BeFalse(); // 缺模型
        cloud.Model.Should().Be("");
    }

    [Fact]
    public void CloudSlot_NoIndependentUrl_UsesPrimaryGroup()
    {
        var s = new FakeSettings
        {
            AiBaseUrl = "https://api.openai.com/v1",
            AiApiKey = "sk-primary",
            AiModel = "gpt-4o-mini",
            AiCloudBaseUrl = "",
            AiCloudModel = ""
        };
        var c = new OpenAiCompatibleChatClient(s);
        var cloud = c.Resolve(AiEndpointSlot.Cloud, true);
        cloud.IsUsable.Should().BeTrue();
        cloud.Model.Should().Be("gpt-4o-mini");
        cloud.ApiKey.Should().Be("sk-primary");
    }

    [Fact]
    public void Dual_CloudIncomplete_FallsBackLocal_Not401()
    {
        var s = new FakeSettings
        {
            AiEndpointMode = "dual",
            AiBaseUrl = "",
            AiApiKey = "",
            AiModel = "",
            AiCloudBaseUrl = "https://api.deepseek.com",
            AiCloudApiKey = "",
            AiCloudModel = "",
            AiLocalBaseUrl = "http://127.0.0.1:1234/v1",
            AiLocalModel = "qwen"
        };
        var c = new OpenAiCompatibleChatClient(s);
        var insight = c.Resolve(AiEndpointSlot.Auto, true);
        insight.Label.Should().Be("本地");
        insight.IsUsable.Should().BeTrue();
    }
}
