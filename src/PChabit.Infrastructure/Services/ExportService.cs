using System.Text;
using PChabit.Core.Interfaces;

namespace PChabit.Infrastructure.Services;

public class ExportService : IExportService
{
    private readonly Dictionary<string, IExportFormatter> _formatters;
    private readonly IUnitOfWork _unitOfWork;
    
    public ExportService(IEnumerable<IExportFormatter> formatters, IUnitOfWork unitOfWork)
    {
        _formatters = formatters.ToDictionary(f => f.Format, f => f, StringComparer.OrdinalIgnoreCase);
        _unitOfWork = unitOfWork;
    }
    
    public async Task<string> ExportAsync(ExportRequest request, CancellationToken cancellationToken = default)
    {
        var formatter = GetFormatter(request.Format);
        var data = await CollectDataAsync(request, cancellationToken);
        return await formatter.FormatAsync(data, request.Options);
    }
    
    public async Task ExportToFileAsync(ExportRequest request, string filePath, CancellationToken cancellationToken = default)
    {
        var content = await ExportAsync(request, cancellationToken);
        await File.WriteAllTextAsync(filePath, content, Encoding.UTF8, cancellationToken);
    }
    
    public IEnumerable<string> GetSupportedFormats()
    {
        return _formatters.Keys;
    }
    
    private IExportFormatter GetFormatter(string format)
    {
        if (!_formatters.TryGetValue(format, out var formatter))
        {
            throw new NotSupportedException($"不支持的导出格式: {format}。支持的格式: {string.Join(", ", _formatters.Keys)}");
        }
        return formatter;
    }
    
    private async Task<ExportData> CollectDataAsync(ExportRequest request, CancellationToken cancellationToken)
    {
        var data = new ExportData
        {
            ExportTime = DateTime.Now,
            StartTime = request.StartTime,
            EndTime = request.EndTime
        };
        
        if (request.DataTypes.HasFlag(ExportDataTypes.AppSessions))
        {
            var sessions = await _unitOfWork.AppSessions.FindAsync(s => s.StartTime >= request.StartTime && s.StartTime <= request.EndTime);
            data.AppSessions.AddRange(sessions.Take(request.Options.MaxItems));
        }
        
        if (request.DataTypes.HasFlag(ExportDataTypes.KeyboardSessions))
        {
            var sessions = await _unitOfWork.KeyboardSessions.FindAsync(s => s.Date >= request.StartTime.Date && s.Date <= request.EndTime.Date);
            data.KeyboardSessions.AddRange(sessions.Take(request.Options.MaxItems));
        }
        
        if (request.DataTypes.HasFlag(ExportDataTypes.MouseSessions))
        {
            var sessions = await _unitOfWork.MouseSessions.FindAsync(s => s.Date >= request.StartTime.Date && s.Date <= request.EndTime.Date);
            data.MouseSessions.AddRange(sessions.Take(request.Options.MaxItems));
        }
        
        if (request.DataTypes.HasFlag(ExportDataTypes.WebSessions))
        {
            var sessions = await _unitOfWork.WebSessions.FindAsync(s => s.StartTime >= request.StartTime && s.StartTime <= request.EndTime);
            data.WebSessions.AddRange(sessions.Take(request.Options.MaxItems));
        }
        
        if (request.DataTypes.HasFlag(ExportDataTypes.DailyPatterns))
        {
            var patterns = await _unitOfWork.DailyPatterns.FindAsync(p => p.Date >= request.StartTime.Date && p.Date <= request.EndTime.Date);
            data.DailyPatterns.AddRange(patterns);
        }
        
        if (request.Options.IncludeStatistics)
        {
            data.Statistics = CalculateStatistics(data);
        }
        
        return data;
    }
    
    private static ExportStatistics CalculateStatistics(ExportData data)
    {
        var topApps = data.AppSessions
            .GroupBy(s => s.ProcessName)
            .OrderByDescending(g => g.Sum(s => s.Duration.TotalMinutes))
            .Take(10)
            .ToDictionary(g => g.Key, g => TimeSpan.FromMinutes(g.Sum(s => s.Duration.TotalMinutes)));
        
        var topWebsites = data.WebSessions
            .GroupBy(s => s.Domain)
            .OrderByDescending(g => g.Sum(s => s.Duration.TotalMinutes))
            .Take(10)
            .ToDictionary(g => g.Key, g => TimeSpan.FromMinutes(g.Sum(s => s.Duration.TotalMinutes)));
        
        var topKeyCategories = data.KeyboardSessions
            .SelectMany(s => s.KeyCategoryFrequency)
            .GroupBy(kv => kv.Key)
            .OrderByDescending(g => g.Sum(kv => kv.Value))
            .Take(10)
            .ToDictionary(g => g.Key, g => g.Sum(kv => kv.Value));
        
        return new ExportStatistics
        {
            TotalAppSessions = data.AppSessions.Count,
            TotalKeyboardSessions = data.KeyboardSessions.Count,
            TotalMouseSessions = data.MouseSessions.Count,
            TotalWebSessions = data.WebSessions.Count,
            TotalActiveTime = data.DailyPatterns.Aggregate(TimeSpan.Zero, (sum, p) => sum + p.TotalActiveTime),
            TotalIdleTime = data.DailyPatterns.Aggregate(TimeSpan.Zero, (sum, p) => sum + p.TotalIdleTime),
            TotalKeyPresses = data.KeyboardSessions.Sum(s => s.TotalKeyPresses),
            TotalMouseClicks = data.MouseSessions.Sum(s => s.LeftClickCount + s.RightClickCount + s.MiddleClickCount),
            TotalWebPages = data.WebSessions.Count,
            TopApplications = topApps,
            TopWebsites = topWebsites,
            TopKeyCategories = topKeyCategories,
            AverageProductivityScore = data.DailyPatterns.Any() ? data.DailyPatterns.Average(p => p.ProductivityScore) : 0,
            TotalFocusBlocks = data.DailyPatterns.Sum(p => p.FocusBlocks.Count),
            TotalDeepWorkTime = data.DailyPatterns.Aggregate(TimeSpan.Zero, (sum, p) => sum + p.DeepWorkTime)
        };
    }
}
