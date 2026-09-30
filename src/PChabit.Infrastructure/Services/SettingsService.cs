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

    public bool AiInsightsEnabled
    {
        get => _settings.AiInsightsEnabled;
        set
        {
            if (_settings.AiInsightsEnabled != value)
            {
                _settings.AiInsightsEnabled = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(AiInsightsEnabled) });
            }
        }
    }

    public string AiBaseUrl
    {
        get => _settings.AiBaseUrl;
        set
        {
            if (_settings.AiBaseUrl != value)
            {
                _settings.AiBaseUrl = value ?? "";
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(AiBaseUrl) });
            }
        }
    }

    public string AiApiKey
    {
        get => _settings.AiApiKey;
        set
        {
            if (_settings.AiApiKey != value)
            {
                _settings.AiApiKey = value ?? "";
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(AiApiKey) });
            }
        }
    }

    public string AiModel
    {
        get => _settings.AiModel;
        set
        {
            if (_settings.AiModel != value)
            {
                _settings.AiModel = value ?? "";
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(AiModel) });
            }
        }
    }

    public string AiProvider
    {
        get => _settings.AiProvider;
        set
        {
            if (_settings.AiProvider != value)
            {
                _settings.AiProvider = value ?? "zhipu";
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(AiProvider) });
            }
        }
    }

    public bool BrowserAutoPush
    {
        get => _settings.BrowserAutoPush;
        set
        {
            if (_settings.BrowserAutoPush != value)
            {
                _settings.BrowserAutoPush = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(BrowserAutoPush) });
            }
        }
    }

    public int AiTimeoutSeconds
    {
        get => _settings.AiTimeoutSeconds;
        set
        {
            var clamped = Math.Clamp(value, 30, 900);
            if (_settings.AiTimeoutSeconds != clamped)
            {
                _settings.AiTimeoutSeconds = clamped;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(AiTimeoutSeconds) });
            }
        }
    }

    public bool AiStrictPrivacy
    {
        get => _settings.AiStrictPrivacy;
        set
        {
            if (_settings.AiStrictPrivacy != value)
            {
                _settings.AiStrictPrivacy = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(AiStrictPrivacy) });
            }
        }
    }

    public string AiModelFast
    {
        get => _settings.AiModelFast;
        set
        {
            if (_settings.AiModelFast != value)
            {
                _settings.AiModelFast = value ?? "";
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(AiModelFast) });
            }
        }
    }

    public bool AiAutoWeeklyInsight
    {
        get => _settings.AiAutoWeeklyInsight;
        set
        {
            if (_settings.AiAutoWeeklyInsight != value)
            {
                _settings.AiAutoWeeklyInsight = value;
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs { PropertyName = nameof(AiAutoWeeklyInsight) });
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

            // 启动时对齐快捷方式：若已开启自启动但快捷方式丢失/路径过期，补写
            ApplySettings();
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
        // 先解析目标 exe：ProcessPath 优先，缺省回退到 BaseDirectory
        var exePath = Environment.ProcessPath
            ?? Path.Combine(AppContext.BaseDirectory, "PChabit.exe");
        if (!File.Exists(exePath))
        {
            Log.Warning("自启动目标不存在: {Path}", exePath);
            // 目标丢了仍尝试清理残留项，避免留下指向失效路径的自启
            RemoveStartupEntries();
            return;
        }

        var quotedExe = "\"" + exePath + "\"";
        var startupFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        var shortcutPath = string.IsNullOrEmpty(startupFolderPath)
            ? null
            : Path.Combine(startupFolderPath, "PChabit.lnk");
        var legacyShortcutPath = string.IsNullOrEmpty(startupFolderPath)
            ? null
            : Path.Combine(startupFolderPath, "Tai.lnk");

        if (!StartWithWindows)
        {
            RemoveStartupEntries();
            return;
        }

        // 1) 注册表 Run（最可靠，开机由 Explorer 直接拉起，不依赖 .lnk）
        try
        {
            using var runKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (runKey == null)
            {
                Log.Warning("无法打开 HKCU Run 键，开机自启降级为快捷方式");
            }
            else
            {
                var current = runKey.GetValue("PChabit") as string;
                if (!string.Equals(current, quotedExe, StringComparison.Ordinal))
                {
                    runKey.SetValue("PChabit", quotedExe);
                    Log.Information("已写入开机自启(注册表 Run): {Path}", exePath);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "写入注册表开机自启失败");
        }

        // 2) 启动文件夹快捷方式（兼容旧路径清理 + 双保险）
        try
        {
            if (legacyShortcutPath != null && File.Exists(legacyShortcutPath))
            {
                File.Delete(legacyShortcutPath);
                Log.Information("已清理旧自启动快捷方式 Tai.lnk");
            }

            if (shortcutPath != null)
            {
                // 仅在缺失或目标不对时重建，避免每次 Load/Save 都删写
                if (!ShortcutTargets(shortcutPath, exePath))
                {
                    // 先写临时文件再替换，避免「先删后写」失败导致自启丢失
                    var tempPath = shortcutPath + ".tmp.lnk";
                    CreateShortcut(tempPath, exePath);
                    if (File.Exists(shortcutPath)) File.Delete(shortcutPath);
                    File.Move(tempPath, shortcutPath);
                    Log.Information("已写入开机自启动: {Shortcut} -> {Target}", shortcutPath, exePath);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "写入开机自启动快捷方式失败");
        }
    }

    private void RemoveStartupEntries()
    {
        try
        {
            using var runKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            runKey?.DeleteValue("PChabit", throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "移除注册表开机自启失败");
        }

        var startupFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        if (string.IsNullOrEmpty(startupFolderPath)) return;

        foreach (var name in new[] { "PChabit.lnk", "Tai.lnk", "PChabit.lnk.tmp.lnk" })
        {
            try
            {
                var p = Path.Combine(startupFolderPath, name);
                if (File.Exists(p))
                {
                    File.Delete(p);
                    Log.Information("已移除开机自启动: {Path}", p);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "移除开机自启动快捷方式失败: {Name}", name);
            }
        }
    }

    /// <summary>快捷方式是否存在且指向期望的 exe。</summary>
    private static bool ShortcutTargets(string shortcutPath, string expectedExe)
    {
        if (!File.Exists(shortcutPath)) return false;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return false;
            dynamic? shell = null;
            dynamic? shortcut = null;
            try
            {
                shell = Activator.CreateInstance(shellType);
                if (shell == null) return false;
                shortcut = shell.GetType().InvokeMember(
                    "CreateShortcut", System.Reflection.BindingFlags.InvokeMethod,
                    null, shell, new object[] { shortcutPath });
                if (shortcut == null) return false;
                var target = shortcut.GetType().InvokeMember(
                    "TargetPath", System.Reflection.BindingFlags.GetProperty,
                    null, shortcut, null) as string;
                return string.Equals(target, expectedExe, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                if (shortcut != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(shortcut);
                if (shell != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
            }
        }
        catch
        {
            return false;
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
            if (shellType == null)
            {
                Log.Warning("WScript.Shell 不可用，无法创建自启动快捷方式");
                return;
            }

            shell = Activator.CreateInstance(shellType);
            if (shell == null)
            {
                Log.Warning("WScript.Shell 实例创建失败");
                return;
            }

            shortcut = shell.GetType().InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
            if (shortcut == null)
            {
                Log.Warning("CreateShortcut 返回空: {Path}", shortcutPath);
                return;
            }

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

    public bool AiInsightsEnabled { get; set; } = false;
    public string AiBaseUrl { get; set; } = "https://api.openai.com/v1";
    public string AiApiKey { get; set; } = "";
    public string AiModel { get; set; } = "glm-4-flash";
    public string AiProvider { get; set; } = "zhipu";
    /// <summary>AI 请求超时秒数；本地模型建议 ≥300。</summary>
    public int AiTimeoutSeconds { get; set; } = 300;
    /// <summary>追问用快捷模型；空表示与 AiModel 相同。</summary>
    public string AiModelFast { get; set; } = "";
    /// <summary>严格隐私：出域不含应用/分类显示名。</summary>
    public bool AiStrictPrivacy { get; set; } = false;
    /// <summary>每周自动 AI 解读。</summary>
    public bool AiAutoWeeklyInsight { get; set; } = false;
    public bool BrowserAutoPush { get; set; } = false;
}

