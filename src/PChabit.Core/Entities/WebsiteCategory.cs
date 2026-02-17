using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PChabit.Core.Entities;

public class WebsiteCategory
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
    public string Icon { get; set; } = "🌐";

    public int SortOrder { get; set; } = 0;

    public bool IsSystem { get; set; } = false;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? UpdatedAt { get; set; }

    public virtual ICollection<WebsiteDomainMapping> DomainMappings { get; set; } = new List<WebsiteDomainMapping>();
}

public class WebsiteDomainMapping
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string DomainPattern { get; set; } = string.Empty;

    public int CategoryId { get; set; }

    [ForeignKey("CategoryId")]
    public virtual WebsiteCategory? Category { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? UpdatedAt { get; set; }
}
