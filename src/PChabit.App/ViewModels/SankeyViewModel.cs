using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Serilog;
using PChabit.Application.Aggregators;

namespace PChabit.App.ViewModels;

public partial class SankeyViewModel : DbSafeViewModel<SankeyData?>
{
    private readonly SankeyAggregator _aggregator;

    public SankeyViewModel(SankeyAggregator aggregator) : base()
    {
        _aggregator = aggregator;
        Title = "桑基图";
    }

    [ObservableProperty]
    private DateTimeOffset? _startDate = DateTime.Today.AddDays(-6);

    [ObservableProperty]
    private DateTimeOffset? _endDate = DateTime.Today;

    [ObservableProperty]
    private int _topN = 15;

    [ObservableProperty]
    private SankeyData? _data;

    [ObservableProperty]
    private string _summaryText = "选择日期范围查看流向分析";

    // === DbSafeViewModel 抽象方法 ===

    protected override async Task<SankeyData?> LoadStatsOnBackgroundAsync()
    {
        var startDt = StartDate?.Date ?? DateTime.Today.AddDays(-6);
        var endDt = EndDate?.Date.AddDays(1) ?? DateTime.Today.AddDays(1);
        return await _aggregator.GetSankeyDataAsync(startDt, endDt, TopN);
    }

    protected override async Task ApplyStatsOnUIAsync(SankeyData? data)
    {
        Data = data;

        if (data == null)
        {
            SummaryText = "暂无数据";
            var sd = StartDate?.Date.ToString("yyyy-MM-dd") ?? "?";
            var ed = EndDate?.Date.ToString("yyyy-MM-dd") ?? "?";
            Log.Debug("[Sankey] 查询无数据: {Start} ~ {End}", sd, ed);
            return;
        }

        int totalLinks = data.Links?.Count ?? 0;
        int totalSwitch = data.Links?.Sum(l => l.SwitchCount) ?? 0;
        var showStart = StartDate?.Date.ToString("yyyy-MM-dd") ?? "?";
        var showEnd = EndDate?.Date.ToString("yyyy-MM-dd") ?? "?";
        SummaryText = $"总计 {totalLinks} 条流向, 总切换 {totalSwitch} 次（{showStart} ~ {showEnd}）";
    }

    partial void OnStartDateChanged(DateTimeOffset? value) => _ = LoadDataAsync();
    partial void OnEndDateChanged(DateTimeOffset? value) => _ = LoadDataAsync();
    partial void OnTopNChanged(int value) => _ = LoadDataAsync();

    // === 序列化方法（供 SankeyView.xaml.cs 调用） ===

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

        var dto = new { nodes, links };

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
