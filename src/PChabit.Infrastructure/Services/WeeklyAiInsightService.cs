using Microsoft.EntityFrameworkCore;
using PChabit.Core.Entities;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Analysis;
using PChabit.Infrastructure.Data;
using Serilog;

namespace PChabit.Infrastructure.Services;

/// <summary>
/// 周自动 AI 解读：在后台按周生成一次解读并保存快照 + 通知。
/// 需 AiInsightsEnabled + AiAutoWeeklyInsight。
/// </summary>
public interface IWeeklyAiInsightService
{
    Task<bool> TryRunWeeklyAsync(CancellationToken ct = default);
    bool ShouldRunThisWeek();
}

public sealed class WeeklyAiInsightService : IWeeklyAiInsightService
{
    public const string LastRunKeyPath = "ai-weekly-last-run.txt";

    private readonly IDbContextFactory<PChabitDbContext> _factory;
    private readonly IAnalyticsAiService _ai;
    private readonly IAiInsightHistoryService _history;
    private readonly ISettingsService _settings;
    private readonly INotificationService? _notify;

    public WeeklyAiInsightService(
        IDbContextFactory<PChabitDbContext> factory,
        IAnalyticsAiService ai,
        IAiInsightHistoryService history,
        ISettingsService settings,
        INotificationService? notify = null)
    {
        _factory = factory;
        _ai = ai;
        _history = history;
        _settings = settings;
        _notify = notify;
    }

    private static string LastRunFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PChabit", LastRunKeyPath);

    public bool ShouldRunThisWeek()
    {
        if (!_settings.AiInsightsEnabled || !_settings.AiAutoWeeklyInsight) return false;
        if (!_ai.IsConfigured) return false;
        try
        {
            if (File.Exists(LastRunFile))
            {
                var text = File.ReadAllText(LastRunFile).Trim();
                if (DateTime.TryParse(text, out var last))
                {
                    // 本周一之后才跑
                    var monday = DateTime.Today.AddDays(-((int)DateTime.Today.DayOfWeek + 6) % 7);
                    return last.Date < monday;
                }
            }
            return true;
        }
        catch
        {
            return true;
        }
    }

    public async Task<bool> TryRunWeeklyAsync(CancellationToken ct = default)
    {
        if (!ShouldRunThisWeek()) return false;

        try
        {
            var period = AnalyticsPeriod.FromKind(AnalyticsPeriodKind.ThisWeek);
            await using var db = await _factory.CreateDbContextAsync(ct);

            // 用近 7 天会话 + DailySummaries 汇总
            var appSessions = await db.AppSessions
                .AsNoTracking()
                .Where(s => s.StartTime >= period.Start && s.StartTime < period.EndExclusive)
                .ToListAsync(ct);

            var report = AnalyticsEngine.Compute(period, period.Previous(), appSessions, appSessions, null, null, null);
            var days = await HabitDayLoader.LoadAsync(db, DateTime.Today.AddDays(-28), DateTime.Today.AddDays(1), ct);
            var profile = HabitProfileBuilder.Build(days);
            var baseline = PersonalBaselineBuilder.Build(days);
            var trajectory = HabitTrajectoryBuilder.Build(days);
            var lastPlan = await _history.LoadLastPlanAsync(ct);

            var pack = AiContextPack.Build(
                report,
                strictPrivacy: _settings.AiStrictPrivacy,
                goals: _settings.DailyUsageGoalHours > 0
                    ? new AiContextPack.AiGoals
                    {
                        DailyActiveTargetMin = _settings.DailyUsageGoalHours * 60,
                        PlanFeedback = AiPlanFeedbackBuilder.FromPlan(AiInsightHistoryService.ToPackPlan(lastPlan)
                            .Select(p => new AiPlanItem { Title = p.Title, Detail = p.Note ?? p.Title, Status = p.Status }).ToList())
                    }
                    : null,
                lastPlan: AiInsightHistoryService.ToPackPlan(lastPlan),
                profile: profile,
                baseline: baseline,
                deviations: null,
                trajectory: trajectory);

            var raw = await _ai.InterpretAsync(AnalysisReportBuilder.SystemPrompt, pack, ct);
            var parsed = PChabit.Infrastructure.Formatters.AnalyticsAiResponseParser.Parse(raw);
            if (!parsed.Parsed) return false;

            var snap = AiInsightSnapshotFactory.FromParsed(
                period.Start.ToString("yyyy-MM-dd"),
                "本周(自动)",
                period.Start,
                period.EndExclusive,
                parsed);
            await _history.SaveAsync(snap, ct);

            Directory.CreateDirectory(Path.GetDirectoryName(LastRunFile)!);
            await File.WriteAllTextAsync(LastRunFile, DateTime.Now.ToString("o"), ct);

            if (_notify is not null)
            {
                await _notify.ShowReminderAsync(
                    "本周 AI 复盘已生成",
                    string.IsNullOrWhiteSpace(parsed.Summary) ? "打开分析页查看解读。" : Truncate(parsed.Summary, 80));
            }

            Log.Information("周自动 AI 解读完成");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "周自动 AI 解读失败");
            return false;
        }
    }

    private static string InjectTrajectory(string packJson, List<HabitTrajectoryBuilder.HabitTrend> trends)
    {
        // 轨迹已由 AiContextPack.Build 直接写入，此方法保留作兼容空实现
        return packJson;
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}

/// <summary>从历史计划统计反馈，供 prompt 降低无效建议。</summary>
public static class AiPlanFeedbackBuilder
{
    public static AiContextPack.AiPlanFeedback FromPlan(IReadOnlyList<AiPlanItem> items)
    {
        var fb = new AiContextPack.AiPlanFeedback
        {
            Total = items.Count,
            Done = items.Count(i => i.Status == "done"),
            Skipped = items.Count(i => i.Status == "skipped")
        };
        fb.OftenDone.AddRange(items.Where(i => i.Status == "done").Select(i => i.Title).Take(5));
        fb.OftenSkipped.AddRange(items.Where(i => i.Status == "skipped").Select(i => i.Title).Take(5));
        return fb;
    }
}
