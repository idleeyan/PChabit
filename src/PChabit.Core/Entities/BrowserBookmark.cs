using System.ComponentModel.DataAnnotations;

namespace PChabit.Core.Entities;

/// <summary>
/// 浏览器书签权威存储。主键为稳定键：书签 b:{normalizedUrl}，文件夹 f:{path/title}。
/// DB 是 source of truth；扩展只 export/import。
/// </summary>
public class BrowserBookmark
{
    [Key]
    [MaxLength(500)]
    public string Id { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    public string Type { get; set; } = BrowserBookmarkTypes.Bookmark;

    [Required]
    [MaxLength(500)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Url { get; set; }

    /// <summary>JSON 数组，如 ["书签栏","Work"]。文件夹完整路径 = path + title。</summary>
    [Required]
    public string PathJson { get; set; } = "[]";

    public long DateAdded { get; set; }

    public long DateModified { get; set; }

    [MaxLength(50)]
    public string SourceBrowser { get; set; } = string.Empty;

    /// <summary>软删墓碑，供同步传播删除；本机也可物理删。</summary>
    public bool IsDeleted { get; set; }

    public long UpdatedAt { get; set; }
}

public static class BrowserBookmarkTypes
{
    public const string Bookmark = "bookmark";
    public const string Folder = "folder";
}

/// <summary>上次成功同步后的云端条目快照（单行）。</summary>
public class BookmarkSyncBaseline
{
    [Key]
    public int Id { get; set; } = 1;

    public long SavedAt { get; set; }

    /// <summary>与云文件 items 同构的 JSON 数组。</summary>
    public string ItemsJson { get; set; } = "[]";
}

/// <summary>书签/历史同步元数据（key-value）。</summary>
public class BrowserSyncMeta
{
    [Key]
    [MaxLength(100)]
    public string Key { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string Value { get; set; } = string.Empty;
}

public static class BrowserSyncMetaKeys
{
    public const string BookmarksLastSyncAt = "bookmarks_last_sync_at";
    public const string HistoryLastSyncAt = "history_last_sync_at";
    public const string DeviceId = "device_id";

    /// <summary>各浏览器上次成功同步时的导出快照（JSON: {browserName: [items]}），用于检测浏览器侧删除。</summary>
    public const string BrowserLastExports = "browser_last_exports";
}
