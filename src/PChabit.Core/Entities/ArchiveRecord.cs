namespace PChabit.Core.Entities;

public class ArchiveRecord : EntityBase
{
    public string FilePath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public long FileSize { get; set; }
    public DateTime DateRangeStart { get; set; }
    public DateTime DateRangeEnd { get; set; }
    public int? RecordCount { get; set; }
    public string? Description { get; set; }
}
