namespace PChabit.Core.Sync;

public enum FlatItemType
{
    Bookmark,
    Folder
}

/// <summary>扁平书签/文件夹项，合并算法的统一输入输出。</summary>
public class FlatItem
{
    public FlatItemType Type { get; set; }

    /// <summary>本机浏览器 id；云端/基线项可为空。</summary>
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Url { get; set; }

    /// <summary>祖先文件夹标题链（不含自身标题）。</summary>
    public IReadOnlyList<string> Path { get; set; } = Array.Empty<string>();

    public long DateAdded { get; set; }

    public long DateModified { get; set; }

    /// <summary>稳定键；缺失时由 ItemKey 补算。</summary>
    public string Key { get; set; } = string.Empty;

    public bool IsBookmark => Type == FlatItemType.Bookmark;

    public bool IsFolder => Type == FlatItemType.Folder;

    public FlatItem Clone() => new()
    {
        Type = Type,
        Id = Id,
        Title = Title,
        Url = Url,
        Path = Path.ToArray(),
        DateAdded = DateAdded,
        DateModified = DateModified,
        Key = Key
    };
}

public class RenameOp
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
}

public class MergeCounts
{
    public int Local { get; set; }
    public int Cloud { get; set; }
    public int Last { get; set; }
    public int AddLocal { get; set; }
    public int RemoveLocal { get; set; }
    public int RenameLocal { get; set; }
    public int Merged { get; set; }
}

/// <summary>三路合并计划：本机待变更 + 合并后的云端权威集合。</summary>
public class MergePlan
{
    public List<FlatItem> ToAddLocal { get; set; } = new();
    public List<FlatItem> ToRemoveLocal { get; set; } = new();
    /// <summary>待从各浏览器/本地删除的空文件夹（path+title）。</summary>
    public List<FlatItem> ToRemoveFolders { get; set; } = new();
    public List<RenameOp> ToRenameLocal { get; set; } = new();
    public List<FlatItem> MergedCloud { get; set; } = new();
    public bool HasHistory { get; set; }
    public MergeCounts Counts { get; set; } = new();
}

public class BookmarkSyncResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int Added { get; set; }
    public int Removed { get; set; }
    public int Renamed { get; set; }
    public int LocalTotal { get; set; }
    public List<string> Errors { get; set; } = new();
}
