using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using PChabit.Core.Entities;
using PChabit.Infrastructure.Data;

namespace PChabit.App.Services;

/// <summary>
/// 分析升级 P0：DailySummaries 扩展指标的硬件日聚合与历史回填。
/// 数据源为 HardwareSamples 分钟样本（每日 0:00-23:59，字符串时间键可直接按字典序范围过滤）。
/// </summary>
public partial class DataCollectionService
{
    /// <summary>硬件日聚合结果（分钟样本均值/峰值的二次聚合）。</summary>
    private sealed class HardwareDayMetrics
    {
        public double? CpuLoadAvg { get; init; }
        public double? CpuLoadP95 { get; init; }
        public double? GpuLoadAvg { get; init; }
        public double? GpuTempMax { get; init; }
        public double? MemLoadAvg { get; init; }
    }

    /// <summary>EF 投影行：只取日聚合需要的 4 列。</summary>
    private sealed class HardwareDayRow
    {
        public double? CpuLoadAvg { get; init; }
        public double? GpuLoadAvg { get; init; }
        public double? GpuTempMax { get; init; }
        public double? MemLoadAvg { get; init; }
    }

    /// <summary>
    /// 聚合某日 HardwareSamples 为日硬件指标。
    /// 日均=分钟均值的算术平均；P95=分钟 CpuLoadAvg 升序第 ceil(0.95*n) 项；温度取分钟峰值的最大值。
    /// 时间键定长且前缀即日期，用 LIKE 前缀匹配（可走 Timestamp 索引；
    /// 注意 EF Core SQLite 不翻译 string.CompareOrdinal，会抛 InvalidOperationException）。
    /// </summary>
    private static async Task<HardwareDayMetrics?> AggregateHardwareDayAsync(
        PChabitDbContext dbContext, DateTime date, string dateKey)
    {
        var rows = await dbContext.HardwareSamples
            .AsNoTracking()
            .Where(s => EF.Functions.Like(s.Timestamp, dateKey + "%"))
            .Select(s => new HardwareDayRow
            {
                CpuLoadAvg = s.CpuLoadAvg,
                GpuLoadAvg = s.GpuLoadAvg,
                GpuTempMax = s.GpuTempMax,
                MemLoadAvg = s.MemLoadAvg
            })
            .ToListAsync();

        if (rows.Count == 0) return null;

        return new HardwareDayMetrics
        {
            CpuLoadAvg = Mean(rows.Select(r => r.CpuLoadAvg)),
            CpuLoadP95 = Percentile95(rows.Select(r => r.CpuLoadAvg)),
            GpuLoadAvg = Mean(rows.Select(r => r.GpuLoadAvg)),
            GpuTempMax = Max(rows.Select(r => r.GpuTempMax)),
            MemLoadAvg = Mean(rows.Select(r => r.MemLoadAvg))
        };
    }

    private static double? Mean(IEnumerable<double?> values)
    {
        double sum = 0;
        var n = 0;
        foreach (var v in values)
        {
            if (!v.HasValue) continue;
            sum += v.Value;
            n++;
        }
        return n > 0 ? Math.Round(sum / n, 2) : null;
    }

    private static double? Max(IEnumerable<double?> values)
    {
        double? max = null;
        foreach (var v in values)
        {
            if (!v.HasValue) continue;
            if (!max.HasValue || v.Value > max.Value) max = v.Value;
        }
        return max.HasValue ? Math.Round(max.Value, 2) : null;
    }

    /// <summary>最近秩法 P95：升序第 ceil(0.95*n) 项（索引 -1）。</summary>
    private static double? Percentile95(IEnumerable<double?> values)
    {
        var ordered = values.Where(v => v.HasValue).Select(v => v!.Value).OrderBy(x => x).ToList();
        if (ordered.Count == 0) return null;
        var index = (int)Math.Ceiling(0.95 * ordered.Count) - 1;
        if (index < 0) index = 0;
        return Math.Round(ordered[index], 2);
    }

    /// <summary>
    /// 升级回填：最近 30 天 MetricsVersion &lt; 2 的 DailySummaries，
    /// 补硬件日指标与 WebMinutes。当日无分钟样本（升级前日期）时硬件列保持 null，仍标记 v2 避免反复重扫。
    /// </summary>
    private async Task EnsureHardwareMetricsBackfillAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<PChabitDbContext>();

            var startKey = DateTime.Today.AddDays(-30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            // DailySummaries 为天级长期表（行数=天数），且 EF SQLite 不翻译 CompareOrdinal，
            // 全表取回后在客户端按字典序过滤（跟踪查询以便直接更新）
            var pending = (await dbContext.DailySummaries.ToListAsync())
                .Where(s => string.CompareOrdinal(s.Date, startKey) >= 0
                         && (s.MetricsVersion == null || s.MetricsVersion < 2))
                .ToList();

            if (pending.Count == 0) return;

            Log.Information("DailySummaries 扩展指标回填开始，待处理 {Count} 天", pending.Count);

            foreach (var summary in pending)
            {
                if (!DateTime.TryParseExact(summary.Date, "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    continue;

                var hw = await AggregateHardwareDayAsync(dbContext, date, summary.Date);

                summary.WebMinutes = summary.WebActiveDurationTicks > 0
                    ? Math.Round(summary.WebActiveDurationTicks / (double)TimeSpan.TicksPerMinute, 1)
                    : 0;

                if (hw != null)
                {
                    summary.CpuLoadAvg = hw.CpuLoadAvg;
                    summary.CpuLoadP95 = hw.CpuLoadP95;
                    summary.GpuLoadAvg = hw.GpuLoadAvg;
                    summary.GpuTempMax = hw.GpuTempMax;
                    summary.MemLoadAvg = hw.MemLoadAvg;
                }

                summary.MetricsVersion = 2;
                summary.LastUpdated = DateTime.Now;
            }

            await dbContext.SaveChangesAsync();
            Log.Information("DailySummaries 扩展指标回填完成，处理 {Count} 天", pending.Count);
        }
        catch (Exception ex)
        {
            // 表缺失/写锁竞争等异常不阻断主流程，下次定时检查继续补齐
            Log.Error(ex, "DailySummaries 扩展指标回填失败（将由定时检查自动补齐）");
        }
    }
}
