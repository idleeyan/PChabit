namespace PChabit.Core.Entities;

public class BackupRecord : EntityBase
{
    public string FilePath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public long FileSize { get; set; }
    public int? RecordCount { get; set; }
    public bool IsAutomatic { get; set; }
    public string? Description { get; set; }
}
