using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PChabit.Core.Interfaces;
using Serilog;

namespace PChabit.Infrastructure.Services;

public enum AiProviderKind
{
    Zhipu,
    DeepSeek,
    LmStudio,
    Custom
}

public sealed record AiProviderPreset(
    AiProviderKind Kind,
    string Label,
    string DefaultBaseUrl,
    string DefaultModel);

public static class AiProviderPresets
{
    public static readonly AiProviderPreset[] All =
    {
        new(AiProviderKind.Zhipu, "智谱", "https://open.bigmodel.cn/api/paas/v4", "glm-4-flash"),
        new(AiProviderKind.DeepSeek, "DeepSeek", "https://api.deepseek.com", "deepseek-chat"),
        new(AiProviderKind.LmStudio, "LM Studio", "http://127.0.0.1:1234/v1", "local-model"),
        new(AiProviderKind.Custom, "自定义 OpenAI 兼容", "https://api.openai.com/v1", "gpt-4o-mini")
    };

    public static AiProviderPreset Get(AiProviderKind kind)
        => All.FirstOrDefault(p => p.Kind == kind) ?? All[^1];
}

public interface IAiChatService
{
    bool IsConfigured { get; }
    Task<string> ChatAsync(string systemPrompt, string userPayload, CancellationToken ct = default);
}

/// <summary>
/// 通用 OpenAI 兼容对话（智谱 / DeepSeek / LM Studio / 自定义）。
/// 设置字段：AiProvider / AiBaseUrl / AiApiKey / AiModel / AiTimeoutSeconds（settings.json）。
/// </summary>
public sealed class AiChatService : IAiChatService
{
    private readonly ISettingsService _settings;
    private readonly OpenAiCompatibleChatClient _client;

    public AiChatService(ISettingsService settings)
    {
        _settings = settings;
        _client = new OpenAiCompatibleChatClient(settings);
    }

    public bool IsConfigured => _client.IsConfigured;

    public Task<string> ChatAsync(string systemPrompt, string userPayload, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("AI 未配置：请在设置中选择 Provider 并填写 Base URL / 模型");

        return _client.CompleteAsync(new OpenAiChatRequest
        {
            SystemPrompt = systemPrompt,
            UserContent = userPayload,
            Temperature = 0.5,
            MaxTokens = 1600,
            IsInsight = false,
            Slot = AiEndpointSlot.Auto
        }, ct);
    }
}

/// <summary>
/// AI 配置 WebDAV 全量同步（个人网络：含 API Key，明文）。
/// 云端路径：pchabit/ai-settings-v1.json
/// </summary>
public class AiSettingsSyncService
{
    private readonly IWebDAVSyncService? _webDav;
    private readonly ISettingsService _settings;

    public AiSettingsSyncService(ISettingsService settings, IWebDAVSyncService? webDav = null)
    {
        _settings = settings;
        _webDav = webDav;
    }

    private static string LocalPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PChabit", "ai-settings-v1.json");

    private Dictionary<string, object?> Snapshot()
    {
        return new Dictionary<string, object?>
        {
            ["AiProvider"] = _settings.AiProvider,
            ["AiBaseUrl"] = _settings.AiBaseUrl,
            ["AiApiKey"] = _settings.AiApiKey,
            ["AiModel"] = _settings.AiModel,
            ["AiTimeoutSeconds"] = _settings.AiTimeoutSeconds,
            ["AiInsightsEnabled"] = _settings.AiInsightsEnabled,
            ["BrowserAutoPush"] = _settings.BrowserAutoPush,
            ["BrowserSyncEnabled"] = _settings.BrowserSyncEnabled,
            ["WebDAVUrl"] = _settings.WebDAVUrl,
            ["WebDAVUsername"] = _settings.WebDAVUsername,
            ["WebDAVPassword"] = _settings.WebDAVPassword,
            ["WebDAVEnabled"] = _settings.WebDAVEnabled,
            ["updatedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    public async Task<bool> PushAsync(CancellationToken ct = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(Snapshot(), new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(LocalPath, json, ct);
            if (_webDav == null || string.IsNullOrWhiteSpace(_settings.WebDAVUrl)) return false;
            var bytes = Encoding.UTF8.GetBytes(json);
            var path = await _webDav.UploadFileAsync(
                _settings.WebDAVUrl, _settings.WebDAVUsername, _settings.WebDAVPassword,
                "pchabit/ai-settings-v1.json", bytes, ct);
            Log.Information("AI 配置已同步到 WebDAV：{Path}", path);
            return path != null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "AI 配置 WebDAV 同步失败");
            return false;
        }
    }

    public async Task<bool> PullAsync(bool overwriteLocal = true, CancellationToken ct = default)
    {
        try
        {
            if (_webDav == null || string.IsNullOrWhiteSpace(_settings.WebDAVUrl)) return false;
            var bytes = await _webDav.DownloadFileAsync(
                _settings.WebDAVUrl, _settings.WebDAVUsername, _settings.WebDAVPassword,
                "pchabit/ai-settings-v1.json", ct);
            if (bytes is not { Length: > 0 }) return false;
            var json = Encoding.UTF8.GetString(bytes);
            await File.WriteAllTextAsync(LocalPath, json, ct);
            if (!overwriteLocal) return true;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string S(string k, string fallback) =>
                root.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;
            int I(string k, int fallback) =>
                root.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : fallback;
            bool B(string k, bool fallback) =>
                root.TryGetProperty(k, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)
                    ? v.GetBoolean() : fallback;

            _settings.AiProvider = S("AiProvider", _settings.AiProvider);
            _settings.AiBaseUrl = S("AiBaseUrl", _settings.AiBaseUrl);
            _settings.AiApiKey = S("AiApiKey", _settings.AiApiKey);
            _settings.AiModel = S("AiModel", _settings.AiModel);
            _settings.AiTimeoutSeconds = I("AiTimeoutSeconds", _settings.AiTimeoutSeconds);
            _settings.AiInsightsEnabled = B("AiInsightsEnabled", _settings.AiInsightsEnabled);
            _settings.BrowserAutoPush = B("BrowserAutoPush", _settings.BrowserAutoPush);
            if (root.TryGetProperty("WebDAVUrl", out _))
            {
                _settings.WebDAVUrl = S("WebDAVUrl", _settings.WebDAVUrl);
                _settings.WebDAVUsername = S("WebDAVUsername", _settings.WebDAVUsername);
                _settings.WebDAVPassword = S("WebDAVPassword", _settings.WebDAVPassword);
                _settings.WebDAVEnabled = B("WebDAVEnabled", _settings.WebDAVEnabled);
            }
            _settings.Save();
            Log.Information("AI 配置已从 WebDAV 拉取并覆盖本机（含 Key）");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "AI 配置 WebDAV 拉取失败");
            return false;
        }
    }
}
