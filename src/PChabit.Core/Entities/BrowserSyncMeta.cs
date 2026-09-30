namespace PChabit.Core.Entities;

/// <summary>浏览器同步元数据（历史同步用）。</summary>
public class BrowserSyncMeta
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public static class BrowserSyncMetaKeys
{
    public const string HistoryLastSyncAt = "history_last_sync_at";
}
