using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml.Media;
using Serilog;
using Tai.Infrastructure.Data;

namespace Tai.App.ViewModels;

public partial class DetailDialogViewModel : ObservableObject
{
    private readonly TaiDbContext _dbContext;
    
    public DetailDialogViewModel(TaiDbContext dbContext)
    {
        _dbContext = dbContext;
    }
    
    [ObservableProperty]
    private string _title = string.Empty;
    
    [ObservableProperty]
    private string _summary = string.Empty;
    
    public ObservableCollection<DetailItem> Items { get; } = new();
    public ObservableCollection<WebDetailItem> WebItems { get; } = new();
    
    public async Task LoadKeyboardDetailsAsync(DateTime date)
    {
        Title = "按键详情";
        
        List<Core.Entities.KeyboardSession> sessions;
        
        try
        {
            sessions = await _dbContext.KeyboardSessions
                .Where(s => s.Date == date)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "加载 KeyboardSessions 失败");
            sessions = new List<Core.Entities.KeyboardSession>();
        }
        
        var totalPresses = sessions.Sum(s => s.TotalKeyPresses);
        var totalBackspace = sessions.Sum(s => s.BackspaceCount);
        var totalDelete = sessions.Sum(s => s.DeleteCount);
        var totalShortcuts = sessions.Sum(s => s.Shortcuts?.Count ?? 0);
        
        Summary = $"今日总按键: {totalPresses:N0} 次 | 退格: {totalBackspace:N0} | 删除: {totalDelete:N0} | 快捷键: {totalShortcuts:N0}";
        
        Items.Clear();
        
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
        
        var topKeys = allKeyFreq
            .OrderByDescending(x => x.Value)
            .Take(15)
            .ToList();
        
        Items.Add(new DetailItem { Label = "按键使用排行", Value = "", SubValue = "", IsDivider = true });
        
        foreach (var key in topKeys)
        {
            var keyName = GetKeyName(key.Key);
            var percentage = totalPresses > 0 ? (double)key.Value / totalPresses * 100 : 0;
            Items.Add(new DetailItem
            {
                Label = keyName,
                Value = key.Value.ToString("N0"),
                SubValue = $"{percentage:F1}%",
                Color = GetKeyColor(keyName)
            });
        }
        
        Items.Add(new DetailItem { Label = "", Value = "", SubValue = "", IsDivider = true });
        Items.Add(new DetailItem { Label = "应用按键统计", Value = "", SubValue = "", IsDivider = true });
        
        var appGroups = sessions
            .Where(s => !string.IsNullOrEmpty(s.ProcessName))
            .GroupBy(s => s.ProcessName!)
            .Select(g => new
            {
                ProcessName = g.Key,
                TotalPresses = g.Sum(s => s.TotalKeyPresses)
            })
            .OrderByDescending(x => x.TotalPresses)
            .Take(10);
        
        foreach (var app in appGroups)
        {
            var percentage = totalPresses > 0 ? (double)app.TotalPresses / totalPresses * 100 : 0;
            Items.Add(new DetailItem
            {
                Label = app.ProcessName,
                Value = app.TotalPresses.ToString("N0"),
                SubValue = $"{percentage:F1}%"
            });
        }
        
        Items.Add(new DetailItem { Label = "", Value = "", SubValue = "", IsDivider = true });
        Items.Add(new DetailItem { Label = "每小时分布", Value = "", SubValue = "", IsDivider = true });
        
        var hourlyData = sessions
            .GroupBy(s => s.Hour)
            .Select(g => new DetailItem
            {
                Label = $"{g.Key}:00 - {g.Key + 1}:00",
                Value = g.Sum(s => s.TotalKeyPresses).ToString("N0"),
                SubValue = "次"
            })
            .OrderBy(x => x.Label);
        
        foreach (var item in hourlyData)
        {
            Items.Add(item);
        }
    }
    
    public async Task LoadMouseDetailsAsync(DateTime date)
    {
        Title = "鼠标点击详情";
        
        List<Core.Entities.MouseSession> sessions;
        
        try
        {
            sessions = await _dbContext.MouseSessions
                .Where(s => s.Date == date)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "加载 MouseSessions 失败");
            sessions = new List<Core.Entities.MouseSession>();
        }
        
        var totalClicks = sessions.Sum(s => s.LeftClickCount + s.RightClickCount + s.MiddleClickCount);
        var leftClicks = sessions.Sum(s => s.LeftClickCount);
        var rightClicks = sessions.Sum(s => s.RightClickCount);
        var middleClicks = sessions.Sum(s => s.MiddleClickCount);
        var totalScrolls = sessions.Sum(s => s.ScrollCount);
        
        Summary = $"总点击: {totalClicks:N0} | 左键: {leftClicks:N0} | 右键: {rightClicks:N0} | 中键: {middleClicks:N0} | 滚动: {totalScrolls:N0}";
        
        Items.Clear();
        
        Items.Add(new DetailItem { Label = "左键点击", Value = leftClicks.ToString("N0"), SubValue = "次", Color = CreateBrush("#0078D4") });
        Items.Add(new DetailItem { Label = "右键点击", Value = rightClicks.ToString("N0"), SubValue = "次", Color = CreateBrush("#107C10") });
        Items.Add(new DetailItem { Label = "中键点击", Value = middleClicks.ToString("N0"), SubValue = "次", Color = CreateBrush("#FF8C00") });
        Items.Add(new DetailItem { Label = "滚轮滚动", Value = totalScrolls.ToString("N0"), SubValue = "次", Color = CreateBrush("#6B7280") });
        
        Items.Add(new DetailItem { Label = "", Value = "", SubValue = "", IsDivider = true });
        
        var hourlyData = sessions
            .GroupBy(s => s.Hour)
            .Select(g => new
            {
                Hour = g.Key,
                Total = g.Sum(s => s.LeftClickCount + s.RightClickCount + s.MiddleClickCount),
                Left = g.Sum(s => s.LeftClickCount),
                Right = g.Sum(s => s.RightClickCount),
                Middle = g.Sum(s => s.MiddleClickCount)
            })
            .OrderBy(x => x.Hour);
        
        foreach (var h in hourlyData)
        {
            Items.Add(new DetailItem
            {
                Label = $"{h.Hour}:00 - {h.Hour + 1}:00",
                Value = h.Total.ToString("N0"),
                SubValue = $"左:{h.Left} 右:{h.Right} 中:{h.Middle}"
            });
        }
    }
    
    public async Task LoadWebDetailsAsync(DateTime date)
    {
        Title = "网页访问详情";
        
        try { _dbContext.ChangeTracker.Clear(); } catch { }
        
        var tomorrow = date.AddDays(1);
        List<Core.Entities.WebSession> sessions;
        
        try
        {
            sessions = await _dbContext.WebSessions
                .AsNoTracking()
                .Where(s => s.StartTime >= date && s.StartTime < tomorrow)
                .OrderByDescending(s => s.StartTime)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "加载 WebSessions 失败");
            sessions = new List<Core.Entities.WebSession>();
        }
        
        var totalPages = sessions.Count;
        var uniqueDomains = sessions.Select(s => s.Domain).Distinct().Count();
        var totalDuration = sessions.Where(s => s.Duration.TotalSeconds > 0).Sum(s => s.Duration.TotalMinutes);
        
        Summary = $"总访问: {totalPages:N0} 页 | 独立域名: {uniqueDomains:N0} 个 | 总时长: {(int)totalDuration} 分钟";
        
        Items.Clear();
        WebItems.Clear();
        
        foreach (var session in sessions.Take(50))
        {
            var category = GetWebCategory(session.Domain, session.Url);
            var duration = session.Duration.TotalSeconds > 0 
                ? $"{(int)session.Duration.TotalMinutes}分{(int)session.Duration.Seconds}秒"
                : "-";
            
            WebItems.Add(new WebDetailItem
            {
                Domain = session.Domain,
                Title = session.Title,
                Url = session.Url,
                VisitTime = session.StartTime.ToString("HH:mm:ss"),
                Duration = duration,
                Category = category,
                CategoryColor = GetCategoryColor(category),
                ScrollDepth = session.ScrollDepth,
                ClickCount = session.ClickCount,
                HasInteraction = session.HasFormInteraction || session.ClickCount > 0
            });
        }
    }
    
    private static string GetWebCategory(string domain, string url)
    {
        if (string.IsNullOrEmpty(domain)) return "其他";
        
        var lowerDomain = domain.ToLower();
        var lowerUrl = url.ToLower();
        
        if (lowerDomain.Contains("google") || lowerDomain.Contains("baidu") || 
            lowerDomain.Contains("bing") || lowerDomain.Contains("sogou") ||
            lowerDomain.Contains("duckduckgo"))
            return "搜索";
        
        if (lowerDomain.Contains("github") || lowerDomain.Contains("gitlab") ||
            lowerDomain.Contains("stackoverflow") || lowerDomain.Contains("csdn") ||
            lowerDomain.Contains("juejin") || lowerDomain.Contains("segmentfault") ||
            lowerDomain.Contains("npmjs") || lowerDomain.Contains("nuget"))
            return "开发";
        
        if (lowerDomain.Contains("youtube") || lowerDomain.Contains("bilibili") ||
            lowerDomain.Contains("netflix") || lowerDomain.Contains("youku") ||
            lowerDomain.Contains("iqiyi") || lowerDomain.Contains("tencent") && lowerUrl.Contains("video"))
            return "视频";
        
        if (lowerDomain.Contains("twitter") || lowerDomain.Contains("weibo") ||
            lowerDomain.Contains("facebook") || lowerDomain.Contains("instagram") ||
            lowerDomain.Contains("linkedin") || lowerDomain.Contains("zhihu") ||
            lowerDomain.Contains("xiaohongshu") || lowerDomain.Contains("douban"))
            return "社交";
        
        if (lowerDomain.Contains("amazon") || lowerDomain.Contains("taobao") ||
            lowerDomain.Contains("jd") || lowerDomain.Contains("tmall") ||
            lowerDomain.Contains("pinduoduo"))
            return "购物";
        
        if (lowerDomain.Contains("mail") || lowerDomain.Contains("outlook") ||
            lowerDomain.Contains("gmail") || lowerDomain.Contains("qq") && lowerUrl.Contains("mail"))
            return "邮件";
        
        if (lowerDomain.Contains("notion") || lowerDomain.Contains("docs.qq") ||
            lowerDomain.Contains("yuque") || lowerDomain.Contains("confluence") ||
            lowerDomain.Contains("feishu") || lowerDomain.Contains("dingtalk"))
            return "办公";
        
        if (lowerDomain.Contains("news") || lowerDomain.Contains("bbc") ||
            lowerDomain.Contains("cnn") || lowerDomain.Contains("sina") ||
            lowerDomain.Contains("sohu") || lowerDomain.Contains("163"))
            return "新闻";
        
        return "浏览";
    }
    
    private static SolidColorBrush GetCategoryColor(string category)
    {
        var hexColor = category switch
        {
            "搜索" => "#0078D4",
            "开发" => "#512BD4",
            "视频" => "#FF8C00",
            "社交" => "#107C10",
            "购物" => "#E81123",
            "邮件" => "#00B7C3",
            "办公" => "#6B7280",
            "新闻" => "#8764B8",
            _ => "#9CA3AF"
        };
        return CreateBrush(hexColor);
    }
    
    private static SolidColorBrush CreateBrush(string hexColor)
    {
        var color = Microsoft.UI.Colors.Transparent;
        if (hexColor.StartsWith("#") && hexColor.Length == 7)
        {
            var r = Convert.ToByte(hexColor.Substring(1, 2), 16);
            var g = Convert.ToByte(hexColor.Substring(3, 2), 16);
            var b = Convert.ToByte(hexColor.Substring(5, 2), 16);
            color = Microsoft.UI.ColorHelper.FromArgb(255, r, g, b);
        }
        return new SolidColorBrush(color);
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
        var hexColor = keyName switch
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
        return CreateBrush(hexColor);
    }
}

public class DetailItem
{
    public string Label { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string SubValue { get; init; } = string.Empty;
    public SolidColorBrush? Color { get; init; }
    public bool IsDivider { get; init; }
}

public class WebDetailItem
{
    public string Domain { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string VisitTime { get; init; } = string.Empty;
    public string Duration { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public SolidColorBrush CategoryColor { get; init; } = new SolidColorBrush();
    public int ScrollDepth { get; init; }
    public int ClickCount { get; init; }
    public bool HasInteraction { get; init; }
}
