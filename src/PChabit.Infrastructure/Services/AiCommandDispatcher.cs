using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using PChabit.Core.Entities;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Analysis;
using PChabit.Infrastructure.Data;

namespace PChabit.Infrastructure.Services;

/// <summary>
/// AI 命令分发器（3.42.0）：在现有 WebSocketServer 的 8765 端口上提供 /api/* 路由，
/// AI / 脚本通过 HTTP（curl / Invoke-RestMethod）直接调用，返回 JSON。
/// 仅绑定本机回环地址，信任级别与浏览器同步 WebSocket 相同。
///
/// 命令表（method path → 说明）：
///   GET  /api/health                     服务健康 + 版本
///   GET  /api/apps/stats?period=today    应用统计（today/yesterday/thisweek/lastweek/last7days/last30days/thismonth）
///   GET  /api/apps/detail?process=xx&period=today   单应用详情
///   GET  /api/apps/category?process=xx   查询应用分类
///   PUT  /api/apps/category {"process":"xx","category":"开发"}  修改应用分类
///   GET  /api/apps/description?process=xx   查询应用说明
///   PUT  /api/apps/description {"process":"xx","description":"..."}  修改应用说明
///   GET  /api/summary?period=today        分析汇总（KPI/热力/构成/洞察/键鼠网页硬件流量）
///   GET  /api/settings                    全部设置（camelCase key）
///   PUT  /api/settings {"key":"AiBaseUrl","value":"..."}  修改设置（白名单属性 + 类型转换，自动 Save）
/// </summary>
public class AiCommandDispatcher
{
    private readonly IDbContextFactory<PChabitDbContext> _dbFactory;
    private readonly ISettingsService _settingsService;
    private readonly IServiceScopeFactory _scopeFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static readonly string[] PeriodAliases =
    {
        "today", "yesterday", "thisweek", "lastweek", "last7days", "last30days", "thismonth"
    };

    public AiCommandDispatcher(
        IDbContextFactory<PChabitDbContext> dbFactory,
        ISettingsService settingsService,
        IServiceScopeFactory scopeFactory)
    {
        _dbFactory = dbFactory;
        _settingsService = settingsService;
        _scopeFactory = scopeFactory;
    }

    /// <summary>入口：由 WebSocketServer 对 /api/* 请求调用。任何异常都回 500 JSON，不让连接悬挂。</summary>
    public async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath ?? "";
            var method = context.Request.HttpMethod;

            object? result = null;
            switch (method + " " + path)
            {
                case "GET /api/health":
                    result = await HealthAsync();
                    break;

                case "GET /api/apps/stats":
                    result = await AppStatsAsync(PeriodFromQuery(context.Request));
                    break;

                case "GET /api/apps/detail":
                    result = await AppDetailAsync(context.Request);
                    break;

                case "GET /api/apps/category":
                    result = await GetCategoryAsync(context.Request);
                    break;

                case "PUT /api/apps/category":
                    result = await SetCategoryAsync(await ReadJsonAsync(context.Request));
                    break;

                case "GET /api/apps/description":
                    result = await GetDescriptionAsync(context.Request);
                    break;

                case "PUT /api/apps/description":
                    result = await SetDescriptionAsync(await ReadJsonAsync(context.Request));
                    break;

                case "GET /api/summary":
                    result = await SummaryAsync(PeriodFromQuery(context.Request));
                    break;

                case "GET /api/settings":
                    result = await GetAllSettingsAsync();
                    break;

                case "PUT /api/settings":
                    result = await SetSettingAsync(await ReadJsonAsync(context.Request));
                    break;

                default:
                    await WriteJsonAsync(context, 404, new { error = $"未知命令: {method} {path}" });
                    return;
            }

            await WriteJsonAsync(context, 200, result);
        }
        catch (ArgumentException ex)
        {
            Log.Warning("AI 命令参数错误: {Message}", ex.Message);
            await WriteJsonAsync(context, 400, new { error = ex.Message });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "AI 命令执行失败");
            await WriteJsonAsync(context, 500, new { error = "内部错误: " + ex.Message });
        }
    }

    // ---------- 查询命令 ----------

    private async Task<object> HealthAsync()
    {
        var app = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "PChabit");
        var version = app?.GetName().Version?.ToString(3)
                      ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
                      ?? "unknown";
        return new
        {
            status = "ok",
            service = "pchabit",
            version,
            pid = Environment.ProcessId,
            startedAt = DateTime.Now
        };
    }

    private async Task<object> AppStatsAsync(AnalyticsPeriod period)
    {
        var report = await AppStatsEngine.BuildAsync(_dbFactory, period, null, null);
        return new
        {
            period = new { period.Label, period.Start, period.EndExclusive },
            totalMinutes = report.TotalMinutes,
            activeApps = report.ActiveApps,
            topApp = report.TopAppName,
            topAppMinutes = report.TopAppMinutes,
            focusSessions = report.FocusSessions,
            apps = report.Rows.Select(r => new
            {
                process = r.ProcessName,
                appName = r.AppName,
                category = r.Category,
                categoryColor = r.CategoryColorHex,
                sessions = r.Sessions,
                minutes = r.DurationMinutes,
                focusMinutes = r.FocusMinutes
            }),
            categoryShares = report.CategoryShares.Select(c => new
            {
                category = c.Category,
                minutes = c.Minutes,
                percent = c.Percentage
            })
        };
    }

    private async Task<object> AppDetailAsync(HttpListenerRequest request)
    {
        var process = GetQuery(request, "process");
        if (string.IsNullOrWhiteSpace(process))
            throw new ArgumentException("缺少 process 参数");

        var period = PeriodFromQuery(request);
        var report = await AppDetailEngine.BuildAsync(_dbFactory, period, process, process, AppStatsEngine.UncategorizedName, null);
        return new
        {
            period = new { period.Label, period.Start, period.EndExclusive },
            appName = report.AppName ?? process,
            process,
            totalMinutes = report.TotalMinutes,
            sessions = report.Sessions,
            avgMinutes = report.AvgMinutes,
            sharePct = report.SharePct,
            focusMinutes = report.FocusMinutes,
            focusCount = report.FocusCount,
            longestSessionMinutes = report.LongestSessionMinutes,
            keyPresses = report.KeyPresses,
            keysPerHour = report.KeysPerHour,
            hasInput = report.HasInput,
            hasHardware = report.HasHardware,
            topWindowTitles = report.TopWindowTitles,
            hourly = report.Hourly?.Select(h => new { hour = h.Hour, label = h.Label, minutes = h.Minutes }).Cast<object>() ?? Array.Empty<object>(),
            dailyTrend = report.DailyTrend?.Select(d => new { date = d.DateLabel, minutes = d.Minutes }).Cast<object>() ?? Array.Empty<object>()
        };
    }

    private async Task<object> GetCategoryAsync(HttpListenerRequest request)
    {
        var process = GetQuery(request, "process");
        if (string.IsNullOrWhiteSpace(process))
            throw new ArgumentException("缺少 process 参数");

        using var scope = _scopeFactory.CreateScope();
        var categoryService = scope.ServiceProvider.GetRequiredService<ICategoryService>();
        var mapping = await categoryService.GetMappingByProcessNameAsync(process);
        var categories = await categoryService.GetAllCategoriesAsync();
        var category = mapping == null ? null : categories.FirstOrDefault(c => c.Id == mapping.CategoryId);

        return new
        {
            process,
            category = category?.Name ?? AppStatsEngine.UncategorizedName,
            categoryIcon = category?.Icon ?? "",
            categoryId = category?.Id ?? 0
        };
    }

    private async Task<object> GetDescriptionAsync(HttpListenerRequest request)
    {
        var process = GetQuery(request, "process");
        if (string.IsNullOrWhiteSpace(process))
            throw new ArgumentException("缺少 process 参数");

        using var scope = _scopeFactory.CreateScope();
        var descriptionService = scope.ServiceProvider.GetRequiredService<IAppDescriptionService>();
        var description = await descriptionService.GetDescriptionAsync(process);
        return new { process, description = description ?? "" };
    }

    private async Task<object> SummaryAsync(AnalyticsPeriod period)
    {
        var report = await AnalyticsEngine.BuildAsync(_dbFactory, period);
        return new
        {
            period = new { period.Label, period.Start, period.EndExclusive },
            kpis = report.Kpis.Select(k => new { id = k.Id, name = k.Name, value = k.Value, deltaText = k.DeltaText, deltaDirection = k.DeltaDirection, hint = k.Hint }),
            dayCountWithData = report.DayCountWithData,
            hasEnoughData = report.HasEnoughData,
            extra = report.Extra,
            topChanges = report.TopChanges.Select(t => new { name = t.Name, kind = t.Kind, currentMinutes = t.CurrentMinutes, previousMinutes = t.PreviousMinutes, deltaText = t.DeltaText, direction = t.Direction }),
            categoryShares = report.CategoryShares.Select(c => new { c.Category, c.Minutes, c.Percentage }),
            insights = report.Insights.Select(i => new { i.Severity, i.Title, i.Message, i.ActionKey })
        };
    }

    private async Task<object> GetAllSettingsAsync()
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var prop in SettingsProperties())
        {
            try
            {
                result[ToCamel(prop.Name)] = prop.GetValue(_settingsService);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "读取设置 {Key} 失败", prop.Name);
            }
        }
        return result;
    }

    // ---------- 写命令 ----------

    private async Task<object> SetCategoryAsync(JsonDocument? body)
    {
        var process = GetBodyString(body, "process");
        var category = GetBodyString(body, "category");
        if (string.IsNullOrWhiteSpace(process))
            throw new ArgumentException("缺少 process 字段");

        using var scope = _scopeFactory.CreateScope();
        var categoryService = scope.ServiceProvider.GetRequiredService<ICategoryService>();

        // 空分类 = 取消分类（删除映射）
        if (string.IsNullOrWhiteSpace(category))
        {
            var existing = await categoryService.GetMappingByProcessNameAsync(process);
            if (existing != null)
            {
                await categoryService.DeleteMappingAsync(existing.Id);
                Log.Information("AI 命令已取消应用 {Process} 的分类", process);
            }
            return new { process, category = AppStatsEngine.UncategorizedName, categoryId = 0 };
        }

        var categories = await categoryService.GetAllCategoriesAsync();
        var target = categories.FirstOrDefault(c => c.IsActive && string.Equals(c.Name, category, StringComparison.OrdinalIgnoreCase));
        if (target == null)
        {
            var available = string.Join(", ", categories.Where(c => c.IsActive).Select(c => c.Name));
            throw new ArgumentException($"分类「{category}」不存在。可用分类: {available}");
        }

        var mapping = await categoryService.GetMappingByProcessNameAsync(process);
        if (mapping != null)
        {
            mapping.CategoryId = target.Id;
            mapping.UpdatedAt = DateTime.Now;
            await categoryService.UpdateMappingAsync(mapping);
        }
        else
        {
            await categoryService.CreateMappingAsync(new ProgramCategoryMapping
            {
                ProcessName = process,
                CategoryId = target.Id,
                CreatedAt = DateTime.Now
            });
        }

        Log.Information("AI 命令已修改应用 {Process} 分类为 {Category}", process, target.Name);
        return new { process, category = target.Name, categoryId = target.Id };
    }

    private async Task<object> SetDescriptionAsync(JsonDocument? body)
    {
        var process = GetBodyString(body, "process");
        var description = GetBodyString(body, "description") ?? "";
        if (string.IsNullOrWhiteSpace(process))
            throw new ArgumentException("缺少 process 字段");

        using var scope = _scopeFactory.CreateScope();
        var descriptionService = scope.ServiceProvider.GetRequiredService<IAppDescriptionService>();
        await descriptionService.SaveUserDescriptionAsync(process, description);

        Log.Information("AI 命令已保存应用 {Process} 说明", process);
        return new { process, saved = true };
    }

    private async Task<object> SetSettingAsync(JsonDocument? body)
    {
        var key = GetBodyString(body, "key");
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("缺少 key 字段");
        var value = GetBodyString(body, "value");

        var prop = SettingsProperties().FirstOrDefault(p => string.Equals(p.Name, ToPascal(key), StringComparison.OrdinalIgnoreCase)
                                                          || string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase));
        if (prop == null)
        {
            var known = string.Join(", ", SettingsProperties().Select(p => ToCamel(p.Name)));
            throw new ArgumentException($"设置项「{key}」不存在。可设置: {known}");
        }

        prop.SetValue(_settingsService, ConvertValue(prop, value));
        await _settingsService.SaveAsync();

        Log.Information("AI 命令已修改设置 {Key} = {Value}", key, value);
        return new { key, updated = true, value };
    }

    // ---------- 工具 ----------

    private static AnalyticsPeriod PeriodFromQuery(HttpListenerRequest request)
    {
        var raw = GetQuery(request, "period");
        if (string.IsNullOrWhiteSpace(raw)) return AnalyticsPeriod.FromKind(AnalyticsPeriodKind.Today);

        var kind = raw.Trim().ToLowerInvariant() switch
        {
            "today" => AnalyticsPeriodKind.Today,
            "yesterday" => AnalyticsPeriodKind.Yesterday,
            "thisweek" or "week" or "7d" => AnalyticsPeriodKind.ThisWeek,
            "lastweek" => AnalyticsPeriodKind.LastWeek,
            "last7days" or "7days" => AnalyticsPeriodKind.Last7Days,
            "last30days" or "30days" or "month" => AnalyticsPeriodKind.Last30Days,
            "thismonth" => AnalyticsPeriodKind.ThisMonth,
            _ => throw new ArgumentException($"period 参数无效「{raw}」。可用: {string.Join(" / ", PeriodAliases)}")
        };
        return AnalyticsPeriod.FromKind(kind);
    }

    private static string GetQuery(HttpListenerRequest request, string key)
    {
        var value = request.QueryString[key];
        return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
    }

    private static async Task<JsonDocument?> ReadJsonAsync(HttpListenerRequest request)
    {
        if (request.ContentLength64 <= 0) return null;
        using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
        var text = await reader.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(text)) return null;
        return JsonDocument.Parse(text);
    }

    private static string GetBodyString(JsonDocument? body, string key)
    {
        if (body == null || !body.RootElement.TryGetProperty(key, out var el)) return "";
        return el.ValueKind == JsonValueKind.String ? el.GetString() ?? "" : el.GetRawText();
    }

    private static async Task WriteJsonAsync(HttpListenerContext context, int statusCode, object? payload)
    {
        var json = payload is null ? "{}" : JsonSerializer.Serialize(payload, JsonOptions);
        var buffer = Encoding.UTF8.GetBytes(json);
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength64 = buffer.Length;
        await context.Response.OutputStream.WriteAsync(buffer);
        context.Response.Close();
    }

    private static IEnumerable<PropertyInfo> SettingsProperties()
    {
        return typeof(ISettingsService).GetProperties()
            .Where(p => p.CanWrite && p.GetIndexParameters().Length == 0)
            .OrderBy(p => p.Name, StringComparer.Ordinal);
    }

    private static object? ConvertValue(PropertyInfo prop, string? value)
    {
        var type = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
        if (type == typeof(string)) return value ?? "";
        if (type == typeof(bool))
        {
            return value?.Trim().ToLowerInvariant() switch
            {
                "true" or "1" or "yes" or "on" => true,
                "false" or "0" or "no" or "off" => false,
                _ => throw new ArgumentException($"布尔值无法解析: {value}")
            };
        }
        if (type == typeof(int)) return int.Parse(value ?? "", System.Globalization.CultureInfo.InvariantCulture);
        if (type == typeof(double)) return double.Parse(value ?? "", System.Globalization.CultureInfo.InvariantCulture);
        if (type == typeof(DateTime) || type == typeof(DateTime?))
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        throw new ArgumentException($"不支持的类型: {prop.PropertyType.Name}");
    }

    private static string ToCamel(string name) =>
        name.Length <= 1 ? name.ToLowerInvariant() : char.ToLowerInvariant(name[0]) + name[1..];

    private static string ToPascal(string name) =>
        name.Length <= 1 ? name.ToUpperInvariant() : char.ToUpperInvariant(name[0]) + name[1..];
}
