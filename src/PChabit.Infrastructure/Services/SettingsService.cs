using PChabit.Core.Interfaces;

namespace PChabit.Infrastructure.Services;

public partial class SettingsService : ISettingsService
{
    private readonly string _settingsPath;
    private AppSettings _settings;

    public bool StartWithWindows 
    { 
        get => _settings.StartWithWindows; 
        set 
        {
            if (_settings.StartWithWindows != value)
            {
                _settings.StartWithWindows = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(StartWithWindows) });
            }
        }
    }
    
    public bool MinimizeToTray 
    { 
        get => _settings.MinimizeToTray; 
        set 
        {
            if (_settings.MinimizeToTray != value)
            {
                _settings.MinimizeToTray = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(MinimizeToTray) });
            }
        }
    }
    
    public bool ShowNotifications 
    { 
        get => _settings.ShowNotifications; 
        set 
        {
            if (_settings.ShowNotifications != value)
            {
                _settings.ShowNotifications = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(ShowNotifications) });
            }
        }
    }
    
    public bool AutoStartMonitoring 
    { 
        get => _settings.AutoStartMonitoring; 
        set 
        {
            if (_settings.AutoStartMonitoring != value)
            {
                _settings.AutoStartMonitoring = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(AutoStartMonitoring) });
            }
        }
    }
    
    public int MonitoringInterval 
    { 
        get => _settings.MonitoringInterval; 
        set 
        {
            if (_settings.MonitoringInterval != value)
            {
                _settings.MonitoringInterval = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(MonitoringInterval) });
            }
        }
    }
    
    public int IdleThreshold 
    { 
        get => _settings.IdleThreshold; 
        set 
        {
            if (_settings.IdleThreshold != value)
            {
                _settings.IdleThreshold = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(IdleThreshold) });
            }
        }
    }
    
    public bool TrackKeyboard 
    { 
        get => _settings.TrackKeyboard; 
        set 
        {
            if (_settings.TrackKeyboard != value)
            {
                _settings.TrackKeyboard = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(TrackKeyboard) });
            }
        }
    }
    
    public bool TrackMouse 
    { 
        get => _settings.TrackMouse; 
        set 
        {
            if (_settings.TrackMouse != value)
            {
                _settings.TrackMouse = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(TrackMouse) });
            }
        }
    }
    
    public bool TrackWebBrowsing 
    { 
        get => _settings.TrackWebBrowsing; 
        set 
        {
            if (_settings.TrackWebBrowsing != value)
            {
                _settings.TrackWebBrowsing = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(TrackWebBrowsing) });
            }
        }
    }
    
    public bool AnonymizeData 
    { 
        get => _settings.AnonymizeData; 
        set 
        {
            if (_settings.AnonymizeData != value)
            {
                _settings.AnonymizeData = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(AnonymizeData) });
            }
        }
    }
    
    public int RetentionDays 
    { 
        get => _settings.RetentionDays; 
        set 
        {
            if (_settings.RetentionDays != value)
            {
                _settings.RetentionDays = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(RetentionDays) });
            }
        }
    }
    
    public string WebSocketPort 
    { 
        get => _settings.WebSocketPort; 
        set 
        {
            if (_settings.WebSocketPort != value)
            {
                _settings.WebSocketPort = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(WebSocketPort) });
            }
        }
    }
    
    public string CurrentTheme 
    { 
        get => _settings.CurrentTheme; 
        set 
        {
            if (_settings.CurrentTheme != value)
            {
                _settings.CurrentTheme = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(CurrentTheme) });
            }
        }
    }
    
    public string CurrentLanguage 
    { 
        get => _settings.CurrentLanguage; 
        set 
        {
            if (_settings.CurrentLanguage != value)
            {
                _settings.CurrentLanguage = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(CurrentLanguage) });
            }
        }
    }
    
    public string WebDAVUrl 
    { 
        get => _settings.WebDAVUrl; 
        set 
        {
            if (_settings.WebDAVUrl != value)
            {
                _settings.WebDAVUrl = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(WebDAVUrl) });
            }
        }
    }
    
    public string WebDAVUsername 
    { 
        get => _settings.WebDAVUsername; 
        set 
        {
            if (_settings.WebDAVUsername != value)
            {
                _settings.WebDAVUsername = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(WebDAVUsername) });
            }
        }
    }
    
    public string WebDAVPassword 
    { 
        get => _settings.WebDAVPassword; 
        set 
        {
            if (_settings.WebDAVPassword != value)
            {
                _settings.WebDAVPassword = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(WebDAVPassword) });
            }
        }
    }
    
    public bool WebDAVEnabled 
    { 
        get => _settings.WebDAVEnabled; 
        set 
        {
            if (_settings.WebDAVEnabled != value)
            {
                _settings.WebDAVEnabled = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(WebDAVEnabled) });
            }
        }
    }
    
    public DateTime? WebDAVLastSync
    {
        get => _settings.WebDAVLastSync;
        set
        {
            if (_settings.WebDAVLastSync != value)
            {
                _settings.WebDAVLastSync = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(WebDAVLastSync) });
            }
        }
    }

    public bool BrowserSyncEnabled
    {
        get => _settings.BrowserSyncEnabled;
        set
        {
            if (_settings.BrowserSyncEnabled != value)
            {
                _settings.BrowserSyncEnabled = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(BrowserSyncEnabled) });
            }
        }
    }


    public bool BrowserHistoryIngestEnabled
    {
        get => _settings.BrowserHistoryIngestEnabled;
        set
        {
            if (_settings.BrowserHistoryIngestEnabled != value)
            {
                _settings.BrowserHistoryIngestEnabled = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(BrowserHistoryIngestEnabled) });
            }
        }
    }

    public int BrowserSyncIntervalMinutes
    {
        get => _settings.BrowserSyncIntervalMinutes;
        set
        {
            if (_settings.BrowserSyncIntervalMinutes != value)
            {
                _settings.BrowserSyncIntervalMinutes = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(BrowserSyncIntervalMinutes) });
            }
        }
    }

    public bool TaskbarEnabled
    {
        get => _settings.TaskbarEnabled;
        set
        {
            if (_settings.TaskbarEnabled != value)
            {
                _settings.TaskbarEnabled = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(TaskbarEnabled) });
            }
        }
    }

    public bool TaskbarShowCpu
    {
        get => _settings.TaskbarShowCpu;
        set
        {
            if (_settings.TaskbarShowCpu != value)
            {
                _settings.TaskbarShowCpu = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(TaskbarShowCpu) });
            }
        }
    }

    public bool TaskbarShowMemory
    {
        get => _settings.TaskbarShowMemory;
        set
        {
            if (_settings.TaskbarShowMemory != value)
            {
                _settings.TaskbarShowMemory = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(TaskbarShowMemory) });
            }
        }
    }

    public bool TaskbarShowGpu
    {
        get => _settings.TaskbarShowGpu;
        set
        {
            if (_settings.TaskbarShowGpu != value)
            {
                _settings.TaskbarShowGpu = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(TaskbarShowGpu) });
            }
        }
    }

    public bool TaskbarShowNet
    {
        get => _settings.TaskbarShowNet;
        set
        {
            if (_settings.TaskbarShowNet != value)
            {
                _settings.TaskbarShowNet = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(TaskbarShowNet) });
            }
        }
    }

    public bool TaskbarShowDisk
    {
        get => _settings.TaskbarShowDisk;
        set
        {
            if (_settings.TaskbarShowDisk != value)
            {
                _settings.TaskbarShowDisk = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(TaskbarShowDisk) });
            }
        }
    }

    public bool TaskbarShowTemp
    {
        get => _settings.TaskbarShowTemp;
        set
        {
            if (_settings.TaskbarShowTemp != value)
            {
                _settings.TaskbarShowTemp = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(TaskbarShowTemp) });
            }
        }
    }

    public bool TaskbarShowUsage
    {
        get => _settings.TaskbarShowUsage;
        set
        {
            if (_settings.TaskbarShowUsage != value)
            {
                _settings.TaskbarShowUsage = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(TaskbarShowUsage) });
            }
        }
    }

    public double DailyUsageGoalHours
    {
        get => _settings.DailyUsageGoalHours;
        set
        {
            if (_settings.DailyUsageGoalHours != value)
            {
                _settings.DailyUsageGoalHours = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(DailyUsageGoalHours) });
            }
        }
    }

    public string BackupPath 
    { 
        get => _settings.BackupPath; 
        set 
        {
            if (_settings.BackupPath != value)
            {
                _settings.BackupPath = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(BackupPath) });
            }
        }
    }
    
    public bool AutoBackupEnabled 
    { 
        get => _settings.AutoBackupEnabled; 
        set 
        {
            if (_settings.AutoBackupEnabled != value)
            {
                _settings.AutoBackupEnabled = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(AutoBackupEnabled) });
            }
        }
    }
    
    public int AutoBackupIntervalHours 
    { 
        get => _settings.AutoBackupIntervalHours; 
        set 
        {
            if (_settings.AutoBackupIntervalHours != value)
            {
                _settings.AutoBackupIntervalHours = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(AutoBackupIntervalHours) });
            }
        }
    }
    
    public int MaxBackupCount 
    { 
        get => _settings.MaxBackupCount; 
        set 
        {
            if (_settings.MaxBackupCount != value)
            {
                _settings.MaxBackupCount = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(MaxBackupCount) });
            }
        }
    }
    
    public int DataRetentionDays 
    { 
        get => _settings.DataRetentionDays; 
        set 
        {
            if (_settings.DataRetentionDays != value)
            {
                _settings.DataRetentionDays = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(DataRetentionDays) });
            }
        }
    }
    
    public bool AutoCleanupEnabled 
    { 
        get => _settings.AutoCleanupEnabled; 
        set 
        {
            if (_settings.AutoCleanupEnabled != value)
            {
                _settings.AutoCleanupEnabled = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(AutoCleanupEnabled) });
            }
        }
    }
    
    public bool ArchiveBeforeCleanup 
    { 
        get => _settings.ArchiveBeforeCleanup; 
        set 
        {
            if (_settings.ArchiveBeforeCleanup != value)
            {
                _settings.ArchiveBeforeCleanup = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(ArchiveBeforeCleanup) });
            }
        }
    }
    
    public int MaxCloudBackupCount
    {
        get => _settings.MaxCloudBackupCount;
        set
        {
            if (_settings.MaxCloudBackupCount != value)
            {
                _settings.MaxCloudBackupCount = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(MaxCloudBackupCount) });
            }
        }
    }

    public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;

    public SettingsService()
    {
        var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PChabit");
        Directory.CreateDirectory(appDataPath);
        _settingsPath = Path.Combine(appDataPath, "settings.json");
        _settings = new AppSettings();
    }
}
