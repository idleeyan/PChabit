using System.Text;
using PChabit.Core.Interfaces;

namespace PChabit.Infrastructure.Formatters;

public class MarkdownExportFormatter : IExportFormatter
{
    public string Format => "markdown";
    public string FileExtension => ".md";
    public string MimeType => "text/markdown";
    
    public Task<string> FormatAsync(ExportData data, ExportOptions? options = null)
    {
        var sb = new StringBuilder();
        
        WriteHeader(sb, data);
        
        if (data.Statistics != null && options?.IncludeStatistics == true)
        {
            WriteStatistics(sb, data.Statistics);
        }
        
        if (data.AppSessions.Count > 0)
        {
            WriteAppSessions(sb, data.AppSessions, options);
        }
        
        if (data.KeyboardSessions.Count > 0)
        {
            WriteKeyboardSessions(sb, data.KeyboardSessions);
        }
        
        if (data.MouseSessions.Count > 0)
        {
            WriteMouseSessions(sb, data.MouseSessions);
        }
        
        if (data.WebSessions.Count > 0)
        {
            WriteWebSessions(sb, data.WebSessions, options);
        }
        
        if (data.DailyPatterns.Count > 0)
        {
            WriteDailyPatterns(sb, data.DailyPatterns);
        }
        
        return Task.FromResult(sb.ToString());
    }
    
    private static void WriteHeader(StringBuilder sb, ExportData data)
    {
        sb.AppendLine("# PChabit 使用习惯报告");
        sb.AppendLine();
        sb.AppendLine($"**导出时间**: {data.ExportTime:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"**统计周期**: {data.StartTime:yyyy-MM-dd} 至 {data.EndTime:yyyy-MM-dd}");
        sb.AppendLine($"**总时长**: {FormatDuration(data.Duration)}");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
    }
    
    private static void WriteStatistics(StringBuilder sb, ExportStatistics stats)
    {
        sb.AppendLine("## 📊 统计概览");
        sb.AppendLine();
        
        sb.AppendLine("### 活动统计");
        sb.AppendLine();
        sb.AppendLine("| 指标 | 数值 |");
        sb.AppendLine("|------|------|");
        sb.AppendLine($"| 应用会话数 | {stats.TotalAppSessions} |");
        sb.AppendLine($"| 键盘会话数 | {stats.TotalKeyboardSessions} |");
        sb.AppendLine($"| 鼠标会话数 | {stats.TotalMouseSessions} |");
        sb.AppendLine($"| 网页会话数 | {stats.TotalWebSessions} |");
        sb.AppendLine($"| 总按键次数 | {stats.TotalKeyPresses:N0} |");
        sb.AppendLine($"| 总点击次数 | {stats.TotalMouseClicks:N0} |");
        sb.AppendLine();
        
        sb.AppendLine("### 时间统计");
        sb.AppendLine();
        sb.AppendLine("| 指标 | 时长 |");
        sb.AppendLine("|------|------|");
        sb.AppendLine($"| 活跃时间 | {FormatDuration(stats.TotalActiveTime)} |");
        sb.AppendLine($"| 空闲时间 | {FormatDuration(stats.TotalIdleTime)} |");
        sb.AppendLine($"| 深度工作 | {FormatDuration(stats.TotalDeepWorkTime)} |");
        sb.AppendLine($"| 专注块数 | {stats.TotalFocusBlocks} |");
        sb.AppendLine($"| 平均生产力评分 | {stats.AverageProductivityScore:F1}/100 |");
        sb.AppendLine();
        
        if (stats.TopApplications.Count > 0)
        {
            sb.AppendLine("### 🖥️ 最常使用的应用");
            sb.AppendLine();
            sb.AppendLine("| 应用 | 时长 |");
            sb.AppendLine("|------|------|");
            foreach (var app in stats.TopApplications.Take(10))
            {
                sb.AppendLine($"| {app.Key} | {FormatDuration(app.Value)} |");
            }
            sb.AppendLine();
        }
        
        if (stats.TopWebsites.Count > 0)
        {
            sb.AppendLine("### 🌐 最常访问的网站");
            sb.AppendLine();
            sb.AppendLine("| 网站 | 时长 |");
            sb.AppendLine("|------|------|");
            foreach (var site in stats.TopWebsites.Take(10))
            {
                sb.AppendLine($"| {site.Key} | {FormatDuration(site.Value)} |");
            }
            sb.AppendLine();
        }
        
        sb.AppendLine("---");
        sb.AppendLine();
    }
    
    private static void WriteAppSessions(StringBuilder sb, List<Core.Entities.AppSession> sessions, ExportOptions? options)
    {
        sb.AppendLine("## 🖥️ 应用使用记录");
        sb.AppendLine();
        
        var groupedByDay = sessions.GroupBy(s => s.StartTime.Date).OrderByDescending(g => g.Key);
        
        foreach (var dayGroup in groupedByDay)
        {
            sb.AppendLine($"### {dayGroup.Key:yyyy-MM-dd}");
            sb.AppendLine();
            sb.AppendLine("| 时间 | 应用 | 窗口标题 | 分类 | 时长 |");
            sb.AppendLine("|------|------|----------|------|------|");
            
            foreach (var session in dayGroup.OrderBy(s => s.StartTime))
            {
                var title = Truncate(session.WindowTitle, 30);
                sb.AppendLine($"| {session.StartTime:HH:mm} | {session.ProcessName} | {title} | {session.Category} | {FormatDuration(session.Duration)} |");
            }
            sb.AppendLine();
        }
        
        sb.AppendLine("---");
        sb.AppendLine();
    }
    
    private static void WriteKeyboardSessions(StringBuilder sb, List<Core.Entities.KeyboardSession> sessions)
    {
        sb.AppendLine("## ⌨️ 键盘活动记录");
        sb.AppendLine();
        
        sb.AppendLine("| 日期 | 小时 | 按键次数 | 快捷键使用 |");
        sb.AppendLine("|------|------|----------|------------|");
        
        foreach (var session in sessions.OrderBy(s => s.Date).ThenBy(s => s.Hour))
        {
            var shortcuts = session.Shortcuts.Count > 0 ? string.Join(", ", session.Shortcuts.Take(3)) : "-";
            sb.AppendLine($"| {session.Date:yyyy-MM-dd} | {session.Hour}:00 | {session.TotalKeyPresses} | {shortcuts} |");
        }
        
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
    }
    
    private static void WriteMouseSessions(StringBuilder sb, List<Core.Entities.MouseSession> sessions)
    {
        sb.AppendLine("## 🖱️ 鼠标活动记录");
        sb.AppendLine();
        
        sb.AppendLine("| 日期 | 小时 | 左键 | 右键 | 滚动 | 移动距离 |");
        sb.AppendLine("|------|------|------|------|------|----------|");
        
        foreach (var session in sessions.OrderBy(s => s.Date).ThenBy(s => s.Hour))
        {
            sb.AppendLine($"| {session.Date:yyyy-MM-dd} | {session.Hour}:00 | {session.LeftClickCount} | {session.RightClickCount} | {session.ScrollCount} | {session.TotalDistance:F0}px |");
        }
        
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
    }
    
    private static void WriteWebSessions(StringBuilder sb, List<Core.Entities.WebSession> sessions, ExportOptions? options)
    {
        sb.AppendLine("## 🌐 浏览记录");
        sb.AppendLine();
        
        var groupedByDay = sessions.GroupBy(s => s.StartTime.Date).OrderByDescending(g => g.Key);
        
        foreach (var dayGroup in groupedByDay)
        {
            sb.AppendLine($"### {dayGroup.Key:yyyy-MM-dd}");
            sb.AppendLine();
            sb.AppendLine("| 时间 | 网站 | 页面标题 | 浏览器 | 时长 |");
            sb.AppendLine("|------|------|----------|--------|------|");
            
            foreach (var session in dayGroup.OrderBy(s => s.StartTime))
            {
                var url = options?.AnonymizeUrls == true ? AnonymizeUrl(session.Url) : session.Url;
                var title = Truncate(session.Title, 25);
                sb.AppendLine($"| {session.StartTime:HH:mm} | {session.Domain} | {title} | {session.Browser} | {FormatDuration(session.Duration)} |");
            }
            sb.AppendLine();
        }
        
        sb.AppendLine("---");
        sb.AppendLine();
    }
    
    private static void WriteDailyPatterns(StringBuilder sb, List<Core.Entities.DailyPattern> patterns)
    {
        sb.AppendLine("## 📅 每日模式分析");
        sb.AppendLine();
        
        sb.AppendLine("| 日期 | 活跃时间 | 空闲时间 | 生产力评分 | 专注块 | 深度工作 |");
        sb.AppendLine("|------|----------|----------|------------|--------|----------|");
        
        foreach (var pattern in patterns.OrderBy(p => p.Date))
        {
            sb.AppendLine($"| {pattern.Date:yyyy-MM-dd} | {FormatDuration(pattern.TotalActiveTime)} | {FormatDuration(pattern.TotalIdleTime)} | {pattern.ProductivityScore}/100 | {pattern.FocusBlocks.Count} | {FormatDuration(pattern.DeepWorkTime)} |");
        }
        
        sb.AppendLine();
    }
    
    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        }
        return $"{duration.Minutes}m";
    }
    
    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            return text ?? "";
        }
        return text[..maxLength] + "...";
    }
    
    private static string AnonymizeUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            return $"{uri.Scheme}://{uri.Host}/***";
        }
        catch
        {
            return "***";
        }
    }
}
