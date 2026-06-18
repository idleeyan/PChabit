using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using PChabit.Application.Aggregators;

namespace PChabit.App.ViewModels;

public partial class HeatmapViewModel : DbSafeViewModel<HeatmapViewModel.HeatmapData>
{
    private readonly HeatmapAggregator _heatmapAggregator;
    private bool _isWeeklyLoadingInternal;
    private bool _isMonthlyLoadingInternal;

    public HeatmapViewModel(HeatmapAggregator heatmapAggregator) : base()
    {
        _heatmapAggregator = heatmapAggregator;
        Title = "热力图";
    }

    [ObservableProperty]
    private DateTime _weekStart = DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek);

    [ObservableProperty]
    private DateTime _monthStart = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    [ObservableProperty]
    private string _selectedDimension = "活动强度";

    [ObservableProperty]
    private string _weeklyErrorMessage = string.Empty;
    [ObservableProperty]
    private string _monthlyErrorMessage = string.Empty;

    [ObservableProperty]
    private TimeSpan _weeklyTotalUsage;
    [ObservableProperty]
    private int _weeklyTotalKeyPresses;
    [ObservableProperty]
    private int _weeklyTotalMouseClicks;
    [ObservableProperty]
    private double _weeklyAverageActivity;

    [ObservableProperty]
    private TimeSpan _monthlyTotalUsage;
    [ObservableProperty]
    private int _monthlyTotalKeyPresses;
    [ObservableProperty]
    private int _monthlyTotalMouseClicks;
    [ObservableProperty]
    private double _monthlyAverageActivity;
    [ObservableProperty]
    private int _monthlyActiveDays;

    [ObservableProperty]
    private DailyHeatmapCell? _selectedDailyCell;

    public ObservableCollection<DailyHeatmapCell> WeeklyHeatmapData { get; } = new();
    public ObservableCollection<DailyHeatmapCell> MonthlyHeatmapData { get; } = new();

    public List<string> Dimensions { get; } = new() { "活动强度", "应用使用", "键盘活动", "鼠标活动" };
    public string WeekRangeText => $"{WeekStart:MM月dd日} - {WeekStart.AddDays(6):MM月dd日}";
    public string MonthRangeText => $"{MonthStart:yyyy年MM月}";

    // === Phase 1 中间数据 ===

    public sealed class HeatmapData
    {
        public List<DailyHeatmapCell> WeeklyCells = new();
        public List<DailyHeatmapCell> MonthlyCells = new();
    }

    // === DbSafeViewModel 抽象方法 ===

    protected override async Task<HeatmapData> LoadStatsOnBackgroundAsync()
    {
        var weekStart = WeekStart.Date;
        var monthStart = MonthStart.Date;

        var weeklyCells = await _heatmapAggregator.GetWeeklyDailyHeatmapDataAsync(weekStart);
        var monthlyCells = await _heatmapAggregator.GetMonthlyHeatmapDataAsync(monthStart);

        return new HeatmapData
        {
            WeeklyCells = weeklyCells,
            MonthlyCells = monthlyCells
        };
    }

    protected override async Task ApplyStatsOnUIAsync(HeatmapData data)
    {
        WeeklyHeatmapData.Clear();
        foreach (var cell in data.WeeklyCells) WeeklyHeatmapData.Add(cell);

        MonthlyHeatmapData.Clear();
        foreach (var cell in data.MonthlyCells) MonthlyHeatmapData.Add(cell);

        // 计算统计摘要
        if (data.WeeklyCells.Count > 0)
        {
            WeeklyTotalUsage = TimeSpan.FromMinutes(data.WeeklyCells.Sum(c => c.TotalUsage.TotalMinutes));
            WeeklyTotalKeyPresses = data.WeeklyCells.Sum(c => c.KeyPresses);
            WeeklyTotalMouseClicks = data.WeeklyCells.Sum(c => c.MouseClicks);
            WeeklyAverageActivity = data.WeeklyCells.Average(c => c.ActivityScore);
        }

        if (data.MonthlyCells.Count > 0)
        {
            MonthlyTotalUsage = TimeSpan.FromMinutes(data.MonthlyCells.Sum(c => c.TotalUsage.TotalMinutes));
            MonthlyTotalKeyPresses = data.MonthlyCells.Sum(c => c.KeyPresses);
            MonthlyTotalMouseClicks = data.MonthlyCells.Sum(c => c.MouseClicks);
            MonthlyAverageActivity = data.MonthlyCells.Average(c => c.ActivityScore);
            MonthlyActiveDays = data.MonthlyCells.Count(c => c.HasData);
        }
    }

    partial void OnWeekStartChanged(DateTime value) => _ = LoadDataAsync();
    partial void OnMonthStartChanged(DateTime value) => _ = LoadDataAsync();

    [RelayCommand]
    private async Task PreviousWeekAsync()
    {
        WeekStart = WeekStart.AddDays(-7);
        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task NextWeekAsync()
    {
        WeekStart = WeekStart.AddDays(7);
        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task PreviousMonthAsync()
    {
        MonthStart = MonthStart.AddMonths(-1);
        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task NextMonthAsync()
    {
        MonthStart = MonthStart.AddMonths(1);
        await LoadDataAsync();
    }
}
