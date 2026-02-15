namespace Tai.Infrastructure.Services;

public class ShortcutDetector
{
    private static readonly HashSet<string> KnownShortcuts = new()
    {
        "Ctrl+C", "Ctrl+V", "Ctrl+X", "Ctrl+Z", "Ctrl+Y",
        "Ctrl+A", "Ctrl+S", "Ctrl+F", "Ctrl+H", "Ctrl+G",
        "Ctrl+N", "Ctrl+O", "Ctrl+W", "Ctrl+P", "Ctrl+Q",
        "Ctrl+T", "Ctrl+Tab", "Ctrl+Shift+Tab",
        "Ctrl+Shift+N", "Ctrl+Shift+S", "Ctrl+Shift+T",
        "Alt+F4", "Alt+Tab", "Alt+Enter", "Alt+F4",
        "Ctrl+Alt+Delete", "Ctrl+Alt+Tab",
        "Win+E", "Win+R", "Win+D", "Win+L", "Win+Tab",
        "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
        "Ctrl+F1", "Ctrl+F2", "Ctrl+F4", "Ctrl+F5",
        "Shift+F10", "Shift+F11", "Shift+F12",
        "Ctrl+Shift+Z", "Ctrl+Shift+Y",
    };
    
    public event EventHandler<ShortcutDetectedEventArgs>? OnShortcutDetected;
    
    public void Detect(string shortcut, string? processName, DateTime timestamp)
    {
        if (!KnownShortcuts.Contains(shortcut)) return;
        
        var description = GetShortcutDescription(shortcut);
        
        var args = new ShortcutDetectedEventArgs(
            shortcut,
            description,
            processName,
            timestamp);
        
        OnShortcutDetected?.Invoke(this, args);
    }
    
    public bool IsKnownShortcut(string shortcut)
    {
        return KnownShortcuts.Contains(shortcut);
    }
    
    private static string GetShortcutDescription(string shortcut)
    {
        return shortcut switch
        {
            "Ctrl+C" => "复制",
            "Ctrl+V" => "粘贴",
            "Ctrl+X" => "剪切",
            "Ctrl+Z" => "撤销",
            "Ctrl+Y" => "重做",
            "Ctrl+A" => "全选",
            "Ctrl+S" => "保存",
            "Ctrl+F" => "查找",
            "Ctrl+H" => "替换",
            "Ctrl+N" => "新建",
            "Ctrl+O" => "打开",
            "Ctrl+W" => "关闭",
            "Ctrl+P" => "打印",
            "Ctrl+T" => "新标签页",
            "Ctrl+Tab" => "切换标签页",
            "Ctrl+Shift+Tab" => "反向切换标签页",
            "Alt+F4" => "关闭窗口",
            "Alt+Tab" => "切换窗口",
            "Win+E" => "打开资源管理器",
            "Win+R" => "运行",
            "Win+D" => "显示桌面",
            "Win+L" => "锁定",
            "F1" => "帮助",
            "F2" => "重命名",
            "F5" => "刷新",
            "F11" => "全屏",
            _ => shortcut
        };
    }
}

public class ShortcutDetectedEventArgs : EventArgs
{
    public string Shortcut { get; }
    public string Description { get; }
    public string? ProcessName { get; }
    public DateTime Timestamp { get; }
    
    public ShortcutDetectedEventArgs(string shortcut, string description, string? processName, DateTime timestamp)
    {
        Shortcut = shortcut;
        Description = description;
        ProcessName = processName;
        Timestamp = timestamp;
    }
}
