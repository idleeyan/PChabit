using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tai.Core.Interfaces;

namespace Tai.App.ViewModels;

public partial class ExportViewModel : ViewModelBase
{
    private readonly IExportService? _exportService;
    
    [ObservableProperty]
    private string _selectedFormat = "json";
    
    [ObservableProperty]
    private DateTime _startDate = DateTime.Today.AddDays(-7);
    
    [ObservableProperty]
    private DateTime _endDate = DateTime.Today;
    
    [ObservableProperty]
    private bool _includeAppSessions = true;
    
    [ObservableProperty]
    private bool _includeKeyboardSessions = true;
    
    [ObservableProperty]
    private bool _includeMouseSessions = true;
    
    [ObservableProperty]
    private bool _includeWebSessions = true;
    
    [ObservableProperty]
    private bool _includeStatistics = true;
    
    [ObservableProperty]
    private bool _anonymizeUrls = false;
    
    [ObservableProperty]
    private string _exportStatus = string.Empty;
    
    [ObservableProperty]
    private bool _isExporting;
    
    public ObservableCollection<FormatOption> FormatOptions { get; } = new()
    {
        new FormatOption { Key = "json", Label = "JSON", Description = "结构化数据，适合程序处理" },
        new FormatOption { Key = "markdown", Label = "Markdown", Description = "可读性强，适合人工阅读" },
        new FormatOption { Key = "ai-prompt", Label = "AI Prompt", Description = "AI 分析提示词格式" }
    };
    
    public ObservableCollection<ExportHistoryItem> ExportHistory { get; } = new();
    
    public ExportViewModel() : base()
    {
        Title = "数据导出";
    }
    
    public ExportViewModel(IExportService exportService) : this()
    {
        _exportService = exportService;
    }
    
    [RelayCommand]
    private async Task ExportAsync()
    {
        if (_exportService == null)
        {
            ExportStatus = "导出服务未初始化";
            return;
        }
        
        IsExporting = true;
        ExportStatus = "正在导出...";
        
        try
        {
            var dataTypes = ExportDataTypes.None;
            if (IncludeAppSessions) dataTypes |= ExportDataTypes.AppSessions;
            if (IncludeKeyboardSessions) dataTypes |= ExportDataTypes.KeyboardSessions;
            if (IncludeMouseSessions) dataTypes |= ExportDataTypes.MouseSessions;
            if (IncludeWebSessions) dataTypes |= ExportDataTypes.WebSessions;
            
            var request = new ExportRequest
            {
                StartTime = StartDate,
                EndTime = EndDate,
                Format = SelectedFormat,
                DataTypes = dataTypes,
                Options = new ExportOptions
                {
                    IncludeStatistics = IncludeStatistics,
                    AnonymizeUrls = AnonymizeUrls
                }
            };
            
            var result = await _exportService.ExportAsync(request);
            
            var fileName = $"tai-export-{DateTime.Now:yyyyMMdd-HHmmss}{GetFileExtension()}";
            var folderPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var filePath = Path.Combine(folderPath, fileName);
            
            await File.WriteAllTextAsync(filePath, result);
            ExportStatus = $"导出成功: {filePath}";
            
            ExportHistory.Insert(0, new ExportHistoryItem
            {
                Time = DateTime.Now,
                Format = SelectedFormat,
                FilePath = filePath,
                RecordCount = 0
            });
        }
        catch (Exception ex)
        {
            ExportStatus = $"导出失败: {ex.Message}";
        }
        finally
        {
            IsExporting = false;
        }
    }
    
    [RelayCommand]
    private void SelectAllDataTypes()
    {
        IncludeAppSessions = true;
        IncludeKeyboardSessions = true;
        IncludeMouseSessions = true;
        IncludeWebSessions = true;
    }
    
    [RelayCommand]
    private void DeselectAllDataTypes()
    {
        IncludeAppSessions = false;
        IncludeKeyboardSessions = false;
        IncludeMouseSessions = false;
        IncludeWebSessions = false;
    }
    
    private string GetFileExtension()
    {
        return SelectedFormat switch
        {
            "json" => ".json",
            "markdown" => ".md",
            "ai-prompt" => ".txt",
            _ => ".txt"
        };
    }
}

public class FormatOption
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}

public class ExportHistoryItem
{
    public DateTime Time { get; init; }
    public string Format { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public int RecordCount { get; init; }
}
