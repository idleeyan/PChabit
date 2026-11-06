using PChabit.Core.Entities;

namespace PChabit.App.ViewModels;

/// <summary>
/// 网站分类管理（3.33.0 由CategoryCrudViewModelBase 承接通用 CRUD，
/// 本类只提供「域名映射」相关的差异实现）。
///
/// 3.33.0 移除了此处内联的第二份默认分类种子数据——
/// 种子补齐统一由 <see cref="PChabit.Infrastructure.Services.WebsiteCategoryService.EnsureCatalogSeededAsync"/>
/// 在数据库初始化阶段完成（见 DatabaseInitializer.SeedDefaultDataAsync）。
/// E3-7（3.80.0）：数据操作经 WebsiteCategoryCrudService（Application/UseCases）。
/// </summary>
public partial class WebsiteCategoryManagementViewModel
    : CategoryCrudViewModelBase<WebsiteCategory, WebsiteDomainMapping>
{
    protected override string LogPrefix => "WebsiteCategoryManagementViewModel";

    public WebsiteCategoryManagementViewModel(Application.UseCases.WebsiteCategoryCrudService crud) : base(crud)
    {
    }

    /// <summary>搜索键 = 域名模式。</summary>
    protected override string GetMappingKey(WebsiteDomainMapping mapping) => mapping.DomainPattern;
}
