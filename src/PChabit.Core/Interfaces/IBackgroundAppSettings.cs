namespace PChabit.Core.Interfaces;

public interface IBackgroundAppSettings
{
    HashSet<string> GetBackgroundApps();
    void SetBackgroundApp(string processName, bool isBackground);
    bool IsBackgroundApp(string processName);
    void SaveSettings();
    void LoadSettings();
}

public class BackgroundAppSettings : IBackgroundAppSettings
{
    private readonly string _settingsPath;
    private HashSet<string> _backgroundApps = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    
    public BackgroundAppSettings()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
            "Tai");
        
        Directory.CreateDirectory(appDataPath);
        _settingsPath = Path.Combine(appDataPath, "background_apps.json");
        
        LoadSettings();
    }
    
    public HashSet<string> GetBackgroundApps()
    {
        lock (_lock)
        {
            return new HashSet<string>(_backgroundApps, StringComparer.OrdinalIgnoreCase);
        }
    }
    
    public void SetBackgroundApp(string processName, bool isBackground)
    {
        lock (_lock)
        {
            if (isBackground)
            {
                _backgroundApps.Add(processName);
            }
            else
            {
                _backgroundApps.Remove(processName);
            }
            SaveSettings();
        }
    }
    
    public bool IsBackgroundApp(string processName)
    {
        lock (_lock)
        {
            return _backgroundApps.Contains(processName);
        }
    }
    
    public void SaveSettings()
    {
        try
        {
            lock (_lock)
            {
                var json = System.Text.Json.JsonSerializer.Serialize(_backgroundApps.ToList());
                File.WriteAllText(_settingsPath, json);
            }
        }
        catch (Exception)
        {
            // [豁免登记] Core 层无日志设施（保持零依赖，见 E1 分层规约）。
            // 写失败仅影响后台应用名单持久化，下次保存自动重试；
            // 待 E2 迁移到 Infrastructure 时随层补 Log.Warning。
        }
    }

    public void LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                var apps = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json);
                if (apps != null)
                {
                    _backgroundApps = new HashSet<string>(apps, StringComparer.OrdinalIgnoreCase);
                }
            }
        }
        catch (Exception)
        {
            // [豁免登记] 配置损坏时回退为空名单（默认行为），见 SaveSettings 的豁免说明
            _backgroundApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
