using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml.Media;
using Serilog;
using PChabit.App.Services;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Analysis;
using PChabit.Infrastructure.Data;
using PChabit.Infrastructure.Services;

namespace PChabit.App.ViewModels;

/// <summary>详情卡片小时点 UI 模型。</summary>
public class AppDetailHourItem
{
    public string Label { get; init; } = "";
    public double Minutes { get; init; }
    public string MinutesText { get; init; } = "—";
    public int Intensity { get; init; }
}

/// <summary>详情卡片日趋势点 UI 模型。</summary>
public class AppDetailDayItem
{
    public string DateLabel { get; init; } = "";
    public double Minutes { get; init; }
    public string MinutesText { get; init; } = "—";
    /// <summary>柱高像素（0–48）。</summary>
    public double BarHeight { get; init; }
}

/// <summary>应用详情卡片 ViewModel（与 AppDetailDialog.xaml 绑定）。</summary>
public partial class AppDetailViewModel : ObservableObject
{
    private readonly IDbContextFactory<PChabitDbContext> _dbFactory;
    private readonly IAppIconService _iconService;
    private readonly IAppDescriptionService? _descriptionService;

    private AnalyticsPeriod _period = AnalyticsPeriod.FromKind(AnalyticsPeriodKind.Today);
    private string _processName = "";
    private string _appName = "";
    private string _category = AppStatsEngine.UncategorizedName;
    private string _categoryColorHex = "#6B7280";
    private double _totalMinutesAllApps;

    [ObservableProperty]
    private ImageSource? _icon;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _appTitle = "";

    [ObservableProperty]
    private string _processText = "";

    [ObservableProperty]
    private string _periodText = "";

    [ObservableProperty]
    private string _categoryText = "";

    [ObservableProperty]
    private string _totalText = "—";

    [ObservableProperty]
    private string _sessionsText = "—";

    [ObservableProperty]
    private string _avgText = "—";

    [ObservableProperty]
    private string _shareText = "—";

    [ObservableProperty]
    private string _focusText = "—";

    [ObservableProperty]
    private string _deltaText = "—";

    [ObservableProperty]
    private string _longestText = "—";

    [ObservableProperty]
    private bool _hasKeyMouse;

    /// <summary>键鼠统计五项（3.46.0：自键鼠页「按应用键鼠统计」并入详情弹窗，缺失侧为 —）。</summary>
    [ObservableProperty]
    private string _keyPressesText = "—";

    [ObservableProperty]
    private string _keysPerHourText = "—";

    [ObservableProperty]
    private string _mouseClicksText = "—";

    [ObservableProperty]
    private string _mouseMoveText = "—";

    [ObservableProperty]
    private string _mouseScrollsText = "—";

    [ObservableProperty]
    private bool _hasHardware;

    [ObservableProperty]
    private string _hardwareSummary = "";

    [ObservableProperty]
    private bool _hasData;

    [ObservableProperty]
    private string _emptyMessage = "";

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private bool _hasDescription;

    [ObservableProperty]
    private bool _isDescriptionLoading;

    public ObservableCollection<AppDetailHourItem> Hourly { get; } = new();
    public ObservableCollection<AppDetailDayItem> Days { get; } = new();
    public ObservableCollection<string> WindowTitles { get; } = new();

    public AppDetailViewModel(
        IDbContextFactory<PChabitDbContext> dbFactory,
        IAppIconService iconService,
        ICategoryService? categoryService = null,
        IAppDescriptionService? descriptionService = null)
    {
        _dbFactory = dbFactory;
        _iconService = iconService;
        _ = categoryService;
        _descriptionService = descriptionService;
    }

    /// <summary>排行调用：配置参数后再 LoadReportAsync。</summary>
    public void Configure(
        AnalyticsPeriod period,
        string processName,
        string appName,
        string category,
        string categoryColorHex,
        double totalMinutesAllApps)
    {
        _period = period;
        _processName = processName ?? "";
        _appName = string.IsNullOrWhiteSpace(appName) ? processName : appName;
        // 统一兜底名，禁止在本文件另写一份字面量（陷阱 43：同一兜底值散落多处必然分叉）
        _category = string.IsNullOrWhiteSpace(category) ? AppStatsEngine.UncategorizedName : category;
        _categoryColorHex = string.IsNullOrWhiteSpace(categoryColorHex) ? "#6B7280" : categoryColorHex;
        _totalMinutesAllApps = totalMinutesAllApps;

        AppTitle = _appName;
        ProcessText = _processName;
        PeriodText = $"{period.Label}　{period.Start:yyyy-MM-dd} ~ {period.EndExclusive.AddDays(-1):yyyy-MM-dd}";
        CategoryText = _category;
        _ = _categoryColorHex;
    }

    public async Task LoadReportAsync()
    {
        if (string.IsNullOrWhiteSpace(_processName))
        {
            HasData = false;
            EmptyMessage = "未指定应用";
            return;
        }

        IsLoading = true;
        HasData = false;
        EmptyMessage = "";
        IsDescriptionLoading = true;
        try
        {
            // 说明查询与详情统计并行（陷阱 13 风格：数据加载不串行依赖）
            var descTask = _descriptionService?.GetDescriptionAsync(_processName);

            var report = await Task.Run(() => AppDetailEngine.BuildAsync(
                _dbFactory, _period, _processName, _appName, _category, _category));

            var share = _totalMinutesAllApps > 0 && report.TotalMinutes > 0
                ? report.TotalMinutes / _totalMinutesAllApps * 100
                : 0;
            report = AppDetailEngine.WithShare(report, share);

            TotalText = report.TotalText;
            SessionsText = $"{report.Sessions} 次";
            AvgText = report.AvgText;
            ShareText = report.ShareText;
            FocusText = report.KpiFocusText;
            DeltaText = report.DeltaText;
            LongestText = report.LongestText;

            HasKeyMouse = report.HasKeyMouse;
            KeyPressesText = report.KeyPressesText;
            KeysPerHourText = report.KeysPerHourText;
            MouseClicksText = report.MouseClicksText;
            MouseMoveText = report.MouseMoveText;
            MouseScrollsText = report.MouseScrollsText;

            HasHardware = report.HasHardware;
            if (report.HasHardware)
            {
                var parts = new List<string>();
                if (report.CpuLoadAvg is { } c) parts.Add($"CPU均 {c:F0}%");
                if (report.GpuLoadAvg is { } g) parts.Add($"GPU均 {g:F0}%");
                if (report.GpuTempMax is { } t) parts.Add($"峰温 {t:F0}°C");
                HardwareSummary = string.Join(" · ", parts);
            }
            else
            {
                HardwareSummary = "";
            }

            var maxHour = report.Hourly.Count > 0 ? report.Hourly.Max(h => h.Minutes) : 0;
            if (maxHour <= 0) maxHour = 1;
            Hourly.Clear();
            foreach (var h in report.Hourly.Where(x => x.Minutes > 0.05))
            {
                var intensity = h.Minutes <= 0 ? 0 : (int)Math.Ceiling(h.Minutes / maxHour * 4.0);
                Hourly.Add(new AppDetailHourItem
                {
                    Label = h.Label,
                    Minutes = h.Minutes,
                    MinutesText = AnalyticsEngine.FormatHours(h.Minutes),
                    Intensity = Math.Clamp(intensity, 0, 4)
                });
            }

            var maxDay = report.DailyTrend.Count > 0 ? report.DailyTrend.Max(d => d.Minutes) : 0;
            if (maxDay <= 0) maxDay = 1;
            Days.Clear();
            foreach (var d in report.DailyTrend)
            {
                var bar = d.Minutes <= 0 ? 0 : Math.Clamp(d.Minutes / maxDay * 48.0, 2, 48);
                Days.Add(new AppDetailDayItem
                {
                    DateLabel = d.DateLabel,
                    Minutes = d.Minutes,
                    MinutesText = d.MinutesText,
                    BarHeight = bar
                });
            }

            WindowTitles.Clear();
            foreach (var t in report.TopWindowTitles) WindowTitles.Add(t);

            HasData = report.HasData;
            if (!report.HasData)
                EmptyMessage = "所选周期内该应用没有已结束会话。";

            try
            {
                var icon = await _iconService.GetAppIconAsync(_processName);
                if (icon != null) Icon = icon;
            }
            catch (Exception iconEx)
            {
                Log.Debug(iconEx, "应用详情图标加载失败");
            }

            // 填充应用详细说明（预置库 + 用户编辑）
            if (descTask != null)
            {
                try
                {
                    var desc = await descTask;
                    Description = desc ?? "";
                    HasDescription = !string.IsNullOrWhiteSpace(Description);
                }
                catch (Exception descEx)
                {
                    Log.Debug(descEx, "应用说明加载失败 {Process}", _processName);
                    HasDescription = false;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "应用详情加载失败 {Process}", _processName);
            HasData = false;
            EmptyMessage = $"加载失败：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
            IsDescriptionLoading = false;
        }
    }

    /// <summary>保存用户编辑的应用说明（预置库条目被编辑后自动转为用户条目，种子不再覆盖）。</summary>
    public async Task<bool> SaveDescriptionAsync(string text)
    {
        if (_descriptionService == null) return false;
        try
        {
            var trimmed = (text ?? "").Trim();
            await _descriptionService.SaveUserDescriptionAsync(_processName, trimmed);
            Description = trimmed;
            HasDescription = !string.IsNullOrWhiteSpace(trimmed);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "保存应用说明失败 {Process}", _processName);
            return false;
        }
    }

    [RelayCommand]
    private void CopySummary()
    {
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"# {AppTitle} · {PeriodText}");
            sb.AppendLine($"进程：`{ProcessText}`　分类：{CategoryText}");
            sb.AppendLine();
            sb.AppendLine($"- 总时长：{TotalText}（{DeltaText}）");
            sb.AppendLine($"- 会话：{SessionsText}　均长：{AvgText}");
            sb.AppendLine($"- 占比：{ShareText}　专注：{FocusText}");
            sb.AppendLine($"- 最长会话：{LongestText}");
            if (HasKeyMouse)
                sb.AppendLine($"- 键鼠：按键 {KeyPressesText} · KPM {KeysPerHourText} · 点击 {MouseClicksText} · 移动 {MouseMoveText} · 滚动 {MouseScrollsText}");
            if (HasHardware) sb.AppendLine($"- 硬件：{HardwareSummary}");
            if (WindowTitles.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## 窗口标题 Top");
                foreach (var t in WindowTitles)
                    sb.AppendLine($"- {t}");
            }

            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(sb.ToString());
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "复制应用详情摘要失败");
        }
    }
}
