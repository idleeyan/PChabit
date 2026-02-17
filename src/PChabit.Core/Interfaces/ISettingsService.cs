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
    bool ArchiveBeforeCleanup { get; set; }
    
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
