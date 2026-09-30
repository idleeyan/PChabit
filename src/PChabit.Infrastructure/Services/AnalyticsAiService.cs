using System.Runtime.CompilerServices;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Analysis;
using Serilog;

namespace PChabit.Infrastructure.Services;

public interface IAnalyticsAiService
{
    bool IsConfigured { get; }
    string ActiveEndpointLabel { get; }
    Task<string> InterpretAsync(string systemPrompt, string userPayload, CancellationToken ct = default);
    IAsyncEnumerable<string> InterpretStreamAsync(string systemPrompt, string userPayload, CancellationToken ct = default);
    Task<string> ChatFastAsync(string systemPrompt, string userPayload, CancellationToken ct = default);
}

/// <summary>
/// 分析页「AI 深度解读」。支持本地 LM Studio + 云端双端点。
/// 解读/追问均强制简体中文；偏英文时自动加约束重试。
/// </summary>
public sealed class AnalyticsAiService : IAnalyticsAiService
{
    private readonly ISettingsService _settings;
    private readonly OpenAiCompatibleChatClient _client;

    public AnalyticsAiService(ISettingsService settings)
    {
        _settings = settings;
        _client = new OpenAiCompatibleChatClient(settings);
    }

    public bool IsConfigured => _settings.AiInsightsEnabled && _client.IsConfigured;

    public string ActiveEndpointLabel
    {
        get
        {
            var insight = _client.Resolve(AiEndpointSlot.Auto, isInsight: true);
            var chat = _client.Resolve(AiEndpointSlot.Auto, isInsight: false);
            return _client.EndpointMode switch
            {
                "dual" => $"双端点：解读→{insight.Label} · 追问→{chat.Label}",
                "local" => $"本地：{insight.Label}",
                _ => $"云端：{insight.Label}"
            };
        }
    }

    public Task<string> InterpretAsync(string systemPrompt, string userPayload, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("AI 深度解读未配置或未启用（设置 → 分析 AI）。云端需填 Key；本地端点可留空 Key。");
        return CompleteChineseAsync(systemPrompt, userPayload, preferJson: true, isInsight: true, ct);
    }

    public async IAsyncEnumerable<string> InterpretStreamAsync(
        string systemPrompt,
        string userPayload,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("AI 深度解读未配置或未启用（设置 → 分析 AI）。云端需填 Key；本地端点可留空 Key。");

        var cfg = _client.Resolve(AiEndpointSlot.Auto, isInsight: true);

        // 本地端点优先非流式
        if (cfg.IsLocalHost)
        {
            var text = await CompleteChineseAsync(systemPrompt, userPayload, preferJson: true, isInsight: true, ct)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(text))
                yield return text;
            yield break;
        }

        await foreach (var chunk in _client.StreamAsync(new OpenAiChatRequest
        {
            SystemPrompt = systemPrompt,
            UserContent = WrapUser(userPayload),
            Temperature = 0.2,
            MaxTokens = 2000,
            PreferJson = true,
            Stream = true,
            IsInsight = true,
            Slot = AiEndpointSlot.Auto
        }, ct).ConfigureAwait(false))
        {
            yield return chunk;
        }
    }

    public Task<string> ChatFastAsync(string systemPrompt, string userPayload, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("AI 深度解读未配置或未启用（设置 → 分析 AI）。");
        var fast = _settings.AiModelFast;
        return CompleteChineseAsync(systemPrompt, userPayload, preferJson: false, isInsight: false, ct, fast);
    }

    private static string WrapUser(string payload)
        => AnalysisReportBuilder.UserLanguagePreamble + payload + AnalysisReportBuilder.UserLanguageSuffix;

    private async Task<string> CompleteChineseAsync(
        string systemPrompt,
        string userPayload,
        bool preferJson,
        bool isInsight,
        CancellationToken ct,
        string? modelOverride = null)
    {
        var first = await _client.CompleteAsync(new OpenAiChatRequest
        {
            SystemPrompt = systemPrompt,
            UserContent = WrapUser(userPayload),
            Temperature = 0.2,
            MaxTokens = 2000,
            PreferJson = preferJson,
            ModelOverride = string.IsNullOrWhiteSpace(modelOverride) ? null : modelOverride,
            IsInsight = isInsight,
            Slot = AiEndpointSlot.Auto
        }, ct).ConfigureAwait(false);

        if (!AnalysisReportBuilder.LooksMostlyEnglish(first))
            return first;

        Log.Warning("AI 输出偏英文，中文强制重试");
        var second = await _client.CompleteAsync(new OpenAiChatRequest
        {
            SystemPrompt = "【上次你用了英文，这是错误的】必须只用简体中文，只输出 JSON，禁止英文句子。" + systemPrompt,
            UserContent = WrapUser(userPayload),
            Temperature = 0.1,
            MaxTokens = 2000,
            PreferJson = preferJson,
            ModelOverride = string.IsNullOrWhiteSpace(modelOverride) ? null : modelOverride,
            IsInsight = isInsight,
            Slot = AiEndpointSlot.Auto
        }, ct).ConfigureAwait(false);

        return string.IsNullOrWhiteSpace(second) ? first : second;
    }
}
