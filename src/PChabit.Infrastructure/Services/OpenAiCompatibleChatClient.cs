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

    /// <summary>本地端点（LM Studio 等）。Key 恒为空。</summary>
    public AiEndpointConfig LocalConfig => new(
        _settings.AiLocalBaseUrl ?? "",
        "",
        _settings.AiLocalModel ?? "",
        "本地");

    /// <summary>
    /// 云端端点 = 主配置三件套（BaseUrl / ApiKey / Model）整组生效。
    /// 不再维护独立「云端槽」，避免主配置与云端配置混搭导致 401。
    /// </summary>
    public AiEndpointConfig CloudConfig => new(
        _settings.AiBaseUrl ?? "",
        _settings.AiApiKey ?? "",
        _settings.AiModel ?? "",
        "云端");

    /// <summary>与 CloudConfig 同义。</summary>
    public AiEndpointConfig PrimaryConfig => CloudConfig;

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
                    _ => cloud.IsUsable ? cloud : local
                }
            };
        }

        var picked = Pick();

        // 显式指定槽位时不串槽（避免「删了云端模型却拿本地/主配置顶上」）
        if (slot is AiEndpointSlot.Local or AiEndpointSlot.Cloud or AiEndpointSlot.Primary)
            return picked;

        if (picked.IsUsable) return picked;
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
            {
                var content = ExtractContent(text1);
                if (string.IsNullOrWhiteSpace(content))
                {
                    Log.Warning("AI 响应成功但 content 为空 via {Slot}: {Body}", cfg.Label, Truncate(text1, 400));
                    throw new HttpRequestException(
                        $"AI 服务（{cfg.Label}）返回成功但正文为空。原始片段：{Truncate(text1, 240)}");
                }
                return content;
            }

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
                    var content2 = ExtractContent(text2);
                    if (string.IsNullOrWhiteSpace(content2))
                        throw new HttpRequestException(
                            $"AI 服务（{cfg.Label}）返回成功但正文为空。原始片段：{Truncate(text2, 240)}");
                    return content2;
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
            if (string.IsNullOrWhiteSpace(fallbackContent))
                throw new HttpRequestException(
                    $"AI 服务（{cfg.Label}）流式返回成功但正文为空。请改用非流式或检查模型输出。");
            yield return fallbackContent;
            yield break;
        }

        if (streamed is not null)
        {
            var joined = string.Concat(streamed);
            if (string.IsNullOrWhiteSpace(joined))
                throw new HttpRequestException(
                    $"AI 服务（{cfg.Label}）流式返回 {streamed.Count} 段但正文为空。可尝试关闭流式或更新 LM Studio。");
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
        // Key 只去首尾空白（防粘贴时带换行），中间字符原样发送
        var key = cfg.ApiKey?.Trim();
        if (!string.IsNullOrEmpty(key))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        Log.Information("AI 请求 → {Label} {Url} model={Model} keyLen={KeyLen}",
            cfg.Label, BuildEndpoint(cfg), model, key?.Length ?? 0);
        return req;
    }

    private static string Truncate(string s, int n)
    {
        s = s.Replace('\n', ' ').Replace('\r', ' ');
        return s.Length <= n ? s : s[..n] + "…";
    }

    /// <summary>从多种 OpenAI 兼容响应形状提取正文（含 reasoning 模型、completions 风格）。</summary>
    public static string ExtractContent(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                return "";
            var c0 = choices[0];

            // chat: 只要 content。reasoning_content 多为英文思考过程，不能当正文
            if (c0.TryGetProperty("message", out var msg))
            {
                var content = ReadString(msg, "content");
                if (!string.IsNullOrWhiteSpace(content)) return content;
            }

            // chat delta style
            if (c0.TryGetProperty("delta", out var delta))
            {
                var d = ReadString(delta, "content");
                if (!string.IsNullOrWhiteSpace(d)) return d;
            }

            // legacy completions
            var text = ReadString(c0, "text");
            if (!string.IsNullOrWhiteSpace(text)) return text;

            return "";
        }
        catch
        {
            return "";
        }
    }

    private static string ReadString(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";

    private static string? ExtractStreamDelta(string data)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;
            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            {
                // 有的服务器把正文放在顶层 message / content
                if (root.TryGetProperty("message", out var m))
                {
                    var t = ReadString(m, "content");
                    return string.IsNullOrWhiteSpace(t) ? null : t;
                }
                if (root.TryGetProperty("content", out var top))
                    return top.ValueKind == JsonValueKind.String ? top.GetString() : null;
                return null;
            }

            var c0 = choices[0];
            if (c0.TryGetProperty("delta", out var delta))
            {
                var d = ReadString(delta, "content");
                if (!string.IsNullOrWhiteSpace(d)) return d;
                var r = ReadString(delta, "reasoning_content");
                return string.IsNullOrWhiteSpace(r) ? null : r;
            }

            if (c0.TryGetProperty("message", out var msg))
            {
                var t = ReadString(msg, "content");
                return string.IsNullOrWhiteSpace(t) ? null : t;
            }

            var text = ReadString(c0, "text");
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch
        {
            return null;
        }
    }
}
