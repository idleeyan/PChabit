using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml.Media;
using Serilog;
using Tai.Infrastructure.Data;

namespace Tai.App.ViewModels;

public partial class KeyboardDetailsViewModel : ViewModelBase
{
    private readonly TaiDbContext _dbContext;
    
    public KeyboardDetailsViewModel(TaiDbContext dbContext) : base()
    {
        _dbContext = dbContext;
        Title = "按键详情";
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
    
    public ObservableCollection<KeyUsageItem> KeyUsageList { get; } = new();
    public ObservableCollection<HourlyKeyItem> HourlyData { get; } = new();
    public ObservableCollection<ShortcutItem> TopShortcuts { get; } = new();
    public ObservableCollection<AppKeyItem> AppKeyUsage { get; } = new();
    
    public async Task LoadDataAsync()
    {
        IsLoading = true;
        
        try
        {
            try { _dbContext.ChangeTracker.Clear(); } catch { }
            
            List<Core.Entities.KeyboardSession> sessions;
            
            try
            {
                sessions = await _dbContext.KeyboardSessions
                    .Where(s => s.Date == SelectedDate)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "加载 KeyboardSessions 失败");
                sessions = new List<Core.Entities.KeyboardSession>();
            }
            
            var totalPresses = sessions.Sum(s => s.TotalKeyPresses);
            var avgSpeed = sessions.Average(s => s.AverageTypingSpeed);
            var peakSpeed = sessions.Max(s => s.PeakTypingSpeed);
            var totalShortcuts = sessions.Sum(s => s.Shortcuts?.Count ?? 0);
            var totalBackspace = sessions.Sum(s => s.BackspaceCount);
            var totalDelete = sessions.Sum(s => s.DeleteCount);
            
            TotalKeyPresses = totalPresses.ToString("N0");
            AverageSpeed = $"{avgSpeed:F1} 字/分钟";
            PeakSpeed = $"{peakSpeed:F0} 字/分钟";
            ShortcutsUsed = totalShortcuts.ToString("N0");
            BackspaceCount = totalBackspace.ToString("N0");
            DeleteCount = totalDelete.ToString("N0");
            
            LoadKeyUsage(sessions);
            LoadHourlyData(sessions);
            LoadShortcuts(sessions);
            LoadAppKeyUsage(sessions);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "加载按键详情失败");
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    private void LoadKeyUsage(List<Core.Entities.KeyboardSession> sessions)
    {
        KeyUsageList.Clear();
        
        Log.Information("LoadKeyUsage: 开始加载按键使用统计, 会话数={SessionCount}", sessions.Count);
        
        var allKeyFreq = new Dictionary<int, int>();
        foreach (var session in sessions)
        {
            Log.Information("LoadKeyUsage: 会话 {Date} {Hour}:00, KeyFrequency={KeyFrequency}", 
                session.Date, session.Hour, 
                System.Text.Json.JsonSerializer.Serialize(session.KeyFrequency));
            
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
        Log.Information("LoadKeyUsage: 总按键数={TotalKeys}, 不同按键数={UniqueKeys}", totalKeys, allKeyFreq.Count);
        
        var topKeys = allKeyFreq
            .OrderByDescending(x => x.Value)
            .Take(20)
            .ToList();
        
        foreach (var key in topKeys)
        {
            var keyName = GetKeyName(key.Key);
            var percentage = totalKeys > 0 ? (double)key.Value / totalKeys * 100 : 0;
            
            KeyUsageList.Add(new KeyUsageItem
            {
                KeyCode = key.Key,
                KeyName = keyName,
                Count = key.Value,
                Percentage = percentage,
                Color = GetKeyColor(keyName)
            });
        }
        
        Log.Information("LoadKeyUsage: 加载完成, KeyUsageList数量={Count}", KeyUsageList.Count);
    }
    
    private void LoadHourlyData(List<Core.Entities.KeyboardSession> sessions)
    {
        HourlyData.Clear();
        
        var hourlyGroups = sessions
            .GroupBy(s => s.Hour)
            .Select(g => new
            {
                Hour = g.Key,
                TotalPresses = g.Sum(s => s.TotalKeyPresses),
                AvgSpeed = g.Average(s => s.AverageTypingSpeed)
            })
            .OrderBy(x => x.Hour);
        
        foreach (var h in hourlyGroups)
        {
            HourlyData.Add(new HourlyKeyItem
            {
                Hour = $"{h.Hour}:00",
                Presses = h.TotalPresses,
                Speed = $"{h.AvgSpeed:F1}"
            });
        }
    }
    
    private void LoadShortcuts(List<Core.Entities.KeyboardSession> sessions)
    {
        TopShortcuts.Clear();
        
        Log.Information("LoadShortcuts: 开始加载快捷键, 会话数={SessionCount}", sessions.Count);
        
        var allShortcuts = new Dictionary<string, int>();
        foreach (var session in sessions)
        {
            if (session.Shortcuts != null && session.Shortcuts.Count > 0)
            {
                Log.Information("LoadShortcuts: 会话 {Date} {Hour}:00 有 {Count} 个快捷键", 
                    session.Date, session.Hour, session.Shortcuts.Count);
                    
                foreach (var shortcut in session.Shortcuts)
                {
                    if (allShortcuts.ContainsKey(shortcut.Shortcut))
                        allShortcuts[shortcut.Shortcut]++;
                    else
                        allShortcuts[shortcut.Shortcut] = 1;
                }
            }
        }
        
        Log.Information("LoadShortcuts: 总共 {Count} 个不同快捷键", allShortcuts.Count);
        
        var topShortcuts = allShortcuts
            .OrderByDescending(x => x.Value)
            .Take(10)
            .ToList();
        
        foreach (var s in topShortcuts)
        {
            TopShortcuts.Add(new ShortcutItem
            {
                Shortcut = s.Key,
                Count = s.Value
            });
        }
    }
    
    private void LoadAppKeyUsage(List<Core.Entities.KeyboardSession> sessions)
    {
        AppKeyUsage.Clear();
        
        var appGroups = sessions
            .Where(s => !string.IsNullOrEmpty(s.ProcessName))
            .GroupBy(s => s.ProcessName!)
            .Select(g => new
            {
                ProcessName = g.Key,
                TotalPresses = g.Sum(s => s.TotalKeyPresses),
                AvgSpeed = g.Average(s => s.AverageTypingSpeed)
            })
            .OrderByDescending(x => x.TotalPresses)
            .Take(10);
        
        foreach (var app in appGroups)
        {
            AppKeyUsage.Add(new AppKeyItem
            {
                ProcessName = app.ProcessName,
                KeyPresses = app.TotalPresses,
                AvgSpeed = $"{app.AvgSpeed:F1}"
            });
        }
    }
    
    private static string GetKeyName(int vkCode)
    {
        return vkCode switch
        {
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x10 => "Shift",
            0x11 => "Ctrl",
            0x12 => "Alt",
            0x13 => "Pause",
            0x14 => "CapsLock",
            0x1B => "Esc",
            0x20 => "Space",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2C => "PrintScreen",
            0x2D => "Insert",
            0x2E => "Delete",
            >= 0x30 and <= 0x39 => ((char)('0' + vkCode - 0x30)).ToString(),
            >= 0x41 and <= 0x5A => ((char)('A' + vkCode - 0x41)).ToString(),
            >= 0x70 and <= 0x87 => $"F{vkCode - 0x6F}",
            _ => $"Key{vkCode}"
        };
    }
    
    private static SolidColorBrush GetKeyColor(string keyName)
    {
        var color = keyName switch
        {
            "Space" => "#0078D4",
            "Enter" => "#107C10",
            "Backspace" => "#D13438",
            "Delete" => "#D13438",
            "Tab" => "#FF8C00",
            "Esc" => "#6B7280",
            var k when k.StartsWith("F") => "#8764B8",
            var k when k.Length == 1 && char.IsLetter(k[0]) => "#512BD4",
            var k when k.Length == 1 && char.IsDigit(k[0]) => "#00B7C3",
            _ => "#9CA3AF"
        };
        
        if (color.StartsWith("#") && color.Length == 7)
        {
            var r = Convert.ToByte(color.Substring(1, 2), 16);
            var g = Convert.ToByte(color.Substring(3, 2), 16);
            var b = Convert.ToByte(color.Substring(5, 2), 16);
            return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, r, g, b));
        }
        return new SolidColorBrush(Microsoft.UI.Colors.Gray);
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
    public SolidColorBrush Color { get; init; } = new SolidColorBrush();
}

public class HourlyKeyItem
{
    public string Hour { get; init; } = string.Empty;
    public int Presses { get; init; }
    public string Speed { get; init; } = string.Empty;
}

public class ShortcutItem
{
    public string Shortcut { get; init; } = string.Empty;
    public int Count { get; init; }
}

public class AppKeyItem
{
    public string ProcessName { get; init; } = string.Empty;
    public int KeyPresses { get; init; }
    public string AvgSpeed { get; init; } = string.Empty;
}
