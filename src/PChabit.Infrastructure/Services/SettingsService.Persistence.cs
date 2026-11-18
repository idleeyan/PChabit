using System.Text.Json;
using Serilog;
using PChabit.Core.Interfaces;

namespace PChabit.Infrastructure.Services;

public partial class SettingsService
{
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
                    Log.Information("设置已加载: {Path}", _settingsPath);
                    return;
                }
            }
            // 文件不存在：保持默认，但不覆盖已有内存配置
            Log.Information("设置文件不存在，使用默认: {Path}", _settingsPath);
        }
        catch (Exception ex)
        {
            // 绝不整体清空：解析失败时保留当前 _settings，避免把用户 API 配置刷成默认
            Log.Error(ex, "加载设置失败，保留当前配置不重置: {Path}", _settingsPath);
        }
    }

    public async Task SaveAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            var tmp = _settingsPath + ".tmp";
            await File.WriteAllTextAsync(tmp, json);
            File.Move(tmp, _settingsPath, overwrite: true);
            Log.Information("设置已保存: {Path}", _settingsPath);

            ApplySettings();

            SettingsChanged?.Invoke(this, new SettingsChangedEventArgs());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "保存设置失败: {Path}", _settingsPath);
        }
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            var tmp = _settingsPath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _settingsPath, overwrite: true);
            Log.Information("设置已保存: {Path}", _settingsPath);

            ApplySettings();

            SettingsChanged?.Invoke(this, new SettingsChangedEventArgs());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "保存设置失败: {Path}", _settingsPath);
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

    public bool BrowserSyncEnabled { get; set; } = true;
    public bool BrowserHistoryIngestEnabled { get; set; } = true;
    public int BrowserSyncIntervalMinutes { get; set; } = 60;

    public string BackupPath { get; set; } = "";
    public bool AutoBackupEnabled { get; set; } = true;
    public int AutoBackupIntervalHours { get; set; } = 4;
    public int MaxBackupCount { get; set; } = 7;
    public int DataRetentionDays { get; set; } = 90;
    public bool AutoCleanupEnabled { get; set; } = true;
    public bool ArchiveBeforeCleanup { get; set; } = true;
    public int MaxCloudBackupCount { get; set; } = 5;

    public bool TaskbarEnabled { get; set; } = true;
    public bool TaskbarShowCpu { get; set; } = true;
    public bool TaskbarShowMemory { get; set; } = true;
    public bool TaskbarShowGpu { get; set; } = true;
    public bool TaskbarShowNet { get; set; } = true;
    public bool TaskbarShowDisk { get; set; } = true;
    public bool TaskbarShowTemp { get; set; } = true;
    public bool TaskbarShowUsage { get; set; } = true;
    public double DailyUsageGoalHours { get; set; } = 6;

    // ===== 桌面硬件悬浮插件（3.23.0，默认值照抄 LiteMonitor Settings）=====
    public bool DesktopWidgetEnabled { get; set; } = false;
    public bool DesktopWidgetShowCpu { get; set; } = true;
    public bool DesktopWidgetShowMemory { get; set; } = true;
    public bool DesktopWidgetShowGpu { get; set; } = true;
    public bool DesktopWidgetShowVram { get; set; } = true;
    public bool DesktopWidgetShowNet { get; set; } = true;
    public bool DesktopWidgetShowDisk { get; set; } = true;
    public bool DesktopWidgetShowUsage { get; set; } = true;
    public bool DesktopWidgetShowCurrentApp { get; set; } = true;
    public bool DesktopWidgetTopmost { get; set; } = true;
    public bool DesktopWidgetClickThrough { get; set; } = false;
    public bool DesktopWidgetClampToScreen { get; set; } = true;
    public string DesktopWidgetScreen { get; set; } = "";
    public int DesktopWidgetLeft { get; set; } = -1;
    public int DesktopWidgetTop { get; set; } = -1;
    public int DesktopWidgetWidth { get; set; } = -1;
    public int DesktopWidgetHeight { get; set; } = -1;

    public bool BrowserAutoPush { get; set; } = false;

    // ===== 便签（3.24.0）=====
    public bool StickyNotesEnabled { get; set; } = true;
    public string StickyNotesHotkeyNew { get; set; } = "Ctrl+Alt+N";
    public string StickyNotesHotkeyBoard { get; set; } = "Ctrl+Alt+B";
    public bool StickyNotesSyncEnabled { get; set; } = false;
    public DateTime? StickyNotesLastSync { get; set; }
    public int StickyNotesRetentionDays { get; set; } = 30;
    public bool StickyNotesQuickDismissOnFocusLost { get; set; } = true;
    public string StickyNotesDeviceId { get; set; } = "";
}
