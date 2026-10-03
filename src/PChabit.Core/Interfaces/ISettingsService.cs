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

    // ===== 桌面硬件悬浮插件（3.23.0，行为照抄 LiteMonitor）=====
    /// <summary>桌面悬浮窗总开关（默认关）。</summary>
    bool DesktopWidgetEnabled { get; set; }
    bool DesktopWidgetShowCpu { get; set; }
    bool DesktopWidgetShowMemory { get; set; }
    bool DesktopWidgetShowGpu { get; set; }
    /// <summary>显存独立成行（GPU 行显示负载/温度）。</summary>
    bool DesktopWidgetShowVram { get; set; }
    bool DesktopWidgetShowNet { get; set; }
    bool DesktopWidgetShowDisk { get; set; }
    bool DesktopWidgetShowUsage { get; set; }
    /// <summary>窗口置顶（默认开）。</summary>
    bool DesktopWidgetTopmost { get; set; }
    /// <summary>鼠标点击穿透（默认关）。</summary>
    bool DesktopWidgetClickThrough { get; set; }
    /// <summary>拖拽结束时限制完全跑出屏幕（默认开）。</summary>
    bool DesktopWidgetClampToScreen { get; set; }
    /// <summary>位置记忆：显示器 DeviceName（\\.\DISPLAY1），空串表示未保存。</summary>
    string DesktopWidgetScreen { get; set; }
    /// <summary>位置记忆：窗口左上角屏幕坐标，-1 表示未保存。</summary>
    int DesktopWidgetLeft { get; set; }
    int DesktopWidgetTop { get; set; }
    /// <summary>尺寸记忆：窗口物理宽（3.23.1 可调整大小），-1 表示未保存。</summary>
    int DesktopWidgetWidth { get; set; }
    /// <summary>尺寸记忆：窗口物理高，-1 表示未保存。</summary>
    int DesktopWidgetHeight { get; set; }

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
    // ===== 双端点（本地 LM Studio + 云端）=====
    /// <summary>本地端点 Base URL，如 http://127.0.0.1:1234/v1</summary>
    string AiLocalBaseUrl { get; set; }
    /// <summary>本地模型名</summary>
    string AiLocalModel { get; set; }
    /// <summary>云端 Base URL（空则回退 AiBaseUrl）</summary>
    string AiCloudBaseUrl { get; set; }
    /// <summary>云端 API Key</summary>
    string AiCloudApiKey { get; set; }
    /// <summary>云端模型（空则回退 AiModel）</summary>
    string AiCloudModel { get; set; }
    /// <summary>端点模式：cloud | local | dual（解读云端、追问本地）</summary>
    string AiEndpointMode { get; set; }
    /// <summary>严格隐私：出域指标包不含应用/分类显示名，仅数值与标签。</summary>
    bool AiStrictPrivacy { get; set; }
    /// <summary>每周自动跑一次 AI 解读（需先启用 AI）。</summary>
    bool AiAutoWeeklyInsight { get; set; }
    /// <summary>书签库整树自动推送到浏览器。</summary>
    bool BrowserAutoPush { get; set; }

    // ===== 便签（3.24.0）=====
    /// <summary>便签功能总开关（默认开；关闭时注销热键、不建任何同步定时器）。</summary>
    bool StickyNotesEnabled { get; set; }
    /// <summary>全局热键-新建便签（"Ctrl+Alt+N"，空串=不注册）。</summary>
    string StickyNotesHotkeyNew { get; set; }
    /// <summary>全局热键-打开便签页（"Ctrl+Alt+B"，空串=不注册）。</summary>
    string StickyNotesHotkeyBoard { get; set; }
    /// <summary>便签随 WebDAV 账号云同步（默认关）。</summary>
    bool StickyNotesSyncEnabled { get; set; }
    /// <summary>上次便签云同步时间。</summary>
    DateTime? StickyNotesLastSync { get; set; }
    /// <summary>回收站墓碑保留天数。</summary>
    int StickyNotesRetentionDays { get; set; }
    /// <summary>快速录入窗失焦自动保存关闭（默认开）。</summary>
    bool StickyNotesQuickDismissOnFocusLost { get; set; }
    /// <summary>本机设备短 ID（首次使用时自动生成，同步冲突留痕用）。</summary>
    string StickyNotesDeviceId { get; set; }

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

