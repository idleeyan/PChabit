namespace PChabit.Core.Interfaces;

public interface ISettingsService
{
    bool StartWithWindows { get; set; }
    bool MinimizeToTray { get; set; }
    bool ShowNotifications { get; set; }
    bool AutoStartMonitoring { get; set; }
    int MonitoringInterval { get; set; }
    int IdleThreshold { get; set; }
    bool TrackKeyboard { get; set; }
    bool TrackMouse { get; set; }
    bool TrackWebBrowsing { get; set; }
    bool AnonymizeData { get; set; }
    int RetentionDays { get; set; }
    string WebSocketPort { get; set; }
    string CurrentTheme { get; set; }
    string CurrentLanguage { get; set; }
    
    string WebDAVUrl { get; set; }
    string WebDAVUsername { get; set; }
    string WebDAVPassword { get; set; }
    bool WebDAVEnabled { get; set; }
    DateTime? WebDAVLastSync { get; set; }
    
    string BackupPath { get; set; }
    bool AutoBackupEnabled { get; set; }
    int AutoBackupIntervalHours { get; set; }
    int MaxBackupCount { get; set; }
    int DataRetentionDays { get; set; }
    bool AutoCleanupEnabled { get; set; }
    bool ArchiveBeforeCleanup { get; set; }
    int MaxCloudBackupCount { get; set; }

    bool BrowserSyncEnabled { get; set; }
    bool BrowserHistoryIngestEnabled { get; set; }
    int BrowserSyncIntervalMinutes { get; set; }

    bool TaskbarEnabled { get; set; }
    // 任务栏小窗显示项（3.9.4：删除托盘悬停提示后由 TrayTipShow* 更名而来）
    bool TaskbarShowCpu { get; set; }
    bool TaskbarShowMemory { get; set; }
    bool TaskbarShowGpu { get; set; }
    bool TaskbarShowNet { get; set; }
    bool TaskbarShowDisk { get; set; }
    bool TaskbarShowTemp { get; set; }
    bool TaskbarShowUsage { get; set; }
    double DailyUsageGoalHours { get; set; }

    /// <summary>分析页「AI 深度解读」总开关（默认关）。仅上传聚合指标。</summary>
    bool AiInsightsEnabled { get; set; }
    /// <summary>OpenAI 兼容 BaseUrl，如 https://api.openai.com/v1 或本地 Ollama。</summary>
    string AiBaseUrl { get; set; }
    string AiApiKey { get; set; }
    string AiModel { get; set; }
    /// <summary>AI Provider：zhipu | deepseek | lmstudio | custom</summary>
    string AiProvider { get; set; }
    /// <summary>AI 解读超时（秒）。本地大模型推理慢，默认 300；范围 30–900。</summary>
    int AiTimeoutSeconds { get; set; }
    /// <summary>追问用快捷模型（空则与 AiModel 相同）。</summary>
    string AiModelFast { get; set; }
    /// <summary>严格隐私：出域指标包不含应用/分类显示名，仅数值与标签。</summary>
    bool AiStrictPrivacy { get; set; }
    /// <summary>每周自动跑一次 AI 解读（需先启用 AI）。</summary>
    bool AiAutoWeeklyInsight { get; set; }
    /// <summary>书签库整树自动推送到浏览器。</summary>
    bool BrowserAutoPush { get; set; }

    event EventHandler<SettingsChangedEventArgs>? SettingsChanged;
    
    Task LoadAsync();
    Task SaveAsync();
    void Load();
    void Save();
    void ResetToDefaults();
}

public class SettingsChangedEventArgs : EventArgs
{
    public string? PropertyName { get; init; }
}

