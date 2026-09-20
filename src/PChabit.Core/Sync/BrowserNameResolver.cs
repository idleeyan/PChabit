namespace PChabit.Core.Sync;

/// <summary>
/// 浏览器显示名解析（纯函数，可单测）。
/// 优先级：用户显式标签 &gt; 桌面进程识别 &gt; 扩展自动检测 &gt; Unknown。
/// </summary>
public static class BrowserNameResolver
{
    public const string Unknown = "Unknown";
    public const string ProcessUnknown = "未知浏览器";
    public const string ChromiumFallback = "Chromium";

    /// <summary>
    /// 合成最终显示名。
    /// </summary>
    /// <param name="reported">扩展上报的 browser 字段</param>
    /// <param name="isUserOverride">扩展是否声明为用户显式指定</param>
    /// <param name="processName">桌面 TCP→PID→exe 识别结果；空或 ProcessUnknown 视为失败</param>
    public static string Resolve(string? reported, bool isUserOverride, string? processName)
    {
        // 1) 用户显式标签最高优先（空白视为未指定）
        if (isUserOverride && !string.IsNullOrWhiteSpace(reported))
            return reported.Trim();

        // 2) 进程识别（本机硬证据）
        if (!string.IsNullOrWhiteSpace(processName) && processName.Trim() != ProcessUnknown)
            return processName.Trim();

        // 3) 扩展自报（允许 Chromium 兜底，便于日志区分）
        if (!string.IsNullOrWhiteSpace(reported) && reported.Trim() != Unknown)
            return reported.Trim();

        return Unknown;
    }

    /// <summary>标签合法性：1–64 字符，trim 后非空（展示层再截断到 32）。</summary>
    public static bool IsValidLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return false;
        var t = label.Trim();
        return t.Length is >= 1 and <= 64;
    }

    /// <summary>截断到 32 字符并 trim；非法返回 null。</summary>
    public static string? NormalizeLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return null;
        var t = label.Trim();
        if (t.Length == 0) return null;
        return t.Length <= 32 ? t : t[..32];
    }
}
