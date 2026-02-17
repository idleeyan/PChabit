using System.Text;
using PChabit.Core.Interfaces;

namespace PChabit.Infrastructure.Formatters;

public class AiPromptExportFormatter : IExportFormatter
{
    public string Format => "ai-prompt";
    public string FileExtension => ".txt";
    public string MimeType => "text/plain";
    
    public Task<string> FormatAsync(ExportData data, ExportOptions? options = null)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("# 用户习惯数据分析提示词");
        sb.AppendLine();
        sb.AppendLine("以下是用户在指定时间段内的电脑使用习惯数据，请帮助分析用户的行为模式、工作效率和潜在改进建议。");
        sb.AppendLine();
        
        WriteContextSection(sb, data);
        WriteSummarySection(sb, data);
        WriteDetailedDataSection(sb, data, options);
        WriteAnalysisPromptSection(sb);
        
        return Task.FromResult(sb.ToString());
    }
    
    private static void WriteContextSection(StringBuilder sb, ExportData data)
    {
        sb.AppendLine("## 数据上下文");
        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine($"导出时间: {data.ExportTime:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"统计周期: {data.StartTime:yyyy-MM-dd} 至 {data.EndTime:yyyy-MM-dd}");
        sb.AppendLine($"总时长: {FormatDuration(data.Duration)}");
        sb.AppendLine("```");
        sb.AppendLine();
    }
    
    private static void WriteSummarySection(StringBuilder sb, ExportData data)
    {
        if (data.Statistics == null) return;
        
        var stats = data.Statistics;
        
        sb.AppendLine("## 数据摘要");
        sb.AppendLine();
        sb.AppendLine("### 整体活动");
        sb.AppendLine("```");
        sb.AppendLine($"应用会话: {stats.TotalAppSessions} 次");
        sb.AppendLine($"键盘会话: {stats.TotalKeyboardSessions} 次");
        sb.AppendLine($"鼠标会话: {stats.TotalMouseSessions} 次");
        sb.AppendLine($"网页浏览: {stats.TotalWebSessions} 次");
        sb.AppendLine($"总按键: {stats.TotalKeyPresses:N0} 次");
        sb.AppendLine($"总点击: {stats.TotalMouseClicks:N0} 次");
        sb.AppendLine("```");
        sb.AppendLine();
        
        sb.AppendLine("### 时间分布");
        sb.AppendLine("```");
        sb.AppendLine($"活跃时间: {FormatDuration(stats.TotalActiveTime)}");
        sb.AppendLine($"空闲时间: {FormatDuration(stats.TotalIdleTime)}");
        sb.AppendLine($"深度工作: {FormatDuration(stats.TotalDeepWorkTime)}");
        sb.AppendLine($"专注块数: {stats.TotalFocusBlocks}");
        sb.AppendLine($"生产力评分: {stats.AverageProductivityScore:F1}/100");
        sb.AppendLine("```");
        sb.AppendLine();
        
        if (stats.TopApplications.Count > 0)
        {
            sb.AppendLine("### 应用使用排行");
            sb.AppendLine("```");
            var rank = 1;
            foreach (var app in stats.TopApplications.Take(5))
            {
                sb.AppendLine($"{rank}. {app.Key}: {FormatDuration(app.Value)}");
                rank++;
            }
            sb.AppendLine("```");
            sb.AppendLine();
        }
        
        if (stats.TopWebsites.Count > 0)
        {
            sb.AppendLine("### 网站访问排行");
            sb.AppendLine("```");
            var rank = 1;
            foreach (var site in stats.TopWebsites.Take(5))
            {
                sb.AppendLine($"{rank}. {site.Key}: {FormatDuration(site.Value)}");
                rank++;
            }
            sb.AppendLine("```");
            sb.AppendLine();
        }
    }
    
    private static void WriteDetailedDataSection(StringBuilder sb, ExportData data, ExportOptions? options)
    {
        sb.AppendLine("## 详细数据");
        sb.AppendLine();
        
        if (data.AppSessions.Count > 0)
        {
            sb.AppendLine("### 应用使用详情");
            sb.AppendLine("```json");
            sb.AppendLine("[");
            
            var appData = data.AppSessions
                .GroupBy(s => s.ProcessName)
                .Select(g => new
                {
                    process = g.Key,
                    category = g.First().Category.ToString(),
                    totalDuration = FormatDuration(TimeSpan.FromMinutes(g.Sum(s => s.Duration.TotalMinutes))),
                    sessionCount = g.Count(),
                    avgDuration = FormatDuration(TimeSpan.FromMinutes(g.Average(s => s.Duration.TotalMinutes)))
                })
                .OrderByDescending(a => a.sessionCount)
                .Take(20);
            
            var items = appData.Select(a => 
                $"  {{\"process\": \"{a.process}\", \"category\": \"{a.category}\", \"totalDuration\": \"{a.totalDuration}\", \"sessions\": {a.sessionCount}, \"avgDuration\": \"{a.avgDuration}\"}}");
            
            sb.AppendLine(string.Join(",\n", items));
            sb.AppendLine("]");
            sb.AppendLine("```");
            sb.AppendLine();
        }
        
        if (data.WebSessions.Count > 0)
        {
            sb.AppendLine("### 网站访问详情");
            sb.AppendLine("```json");
            sb.AppendLine("[");
            
            var webData = data.WebSessions
                .GroupBy(s => s.Domain)
                .Select(g => new
                {
                    domain = g.Key,
                    browser = g.First().Browser,
                    totalDuration = FormatDuration(TimeSpan.FromMinutes(g.Sum(s => s.Duration.TotalMinutes))),
                    visitCount = g.Count(),
                    avgDuration = FormatDuration(TimeSpan.FromMinutes(g.Average(s => s.Duration.TotalMinutes)))
                })
                .OrderByDescending(w => w.visitCount)
                .Take(20);
            
            var items = webData.Select(w => 
                $"  {{\"domain\": \"{w.domain}\", \"browser\": \"{w.browser}\", \"totalDuration\": \"{w.totalDuration}\", \"visits\": {w.visitCount}, \"avgDuration\": \"{w.avgDuration}\"}}");
            
            sb.AppendLine(string.Join(",\n", items));
            sb.AppendLine("]");
            sb.AppendLine("```");
            sb.AppendLine();
        }
        
        if (data.KeyboardSessions.Count > 0)
        {
            sb.AppendLine("### 键盘活动模式");
            sb.AppendLine("```");
            
            var hourlyKeys = data.KeyboardSessions
                .GroupBy(s => s.Hour)
                .OrderBy(g => g.Key)
                .Select(g => $"  {g.Key:D2}:00 - {g.Sum(s => s.TotalKeyPresses)} 按键");
            
            sb.AppendLine("按键分布 (按小时):");
            sb.AppendLine(string.Join("\n", hourlyKeys));
            
            var allShortcuts = data.KeyboardSessions
                .SelectMany(s => s.Shortcuts)
                .GroupBy(s => s)
                .OrderByDescending(g => g.Count())
                .Take(10);
            
            if (allShortcuts.Any())
            {
                sb.AppendLine();
                sb.AppendLine("常用快捷键:");
                foreach (var shortcut in allShortcuts)
                {
                    sb.AppendLine($"  - {shortcut.Key}: {shortcut.Count()} 次");
                }
            }
            
            sb.AppendLine("```");
            sb.AppendLine();
        }
        
        if (data.DailyPatterns.Count > 0)
        {
            sb.AppendLine("### 每日模式");
            sb.AppendLine("```");
            foreach (var pattern in data.DailyPatterns.OrderBy(p => p.Date))
            {
                sb.AppendLine($"{pattern.Date:yyyy-MM-dd}:");
                sb.AppendLine($"  活跃: {FormatDuration(pattern.TotalActiveTime)}, 空闲: {FormatDuration(pattern.TotalIdleTime)}");
                sb.AppendLine($"  生产力: {pattern.ProductivityScore}/100, 专注块: {pattern.FocusBlocks.Count}");
            }
            sb.AppendLine("```");
            sb.AppendLine();
        }
    }
    
    private static void WriteAnalysisPromptSection(StringBuilder sb)
    {
        sb.AppendLine("## 分析请求");
        sb.AppendLine();
        sb.AppendLine("请基于以上数据，提供以下分析：");
        sb.AppendLine();
        sb.AppendLine("1. **行为模式分析**");
        sb.AppendLine("   - 用户的主要工作/娱乐模式是什么？");
        sb.AppendLine("   - 是否存在明显的时间使用规律？");
        sb.AppendLine("   - 哪些应用/网站占用了最多时间？是否合理？");
        sb.AppendLine();
        sb.AppendLine("2. **效率评估**");
        sb.AppendLine("   - 生产力评分的合理性分析");
        sb.AppendLine("   - 深度工作时间的分布和质量");
        sb.AppendLine("   - 可能的时间浪费点");
        sb.AppendLine();
        sb.AppendLine("3. **改进建议**");
        sb.AppendLine("   - 如何提高工作效率？");
        sb.AppendLine("   - 如何优化时间分配？");
        sb.AppendLine("   - 有哪些健康使用习惯的建议？");
        sb.AppendLine();
        sb.AppendLine("4. **个性化洞察**");
        sb.AppendLine("   - 用户的工作/生活平衡状况");
        sb.AppendLine("   - 潜在的风险信号（如过度使用某些应用）");
        sb.AppendLine("   - 值得保持的好习惯");
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
}
