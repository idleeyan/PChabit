using System.Collections.ObjectModel;

namespace PChabit.App.Services;

/// <summary>全局操作日志：内存环形缓冲 + 按日落盘，供用户与 AI 智能体排查。</summary>
public static class GlobalOpLog
{
    public sealed record OpLogItem(
        DateTime Time,
        string Level,
        string Category,
        string Message)
    {
        public string TimeText => Time.ToString("HH:mm:ss");
        public string Display => $"[{TimeText}] [{Level}] [{Category}] {Message}";
    }

    private static readonly object _sync = new();
    private static readonly List<OpLogItem> _buffer = new();
    private const int MaxBuffer = 500;

    /// <summary>UI 订阅：新日志插入列表。</summary>
    public static event Action<OpLogItem>? Logged;

    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PChabit", "Logs");

    public static void Info(string category, string message)
        => Write("信息", category, message);

    public static void Success(string category, string message)
        => Write("成功", category, message);

    public static void Warn(string category, string message)
        => Write("警告", category, message);

    public static void Error(string category, string message)
        => Write("错误", category, message);

    public static void Action(string category, string message)
        => Write("操作", category, message);

    private static void Write(string level, string category, string message)
    {
        var item = new OpLogItem(DateTime.Now, level, category ?? "系统", message ?? "");
        lock (_sync)
        {
            _buffer.Insert(0, item);
            if (_buffer.Count > MaxBuffer)
                _buffer.RemoveRange(MaxBuffer, _buffer.Count - MaxBuffer);
        }
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var file = Path.Combine(LogDirectory, $"ops-{DateTime.Now:yyyyMMdd}.log");
            var line = $"{item.Time:yyyy-MM-dd HH:mm:ss.fff}\t{level}\t{category}\t{message}{Environment.NewLine}";
            File.AppendAllText(file, line);
        }
        catch { }
        try { Logged?.Invoke(item); } catch { }
    }

    /// <summary>当前缓冲（新→旧）。</summary>
    public static List<OpLogItem> Snapshot()
    {
        lock (_sync) return _buffer.ToList();
    }
}
