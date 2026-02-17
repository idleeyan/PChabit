using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml.Media;
using Serilog;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Data;
using PChabit.Infrastructure.Services;

namespace PChabit.App.ViewModels;

public partial class MouseDetailsViewModel : ViewModelBase
{
    private readonly PChabitDbContext _dbContext;
    private readonly ICategoryService _categoryService;

    [ObservableProperty]
    private int _totalClicks;

    [ObservableProperty]
    private int _leftClicks;

    [ObservableProperty]
    private int _rightClicks;

    [ObservableProperty]
    private int _middleClicks;

    [ObservableProperty]
    private double _totalMoveDistance;

    [ObservableProperty]
    private int _totalScrolls;

    [ObservableProperty]
    private string _averageClickInterval = "0 秒";

    [ObservableProperty]
    private DateTime _selectedDate = DateTime.Today;

    [ObservableProperty]
    private string _selectedPeriod = "今日";

    public ObservableCollection<MouseStatCard> StatCards { get; } = new();
    public ObservableCollection<ProgramMouseStat> ProgramStats { get; } = new();
    public ObservableCollection<HourlyMouseStat> HourlyStats { get; } = new();
    public ObservableCollection<MouseClickDetail> ClickDetails { get; } = new();

    public MouseDetailsViewModel(PChabitDbContext dbContext, ICategoryService categoryService) : base()
    {
        _dbContext = dbContext;
        _categoryService = categoryService;
        Title = "鼠标点击详情";
    }

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        IsLoading = true;
        
        try
        {
            var today = DateTime.Today;
            DateTime startDate, endDate;

            // 根据选择的周期计算日期范围
            switch (SelectedPeriod)
            {
                case "今日":
                    startDate = today;
                    endDate = today.AddDays(1);
                    break;
                case "本周":
                    var diff = (int)today.DayOfWeek - (int)DayOfWeek.Monday;
                    if (diff < 0) diff += 7;
                    startDate = today.AddDays(-diff);
                    endDate = startDate.AddDays(7);
                    break;
                case "上周":
                    var lastWeekDiff = (int)today.DayOfWeek - (int)DayOfWeek.Monday;
                    if (lastWeekDiff < 0) lastWeekDiff += 7;
                    startDate = today.AddDays(-lastWeekDiff - 7);
                    endDate = startDate.AddDays(7);
                    break;
                default:
                    startDate = today;
                    endDate = today.AddDays(1);
                    break;
            }

            Log.Information("MouseDetailsViewModel: 加载数据，开始={StartDate}, 结束={EndDate}", startDate, endDate);

            // 加载鼠标会话数据
            List<Core.Entities.MouseSession> mouseSessions;
            try
            {
                if (SelectedPeriod == "今日")
                {
                    mouseSessions = await _dbContext.MouseSessions
                        .Where(s => s.Date == today)
                        .ToListAsync();
                }
                else
                {
                    mouseSessions = await _dbContext.MouseSessions
                        .Where(s => s.Date >= startDate && s.Date < endDate)
                        .ToListAsync();
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "加载 MouseSessions 失败");
                mouseSessions = new List<Core.Entities.MouseSession>();
            }

            // 计算统计数据
            CalculateStatistics(mouseSessions);
            
            // 按程序统计
            await CalculateProgramStatsAsync(startDate, endDate);
            
            // 按小时统计
            CalculateHourlyStats(mouseSessions);
            
            // 加载详细点击记录
            await LoadClickDetailsAsync(startDate, endDate);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "加载鼠标数据失败");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void CalculateStatistics(List<Core.Entities.MouseSession> sessions)
    {
        TotalClicks = sessions.Sum(s => s.LeftClickCount + s.RightClickCount + s.MiddleClickCount);
        LeftClicks = sessions.Sum(s => s.LeftClickCount);
        RightClicks = sessions.Sum(s => s.RightClickCount);
        MiddleClicks = sessions.Sum(s => s.MiddleClickCount);
        TotalMoveDistance = sessions.Sum(s => s.TotalMoveDistance);
        TotalScrolls = sessions.Sum(s => s.ScrollCount);

        // 计算平均点击间隔
        if (TotalClicks > 1)
        {
            var totalMinutes = sessions.Sum(s => 60); // 每小时会话按 60 分钟计算
            var avgSeconds = (totalMinutes * 60.0) / TotalClicks;
            AverageClickInterval = $"{avgSeconds:F1} 秒";
        }
        else
        {
            AverageClickInterval = "无数据";
        }

        // 更新统计卡片
        StatCards.Clear();
        StatCards.Add(new MouseStatCard
        {
            Title = "总点击次数",
            Value = TotalClicks.ToString("N0"),
            Subtitle = "次",
            Icon = "\uE962",
            Color = CreateBrush("#0078D4")
        });
        StatCards.Add(new MouseStatCard
        {
            Title = "左键点击",
            Value = LeftClicks.ToString("N0"),
            Subtitle = "次",
            Icon = "\uE962",
            Color = CreateBrush("#107C10")
        });
        StatCards.Add(new MouseStatCard
        {
            Title = "右键点击",
            Value = RightClicks.ToString("N0"),
            Subtitle = "次",
            Icon = "\uE962",
            Color = CreateBrush("#E81123")
        });
        StatCards.Add(new MouseStatCard
        {
            Title = "中键点击",
            Value = MiddleClicks.ToString("N0"),
            Subtitle = "次",
            Icon = "\uE962",
            Color = CreateBrush("#FF8C00")
        });
        StatCards.Add(new MouseStatCard
        {
            Title = "移动距离",
            Value = $"{TotalMoveDistance:F0}",
            Subtitle = "像素",
            Icon = "\uE945",
            Color = CreateBrush("#8764B8")
        });
        StatCards.Add(new MouseStatCard
        {
            Title = "滚动次数",
            Value = TotalScrolls.ToString("N0"),
            Subtitle = "次",
            Icon = "\uE931",
            Color = CreateBrush("#00B7C3")
        });
    }

    private async Task CalculateProgramStatsAsync(DateTime startDate, DateTime endDate)
    {
        ProgramStats.Clear();

        try
        {
            // 获取应用会话数据来关联程序名
            var appSessions = await _dbContext.AppSessions
                .Where(s => s.StartTime >= startDate && s.StartTime < endDate)
                .OrderBy(s => s.StartTime)
                .ToListAsync();

            // 按程序名分组统计
            var programGroups = appSessions
                .GroupBy(s => s.ProcessName)
                .Select(g => new
                {
                    ProcessName = g.Key,
                    Duration = TimeSpan.FromTicks(g.Sum(s => ((s.EndTime ?? s.StartTime) - s.StartTime).Ticks))
                })
                .OrderByDescending(g => g.Duration)
                .Take(10)
                .ToList();

            // 获取分类信息
            var mappings = await _categoryService.GetAllMappingsAsync();

            foreach (var program in programGroups)
            {
                var categoryMapping = mappings
                    .FirstOrDefault(m => m.ProcessName.Equals(program.ProcessName, StringComparison.OrdinalIgnoreCase));
                var category = categoryMapping?.Category?.Name ?? "其他";

                ProgramStats.Add(new ProgramMouseStat
                {
                    ProcessName = program.ProcessName,
                    Category = category,
                    ClickCount = 0, // 需要从鼠标会话中获取
                    MoveDistance = 0,
                    Duration = program.Duration
                });
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "计算程序统计失败");
        }
    }

    private void CalculateHourlyStats(List<Core.Entities.MouseSession> sessions)
    {
        HourlyStats.Clear();

        // 按小时分组统计
        var hourlyGroups = sessions
            .GroupBy(s => s.Hour)
            .OrderBy(g => g.Key);

        foreach (var group in hourlyGroups)
        {
            var clicks = group.Sum(s => s.LeftClickCount + s.RightClickCount + s.MiddleClickCount);
            var distance = group.Sum(s => s.TotalMoveDistance);
            var scrolls = group.Sum(s => s.ScrollCount);

            HourlyStats.Add(new HourlyMouseStat
            {
                Hour = group.Key,
                ClickCount = clicks,
                MoveDistance = distance,
                ScrollCount = scrolls
            });
        }
    }

    private async Task LoadClickDetailsAsync(DateTime startDate, DateTime endDate)
    {
        ClickDetails.Clear();

        try
        {
            // 加载最近的鼠标会话详情
            var sessions = await _dbContext.MouseSessions
                .Where(s => s.Date >= startDate && s.Date < endDate)
                .OrderByDescending(s => s.Date)
                .ThenByDescending(s => s.Hour)
                .Take(50)
                .ToListAsync();

            foreach (var session in sessions)
            {
                ClickDetails.Add(new MouseClickDetail
                {
                    Date = session.Date,
                    Hour = session.Hour,
                    LeftClicks = session.LeftClickCount,
                    RightClicks = session.RightClickCount,
                    MiddleClicks = session.MiddleClickCount,
                    MoveDistance = session.TotalMoveDistance,
                    Scrolls = session.ScrollCount
                });
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "加载点击详情失败");
        }
    }

    private static SolidColorBrush CreateBrush(string color)
    {
        return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(
            255,
            (byte)(Convert.ToUInt32(color.Substring(1, 2), 16)),
            (byte)(Convert.ToUInt32(color.Substring(3, 2), 16)),
            (byte)(Convert.ToUInt32(color.Substring(5, 2), 16))
        ));
    }

    partial void OnSelectedPeriodChanged(string value)
    {
        LoadDataCommand.Execute(null);
    }
}

public class MouseStatCard : ObservableObject
{
    public string Title { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string Icon { get; set; } = "\uE962";
    public Brush Color { get; set; } = new SolidColorBrush(Microsoft.UI.Colors.Gray);
}

public class ProgramMouseStat : ObservableObject
{
    public string ProcessName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int ClickCount { get; set; }
    public double MoveDistance { get; set; }
    public TimeSpan Duration { get; set; }
}

public class HourlyMouseStat : ObservableObject
{
    public int Hour { get; set; }
    public int ClickCount { get; set; }
    public double MoveDistance { get; set; }
    public int ScrollCount { get; set; }
}

public class MouseClickDetail : ObservableObject
{
    public DateTime Date { get; set; }
    public int Hour { get; set; }
    public int LeftClicks { get; set; }
    public int RightClicks { get; set; }
    public int MiddleClicks { get; set; }
    public double MoveDistance { get; set; }
    public int Scrolls { get; set; }
}
