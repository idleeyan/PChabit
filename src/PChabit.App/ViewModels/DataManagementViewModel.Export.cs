using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Data;
using PChabit.Infrastructure.Services;
using Serilog;

namespace PChabit.App.ViewModels;

public partial class DataManagementViewModel : ViewModelBase
{
    #region 数据导出

    [ObservableProperty]
    private DateTime _exportStartDate = DateTime.Today.AddDays(-30);

    [ObservableProperty]
    private DateTime _exportEndDate = DateTime.Today;

    public DateTimeOffset? ExportStartDateOffset
    {
        get => new DateTimeOffset(ExportStartDate);
        set { if (value.HasValue) ExportStartDate = value.Value.Date; }
    }

    public DateTimeOffset? ExportEndDateOffset
    {
        get => new DateTimeOffset(ExportEndDate);
        set { if (value.HasValue) ExportEndDate = value.Value.Date; }
    }

    [ObservableProperty]
    private string _exportFormat = "json";

    [ObservableProperty]
    private bool _isExporting;

    [ObservableProperty]
    private string _exportStatus = string.Empty;

    [ObservableProperty]
    private string _lastExportPath = string.Empty;

    public IReadOnlyList<string> ExportFormats { get; } = new[] { "json", "markdown", "csv", "ai-prompt" };

    #endregion
    #region 数据导出（统一用 IExportService）

    [RelayCommand]
    private async Task ExportDataAsync()
    {
        if (IsExporting) return;

        if (ExportEndDate < ExportStartDate)
        {
            ExportStatus = "结束日期不能早于开始日期";
            AddLog("错误", "结束日期不能早于开始日期");
            return;
        }

        Log.Information("开始导出数据");
        IsExporting = true;
        ExportStatus = "正在导出...";
        AddLog("导出", $"正在导出 {ExportStartDate:yyyy-MM-dd} 至 {ExportEndDate:yyyy-MM-dd} 的 {ExportFormat} 数据...");

        try
        {
            var ext = ExportFormat switch
            {
                "json" => ".json",
                "markdown" => ".md",
                "csv" => ".csv",
                "ai-prompt" => ".md",
                _ => ".txt"
            };

            var request = new ExportRequest
            {
                StartTime = ExportStartDate,
                EndTime = ExportEndDate.AddDays(1).AddSeconds(-1),
                Format = ExportFormat,
                Options = new ExportOptions
                {
                    IncludeMetadata = true,
                    IncludeStatistics = true,
                    IncludePatterns = true,
                    GroupByDay = true,
                    MaxItems = 100000
                }
            };

            var content = await _exportService.ExportAsync(request);

            var fileName = $"pchabit_export_{DateTime.Now:yyyyMMdd_HHmmss}{ext}";
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var filePath = Path.Combine(documentsPath, "PChabit", fileName);

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(filePath, content);

            LastExportPath = filePath;
            ExportStatus = $"导出成功！文件: {fileName} ({FormatSize(new FileInfo(filePath).Length)})";
            AddLog("成功", $"导出完成: {fileName}");
            Log.Information("数据已导出到: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            ExportStatus = $"导出失败: {ex.Message}";
            AddLog("错误", $"导出失败: {ex.Message}");
            Log.Error(ex, "导出数据失败");
        }
        finally
        {
            IsExporting = false;
        }
    }

    [RelayCommand]
    private async Task OpenExportFolderAsync()
    {
        try
        {
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var folder = Path.Combine(documentsPath, "PChabit");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            System.Diagnostics.Process.Start("explorer.exe", folder);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "打开导出文件夹失败");
            AddLog("错误", $"打开导出文件夹失败: {ex.Message}");
        }
        await Task.CompletedTask;
    }

    #endregion
}

