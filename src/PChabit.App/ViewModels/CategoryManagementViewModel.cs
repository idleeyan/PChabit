using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Serilog;
using PChabit.Core.Entities;
using PChabit.Infrastructure.Data;

namespace PChabit.App.ViewModels;

public class RunningProcessItem
{
    public string ProcessName { get; set; } = string.Empty;
    public string? MainWindowTitle { get; set; }
    public string? ProcessPath { get; set; }
}

/// <summary>
/// 应用分类管理（3.33.0 由CategoryCrudViewModelBase 承接通用 CRUD，
/// 本类只提供「进程名映射」相关的差异实现）。
/// </summary>
public partial class CategoryManagementViewModel
    : CategoryCrudViewModelBase<ProgramCategory, ProgramCategoryMapping>
{
    protected override string LogPrefix => "CategoryManagementViewModel";

    public CategoryManagementViewModel(IDbContextFactory<PChabitDbContext> dbFactory) : base(dbFactory)
    {
    }

    protected override Task<List<ProgramCategory>> QueryCategoriesAsync(PChabitDbContext db)
        => db.ProgramCategories
            .Include(c => c.ProgramMappings)
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync();

    protected override Task<List<ProgramCategoryMapping>> QueryMappingsAsync(PChabitDbContext db)
        => db.ProgramCategoryMappings.ToListAsync();

    protected override void AddCategoryTo(PChabitDbContext db, ProgramCategory category)
    {
        category.IsActive = true;
        category.CreatedAt = DateTime.Now;
        db.ProgramCategories.Add(category);
    }

    protected override Task<ProgramCategory?> FindCategoryAsync(PChabitDbContext db, int id)
        => Task.FromResult(db.ProgramCategories.Find(id));

    protected override void AddMappingTo(PChabitDbContext db, ProgramCategoryMapping mapping)
    {
        mapping.CreatedAt = DateTime.Now;
        db.ProgramCategoryMappings.Add(mapping);
    }

    protected override Task<ProgramCategoryMapping?> FindMappingAsync(PChabitDbContext db, int id)
        => Task.FromResult(db.ProgramCategoryMappings.Find(id));

    protected override void RemoveMappingFrom(PChabitDbContext db, ProgramCategoryMapping mapping)
        => db.ProgramCategoryMappings.Remove(mapping);

    /// <summary>搜索键 = 进程名 + 别名（两者任一命中即匹配）。</summary>
    protected override string GetMappingKey(ProgramCategoryMapping mapping)
        => string.IsNullOrEmpty(mapping.ProcessAlias)
            ? mapping.ProcessName
            : $"{mapping.ProcessName} {mapping.ProcessAlias}";

    /// <summary>列出当前运行中的程序，供「添加映射」对话框选择。</summary>
    public List<RunningProcessItem> GetRunningProcesses()
    {
        var processes = new List<RunningProcessItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (string.IsNullOrEmpty(process.ProcessName)) continue;
                    if (process.MainWindowHandle == IntPtr.Zero) continue;

                    var processName = process.ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                        ? process.ProcessName
                        : $"{process.ProcessName}.exe";

                    if (!seen.Add(processName)) continue;

                    processes.Add(new RunningProcessItem
                    {
                        ProcessName = processName,
                        MainWindowTitle = process.MainWindowTitle,
                        ProcessPath = process.MainModule?.FileName
                    });
                }
                catch
                {
                    // 跳过无权访问的进程
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "获取运行中的进程失败");
        }

        return processes.OrderBy(p => p.ProcessName).ToList();
    }
}