using OfficeOpenXml;
using OfficeOpenXml.Style;
using PChabit.Core.Interfaces;

namespace PChabit.Infrastructure.Formatters;

public class ExcelExportFormatter : IExportFormatter
{
    public string Format => "excel";
    public string FileExtension => ".xlsx";
    public string MimeType => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public ExcelExportFormatter()
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
    }

    public async Task<string> FormatAsync(ExportData data, ExportOptions? options = null)
    {
        using var package = new ExcelPackage();
        
        AddSummarySheet(package, data);
        AddAppSessionsSheet(package, data, options);
        AddKeyboardSessionsSheet(package, data);
        AddMouseSessionsSheet(package, data);
        AddWebSessionsSheet(package, data, options);
        AddDailyPatternsSheet(package, data);
        AddTopApplicationsSheet(package, data);
        AddTopWebsitesSheet(package, data);
        
        var bytes = await package.GetAsByteArrayAsync();
        return Convert.ToBase64String(bytes);
    }

    private void AddSummarySheet(ExcelPackage package, ExportData data)
    {
        var sheet = package.Workbook.Worksheets.Add("概览");
        
        sheet.Cells["A1"].Value = "PChabit 数据导出报告";
        sheet.Cells["A1"].Style.Font.Size = 16;
        sheet.Cells["A1"].Style.Font.Bold = true;
        sheet.Cells["A1:D1"].Merge = true;
        
        sheet.Cells["A3"].Value = "导出时间";
        sheet.Cells["B3"].Value = data.ExportTime.ToString("yyyy-MM-dd HH:mm:ss");
        
        sheet.Cells["A4"].Value = "时间范围";
        sheet.Cells["B4"].Value = $"{data.StartTime:yyyy-MM-dd HH:mm:ss} - {data.EndTime:yyyy-MM-dd HH:mm:ss}";
        
        sheet.Cells["A5"].Value = "总时长(分钟)";
        sheet.Cells["B5"].Value = data.Duration.TotalMinutes;
        sheet.Cells["B5"].Style.Numberformat.Format = "0.00";
        
        if (data.Statistics != null)
        {
            var stats = data.Statistics;
            int row = 7;
            
            sheet.Cells[$"A{row}"].Value = "统计摘要";
            sheet.Cells[$"A{row}"].Style.Font.Bold = true;
            sheet.Cells[$"A{row}"].Style.Font.Size = 14;
            row++;
            
            var summaryData = new object[,]
            {
                { "应用会话总数", stats.TotalAppSessions },
                { "键盘会话总数", stats.TotalKeyboardSessions },
                { "鼠标会话总数", stats.TotalMouseSessions },
                { "网页会话总数", stats.TotalWebSessions },
                { "总活跃时间(分钟)", stats.TotalActiveTime.TotalMinutes },
                { "总空闲时间(分钟)", stats.TotalIdleTime.TotalMinutes },
                { "总按键次数", stats.TotalKeyPresses },
                { "总鼠标点击次数", stats.TotalMouseClicks },
                { "总网页访问数", stats.TotalWebPages },
                { "平均生产力评分", stats.AverageProductivityScore },
                { "专注块数量", stats.TotalFocusBlocks },
                { "深度工作时间(分钟)", stats.TotalDeepWorkTime.TotalMinutes }
            };
            
            for (int i = 0; i < summaryData.GetLength(0); i++)
            {
                sheet.Cells[$"A{row + i}"].Value = summaryData[i, 0];
                sheet.Cells[$"B{row + i}"].Value = summaryData[i, 1];
                if (summaryData[i, 1] is double)
                {
                    sheet.Cells[$"B{row + i}"].Style.Numberformat.Format = "0.00";
                }
            }
        }
        
        sheet.Column(1).Width = 20;
        sheet.Column(2).Width = 50;
    }

    private void AddAppSessionsSheet(ExcelPackage package, ExportData data, ExportOptions? options)
    {
        if (!data.AppSessions.Any()) return;
        
        var sheet = package.Workbook.Worksheets.Add("应用会话");
        
        var headers = new[] { "ID", "进程名", "窗口标题", "分类", "开始时间", "结束时间", "持续时间(分钟)", "活跃时间(分钟)" };
        for (int i = 0; i < headers.Length; i++)
        {
            sheet.Cells[1, i + 1].Value = headers[i];
            sheet.Cells[1, i + 1].Style.Font.Bold = true;
            sheet.Cells[1, i + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
            sheet.Cells[1, i + 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
        }
        
        var sessions = options?.MaxItems > 0 
            ? data.AppSessions.Take(options.MaxItems) 
            : data.AppSessions;
        
        int row = 2;
        foreach (var session in sessions)
        {
            sheet.Cells[row, 1].Value = session.Id;
            sheet.Cells[row, 2].Value = session.ProcessName;
            sheet.Cells[row, 3].Value = session.WindowTitle ?? "";
            sheet.Cells[row, 4].Value = session.Category ?? "Unknown";
            sheet.Cells[row, 5].Value = session.StartTime.ToString("yyyy-MM-dd HH:mm:ss");
            sheet.Cells[row, 6].Value = session.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "";
            sheet.Cells[row, 7].Value = session.Duration.TotalMinutes;
            sheet.Cells[row, 8].Value = session.ActiveDuration.TotalMinutes;
            sheet.Cells[row, 7].Style.Numberformat.Format = "0.00";
            sheet.Cells[row, 8].Style.Numberformat.Format = "0.00";
            row++;
        }
        
        sheet.Cells[sheet.Dimension.Address].AutoFitColumns();
    }

    private void AddKeyboardSessionsSheet(ExcelPackage package, ExportData data)
    {
        if (!data.KeyboardSessions.Any()) return;
        
        var sheet = package.Workbook.Worksheets.Add("键盘会话");
        
        var headers = new[] { "ID", "日期", "小时", "总按键次数", "快捷键" };
        for (int i = 0; i < headers.Length; i++)
        {
            sheet.Cells[1, i + 1].Value = headers[i];
            sheet.Cells[1, i + 1].Style.Font.Bold = true;
            sheet.Cells[1, i + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
            sheet.Cells[1, i + 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
        }
        
        int row = 2;
        foreach (var session in data.KeyboardSessions)
        {
            sheet.Cells[row, 1].Value = session.Id;
            sheet.Cells[row, 2].Value = session.Date.ToString("yyyy-MM-dd");
            sheet.Cells[row, 3].Value = session.Hour;
            sheet.Cells[row, 4].Value = session.TotalKeyPresses;
            sheet.Cells[row, 5].Value = session.Shortcuts != null ? string.Join("; ", session.Shortcuts.Select(s => s.Shortcut)) : "";
            row++;
        }
        
        sheet.Cells[sheet.Dimension.Address].AutoFitColumns();
    }

    private void AddMouseSessionsSheet(ExcelPackage package, ExportData data)
    {
        if (!data.MouseSessions.Any()) return;
        
        var sheet = package.Workbook.Worksheets.Add("鼠标会话");
        
        var headers = new[] { "ID", "日期", "小时", "左键点击", "右键点击", "中键点击", "滚动次数", "移动距离" };
        for (int i = 0; i < headers.Length; i++)
        {
            sheet.Cells[1, i + 1].Value = headers[i];
            sheet.Cells[1, i + 1].Style.Font.Bold = true;
            sheet.Cells[1, i + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
            sheet.Cells[1, i + 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
        }
        
        int row = 2;
        foreach (var session in data.MouseSessions)
        {
            sheet.Cells[row, 1].Value = session.Id;
            sheet.Cells[row, 2].Value = session.Date.ToString("yyyy-MM-dd");
            sheet.Cells[row, 3].Value = session.Hour;
            sheet.Cells[row, 4].Value = session.LeftClickCount;
            sheet.Cells[row, 5].Value = session.RightClickCount;
            sheet.Cells[row, 6].Value = session.MiddleClickCount;
            sheet.Cells[row, 7].Value = session.ScrollCount;
            sheet.Cells[row, 8].Value = session.TotalDistance;
            sheet.Cells[row, 8].Style.Numberformat.Format = "0.00";
            row++;
        }
        
        sheet.Cells[sheet.Dimension.Address].AutoFitColumns();
    }

    private void AddWebSessionsSheet(ExcelPackage package, ExportData data, ExportOptions? options)
    {
        if (!data.WebSessions.Any()) return;
        
        var sheet = package.Workbook.Worksheets.Add("网页会话");
        
        var headers = new[] { "ID", "URL", "标题", "域名", "浏览器", "开始时间", "持续时间(分钟)" };
        for (int i = 0; i < headers.Length; i++)
        {
            sheet.Cells[1, i + 1].Value = headers[i];
            sheet.Cells[1, i + 1].Style.Font.Bold = true;
            sheet.Cells[1, i + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
            sheet.Cells[1, i + 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
        }
        
        var sessions = options?.MaxItems > 0 
            ? data.WebSessions.Take(options.MaxItems) 
            : data.WebSessions;
        
        int row = 2;
        foreach (var session in sessions)
        {
            var url = options?.AnonymizeUrls == true ? AnonymizeUrl(session.Url) : session.Url;
            sheet.Cells[row, 1].Value = session.Id;
            sheet.Cells[row, 2].Value = url;
            sheet.Cells[row, 3].Value = session.Title ?? "";
            sheet.Cells[row, 4].Value = session.Domain ?? "";
            sheet.Cells[row, 5].Value = session.Browser ?? "";
            sheet.Cells[row, 6].Value = session.StartTime.ToString("yyyy-MM-dd HH:mm:ss");
            sheet.Cells[row, 7].Value = session.Duration.TotalMinutes;
            sheet.Cells[row, 7].Style.Numberformat.Format = "0.00";
            row++;
        }
        
        sheet.Cells[sheet.Dimension.Address].AutoFitColumns();
    }

    private void AddDailyPatternsSheet(ExcelPackage package, ExportData data)
    {
        if (!data.DailyPatterns.Any()) return;
        
        var sheet = package.Workbook.Worksheets.Add("每日模式");
        
        var headers = new[] { "ID", "日期", "活跃时间(分钟)", "空闲时间(分钟)", "生产力评分", "中断次数", "深度工作时间(分钟)" };
        for (int i = 0; i < headers.Length; i++)
        {
            sheet.Cells[1, i + 1].Value = headers[i];
            sheet.Cells[1, i + 1].Style.Font.Bold = true;
            sheet.Cells[1, i + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
            sheet.Cells[1, i + 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
        }
        
        int row = 2;
        foreach (var pattern in data.DailyPatterns)
        {
            sheet.Cells[row, 1].Value = pattern.Id;
            sheet.Cells[row, 2].Value = pattern.Date.ToString("yyyy-MM-dd");
            sheet.Cells[row, 3].Value = pattern.TotalActiveTime.TotalMinutes;
            sheet.Cells[row, 4].Value = pattern.TotalIdleTime.TotalMinutes;
            sheet.Cells[row, 5].Value = pattern.ProductivityScore;
            sheet.Cells[row, 6].Value = pattern.InterruptionCount;
            sheet.Cells[row, 7].Value = pattern.DeepWorkTime.TotalMinutes;
            
            for (int col = 3; col <= 7; col++)
            {
                sheet.Cells[row, col].Style.Numberformat.Format = "0.00";
            }
            row++;
        }
        
        sheet.Cells[sheet.Dimension.Address].AutoFitColumns();
    }

    private void AddTopApplicationsSheet(ExcelPackage package, ExportData data)
    {
        if (data.Statistics?.TopApplications == null || !data.Statistics.TopApplications.Any()) return;
        
        var sheet = package.Workbook.Worksheets.Add("热门应用");
        
        var headers = new[] { "应用名称", "使用时间(分钟)" };
        for (int i = 0; i < headers.Length; i++)
        {
            sheet.Cells[1, i + 1].Value = headers[i];
            sheet.Cells[1, i + 1].Style.Font.Bold = true;
            sheet.Cells[1, i + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
            sheet.Cells[1, i + 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
        }
        
        int row = 2;
        foreach (var kvp in data.Statistics.TopApplications.OrderByDescending(x => x.Value).Take(20))
        {
            sheet.Cells[row, 1].Value = kvp.Key;
            sheet.Cells[row, 2].Value = kvp.Value.TotalMinutes;
            sheet.Cells[row, 2].Style.Numberformat.Format = "0.00";
            row++;
        }
        
        sheet.Cells[sheet.Dimension.Address].AutoFitColumns();
    }

    private void AddTopWebsitesSheet(ExcelPackage package, ExportData data)
    {
        if (data.Statistics?.TopWebsites == null || !data.Statistics.TopWebsites.Any()) return;
        
        var sheet = package.Workbook.Worksheets.Add("热门网站");
        
        var headers = new[] { "网站域名", "访问时间(分钟)" };
        for (int i = 0; i < headers.Length; i++)
        {
            sheet.Cells[1, i + 1].Value = headers[i];
            sheet.Cells[1, i + 1].Style.Font.Bold = true;
            sheet.Cells[1, i + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
            sheet.Cells[1, i + 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
        }
        
        int row = 2;
        foreach (var kvp in data.Statistics.TopWebsites.OrderByDescending(x => x.Value).Take(20))
        {
            sheet.Cells[row, 1].Value = kvp.Key;
            sheet.Cells[row, 2].Value = kvp.Value.TotalMinutes;
            sheet.Cells[row, 2].Style.Numberformat.Format = "0.00";
            row++;
        }
        
        sheet.Cells[sheet.Dimension.Address].AutoFitColumns();
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
