namespace Tai.Core.Interfaces;

public interface IExportService
{
    Task<string> ExportAsync(ExportRequest request, CancellationToken cancellationToken = default);
    Task ExportToFileAsync(ExportRequest request, string filePath, CancellationToken cancellationToken = default);
    IEnumerable<string> GetSupportedFormats();
}

public interface IExportFormatter
{
    string Format { get; }
    string FileExtension { get; }
    string MimeType { get; }
    
    Task<string> FormatAsync(ExportData data, ExportOptions? options = null);
}

public class ExportRequest
{
    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; init; }
    public string Format { get; init; } = "json";
    public ExportOptions Options { get; init; } = new();
    public ExportDataTypes DataTypes { get; init; } = ExportDataTypes.All;
}

public class ExportOptions
{
    public bool IncludeMetadata { get; set; } = true;
    public bool IncludeStatistics { get; set; } = true;
    public bool IncludePatterns { get; set; } = true;
    public bool AnonymizeUrls { get; set; } = false;
    public bool GroupByDay { get; set; } = true;
    public int MaxItems { get; set; } = 1000;
    public string? TimeZone { get; set; }
}

[Flags]
public enum ExportDataTypes
{
    None = 0,
    AppSessions = 1,
    KeyboardSessions = 2,
    MouseSessions = 4,
    WebSessions = 8,
    DailyPatterns = 16,
    All = AppSessions | KeyboardSessions | MouseSessions | WebSessions | DailyPatterns
}
