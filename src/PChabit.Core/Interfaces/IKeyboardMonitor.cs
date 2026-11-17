namespace PChabit.Core.Interfaces;

public interface IKeyboardMonitor : IMonitor<KeyboardEventArgs>
{
    void SetCurrentProcess(string? processName);
}

public class KeyboardEventArgs : EventArgs
{
    public int KeyCode { get; }
    public string? KeyName { get; }
    public bool IsKeyDown { get; }
    public bool IsShiftPressed { get; init; }
    public bool IsCtrlPressed { get; init; }
    public bool IsAltPressed { get; init; }
    public bool IsWinPressed { get; init; }
    public string? ActiveProcess { get; init; }
    public DateTime Timestamp { get; }
    public int KeyCount { get; set; } = 1;

    /// <summary>
    /// 是否为「按住不放」产生的自动重复（auto-repeat）。
    /// 自动重复不计入按键总数与按键分布，只单独累计；否则游戏里长按 W 一分钟就会被记成上千次。
    /// </summary>
    public bool IsAutoRepeat { get; init; }
    
    public KeyboardEventArgs(int keyCode, string? keyName, bool isKeyDown, DateTime timestamp)
    {
        KeyCode = keyCode;
        KeyName = keyName;
        IsKeyDown = isKeyDown;
        Timestamp = timestamp;
    }
    
    public bool IsShortcut => IsCtrlPressed || IsAltPressed || IsWinPressed;
    
    public string GetShortcutString()
    {
        var parts = new List<string>();
        if (IsCtrlPressed) parts.Add("Ctrl");
        if (IsShiftPressed) parts.Add("Shift");
        if (IsAltPressed) parts.Add("Alt");
        if (IsWinPressed) parts.Add("Win");
        if (KeyName != null) parts.Add(KeyName);
        return string.Join("+", parts);
    }
}
