using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using PChabit.Core.Interfaces;
using Serilog;

namespace PChabit.Infrastructure.Services;

/// <summary>统一 OpenAI 兼容客户端选项。</summary>
public sealed class OpenAiChatRequest
{
    public string SystemPrompt { get; init; } = "";
    public string UserContent { get; init; } = "";
    public double Temperature { get; init; } = 0.3;
    public int? MaxTokens { get; init; } = 2000;
    /// <summary>覆盖默认模型（追问用快捷模型）。</summary>
    public string? ModelOverride { get; init; }
    /// <summary>尝试 response_format=json_object（端点不支持时自动忽略）。</summary>
    public bool PreferJson { get; init; }
    public bool Stream { get; init; }
}

/// <summary>
/// OpenAI 兼容 Chat Completions 客户端：URL 补全、鉴权、超时、可选流式。
/// AnalyticsAiService / AiChatService 共用。ApiKey 只存在本机 settings，不写日志。
/// </summary>
public sealed class OpenAiCompatibleChatClient
{
    private readonly ISettingsService _settings;
    private static readonly HttpClient Http = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

    public OpenAiCompatibleChatClient(ISettingsService settings) => _settings = settings;

    public bool IsConfigured
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_settings.AiBaseUrl) || string.IsNullOrWhiteSpace(_settings.AiModel))
                return false;
            if (!string.IsNullOrWhiteSpace(_settings.AiApiKey)) return true;
            var url = _settings.AiBaseUrl;
            return url.Contains("127.0.0.1", StringComparison.Ordinal)
                   || url.Contains("localhost", StringComparison.OrdinalIgnoreCase)
                   || url.Contains("::1", StringComparison.Ordinal);
        }
    }

    public string BuildEndpoint()
    {
        var baseUrl = _settings.AiBaseUrl!.Trim().TrimEnd('/');
        if (baseUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            return baseUrl;
        if (baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            return baseUrl + "/chat/completions";
        return baseUrl + "/v1/chat/completions";
    }

    /// <summary>非流式一次性调用，返回 message.content。</summary>
    public async Task<string> CompleteAsync(OpenAiChatRequest request, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("AI 未配置或未启用（设置 → 分析 AI）。云端需填 Key；本地端点可留空 Key。");

        var timeoutSeconds = Math.Clamp(_settings.AiTimeoutSeconds, 30, 900);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            using var req = BuildRequest(request, stream: false);
            using var resp = await Http.SendAsync(req, timeoutCts.Token).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                Log.Warning("AI HTTP {Status}", (int)resp.StatusCode);
                throw new HttpRequestException($"AI 服务返回 {(int)resp.StatusCode}");
            }
            return ExtractContent(text);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"AI 请求在 {timeoutSeconds} 秒后超时。本地模型较慢时，请到「设置 → 分析 AI」调大超时（当前可设 30–900 秒）。");
        }
    }

    /// <summary>流式调用：逐段产出 content；端点不支持流式时自动回退一次性。</summary>
    public async IAsyncEnumerable<string> StreamAsync(
        OpenAiChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("AI 未配置或未启用（设置 → 分析 AI）。云端需填 Key；本地端点可留空 Key。");

        var timeoutSeconds = Math.Clamp(_settings.AiTimeoutSeconds, 30, 900);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        HttpRequestMessage req;
        try
        {
            req = BuildRequest(request, stream: true);
        }
        catch
        {
            yield break;
        }

        HttpResponseMessage? resp = null;
        string? fallbackContent = null;
        List<string>? streamed = null;
        try
        {
            resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
                Log.Warning("AI stream HTTP {Status}", (int)resp.StatusCode);
                throw new HttpRequestException($"AI 服务返回 {(int)resp.StatusCode}");
            }

            var contentType = resp.Content.Headers.ContentType?.MediaType ?? "";
            if (!contentType.Contains("event-stream", StringComparison.OrdinalIgnoreCase) &&
                !contentType.Contains("text/plain", StringComparison.OrdinalIgnoreCase))
            {
                var full = await resp.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
                fallbackContent = ExtractContent(full);
            }
            else
            {
                streamed = new List<string>();
                using var stream = await resp.Content.ReadAsStreamAsync(timeoutCts.Token).ConfigureAwait(false);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                while (!reader.EndOfStream)
                {
                    timeoutCts.Token.ThrowIfCancellationRequested();
                    var line = await reader.ReadLineAsync(timeoutCts.Token).ConfigureAwait(false);
                    if (line is null) break;
                    if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                    var data = line.Substring(5).Trim();
                    if (data is "[DONE]" or "") continue;
                    var delta = ExtractStreamDelta(data);
                    if (!string.IsNullOrEmpty(delta))
                        streamed.Add(delta);
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"AI 请求在 {timeoutSeconds} 秒后超时。本地模型较慢时，请到「设置 → 分析 AI」调大超时。");
        }
        finally
        {
            resp?.Dispose();
            req.Dispose();
        }

        if (fallbackContent is not null)
        {
            if (fallbackContent.Length > 0)
                yield return fallbackContent;
            yield break;
        }

        if (streamed is not null)
        {
            foreach (var chunk in streamed)
                yield return chunk;
        }
    }

    private HttpRequestMessage BuildRequest(OpenAiChatRequest request, bool stream)
    {
        var model = request.ModelOverride is { Length: > 0 } mo
            ? mo.Trim()
            : _settings.AiModel!.Trim();
        var body = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["temperature"] = request.Temperature,
            ["messages"] = new object[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = request.UserContent }
            }
        };
        if (request.MaxTokens is { } mt)
            body["max_tokens"] = mt;
        if (request.PreferJson)
            body["response_format"] = new { type = "json_object" };
        if (stream)
            body["stream"] = true;

        var req = new HttpRequestMessage(HttpMethod.Post, BuildEndpoint());
        var key = _settings.AiApiKey?.Trim();
        if (!string.IsNullOrEmpty(key))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return req;
    }

    private static string ExtractContent(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";
    }

    private static string? ExtractStreamDelta(string data)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                return null;
            var c0 = choices[0];
            if (!c0.TryGetProperty("delta", out var delta)) return null;
            if (delta.TryGetProperty("content", out var content))
                return content.GetString();
            return null;
        }
        catch
        {
            return null;
        }
    }
}
