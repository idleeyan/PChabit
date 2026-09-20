using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PChabit.Core.Interfaces;
using Serilog;

namespace PChabit.Infrastructure.Services;

public interface IAnalyticsAiService
{
    bool IsConfigured { get; }
    /// <summary>调用兼容 OpenAI Chat Completions 的端点；返回模型原文（期望为 JSON 字符串）。</summary>
    Task<string> InterpretAsync(string systemPrompt, string userPayload, CancellationToken ct = default);
}

/// <summary>
/// 分析页「AI 深度解读」。默认关闭；仅发送 AnalysisReportBuilder 生成的聚合 JSON。
/// ApiKey 只存在本机 settings.json，不写日志。
/// 超时读设置 AiTimeoutSeconds（默认 300s，兼容本地大模型），不用写死 60s。
/// </summary>
public sealed class AnalyticsAiService : IAnalyticsAiService
{
    private readonly ISettingsService _settings;
    // 超时由每请求 CTS 控制；HttpClient 本身不设总超时
    private static readonly HttpClient Http = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

    public AnalyticsAiService(ISettingsService settings) => _settings = settings;

    public bool IsConfigured
    {
        get
        {
            if (!_settings.AiInsightsEnabled) return false;
            if (string.IsNullOrWhiteSpace(_settings.AiBaseUrl) || string.IsNullOrWhiteSpace(_settings.AiModel))
                return false;
            if (!string.IsNullOrWhiteSpace(_settings.AiApiKey)) return true;
            // 本地 OpenAI 兼容端点（Ollama / LM Studio 等）允许空 Key
            var url = _settings.AiBaseUrl;
            return url.Contains("127.0.0.1", StringComparison.Ordinal)
                   || url.Contains("localhost", StringComparison.OrdinalIgnoreCase)
                   || url.Contains("::1", StringComparison.Ordinal);
        }
    }

    public async Task<string> InterpretAsync(string systemPrompt, string userPayload, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("AI 深度解读未配置或未启用（设置 → 分析 AI）。云端需填 Key；本地端点可留空 Key。");

        var baseUrl = _settings.AiBaseUrl!.Trim().TrimEnd('/');
        if (!baseUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            // 允许用户填 https://host/v1 或完整路径
            if (baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
                baseUrl += "/chat/completions";
            else
                baseUrl += "/v1/chat/completions";
        }

        var body = new
        {
            model = _settings.AiModel!.Trim(),
            temperature = 0.4,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPayload }
            }
        };

        var timeoutSeconds = Math.Clamp(_settings.AiTimeoutSeconds, 30, 900);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl);
        var apiKey = _settings.AiApiKey?.Trim();
        if (!string.IsNullOrEmpty(apiKey))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        try
        {
            using var resp = await Http.SendAsync(req, timeoutCts.Token).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                Log.Warning("AI 解读 HTTP {Status}", (int)resp.StatusCode);
                throw new HttpRequestException($"AI 服务返回 {(int)resp.StatusCode}");
            }

            using var doc = JsonDocument.Parse(text);
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "";
            return content;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"AI 请求在 {timeoutSeconds} 秒后超时。本地模型较慢时，请到「设置 → 分析 AI」调大超时（当前可设 30–900 秒）。");
        }
    }
}
