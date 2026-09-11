using PChabit.Core.Interfaces;

namespace PChabit.Core.Entities;

public class WebSession : EntityBase, IAggregateRoot
{
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }

    private TimeSpan? _duration;
    public TimeSpan Duration
    {
        get => _duration ?? (EndTime.HasValue ? EndTime.Value - StartTime : TimeSpan.Zero);
        set => _duration = value;
    }

    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string? Favicon { get; set; }

    public string Browser { get; set; } = string.Empty;
    public int TabId { get; set; }

    public int ScrollDepth { get; set; }
    public int ClickCount { get; set; }
    public bool HasFormInteraction { get; set; }
    public List<string> InteractedElements { get; set; } = [];

    public string? Referrer { get; set; }
    public string? Source { get; set; }
    public List<string> SearchQueries { get; set; } = [];

    public bool IsActiveTab { get; set; }

    private TimeSpan? _activeDuration;
    public TimeSpan ActiveDuration
    {
        get => _activeDuration ?? Duration;
        set => _activeDuration = value;
    }

    /// <summary>空闲累计时长（墙钟 - 有效活跃）</summary>
    private TimeSpan? _idleDuration;
    public TimeSpan IdleDuration
    {
        get => _idleDuration ?? TimeSpan.Zero;
        set => _idleDuration = value;
    }

    /// <summary>有效浏览占比 0-1，由 ActiveDuration / Duration 计算</summary>
    public double FocusRatio => Duration > TimeSpan.Zero
        ? Math.Clamp(ActiveDuration.TotalMilliseconds / Duration.TotalMilliseconds, 0, 1)
        : 0;

    /// <summary>物化后的网站分类 Id</summary>
    public int? CategoryId { get; set; }

    /// <summary>物化后的网站分类名称（冗余，便于历史一致性）</summary>
    public string? CategoryName { get; set; }

    /// <summary>分类来源：Mapping / Fallback / Unknown</summary>
    public string? CategorySource { get; set; }

    /// <summary>历史切片数据标记（旧 30s 快照产生的行）</summary>
    public bool IsLegacy { get; set; }

    /// <summary>是否已持久化（周期保存时 upsert 用）</summary>
    public bool IsPersisted { get; set; }

    public List<TabSwitch> TabSwitches { get; set; } = [];
}

public class TabSwitch
{
    public DateTime Timestamp { get; set; }
    public string? FromUrl { get; set; }
    public string ToUrl { get; set; } = string.Empty;
    public TimeSpan TimeOnPreviousTab { get; set; }
}
