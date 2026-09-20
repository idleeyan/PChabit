namespace PChabit.Core.Entities;

/// <summary>浏览器侧待确认变更（书签库中枢）。</summary>
public class PendingBookmarkChange
{
    public int Id { get; set; }
    /// <summary>new | rename | delete | move</summary>
    public string ChangeType { get; set; } = "delete";
    public string Browser { get; set; } = "";
    public string ItemKey { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Url { get; set; }
    public string PathJson { get; set; } = "[]";
    public long DetectedAt { get; set; }
    /// <summary>pending | confirmed | kept | ignored</summary>
    public string Status { get; set; } = "pending";
}

public static class PendingBookmarkChangeTypes
{
    public const string New = "new";
    public const string Rename = "rename";
    public const string Delete = "delete";
    public const string Move = "move";
}

public static class PendingBookmarkChangeStatus
{
    public const string Pending = "pending";
    public const string Confirmed = "confirmed";
    public const string Kept = "kept";
    public const string Ignored = "ignored";
}
