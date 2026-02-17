using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Serilog;
using PChabit.Application.Aggregators;

namespace PChabit.App.ViewModels;

public partial class SankeyViewModel : ViewModelBase
{
    private readonly SankeyAggregator _aggregator;
    
    [ObservableProperty]
    private DateTimeOffset _startDate = DateTimeOffset.Now.AddDays(-7);
    
    [ObservableProperty]
    private DateTimeOffset _endDate = DateTimeOffset.Now;
    
    [ObservableProperty]
    private int _topN = 10;
    
    [ObservableProperty]
    private string _summaryText = "请选择日期范围后点击刷新";
    
    public SankeyViewModel(SankeyAggregator aggregator) : base()
    {
        _aggregator = aggregator;
        Title = "应用流向";
    }
    
    public async Task<SankeyData?> LoadDataAsync()
    {
        try
        {
            RunOnUIThread(() => IsLoading = true);
            
            var startDateTime = StartDate.DateTime.Date;
            var endDateTime = EndDate.DateTime.Date.AddDays(1).AddSeconds(-1);
            
            Log.Information("[SankeyViewModel] 加载数据: {StartDate} - {EndDate}, TopN: {TopN}", 
                startDateTime, endDateTime, TopN);
            
            var data = await _aggregator.GetSankeyDataAsync(startDateTime, endDateTime, TopN);
            
            var totalSwitches = data.Links.Sum(l => l.SwitchCount);
            var uniqueApps = data.Nodes.Count;
            
            var summary = $"共 {totalSwitches} 次切换，涉及 {uniqueApps} 个应用";
            RunOnUIThread(() => SummaryText = summary);
            
            Log.Information("[SankeyViewModel] 数据加载完成: {Summary}", summary);
            
            return data;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[SankeyViewModel] 加载数据失败");
            RunOnUIThread(() => SummaryText = "加载数据失败: " + ex.Message);
            return null;
        }
        finally
        {
            RunOnUIThread(() => IsLoading = false);
        }
    }
    
    public string ToJson(SankeyData data)
    {
        var nodeMap = data.Nodes.ToDictionary(n => n.Id, n => n.Name);
        
        var nodes = data.Nodes.Select(n => new
        {
            name = n.Name,
            value = (int)n.TotalUsage.TotalMinutes,
            category = GetCategory(n.ProcessName)
        }).ToList();
        
        var links = data.Links.Select(l => new
        {
            source = nodeMap.TryGetValue(l.SourceId, out var srcName) ? srcName : "未知",
            target = nodeMap.TryGetValue(l.TargetId, out var tgtName) ? tgtName : "未知",
            value = l.SwitchCount
        }).Where(l => l.source != l.target).ToList();
        
        var dto = new
        {
            nodes = nodes,
            links = links
        };
        
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        
        return JsonSerializer.Serialize(dto, options);
    }
    
    private static string GetCategory(string? processName)
    {
        if (string.IsNullOrEmpty(processName)) return "其他";
        
        return processName.ToLower() switch
        {
            var p when p.Contains("code") || p.Contains("visual studio") || p.Contains("idea") => "开发",
            var p when p.Contains("chrome") || p.Contains("edge") || p.Contains("firefox") => "浏览",
            var p when p.Contains("slack") || p.Contains("teams") || p.Contains("wechat") => "沟通",
            var p when p.Contains("excel") || p.Contains("word") || p.Contains("powerpoint") => "办公",
            _ => "其他"
        };
    }
}
