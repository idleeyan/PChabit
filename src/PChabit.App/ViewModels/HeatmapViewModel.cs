using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using PChabit.Application.Aggregators;

namespace PChabit.App.ViewModels;

public partial class HeatmapViewModel : ViewModelBase
{
    private readonly HeatmapAggregator _heatmapAggregator;
    private bool _isWeeklyLoadingInternal;
    private bool _isMonthlyLoadingInternal;

    [ObservableProperty]
    private DateTime _weekStart;

    [ObservableProperty]
    private DateTime _monthStart;

    [ObservableProperty]
    private string _selectedDimension = "活动强度";

    [ObservableProperty]
    private bool _isLoading;

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

    public List<string> Dimensions { get; } = new()
    {
        "活动强度",
        "应用使用",
        "键盘活动",
        "鼠标活动"
    };

    public string WeekRangeText => $"{WeekStart:MM月dd日} - {WeekStart.AddDays(6):MM月dd日}";
    public string MonthRangeText => $"{MonthStart:yyyy年MM月}";

    public HeatmapViewModel(HeatmapAggregator heatmapAggregator) : base()
    {
        Log.Information("[HeatmapViewModel] ===== 构造函数开始 =====");
        
        try
        {
            _heatmapAggregator = heatmapAggregator;
            Title = "热力图";

            var today = DateTime.Today;
            var diff = (int)today.DayOfWeek - (int)DayOfWeek.Monday;
            if (diff < 0) diff += 7;
            WeekStart = today.AddDays(-diff);
            MonthStart = new DateTime(today.Year, today.Month, 1);
            
            Log.Information("[HeatmapViewModel] WeekStart={WeekStart}, MonthStart={MonthStart}", 
                WeekStart.ToString("yyyy-MM-dd"), MonthStart.ToString("yyyy-MM-dd"));
            Log.Information("[HeatmapViewModel] ===== 构造函数完成 =====");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[HeatmapViewModel] 构造函数异常");
            throw;
        }
    }

    [RelayCommand]
    public async Task LoadWeeklyHeatmapAsync()
    {
        Log.Information("[HeatmapViewModel] LoadWeeklyHeatmapAsync 开始, _isWeeklyLoadingInternal={IsLoading}", _isWeeklyLoadingInternal);
        
        if (_isWeeklyLoadingInternal)
        {
            Log.Warning("[HeatmapViewModel] 周热力图已在加载中，跳过");
            return;
        }
        
        _isWeeklyLoadingInternal = true;
        IsLoading = true;
        WeeklyErrorMessage = string.Empty;
        
        Log.Information("[HeatmapViewModel] ===== 开始加载周热力图 =====");
        
        try
        {
            Log.Information("[HeatmapViewModel] 调用 _heatmapAggregator.GetWeeklyDailyHeatmapDataAsync");
            var data = await _heatmapAggregator.GetWeeklyDailyHeatmapDataAsync(WeekStart);
            
            Log.Information("[HeatmapViewModel] 收到周热力图数据: {Count} 条", data.Count);
            
            WeeklyHeatmapData.Clear();
            foreach (var cell in data)
            {
                WeeklyHeatmapData.Add(cell);
            }
            
            OnPropertyChanged(nameof(WeeklyHeatmapData));
            UpdateWeeklyStatistics();
            OnPropertyChanged(nameof(WeekRangeText));
            
            Log.Information("[HeatmapViewModel] 周热力图数据已添加到集合");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[HeatmapViewModel] 加载周热力图异常");
            WeeklyErrorMessage = $"加载失败: {ex.Message}";
        }
        finally
        {
            _isWeeklyLoadingInternal = false;
            IsLoading = _isMonthlyLoadingInternal;
            Log.Information("[HeatmapViewModel] ===== 周热力图加载完成 =====");
        }
    }

    [RelayCommand]
    public async Task LoadMonthlyHeatmapAsync()
    {
        Log.Information("[HeatmapViewModel] LoadMonthlyHeatmapAsync 开始, _isMonthlyLoadingInternal={IsLoading}", _isMonthlyLoadingInternal);
        
        if (_isMonthlyLoadingInternal)
        {
            Log.Warning("[HeatmapViewModel] 月热力图已在加载中，跳过");
            return;
        }
        
        _isMonthlyLoadingInternal = true;
        IsLoading = true;
        MonthlyErrorMessage = string.Empty;
        
        Log.Information("[HeatmapViewModel] ===== 开始加载月热力图 =====");
        
        try
        {
            Log.Information("[HeatmapViewModel] 调用 _heatmapAggregator.GetMonthlyHeatmapDataAsync");
            var data = await _heatmapAggregator.GetMonthlyHeatmapDataAsync(MonthStart);
            
            Log.Information("[HeatmapViewModel] 收到月热力图数据: {Count} 条", data.Count);
            
            MonthlyHeatmapData.Clear();
            foreach (var cell in data)
            {
                MonthlyHeatmapData.Add(cell);
            }
            
            OnPropertyChanged(nameof(MonthlyHeatmapData));
            UpdateMonthlyStatistics();
            OnPropertyChanged(nameof(MonthRangeText));
            
            Log.Information("[HeatmapViewModel] 月热力图数据已添加到集合");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[HeatmapViewModel] 加载月热力图异常");
            MonthlyErrorMessage = $"加载失败: {ex.Message}";
        }
        finally
        {
            _isMonthlyLoadingInternal = false;
            IsLoading = _isWeeklyLoadingInternal;
            Log.Information("[HeatmapViewModel] ===== 月热力图加载完成 =====");
        }
    }

    private void UpdateWeeklyStatistics()
    {
        if (WeeklyHeatmapData.Count == 0)
        {
            Log.Warning("[HeatmapViewModel] UpdateWeeklyStatistics: WeeklyHeatmapData 为空");
            return;
        }

        var totalSeconds = WeeklyHeatmapData.Sum(c => c.TotalUsage.TotalSeconds);
        var totalKeyPresses = WeeklyHeatmapData.Sum(c => c.KeyPresses);
        var totalMouseClicks = WeeklyHeatmapData.Sum(c => c.MouseClicks);
        var avgActivity = WeeklyHeatmapData.Where(c => c.HasData).DefaultIfEmpty().Average(c => c?.ActivityScore ?? 0);
        
        Log.Information("[HeatmapViewModel] [诊断] 周统计计算 - 总秒数:{TotalSeconds}, 总按键:{TotalKeyPresses}, 总点击:{TotalMouseClicks}, 平均活动:{AvgActivity}",
            totalSeconds, totalKeyPresses, totalMouseClicks, avgActivity);
        
        var nonZeroUsage = WeeklyHeatmapData.Count(c => c.TotalUsage.TotalSeconds > 0);
        var nonZeroKeyPresses = WeeklyHeatmapData.Count(c => c.KeyPresses > 0);
        Log.Information("[HeatmapViewModel] [诊断] 非零数据 - 有使用时长的单元格:{NonZeroUsage}, 有按键的单元格:{NonZeroKeyPresses}",
            nonZeroUsage, nonZeroKeyPresses);

        WeeklyTotalUsage = TimeSpan.FromSeconds(totalSeconds);
        WeeklyTotalKeyPresses = totalKeyPresses;
        WeeklyTotalMouseClicks = totalMouseClicks;
        WeeklyAverageActivity = avgActivity;
    }

    private void UpdateMonthlyStatistics()
    {
        if (MonthlyHeatmapData.Count == 0) return;

        MonthlyTotalUsage = TimeSpan.FromSeconds(MonthlyHeatmapData.Sum(c => c.TotalUsage.TotalSeconds));
        MonthlyTotalKeyPresses = MonthlyHeatmapData.Sum(c => c.KeyPresses);
        MonthlyTotalMouseClicks = MonthlyHeatmapData.Sum(c => c.MouseClicks);
        MonthlyAverageActivity = MonthlyHeatmapData.Where(c => c.HasData).DefaultIfEmpty().Average(c => c?.ActivityScore ?? 0);
        MonthlyActiveDays = MonthlyHeatmapData.Count(c => c.HasData);
    }

    [RelayCommand]
    public async Task PreviousWeek()
    {
        Log.Information("[HeatmapViewModel] PreviousWeek 被调用");
        WeekStart = WeekStart.AddDays(-7);
        await LoadWeeklyHeatmapAsync();
    }

    [RelayCommand]
    public async Task NextWeek()
    {
        Log.Information("[HeatmapViewModel] NextWeek 被调用");
        WeekStart = WeekStart.AddDays(7);
        await LoadWeeklyHeatmapAsync();
    }

    [RelayCommand]
    public async Task PreviousMonth()
    {
        Log.Information("[HeatmapViewModel] PreviousMonth 被调用");
        MonthStart = MonthStart.AddMonths(-1);
        await LoadMonthlyHeatmapAsync();
    }

    [RelayCommand]
    public async Task NextMonth()
    {
        Log.Information("[HeatmapViewModel] NextMonth 被调用");
        MonthStart = MonthStart.AddMonths(1);
        await LoadMonthlyHeatmapAsync();
    }

    public void SelectDailyCell(DailyHeatmapCell? cell)
    {
        SelectedDailyCell = cell;
    }

    public async Task InitializeAsync()
    {
        Log.Information("[HeatmapViewModel] ===== InitializeAsync 开始 =====");
        
        try
        {
            Log.Information("[HeatmapViewModel] 开始并行加载周和月数据");
            await Task.WhenAll(
                LoadWeeklyHeatmapAsync(),
                LoadMonthlyHeatmapAsync()
            );
            Log.Information("[HeatmapViewModel] 并行加载完成");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[HeatmapViewModel] InitializeAsync 异常");
        }
        
        Log.Information("[HeatmapViewModel] ===== InitializeAsync 完成 =====");
    }
}
