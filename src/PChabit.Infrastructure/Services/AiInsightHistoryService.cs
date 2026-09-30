using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PChabit.Core.Entities;
using PChabit.Infrastructure.Data;
using Serilog;

namespace PChabit.Infrastructure.Services;

/// <summary>AI 解读历史：保存/读取快照，供下次解读的 lastAiPlan 与追问上下文。</summary>
public interface IAiInsightHistoryService
{
    Task SaveAsync(AiInsightSnapshot snapshot, CancellationToken ct = default);
    Task<AiInsightSnapshot?> GetLatestAsync(CancellationToken ct = default);
    Task<AiInsightSnapshot?> GetByPeriodKeyAsync(string periodKey, CancellationToken ct = default);
    Task<List<AiPlanItem>> LoadLastPlanAsync(CancellationToken ct = default);
    Task UpdatePlanStatusAsync(Guid snapshotId, string title, string status, CancellationToken ct = default);
    Task<int> ClearAllAsync(CancellationToken ct = default);
}

public sealed class AiPlanItem
{
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public string? ActionKey { get; set; }
    public string? TargetMetricId { get; set; }
    public double? TargetValue { get; set; }
    public string Status { get; set; } = "pending";
}

public sealed class AiInsightHistoryService : IAiInsightHistoryService
{
    private readonly IDbContextFactory<PChabitDbContext> _factory;

    public AiInsightHistoryService(IDbContextFactory<PChabitDbContext> factory) => _factory = factory;

    public async Task SaveAsync(AiInsightSnapshot snapshot, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // 同周期覆盖，避免重复堆积
        var existing = await db.AiInsightSnapshots
            .FirstOrDefaultAsync(s => s.PeriodKey == snapshot.PeriodKey, ct);
        if (existing is not null)
        {
            existing.Summary = snapshot.Summary;
            existing.FindingsJson = snapshot.FindingsJson;
            existing.PlanJson = snapshot.PlanJson;
            existing.DiagnosisJson = snapshot.DiagnosisJson;
            existing.RisksJson = snapshot.RisksJson;
            existing.Model = snapshot.Model;
            existing.PromptVersion = snapshot.PromptVersion;
            existing.CreatedAt = DateTime.Now;
            existing.PeriodLabel = snapshot.PeriodLabel;
            existing.PeriodStart = snapshot.PeriodStart;
            existing.PeriodEnd = snapshot.PeriodEnd;
        }
        else
        {
            db.AiInsightSnapshots.Add(snapshot);
        }
        await db.SaveChangesAsync(ct);
        Log.Information("AI 解读已保存: {PeriodKey}", snapshot.PeriodKey);
    }

    public async Task<AiInsightSnapshot?> GetLatestAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.AiInsightSnapshots
            .AsNoTracking()
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<AiInsightSnapshot?> GetByPeriodKeyAsync(string periodKey, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.AiInsightSnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.PeriodKey == periodKey, ct);
    }

    public async Task<List<AiPlanItem>> LoadLastPlanAsync(CancellationToken ct = default)
    {
        var snap = await GetLatestAsync(ct);
        if (snap?.PlanJson is null or { Length: 0 })
            return new List<AiPlanItem>();
        try
        {
            return JsonSerializer.Deserialize<List<AiPlanItem>>(snap.PlanJson) ?? new List<AiPlanItem>();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "解析上次计划失败");
            return new List<AiPlanItem>();
        }
    }

    public async Task UpdatePlanStatusAsync(Guid snapshotId, string title, string status, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var snap = await db.AiInsightSnapshots.FirstOrDefaultAsync(s => s.Id == snapshotId, ct);
        if (snap?.PlanJson is null) return;
        try
        {
            var plan = JsonSerializer.Deserialize<List<AiPlanItem>>(snap.PlanJson) ?? new List<AiPlanItem>();
            var item = plan.FirstOrDefault(p => p.Title == title);
            if (item is null) return;
            item.Status = status;
            snap.PlanJson = JsonSerializer.Serialize(plan);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "更新计划状态失败");
        }
    }

    public async Task<int> ClearAllAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.AiInsightSnapshots.ExecuteDeleteAsync(ct);
    }

    /// <summary>供打包进 AiContextPack 的轻量 plan 条目。</summary>
    public static List<Infrastructure.Analysis.AiContextPack.AiPlanItem> ToPackPlan(IEnumerable<AiPlanItem> items)
        => items.Select(p => new Infrastructure.Analysis.AiContextPack.AiPlanItem
        {
            Title = p.Title,
            TargetMetricId = p.TargetMetricId,
            TargetValue = p.TargetValue,
            Status = p.Status,
            Note = p.Detail
        }).ToList();
}

/// <summary>从解析结果组装快照。</summary>
public static class AiInsightSnapshotFactory
{
    public static AiInsightSnapshot FromParsed(
        string periodKey,
        string periodLabel,
        DateTime periodStart,
        DateTime periodEnd,
        PChabit.Infrastructure.Formatters.AnalyticsAiResponseParser.ParsedAiResult parsed,
        string? model = null)
    {
        return new AiInsightSnapshot
        {
            PeriodKey = periodKey,
            PeriodLabel = periodLabel,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Summary = parsed.Summary,
            FindingsJson = parsed.Findings.Count == 0 ? null : JsonSerializer.Serialize(parsed.Findings),
            PlanJson = (parsed.Plan is { Count: > 0 } plan
                    ? plan.Select(p => new AiPlanItem
                    {
                        Title = p.Title,
                        Detail = p.Detail,
                        ActionKey = p.ActionKey,
                        TargetMetricId = p.TargetMetricId,
                        TargetValue = p.TargetValue,
                        Status = "pending"
                    })
                    : parsed.Suggestions.Select(s => new AiPlanItem
                    {
                        Title = s.Title,
                        Detail = s.Action,
                        ActionKey = s.ActionKey,
                        TargetMetricId = s.TargetMetricId,
                        TargetValue = s.TargetValue,
                        Status = "pending"
                    }))
                .ToList() is { Count: > 0 } list
                ? JsonSerializer.Serialize(list, JsonOptions)
                : null,
            DiagnosisJson = parsed.Diagnosis is { Count: > 0 } d ? JsonSerializer.Serialize(d, JsonOptions) : null,
            RisksJson = parsed.Risks.Count == 0 ? null : JsonSerializer.Serialize(parsed.Risks, JsonOptions),
            Model = model,
            PromptVersion = "v2",
            CreatedAt = DateTime.Now
        };
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
