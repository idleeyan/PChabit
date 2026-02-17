using System.Text.Json;
using Serilog;
using PChabit.Core.Interfaces;

namespace PChabit.Infrastructure.Services;

public class SettingsService : ISettingsService
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
    
    public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;
    
    public SettingsService()
    {
        var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PChabit");
        Directory.CreateDirectory(appDataPath);
        _settingsPath = Path.Combine(appDataPath, "settings.json");
        _settings = new AppSettings();
    }
    
    public async Task LoadAsync()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = await File.ReadAllTextAsync(_settingsPath);
                var loadedSettings = JsonSerializer.Deserialize<AppSettings>(json);
                if (loadedSettings != null)
                {
                    _settings = loadedSettings;
                    Log.Information("设置已加载");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "加载设置失败，使用默认设置");
            _settings = new AppSettings();
        }
    }
    
    public void Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                var loadedSettings = JsonSerializer.Deserialize<AppSettings>(json);
                if (loadedSettings != null)
                {
                    _settings = loadedSettings;
                    Log.Information("设置已加载");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "加载设置失败，使用默认设置");
            _settings = new AppSettings();
        }
    }
    
    public async Task SaveAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_settingsPath, json);
            Log.Information("设置已保存");
            
            ApplySettings();
            
            SettingsChanged?.Invoke(this, new SettingsChangedEventArgs());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "保存设置失败");
        }
    }
    
    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);
            Log.Information("设置已保存");
            
            ApplySettings();
            
            SettingsChanged?.Invoke(this, new SettingsChangedEventArgs());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "保存设置失败");
        }
    }
    
    public void ResetToDefaults()
    {
        _settings = new AppSettings();
        Log.Information("设置已重置为默认值");
        SettingsChanged?.Invoke(this, new SettingsChangedEventArgs());
    }
    
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private void ApplySettings()
    {
        try
        {
            ApplyStartupSetting();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "应用启动设置失败");
        }
    }
    
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private void ApplyStartupSetting()
    {
        var startupFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        var shortcutPath = Path.Combine(startupFolderPath, "Tai.lnk");
        
        if (StartWithWindows)
        {
            if (!File.Exists(shortcutPath))
            {
                var exePath = Environment.ProcessPath;
                if (exePath != null)
                {
                    CreateShortcut(shortcutPath, exePath);
                    Log.Information("已添加开机自启动");
                }
            }
        }
        else
        {
            if (File.Exists(shortcutPath))
            {
                File.Delete(shortcutPath);
                Log.Information("已移除开机自启动");
            }
        }
    }
    
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void CreateShortcut(string shortcutPath, string targetPath)
    {
        dynamic? shell = null;
        dynamic? shortcut = null;
        
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            
            shell = Activator.CreateInstance(shellType);
            if (shell == null) return;
            
            shortcut = shell.GetType().InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
            if (shortcut == null) return;
            
            var shortcutType = shortcut.GetType();
            shortcutType.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
            shortcutType.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { Path.GetDirectoryName(targetPath) ?? string.Empty });
            shortcutType.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { "PChabit Activity Tracker" });
            shortcutType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, null);
        }
        finally
        {
            if (shortcut != null)
            {
                System.Runtime.InteropServices.Marshal.ReleaseComObject(shortcut);
            }
            if (shell != null)
            {
                System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
            }
        }
    }
}

internal class AppSettings
{
    public bool StartWithWindows { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public bool ShowNotifications { get; set; } = true;
    public bool AutoStartMonitoring { get; set; } = true;
    public int MonitoringInterval { get; set; } = 1;
    public int IdleThreshold { get; set; } = 5;
    public bool TrackKeyboard { get; set; } = true;
    public bool TrackMouse { get; set; } = true;
    public bool TrackWebBrowsing { get; set; } = true;
    public bool AnonymizeData { get; set; } = false;
    public int RetentionDays { get; set; } = 90;
    public string WebSocketPort { get; set; } = "8765";
    public string CurrentTheme { get; set; } = "system";
    public string CurrentLanguage { get; set; } = "zh-CN";
    
    public string WebDAVUrl { get; set; } = "";
    public string WebDAVUsername { get; set; } = "";
    public string WebDAVPassword { get; set; } = "";
    public bool WebDAVEnabled { get; set; } = false;
    public DateTime? WebDAVLastSync { get; set; }
    
    public string BackupPath { get; set; } = "";
    public bool AutoBackupEnabled { get; set; } = true;
    public int AutoBackupIntervalHours { get; set; } = 4;
    public int MaxBackupCount { get; set; } = 7;
    public int DataRetentionDays { get; set; } = 180;
    public bool ArchiveBeforeCleanup { get; set; } = true;
}
