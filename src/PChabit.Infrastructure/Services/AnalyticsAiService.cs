using System.Runtime.CompilerServices;
using PChabit.Core.Interfaces;
using Serilog;

namespace PChabit.Infrastructure.Services;

public interface IAnalyticsAiService
{
    bool IsConfigured { get; }
    /// <summary>调用兼容 OpenAI Chat Completions 的端点；返回模型原文（期望为 JSON 字符串）。</summary>
    Task<string> InterpretAsync(string systemPrompt, string userPayload, CancellationToken ct = default);
    /// <summary>流式返回 content 片段。</summary>
    IAsyncEnumerable<string> InterpretStreamAsync(string systemPrompt, string userPayload, CancellationToken ct = default);
    /// <summary>追问用快捷模型（可与解读模型不同）。</summary>
    Task<string> ChatFastAsync(string systemPrompt, string userPayload, CancellationToken ct = default);
}

/// <summary>
/// 分析页「AI 深度解读」。默认关闭；仅发送 AnalysisReportBuilder / AiContextPack 生成的聚合 JSON。
/// ApiKey 只存在本机 settings.json，不写日志。超时读设置 AiTimeoutSeconds（默认 300s）。
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
            PreferJson = true
        }, ct).ConfigureAwait(false);
    }

    /// <summary>追问/轻量对话：可用快捷模型（AiModelFast）。</summary>
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
            ModelOverride = string.IsNullOrWhiteSpace(fast) ? null : fast
        }, ct);
    }

    public async IAsyncEnumerable<string> InterpretStreamAsync(
        string systemPrompt,
        string userPayload,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("AI 深度解读未配置或未启用（设置 → 分析 AI）。云端需填 Key；本地端点可留空 Key。");

        await foreach (var chunk in _client.StreamAsync(new OpenAiChatRequest
        {
            SystemPrompt = systemPrompt,
            UserContent = userPayload,
            Temperature = 0.3,
            MaxTokens = 2000,
            PreferJson = true,
            Stream = true
        }, ct).ConfigureAwait(false))
        {
            yield return chunk;
        }
    }
}
