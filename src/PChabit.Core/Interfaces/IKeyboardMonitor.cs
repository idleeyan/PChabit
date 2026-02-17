namespace PChabit.Core.Interfaces;

public interface IKeyboardMonitor : IMonitor<KeyboardEventArgs>
{
}

public class KeyboardEventArgs : EventArgs
{
    public int KeyCode { get; }
    public string? KeyName { get; }
    public bool IsKeyDown { get; }
    public bool IsShiftPressed { get; init; }
    public bool IsCtrlPressed { get; init; }
    public bool IsAltPressed { get; init; }
    public string? ActiveProcess { get; init; }
    public DateTime Timestamp { get; }
    public int KeyCount { get; set; } = 1;
    
    public KeyboardEventArgs(int keyCode, string? keyName, bool isKeyDown, DateTime timestamp)
    {
        KeyCode = keyCode;
        KeyName = keyName;
        IsKeyDown = isKeyDown;
        Timestamp = timestamp;
    }
    
    public bool IsShortcut => IsCtrlPressed || IsAltPressed;
    
    public string GetShortcutString()
    {
        var parts = new List<string>();
        if (IsCtrlPressed) parts.Add("Ctrl");
        if (IsShiftPressed) parts.Add("Shift");
        if (IsAltPressed) parts.Add("Alt");
        if (KeyName != null) parts.Add(KeyName);
        return string.Join("+", parts);
    }
}
