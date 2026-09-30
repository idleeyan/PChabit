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
    /// <summary>覆盖默认模型。</summary>
    public string? ModelOverride { get; init; }
    /// <summary>指定端点槽位；默认 Auto（按 AiEndpointMode 与用途解析）。</summary>
    public AiEndpointSlot Slot { get; init; } = AiEndpointSlot.Auto;
    /// <summary>true=解读（云端优先），false=追问（本地优先）。</summary>
    public bool IsInsight { get; init; } = true;
    public bool PreferJson { get; init; }
    public bool Stream { get; init; }
}

/// <summary>
/// OpenAI 兼容客户端：支持本地 LM Studio + 云端双端点。
/// ApiKey 只存在本机 settings.json，不写日志。
/// </summary>
public sealed class OpenAiCompatibleChatClient
{
    private readonly ISettingsService _settings;
    private static readonly HttpClient Http = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

    public OpenAiCompatibleChatClient(ISettingsService settings) => _settings = settings;

    /// <summary>本地端点配置。</summary>
    public AiEndpointConfig LocalConfig => new(
        _settings.AiLocalBaseUrl ?? "",
        "",
        _settings.AiLocalModel ?? "",
        "本地");

    /// <summary>云端端点配置（空字段回退主配置）。</summary>
    public AiEndpointConfig CloudConfig => new(
        string.IsNullOrWhiteSpace(_settings.AiCloudBaseUrl) ? _settings.AiBaseUrl ?? "" : _settings.AiCloudBaseUrl,
        string.IsNullOrWhiteSpace(_settings.AiCloudApiKey) ? _settings.AiApiKey ?? "" : _settings.AiCloudApiKey,
        string.IsNullOrWhiteSpace(_settings.AiCloudModel) ? _settings.AiModel ?? "" : _settings.AiCloudModel,
        "云端");

    /// <summary>主端点（兼容旧单端点配置）。</summary>
    public AiEndpointConfig PrimaryConfig => new(
        _settings.AiBaseUrl ?? "",
        _settings.AiApiKey ?? "",
        _settings.AiModel ?? "",
        "主端点");

    public string EndpointMode => string.IsNullOrWhiteSpace(_settings.AiEndpointMode)
        ? "cloud"
        : _settings.AiEndpointMode.Trim().ToLowerInvariant();

    public bool IsConfigured => Resolve(AiEndpointSlot.Auto, isInsight: true) is { IsUsable: true };

    /// <summary>按模式解析实际使用的端点。</summary>
    public AiEndpointConfig Resolve(AiEndpointSlot slot, bool isInsight)
    {
        var local = LocalConfig;
        var cloud = CloudConfig;

        AiEndpointConfig Pick()
        {
            return slot switch
            {
                AiEndpointSlot.Local => local,
                AiEndpointSlot.Cloud => cloud,
                AiEndpointSlot.Primary => PrimaryConfig,
                _ => EndpointMode switch
                {
                    "local" => local.IsUsable ? local : cloud,
                    "dual" => isInsight
                        ? (cloud.IsUsable ? cloud : local)
                        : (local.IsUsable ? local : cloud),
                    // cloud（默认）
                    _ => cloud.IsUsable ? cloud : local
                }
            };
        }

        var picked = Pick();
        if (picked.IsUsable) return picked;
        // 回退：任一可用端点
        if (cloud.IsUsable) return cloud;
        if (local.IsUsable) return local;
        return picked;
    }

    public string BuildEndpoint(AiEndpointConfig cfg)
    {
        var baseUrl = (cfg.BaseUrl ?? "").Trim().TrimEnd('/');
        if (baseUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            return baseUrl;
        if (baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            return baseUrl + "/chat/completions";
        return baseUrl + "/v1/chat/completions";
    }

    /// <summary>非流式一次性调用。400 时自动降级重试（去掉 response_format / max_tokens）。</summary>
    public async Task<string> CompleteAsync(OpenAiChatRequest request, CancellationToken ct = default)
    {
        var cfg = Resolve(request.Slot, request.IsInsight);
        if (!cfg.IsUsable)
            throw new InvalidOperationException("AI 未配置或未启用。请在设置中配置云端或本地端点（本地可空 Key）。");

        var timeoutSeconds = Math.Clamp(_settings.AiTimeoutSeconds, 30, 900);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            // 第一次：完整参数；本地端点默认不发 response_format（LM Studio 等常返回 400）
            var minimalFirst = cfg.IsLocalHost;
            using var req1 = BuildRequest(cfg, request, stream: false, minimal: minimalFirst);
            using var resp1 = await Http.SendAsync(req1, timeoutCts.Token).ConfigureAwait(false);
            var text1 = await resp1.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
            if (resp1.IsSuccessStatusCode)
                return ExtractContent(text1);

            Log.Warning("AI HTTP {Status} via {Slot}: {Body}", (int)resp1.StatusCode, cfg.Label, Truncate(text1, 300));

            // 400/404/422：去掉可选字段再试一次
            if ((int)resp1.StatusCode is 400 or 404 or 422 && !minimalFirst)
            {
                using var req2 = BuildRequest(cfg, request, stream: false, minimal: true);
                using var resp2 = await Http.SendAsync(req2, timeoutCts.Token).ConfigureAwait(false);
                var text2 = await resp2.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
                if (resp2.IsSuccessStatusCode)
                {
                    Log.Information("AI 降级请求成功（去掉 response_format/max_tokens）via {Slot}", cfg.Label);
                    return ExtractContent(text2);
                }
                Log.Warning("AI 降级仍失败 {Status}: {Body}", (int)resp2.StatusCode, Truncate(text2, 300));
                throw new HttpRequestException(
                    $"AI 服务（{cfg.Label}）返回 {(int)resp2.StatusCode}：{Truncate(text2, 200)}");
            }

            throw new HttpRequestException(
                $"AI 服务（{cfg.Label}）返回 {(int)resp1.StatusCode}：{Truncate(text1, 200)}");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"AI 请求在 {timeoutSeconds} 秒后超时。本地模型较慢时，请到「设置 → 分析 AI」调大超时。");
        }
    }

    /// <summary>流式：产出 content 片段。</summary>
    public async IAsyncEnumerable<string> StreamAsync(
        OpenAiChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var cfg = Resolve(request.Slot, request.IsInsight);
        if (!cfg.IsUsable)
            throw new InvalidOperationException("AI 未配置或未启用。请在设置中配置云端或本地端点。");

        var timeoutSeconds = Math.Clamp(_settings.AiTimeoutSeconds, 30, 900);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        using var req = BuildRequest(cfg, request, stream: true, minimal: cfg.IsLocalHost);
        HttpResponseMessage? resp = null;
        string? fallbackContent = null;
        List<string>? streamed = null;
        try
        {
            resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
                Log.Warning("AI stream HTTP {Status} via {Slot}: {Body}", (int)resp.StatusCode, cfg.Label, Truncate(body, 300));
                throw new HttpRequestException(
                    $"AI 服务（{cfg.Label}）返回 {(int)resp.StatusCode}：{Truncate(body, 200)}");
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
                while (await reader.ReadLineAsync(timeoutCts.Token).ConfigureAwait(false) is { } line)
                {
                    timeoutCts.Token.ThrowIfCancellationRequested();
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

    private HttpRequestMessage BuildRequest(AiEndpointConfig cfg, OpenAiChatRequest request, bool stream, bool minimal = false)
    {
        var model = request.ModelOverride is { Length: > 0 } mo
            ? mo.Trim()
            : (cfg.Model ?? "").Trim();
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
        // 本地端点与降级请求不发可选字段（LM Studio/Ollama 对 response_format 等敏感）
        var omitOptional = minimal || cfg.IsLocalHost;
        if (request.MaxTokens is { } mt && !omitOptional)
            body["max_tokens"] = mt;
        if (request.PreferJson && !omitOptional)
            body["response_format"] = new { type = "json_object" };
        if (stream)
            body["stream"] = true;

        var req = new HttpRequestMessage(HttpMethod.Post, BuildEndpoint(cfg));
        var key = cfg.ApiKey?.Trim();
        if (!string.IsNullOrEmpty(key))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return req;
    }

    private static string Truncate(string s, int n)
    {
        s = s.Replace('\n', ' ').Replace('\r', ' ');
        return s.Length <= n ? s : s[..n] + "…";
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
