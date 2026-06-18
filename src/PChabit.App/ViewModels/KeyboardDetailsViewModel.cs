using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml.Media;
using Serilog;
using PChabit.Infrastructure.Data;

namespace PChabit.App.ViewModels;

public partial class KeyboardDetailsViewModel : DbSafeViewModel<KeyboardDetailsViewModel.KeyboardDetailsStats>
{
    private readonly IDbContextFactory<PChabitDbContext> _dbContextFactory;

    public KeyboardDetailsViewModel(IDbContextFactory<PChabitDbContext> dbContextFactory) : base()
    {
        _dbContextFactory = dbContextFactory;
        Title = "键鼠详情";
    }
    
    [ObservableProperty]
    private DateTime _selectedDate = DateTime.Today;
    
    [ObservableProperty]
    private string _totalKeyPresses = "0";
    [ObservableProperty]
    private string _averageSpeed = "0";
    [ObservableProperty]
    private string _peakSpeed = "0";
    [ObservableProperty]
    private string _shortcutsUsed = "0";
    [ObservableProperty]
    private string _backspaceCount = "0";
    [ObservableProperty]
    private string _deleteCount = "0";
    
    [ObservableProperty]
    private string _totalClicks = "0";
    [ObservableProperty]
    private string _leftClicks = "0";
    [ObservableProperty]
    private string _rightClicks = "0";
    [ObservableProperty]
    private string _middleClicks = "0";
    [ObservableProperty]
    private string _totalMoveDistance = "0";
    [ObservableProperty]
    private string _totalScrolls = "0";
    [ObservableProperty]
    private string _averageClickInterval = "无数据";
    
    public ObservableCollection<KeyUsageItem> KeyUsageList { get; } = new();
    public ObservableCollection<HourlyKeyItem> HourlyData { get; } = new();
    public ObservableCollection<ShortcutItem> TopShortcuts { get; } = new();
    public ObservableCollection<AppKeyItem> AppKeyUsage { get; } = new();
    public ObservableCollection<HourlyMouseStat> HourlyMouseStats { get; } = new();
    public ObservableCollection<ProgramMouseStat> ProgramMouseStats { get; } = new();
    public ObservableCollection<MouseClickDetail> MouseClickDetails { get; } = new();
    
    // === Phase 1 中间数据（纯 POCO，无 WinRT 类型）===

    public sealed class KeyboardDetailsStats
    {
        public KeyboardPart Keyboard = new();
        public MousePart Mouse = new();
    }

    public sealed class KeyboardPart
    {
        public int TotalPresses;
        public double AvgSpeed;
        public int PeakSpeed;
        public int TotalShortcuts;
        public int BackspaceCount;
        public int DeleteCount;
        public List<KeyUsageItem> KeyUsage = new();
        public List<HourlyKeyItem> Hourly = new();
        public List<ShortcutItem> Shortcuts = new();
        public List<AppKeyItem> AppKey = new();
    }
    
    public sealed class MousePart
    {
        public int TotalClicks;
        public int LeftClicks;
        public int RightClicks;
        public int MiddleClicks;
        public double MoveDistance;
        public int Scrolls;
        public string AverageClickInterval = "无数据";
        public List<HourlyMouseStat> Hourly = new();
        public List<ProgramMouseStat> Program = new();
        public List<MouseClickDetail> ClickDetails = new();
    }
    
    // === DbSafeViewModel 抽象方法 ===

    protected override async Task<KeyboardDetailsStats> LoadStatsOnBackgroundAsync()
    {
        var selectedDate = SelectedDate;
        var stats = new KeyboardDetailsStats();
        
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        
        // 键盘数据
        var keySessions = await dbContext.KeyboardSessions
            .AsNoTracking()
            .Where(s => s.Date >= selectedDate && s.Date < selectedDate.AddDays(1))
            .ToListAsync();

        Log.Information("键盘数据加载: 日期={Date}, 会话数={Count}, 总按键={TotalKeys}",
            selectedDate.ToString("yyyy-MM-dd"), keySessions.Count, keySessions.Sum(s => s.TotalKeyPresses));

        var k = stats.Keyboard;
        k.TotalPresses = keySessions.Sum(s => s.TotalKeyPresses);
        k.AvgSpeed = keySessions.Count > 0 ? keySessions.Average(s => s.AverageTypingSpeed) : 0;
        k.PeakSpeed = keySessions.Count > 0 ? (int)keySessions.Max(s => s.PeakTypingSpeed) : 0;
        k.TotalShortcuts = keySessions.Sum(s => s.Shortcuts?.Count ?? 0);
        k.BackspaceCount = keySessions.Sum(s => s.BackspaceCount);
        k.DeleteCount = keySessions.Sum(s => s.DeleteCount);
        ComputeKeyUsage(keySessions, k);
        ComputeHourlyData(keySessions, k);
        ComputeShortcuts(keySessions, k);
        ComputeAppKeyUsage(keySessions, k);

        // 鼠标数据
        var mouseSessions = await dbContext.MouseSessions
            .AsNoTracking()
            .Where(s => s.Date >= selectedDate && s.Date < selectedDate.AddDays(1))
            .ToListAsync();

        Log.Information("鼠标数据加载: 日期={Date}, 会话数={Count}, 总点击={TotalClicks}",
            selectedDate.ToString("yyyy-MM-dd"), mouseSessions.Count, mouseSessions.Sum(s => s.LeftClickCount + s.RightClickCount + s.MiddleClickCount));

        var m = stats.Mouse;
        m.TotalClicks = mouseSessions.Sum(s => s.LeftClickCount + s.RightClickCount + s.MiddleClickCount);
        m.LeftClicks = mouseSessions.Sum(s => s.LeftClickCount);
        m.RightClicks = mouseSessions.Sum(s => s.RightClickCount);
        m.MiddleClicks = mouseSessions.Sum(s => s.MiddleClickCount);
        m.MoveDistance = mouseSessions.Sum(s => s.TotalMoveDistance);
        m.Scrolls = mouseSessions.Sum(s => s.ScrollCount);
        
        if (m.TotalClicks > 1)
        {
            var totalMinutes = mouseSessions.Count * 60;
            var avgSeconds = (totalMinutes * 60.0) / m.TotalClicks;
            m.AverageClickInterval = $"{avgSeconds:F1} 秒";
        }
        
        ComputeHourlyMouseStats(mouseSessions, m);
        await ComputeProgramMouseStatsAsync(selectedDate, m, dbContext);
        ComputeMouseClickDetails(mouseSessions, m);

        return stats;
    }

    protected override async Task ApplyStatsOnUIAsync(KeyboardDetailsStats stats)
    {
        var k = stats.Keyboard;
        TotalKeyPresses = k.TotalPresses.ToString("N0");
        AverageSpeed = $"{k.AvgSpeed:F1} 键/分钟";
        PeakSpeed = $"{k.PeakSpeed} 键/分钟";
        ShortcutsUsed = k.TotalShortcuts.ToString("N0");
        BackspaceCount = k.BackspaceCount.ToString("N0");
        DeleteCount = k.DeleteCount.ToString("N0");
        
        KeyUsageList.Clear();
        foreach (var item in k.KeyUsage)
        {
            item.Color = GetKeyColor(item.KeyName);
            KeyUsageList.Add(item);
        }
        HourlyData.Clear();
        foreach (var item in k.Hourly) HourlyData.Add(item);
        TopShortcuts.Clear();
        foreach (var item in k.Shortcuts) TopShortcuts.Add(item);
        AppKeyUsage.Clear();
        foreach (var item in k.AppKey) AppKeyUsage.Add(item);

        var m = stats.Mouse;
        TotalClicks = m.TotalClicks.ToString("N0");
        LeftClicks = m.LeftClicks.ToString("N0");
        RightClicks = m.RightClicks.ToString("N0");
        MiddleClicks = m.MiddleClicks.ToString("N0");
        TotalMoveDistance = $"{m.MoveDistance:F0} px";
        TotalScrolls = m.Scrolls.ToString("N0");
        AverageClickInterval = m.AverageClickInterval;
        
        HourlyMouseStats.Clear();
        foreach (var item in m.Hourly) HourlyMouseStats.Add(item);
        ProgramMouseStats.Clear();
        foreach (var item in m.Program) ProgramMouseStats.Add(item);
        MouseClickDetails.Clear();
        foreach (var item in m.ClickDetails) MouseClickDetails.Add(item);
    }
    
    // === 纯数据计算辅助方法 ===
    
    private static void ComputeKeyUsage(List<Core.Entities.KeyboardSession> sessions, KeyboardPart stats)
    {
        var allKeyFreq = new Dictionary<int, int>();
        foreach (var session in sessions)
        {
            if (session.KeyFrequency != null)
            {
                foreach (var kvp in session.KeyFrequency)
                {
                    if (allKeyFreq.ContainsKey(kvp.Key))
                        allKeyFreq[kvp.Key] += kvp.Value;
                    else
                        allKeyFreq[kvp.Key] = kvp.Value;
                }
            }
        }
        var totalKeys = allKeyFreq.Values.Sum();
        var topKeys = allKeyFreq.OrderByDescending(x => x.Value).Take(20).ToList();
        foreach (var key in topKeys)
        {
            var keyName = GetKeyName(key.Key);
            var percentage = totalKeys > 0 ? (double)key.Value / totalKeys * 100 : 0;
            stats.KeyUsage.Add(new KeyUsageItem { KeyCode = key.Key, KeyName = keyName, Count = key.Value, Percentage = percentage });
        }
    }
    
    private static void ComputeHourlyData(List<Core.Entities.KeyboardSession> sessions, KeyboardPart stats)
    {
        var hourlyGroups = sessions
            .GroupBy(s => s.Hour)
            .Select(g => new { Hour = g.Key, TotalPresses = g.Sum(s => s.TotalKeyPresses), AvgSpeed = g.Average(s => s.AverageTypingSpeed) })
            .OrderBy(x => x.Hour);
        foreach (var h in hourlyGroups)
            stats.Hourly.Add(new HourlyKeyItem { Hour = $"{h.Hour}:00", Presses = h.TotalPresses, Speed = $"{h.AvgSpeed:F1}" });
    }
    
    private static void ComputeShortcuts(List<Core.Entities.KeyboardSession> sessions, KeyboardPart stats)
    {
        var allShortcuts = new Dictionary<string, int>();
        foreach (var session in sessions)
        {
            if (session.Shortcuts != null && session.Shortcuts.Count > 0)
            {
                foreach (var shortcut in session.Shortcuts)
                {
                    if (allShortcuts.ContainsKey(shortcut.Shortcut)) allShortcuts[shortcut.Shortcut]++;
                    else allShortcuts[shortcut.Shortcut] = 1;
                }
            }
        }
        foreach (var s in allShortcuts.OrderByDescending(x => x.Value).Take(10))
            stats.Shortcuts.Add(new ShortcutItem { Shortcut = s.Key, Count = s.Value });
    }
    
    private static void ComputeAppKeyUsage(List<Core.Entities.KeyboardSession> sessions, KeyboardPart stats)
    {
        var appGroups = sessions
            .Where(s => !string.IsNullOrEmpty(s.ProcessName))
            .GroupBy(s => s.ProcessName!)
            .Select(g => new { ProcessName = g.Key, TotalPresses = g.Sum(s => s.TotalKeyPresses), AvgSpeed = g.Average(s => s.AverageTypingSpeed) })
            .OrderByDescending(x => x.TotalPresses).Take(10);
        foreach (var app in appGroups)
            stats.AppKey.Add(new AppKeyItem { ProcessName = app.ProcessName, KeyPresses = app.TotalPresses, AvgSpeed = $"{app.AvgSpeed:F1}" });
    }
    
    private static void ComputeHourlyMouseStats(List<Core.Entities.MouseSession> sessions, MousePart stats)
    {
        foreach (var group in sessions.GroupBy(s => s.Hour).OrderBy(g => g.Key))
        {
            stats.Hourly.Add(new HourlyMouseStat
            {
                Hour = group.Key,
                ClickCount = group.Sum(s => s.LeftClickCount + s.RightClickCount + s.MiddleClickCount),
                MoveDistance = group.Sum(s => s.TotalMoveDistance),
                ScrollCount = group.Sum(s => s.ScrollCount),
                Label = $"{group.Key:D2}:00"
            });
        }
    }
    
    private async Task ComputeProgramMouseStatsAsync(DateTime selectedDate, MousePart stats, PChabitDbContext dbContext)
    {
        try
        {
            var sessions = await dbContext.MouseSessions
                .AsNoTracking()
                .Where(s => s.Date >= selectedDate && s.Date < selectedDate.AddDays(1))
                .ToListAsync();
            
            var programStats = sessions
                .Where(s => !string.IsNullOrEmpty(s.ProcessName))
                .GroupBy(s => s.ProcessName!)
                .Select(g => new { ProcessName = g.Key, ClickCount = g.Sum(s => s.LeftClickCount + s.RightClickCount + s.MiddleClickCount), MoveDistance = g.Sum(s => s.TotalMoveDistance), ScrollCount = g.Sum(s => s.ScrollCount) })
                .OrderByDescending(g => g.ClickCount).ToList();
            
            var mappings = await dbContext.ProgramCategoryMappings.Include(m => m.Category).ToListAsync();
            
            foreach (var program in programStats)
            {
                var categoryMapping = mappings.FirstOrDefault(m => m.ProcessName.Equals(program.ProcessName, StringComparison.OrdinalIgnoreCase));
                var category = categoryMapping?.Category?.Name ?? "其他";
                stats.Program.Add(new ProgramMouseStat { ProcessName = program.ProcessName, Category = category, ClickCount = program.ClickCount, MoveDistance = program.MoveDistance, ScrollCount = program.ScrollCount, Duration = TimeSpan.Zero });
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "加载鼠标程序统计失败");
        }
    }
    
    private static void ComputeMouseClickDetails(List<Core.Entities.MouseSession> sessions, MousePart stats)
    {
        foreach (var session in sessions.OrderByDescending(s => s.Hour).Take(50))
        {
            stats.ClickDetails.Add(new MouseClickDetail { Date = session.Date, Hour = session.Hour, LeftClicks = session.LeftClickCount, RightClicks = session.RightClickCount, MiddleClicks = session.MiddleClickCount, MoveDistance = session.TotalMoveDistance, Scrolls = session.ScrollCount });
        }
    }
    
    private static string GetKeyName(int vkCode)
    {
        return vkCode switch
        {
            0x08 => "Backspace", 0x09 => "Tab", 0x0D => "Enter", 0x10 => "Shift",
            0x11 => "Ctrl", 0x12 => "Alt", 0x13 => "Pause", 0x14 => "CapsLock",
            0x1B => "Esc", 0x20 => "Space", 0x21 => "PageUp", 0x22 => "PageDown",
            0x23 => "End", 0x24 => "Home", 0x25 => "Left", 0x26 => "Up", 0x27 => "Right",
            0x28 => "Down", 0x2C => "PrintScreen", 0x2D => "Insert", 0x2E => "Delete",
            >= 0x30 and <= 0x39 => ((char)('0' + vkCode - 0x30)).ToString(),
            >= 0x41 and <= 0x5A => ((char)('A' + vkCode - 0x41)).ToString(),
            >= 0x70 and <= 0x87 => $"F{vkCode - 0x6F}",
            _ => $"Key{vkCode}"
        };
    }
    
    private static SolidColorBrush GetKeyColor(string keyName)
    {
        var hexColor = keyName switch
        {
            "Space" => "#0078D4", "Enter" => "#107C10", "Backspace" => "#D13438",
            "Delete" => "#D13438", "Tab" => "#FF8C00", "Esc" => "#6B7280",
            var k when k.StartsWith("F") => "#8764B8",
            var k when k.Length == 1 && char.IsLetter(k[0]) => "#512BD4",
            var k when k.Length == 1 && char.IsDigit(k[0]) => "#00B7C3",
            _ => "#9CA3AF"
        };
        var r = Convert.ToByte(hexColor.Substring(1, 2), 16);
        var g = Convert.ToByte(hexColor.Substring(3, 2), 16);
        var b = Convert.ToByte(hexColor.Substring(5, 2), 16);
        return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, r, g, b));
    }
    
    public async Task ChangeDateAsync(int days)
    {
        SelectedDate = SelectedDate.AddDays(days);
        await LoadDataAsync();
    }
}

public class KeyUsageItem
{
    public int KeyCode { get; init; }
    public string KeyName { get; init; } = string.Empty;
    public int Count { get; init; }
    public double Percentage { get; init; }
    public SolidColorBrush? Color { get; set; }
}

public class HourlyKeyItem { public string Hour { get; init; } = string.Empty; public int Presses { get; init; } public string Speed { get; init; } = string.Empty; }
public class ShortcutItem { public string Shortcut { get; init; } = string.Empty; public int Count { get; init; } }
public class AppKeyItem { public string ProcessName { get; init; } = string.Empty; public int KeyPresses { get; init; } public string AvgSpeed { get; init; } = string.Empty; }

public class ProgramMouseStat : ObservableObject
{
    public string ProcessName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int ClickCount { get; set; }
    public double MoveDistance { get; set; }
    public int ScrollCount { get; set; }
    public TimeSpan Duration { get; set; }
}

public class HourlyMouseStat : ObservableObject
{
    public int Hour { get; set; }
    public int ClickCount { get; set; }
    public double MoveDistance { get; set; }
    public int ScrollCount { get; set; }
    public string Label { get; set; } = string.Empty;
}

public class MouseClickDetail : ObservableObject
{
    public DateTime Date { get; set; }
    public int Hour { get; set; }
    public int LeftClicks { get; set; }
    public int RightClicks { get; set; }
    public int MiddleClicks { get; set; }
    public double MoveDistance { get; set; }
    public int Scrolls { get; set; }
}
