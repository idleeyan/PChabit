namespace PChabit.Core.Interfaces;

/// <summary>AI 端点槽位：本地 LM Studio / 云端 / 按用途自动。</summary>
public enum AiEndpointSlot
{
    /// <summary>设置里的主端点（兼容旧配置 AiBaseUrl 等）。</summary>
    Primary,
    /// <summary>本地端点（LM Studio / Ollama 等）。</summary>
    Local,
    /// <summary>云端端点。</summary>
    Cloud,
    /// <summary>解读走云端（优先），追问走本地；缺一则回退另一端。</summary>
    Auto
}

/// <summary>单端点连接信息。</summary>
public sealed record AiEndpointConfig(
    string BaseUrl,
    string ApiKey,
    string Model,
    string Label)
{
    public bool HasUrlAndModel =>
        !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(Model);

    public bool IsLocalHost
    {
        get
        {
            var url = BaseUrl ?? "";
            return url.Contains("127.0.0.1", StringComparison.Ordinal)
                   || url.Contains("localhost", StringComparison.OrdinalIgnoreCase)
                   || url.Contains("::1", StringComparison.Ordinal);
        }
    }

    /// <summary>是否可用于请求：有 URL+模型，且（有 Key 或 本地）。</summary>
    public bool IsUsable => HasUrlAndModel && (!string.IsNullOrWhiteSpace(ApiKey) || IsLocalHost);
}
