namespace PChabit.Core.Entities;

/// <summary>
/// 分类实体的公共契约（3.33.0）。
///
/// <see cref="ProgramCategory"/> 与 <see cref="WebsiteCategory"/> 结构同构，
/// 由App 层的 <c>CategoryCrudViewModelBase</c> 通过本接口统一 CRUD 流程，
/// 避免同一套逻辑写两遍、改一处漏一处。
/// </summary>
public interface ICategoryEntity
{
    int Id { get; set; }
    string Name { get; set; }
    string? Description { get; set; }
    string Color { get; set; }
    string Icon { get; set; }
    int SortOrder { get; set; }
    bool IsSystem { get; set; }
    bool IsActive { get; set; }
    DateTime CreatedAt { get; set; }
    DateTime? UpdatedAt { get; set; }
}

/// <summary>分类映射实体的公共契约（进程名映射 / 域名映射）。</summary>
public interface IMappingEntity
{
    int Id { get; set; }
    int CategoryId { get; set; }
    DateTime CreatedAt { get; set; }
    DateTime? UpdatedAt { get; set; }
}