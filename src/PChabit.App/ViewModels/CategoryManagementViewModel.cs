using System.Diagnostics;
using Serilog;

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
/// E3-7（3.80.0）：数据操作经 ProgramCategoryCrudService（Application/UseCases）。
/// </summary>
public partial class CategoryManagementViewModel
    : CategoryCrudViewModelBase<Core.Entities.ProgramCategory, Core.Entities.ProgramCategoryMapping>
{
    protected override string LogPrefix => "CategoryManagementViewModel";

    public CategoryManagementViewModel(Application.UseCases.ProgramCategoryCrudService crud) : base(crud)
    {
    }

    /// <summary>搜索键 = 进程名 + 别名（两者任一命中即匹配）。</summary>
    protected override string GetMappingKey(Core.Entities.ProgramCategoryMapping mapping)
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
                catch (Exception ex)
                {
                    // 预期：系统进程/无权进程 MainModule 访问被拒绝，跳过该进程
                    Log.Debug(ex, "枚举运行进程被拒绝，跳过: {ProcessName}", process.ProcessName);
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
