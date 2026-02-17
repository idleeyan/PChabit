using System.Text.Json;
using System.Text.Json.Serialization;
using PChabit.Core.Interfaces;

namespace PChabit.Infrastructure.Formatters;

public class JsonExportFormatter : IExportFormatter
{
    public string Format => "json";
    public string FileExtension => ".json";
    public string MimeType => "application/json";
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new TimeSpanJsonConverter() }
    };
    
    public Task<string> FormatAsync(ExportData data, ExportOptions? options = null)
    {
        var exportObject = CreateExportObject(data, options);
        var json = JsonSerializer.Serialize(exportObject, JsonOptions);
        return Task.FromResult(json);
    }
    
    private object CreateExportObject(ExportData data, ExportOptions? options)
    {
        return new
        {
            meta = new
            {
                exportTime = data.ExportTime,
                startTime = data.StartTime,
                endTime = data.EndTime,
                duration = data.Duration.TotalMinutes
            },
            statistics = data.Statistics != null ? new
            {
                totalAppSessions = data.Statistics.TotalAppSessions,
                totalKeyboardSessions = data.Statistics.TotalKeyboardSessions,
                totalMouseSessions = data.Statistics.TotalMouseSessions,
                totalWebSessions = data.Statistics.TotalWebSessions,
                totalActiveTime = data.Statistics.TotalActiveTime.TotalMinutes,
                totalIdleTime = data.Statistics.TotalIdleTime.TotalMinutes,
                totalKeyPresses = data.Statistics.TotalKeyPresses,
                totalMouseClicks = data.Statistics.TotalMouseClicks,
                totalWebPages = data.Statistics.TotalWebPages,
                topApplications = data.Statistics.TopApplications.ToDictionary(kv => kv.Key, kv => kv.Value.TotalMinutes),
                topWebsites = data.Statistics.TopWebsites.ToDictionary(kv => kv.Key, kv => kv.Value.TotalMinutes),
                topKeyCategories = data.Statistics.TopKeyCategories,
                averageProductivityScore = data.Statistics.AverageProductivityScore,
                totalFocusBlocks = data.Statistics.TotalFocusBlocks,
                totalDeepWorkTime = data.Statistics.TotalDeepWorkTime.TotalMinutes
            } : null,
            appSessions = data.AppSessions.Select(s => new
            {
                id = s.Id,
                processName = s.ProcessName,
                windowTitle = s.WindowTitle,
                category = s.Category ?? "Unknown",
                startTime = s.StartTime,
                endTime = s.EndTime,
                duration = s.Duration.TotalMinutes,
                activeDuration = s.ActiveDuration.TotalMinutes
            }),
            keyboardSessions = data.KeyboardSessions.Select(s => new
            {
                id = s.Id,
                date = s.Date,
                hour = s.Hour,
                totalKeyPresses = s.TotalKeyPresses,
                keyFrequency = s.KeyFrequency,
                shortcuts = s.Shortcuts
            }),
            mouseSessions = data.MouseSessions.Select(s => new
            {
                id = s.Id,
                date = s.Date,
                hour = s.Hour,
                leftClicks = s.LeftClickCount,
                rightClicks = s.RightClickCount,
                middleClicks = s.MiddleClickCount,
                scrollCount = s.ScrollCount,
                totalDistance = s.TotalDistance
            }),
            webSessions = data.WebSessions.Select(s => new
            {
                id = s.Id,
                url = options?.AnonymizeUrls == true ? AnonymizeUrl(s.Url) : s.Url,
                title = s.Title,
                domain = s.Domain,
                browser = s.Browser,
                startTime = s.StartTime,
                duration = s.Duration.TotalMinutes
            }),
            dailyPatterns = data.DailyPatterns.Select(p => new
            {
                id = p.Id,
                date = p.Date,
                totalActiveTime = p.TotalActiveTime.TotalMinutes,
                totalIdleTime = p.TotalIdleTime.TotalMinutes,
                productivityScore = p.ProductivityScore,
                interruptionCount = p.InterruptionCount,
                deepWorkTime = p.DeepWorkTime.TotalMinutes
            })
        };
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

internal class TimeSpanJsonConverter : JsonConverter<TimeSpan>
{
    public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return TimeSpan.FromMinutes(reader.GetDouble());
    }
    
    public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value.TotalMinutes);
    }
}
