namespace PChabit.Core.Entities;

/// <summary>
/// 桌面便签（3.24.0）。纯文本便签，支持颜色/置顶/归档/软删除。
/// 多设备同步以 Id（GUID）为合并键，UpdatedAt 为 LWW 依据，IsDeleted 为墓碑。
/// </summary>
public sealed class StickyNote
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>纯文本正文（P0）；单条上限 20000 字符。</summary>
    public string Content { get; set; } = "";

    /// <summary>颜色键：yellow | pink | blue | green | gray。</summary>
    public string Color { get; set; } = "yellow";

    /// <summary>同色/列表内手动排序（P1，P0 默认 0）。</summary>
    public int SortOrder { get; set; }

    public bool IsPinned { get; set; }

    /// <summary>归档：不显示在主列表但保留（区别于回收站）。</summary>
    public bool IsArchived { get; set; }

    /// <summary>软删除墓碑：同步依赖，回收站保留期内可还原。</summary>
    public bool IsDeleted { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>最后修改时间：每次内容/颜色/置顶变更必更新，同步 LWW 依据。</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public DateTime? DeletedAt { get; set; }

    /// <summary>最后修改设备短 ID（冲突留痕）。</summary>
    public string DeviceId { get; set; } = "";

    /// <summary>本机修改版本号（诊断用，每次修改递增）。</summary>
    public int Version { get; set; } = 1;

    // ===== P1 桌面便签窗状态（本机私有，不同步）=====
    public bool IsOnDesktop { get; set; }
    public int WindowLeft { get; set; } = -1;
    public int WindowTop { get; set; } = -1;
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
}
