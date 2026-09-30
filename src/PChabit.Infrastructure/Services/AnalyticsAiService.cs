using System.Runtime.CompilerServices;
using PChabit.Core.Interfaces;
using Serilog;

namespace PChabit.Infrastructure.Services;

public interface IAnalyticsAiService
{
    bool IsConfigured { get; }
    /// <summary>当前解析到的端点描述，便于 UI 显示「正在用本地/云端」。</summary>
    string ActiveEndpointLabel { get; }
    Task<string> InterpretAsync(string systemPrompt, string userPayload, CancellationToken ct = default);
    IAsyncEnumerable<string> InterpretStreamAsync(string systemPrompt, string userPayload, CancellationToken ct = default);
    /// <summary>追问用（双端点模式下优先本地）。</summary>
    Task<string> ChatFastAsync(string systemPrompt, string userPayload, CancellationToken ct = default);
}

/// <summary>
/// 分析页「AI 深度解读」。支持本地 LM Studio + 云端双端点：
/// - cloud：解读/追问都走云端
/// - local：都走本地
/// - dual：解读云端优先，追问本地优先
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
            var mode = _client.EndpointMode;
            return mode switch
            {
                "dual" => $"双端点：解读→{insight.Label} · 追问→{chat.Label}",
                "local" => $"本地：{insight.Label}",
                _ => $"云端：{insight.Label}"
            };
        }
    }

    public async Task<string> InterpretAsync(string systemPrompt, string userPayload, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("AI 深度解读未配置或未启用（设置 → 分析 AI）。云端需填 Key；本地端点可留空 Key。");

        return await _client.CompleteAsync(new OpenAiChatRequest
        {
            SystemPrompt = systemPrompt,
            UserContent = userPayload,
            Temperature = 0.3,
            MaxTokens = 2000,
            PreferJson = true,
            IsInsight = true,
            Slot = AiEndpointSlot.Auto
        }, ct).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<string> InterpretStreamAsync(
        string systemPrompt,
        string userPayload,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("AI 深度解读未配置或未启用（设置 → 分析 AI）。云端需填 Key；本地端点可留空 Key。");

        var cfg = _client.Resolve(AiEndpointSlot.Auto, isInsight: true);

        // 本地端点优先非流式：LM Studio 流式解析差异大，容易「跑完为空」
        if (cfg.IsLocalHost)
        {
            var text = await _client.CompleteAsync(new OpenAiChatRequest
            {
                SystemPrompt = systemPrompt,
                UserContent = userPayload,
                Temperature = 0.3,
                MaxTokens = 2000,
                IsInsight = true,
                Slot = AiEndpointSlot.Auto
            }, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(text))
                yield return text;
            yield break;
        }

        await foreach (var chunk in _client.StreamAsync(new OpenAiChatRequest
        {
            SystemPrompt = systemPrompt,
            UserContent = userPayload,
            Temperature = 0.3,
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
        return _client.CompleteAsync(new OpenAiChatRequest
        {
            SystemPrompt = systemPrompt,
            UserContent = userPayload,
            Temperature = 0.5,
            MaxTokens = 1200,
            ModelOverride = string.IsNullOrWhiteSpace(fast) ? null : fast,
            IsInsight = false,
            Slot = AiEndpointSlot.Auto
        }, ct);
    }
}
