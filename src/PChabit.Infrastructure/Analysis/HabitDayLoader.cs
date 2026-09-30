using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PChabit.Core.ValueObjects;
using PChabit.Infrastructure.Data;
using Serilog;

namespace PChabit.Infrastructure.Analysis;

/// <summary>从 DailySummaries 装载 HabitDay（供画像/基线），不读窗口标题/URL。</summary>
public static class HabitDayLoader
{
    public static async Task<List<HabitDay>> LoadAsync(
        PChabitDbContext db,
        DateTime fromInclusive,
        DateTime toExclusive,
        CancellationToken ct = default)
    {
        var fromKey = fromInclusive.ToString("yyyy-MM-dd");
        var toKey = toExclusive.ToString("yyyy-MM-dd");
        var rows = await db.DailySummaries
            .AsNoTracking()
            .Where(s => s.Date.CompareTo(fromKey) >= 0 && s.Date.CompareTo(toKey) < 0)
            .OrderBy(s => s.Date)
            .ToListAsync(ct);

        var result = new List<HabitDay>();
        foreach (var r in rows)
        {
            if (!DateTime.TryParse(r.Date, out var date)) continue;
            double? webShare = null;
            if (r.ActiveMinutes > 0 && (r.WebMinutes ?? 0) > 0)
                webShare = Math.Round((r.WebMinutes!.Value / r.ActiveMinutes) * 100, 1);
            result.Add(new HabitDay(
                date.Date,
                r.ActiveMinutes,
                r.NightMinutes ?? 0,
                r.FirstActiveTime,
                r.LastActiveTime,
                r.FocusMinutesV2,
                r.FocusCountV2,
                r.AppSwitches,
                webShare,
                ParseLabels(r.LabelMinutesJson)));
        }
        return result;
    }

    public static Dictionary<string, double>? ParseLabels(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]") return null;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, double>>(json);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "LabelMinutesJson 解析失败");
            return null;
        }
    }
}
