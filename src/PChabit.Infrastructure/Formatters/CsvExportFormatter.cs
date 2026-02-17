using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using PChabit.Core.Interfaces;

namespace PChabit.Infrastructure.Formatters;

public class CsvExportFormatter : IExportFormatter
{
    public string Format => "csv";
    public string FileExtension => ".csv";
    public string MimeType => "text/csv";

    public Task<string> FormatAsync(ExportData data, ExportOptions? options = null)
    {
        var sb = new StringBuilder();
        
        using var writer = new StringWriter(sb);
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
        
        WriteMetadata(csv, data);
        WriteStatistics(csv, data);
        WriteAppSessions(csv, data, options);
        WriteKeyboardSessions(csv, data);
        WriteMouseSessions(csv, data);
        WriteWebSessions(csv, data, options);
        WriteDailyPatterns(csv, data);
        
        return Task.FromResult(sb.ToString());
    }

    private void WriteMetadata(CsvWriter csv, ExportData data)
    {
        csv.WriteComment($"导出时间: {data.ExportTime:yyyy-MM-dd HH:mm:ss}");
        csv.NextRecord();
        csv.WriteComment($"时间范围: {data.StartTime:yyyy-MM-dd HH:mm:ss} - {data.EndTime:yyyy-MM-dd HH:mm:ss}");
        csv.NextRecord();
        csv.WriteComment($"总时长: {data.Duration.TotalMinutes:F2} 分钟");
        csv.NextRecord();
        csv.NextRecord();
    }

    private void WriteStatistics(CsvWriter csv, ExportData data)
    {
        if (data.Statistics == null) return;

        csv.WriteComment("=== 统计摘要 ===");
        csv.NextRecord();
        
        csv.WriteField("指标");
        csv.WriteField("值");
        csv.NextRecord();
        
        csv.WriteField("应用会话总数");
        csv.WriteField(data.Statistics.TotalAppSessions);
        csv.NextRecord();
        
        csv.WriteField("键盘会话总数");
        csv.WriteField(data.Statistics.TotalKeyboardSessions);
        csv.NextRecord();
        
        csv.WriteField("鼠标会话总数");
        csv.WriteField(data.Statistics.TotalMouseSessions);
        csv.NextRecord();
        
        csv.WriteField("网页会话总数");
        csv.WriteField(data.Statistics.TotalWebSessions);
        csv.NextRecord();
        
        csv.WriteField("总活跃时间(分钟)");
        csv.WriteField(data.Statistics.TotalActiveTime.TotalMinutes.ToString("F2"));
        csv.NextRecord();
        
        csv.WriteField("总空闲时间(分钟)");
        csv.WriteField(data.Statistics.TotalIdleTime.TotalMinutes.ToString("F2"));
        csv.NextRecord();
        
        csv.WriteField("总按键次数");
        csv.WriteField(data.Statistics.TotalKeyPresses);
        csv.NextRecord();
        
        csv.WriteField("总鼠标点击次数");
        csv.WriteField(data.Statistics.TotalMouseClicks);
        csv.NextRecord();
        
        csv.WriteField("总网页访问数");
        csv.WriteField(data.Statistics.TotalWebPages);
        csv.NextRecord();
        
        csv.WriteField("平均生产力评分");
        csv.WriteField(data.Statistics.AverageProductivityScore.ToString("F2"));
        csv.NextRecord();
        
        csv.WriteField("专注块数量");
        csv.WriteField(data.Statistics.TotalFocusBlocks);
        csv.NextRecord();
        
        csv.WriteField("深度工作时间(分钟)");
        csv.WriteField(data.Statistics.TotalDeepWorkTime.TotalMinutes.ToString("F2"));
        csv.NextRecord();
        
        csv.NextRecord();
    }

    private void WriteAppSessions(CsvWriter csv, ExportData data, ExportOptions? options)
    {
        if (!data.AppSessions.Any()) return;

        csv.WriteComment("=== 应用会话 ===");
        csv.NextRecord();
        
        csv.WriteField("ID");
        csv.WriteField("进程名");
        csv.WriteField("窗口标题");
        csv.WriteField("分类");
        csv.WriteField("开始时间");
        csv.WriteField("结束时间");
        csv.WriteField("持续时间(分钟)");
        csv.WriteField("活跃时间(分钟)");
        csv.NextRecord();

        var sessions = options?.MaxItems > 0 
            ? data.AppSessions.Take(options.MaxItems) 
            : data.AppSessions;

        foreach (var session in sessions)
        {
            csv.WriteField(session.Id);
            csv.WriteField(session.ProcessName);
            csv.WriteField(session.WindowTitle ?? "");
            csv.WriteField(session.Category ?? "Unknown");
            csv.WriteField(session.StartTime.ToString("yyyy-MM-dd HH:mm:ss"));
            csv.WriteField(session.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "");
            csv.WriteField(session.Duration.TotalMinutes.ToString("F2"));
            csv.WriteField(session.ActiveDuration.TotalMinutes.ToString("F2"));
            csv.NextRecord();
        }
        
        csv.NextRecord();
    }

    private void WriteKeyboardSessions(CsvWriter csv, ExportData data)
    {
        if (!data.KeyboardSessions.Any()) return;

        csv.WriteComment("=== 键盘会话 ===");
        csv.NextRecord();
        
        csv.WriteField("ID");
        csv.WriteField("日期");
        csv.WriteField("小时");
        csv.WriteField("总按键次数");
        csv.WriteField("快捷键");
        csv.NextRecord();

        foreach (var session in data.KeyboardSessions)
        {
            csv.WriteField(session.Id);
            csv.WriteField(session.Date.ToString("yyyy-MM-dd"));
            csv.WriteField(session.Hour);
            csv.WriteField(session.TotalKeyPresses);
            csv.WriteField(session.Shortcuts != null ? string.Join("; ", session.Shortcuts.Select(s => s.Shortcut)) : "");
            csv.NextRecord();
        }
        
        csv.NextRecord();
    }

    private void WriteMouseSessions(CsvWriter csv, ExportData data)
    {
        if (!data.MouseSessions.Any()) return;

        csv.WriteComment("=== 鼠标会话 ===");
        csv.NextRecord();
        
        csv.WriteField("ID");
        csv.WriteField("日期");
        csv.WriteField("小时");
        csv.WriteField("左键点击");
        csv.WriteField("右键点击");
        csv.WriteField("中键点击");
        csv.WriteField("滚动次数");
        csv.WriteField("移动距离");
        csv.NextRecord();

        foreach (var session in data.MouseSessions)
        {
            csv.WriteField(session.Id);
            csv.WriteField(session.Date.ToString("yyyy-MM-dd"));
            csv.WriteField(session.Hour);
            csv.WriteField(session.LeftClickCount);
            csv.WriteField(session.RightClickCount);
            csv.WriteField(session.MiddleClickCount);
            csv.WriteField(session.ScrollCount);
            csv.WriteField(session.TotalDistance.ToString("F2"));
            csv.NextRecord();
        }
        
        csv.NextRecord();
    }

    private void WriteWebSessions(CsvWriter csv, ExportData data, ExportOptions? options)
    {
        if (!data.WebSessions.Any()) return;

        csv.WriteComment("=== 网页会话 ===");
        csv.NextRecord();
        
        csv.WriteField("ID");
        csv.WriteField("URL");
        csv.WriteField("标题");
        csv.WriteField("域名");
        csv.WriteField("浏览器");
        csv.WriteField("开始时间");
        csv.WriteField("持续时间(分钟)");
        csv.NextRecord();

        var sessions = options?.MaxItems > 0 
            ? data.WebSessions.Take(options.MaxItems) 
            : data.WebSessions;

        foreach (var session in sessions)
        {
            var url = options?.AnonymizeUrls == true ? AnonymizeUrl(session.Url) : session.Url;
            csv.WriteField(session.Id);
            csv.WriteField(url);
            csv.WriteField(session.Title ?? "");
            csv.WriteField(session.Domain ?? "");
            csv.WriteField(session.Browser ?? "");
            csv.WriteField(session.StartTime.ToString("yyyy-MM-dd HH:mm:ss"));
            csv.WriteField(session.Duration.TotalMinutes.ToString("F2"));
            csv.NextRecord();
        }
        
        csv.NextRecord();
    }

    private void WriteDailyPatterns(CsvWriter csv, ExportData data)
    {
        if (!data.DailyPatterns.Any()) return;

        csv.WriteComment("=== 每日模式 ===");
        csv.NextRecord();
        
        csv.WriteField("ID");
        csv.WriteField("日期");
        csv.WriteField("活跃时间(分钟)");
        csv.WriteField("空闲时间(分钟)");
        csv.WriteField("生产力评分");
        csv.WriteField("中断次数");
        csv.WriteField("深度工作时间(分钟)");
        csv.NextRecord();

        foreach (var pattern in data.DailyPatterns)
        {
            csv.WriteField(pattern.Id);
            csv.WriteField(pattern.Date.ToString("yyyy-MM-dd"));
            csv.WriteField(pattern.TotalActiveTime.TotalMinutes.ToString("F2"));
            csv.WriteField(pattern.TotalIdleTime.TotalMinutes.ToString("F2"));
            csv.WriteField(pattern.ProductivityScore.ToString("F2"));
            csv.WriteField(pattern.InterruptionCount);
            csv.WriteField(pattern.DeepWorkTime.TotalMinutes.ToString("F2"));
            csv.NextRecord();
        }
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
