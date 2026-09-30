namespace PChabit.Core.ValueObjects;

/// <summary>
/// 行为语义标签：回答「正在做什么」，而不是「开了哪个应用」。
/// 字面量用于 JSON 序列化与 AI 指标包，保持小写 kebab-case。
/// </summary>
public static class ActivityLabels
{
    public const string Idle = "idle";
    public const string WorkCode = "work-code";
    public const string WorkDoc = "work-doc";
    public const string Meeting = "meeting";
    public const string Learn = "learn";
    public const string BrowseInfo = "browse-info";
    public const string BrowseFun = "browse-fun";
    public const string Game = "game";
    public const string Comms = "comms";
    public const string Admin = "admin";
    public const string Other = "other";

    /// <summary>稳定枚举顺序（供图表与指标包固定顺序）。</summary>
    public static readonly string[] All =
    {
        WorkCode, WorkDoc, Meeting, Learn,
        BrowseInfo, BrowseFun, Game, Comms, Admin, Idle, Other
    };

    public static bool IsKnown(string? label) =>
        label is Idle or WorkCode or WorkDoc or Meeting or Learn
            or BrowseInfo or BrowseFun or Game or Comms or Admin or Other;
}

/// <summary>会话级标签输入。窗口标题/域名仅本地推断，禁止进入云端 payload。</summary>
public sealed record ActivityLabelInput(
    string? ProcessName,
    string? AppCategory,
    string? WindowTitle,
    string? WebCategory,
    string? Domain,
    double KeysPerMinute,
    double SessionMinutes);

/// <summary>标签结果：标签 + 置信度 + 可测的判定原因。</summary>
public sealed record ActivityLabelResult(
    string Label,
    double Confidence,
    string Reason);
