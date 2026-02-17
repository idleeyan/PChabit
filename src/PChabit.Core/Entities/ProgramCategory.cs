using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PChabit.Core.Entities;

public class ProgramCategory
{
    [Key]
    public int Id { get; set; }
    
    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;
    
    [MaxLength(200)]
    public string? Description { get; set; }
    
    [MaxLength(7)]
    public string Color { get; set; } = "#4A90E4";
    
    [MaxLength(50)]
    public string Icon { get; set; } = "📁";
    
    public int SortOrder { get; set; } = 0;
    
    public bool IsSystem { get; set; } = false;
    
    public bool IsActive { get; set; } = true;
    
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    
    public DateTime? UpdatedAt { get; set; }
    
    public virtual ICollection<ProgramCategoryMapping> ProgramMappings { get; set; } = new List<ProgramCategoryMapping>();
}

public class ProgramCategoryMapping
{
    [Key]
    public int Id { get; set; }
    
    [Required]
    [MaxLength(100)]
    public string ProcessName { get; set; } = string.Empty;
    
    [MaxLength(200)]
    public string? ProcessPath { get; set; }
    
    [MaxLength(100)]
    public string? ProcessAlias { get; set; }
    
    public int CategoryId { get; set; }
    
    [ForeignKey("CategoryId")]
    public virtual ProgramCategory? Category { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    
    public DateTime? UpdatedAt { get; set; }
}
