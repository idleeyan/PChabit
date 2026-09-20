using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Serilog;
using PChabit.Infrastructure.Data;
using PChabit.Core.Entities;

namespace PChabit.App.ViewModels;

/// <summary>
/// 浏览历史分析报告 ViewModel。
/// 从本机历史库读取全部 BrowserHistoryItems，输出为前端可渲染的短字段 JSON：
/// [{ u: url, t: title, l: lastVisitTime, v: visitCount }]
/// </summary>
public partial class HistoryReportViewModel : ViewModelBase
{
    private readonly IDbContextFactory<PChabitDbContext> _contextFactory;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _summaryText = "加载中…";

    [ObservableProperty]
    private string? _dataJson;

    public HistoryReportViewModel(IDbContextFactory<PChabitDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task LoadDataAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        try
        {
            var json = await Task.Run(LoadJsonOnBackgroundAsync);
            await RunOnUIThreadAsync(() =>
            {
                DataJson = json;
                SummaryText = $"本机历史库共 {Count} 条记录，数据就绪";
                return Task.CompletedTask;
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[HistoryReport] 数据加载失败");
            await RunOnUIThreadAsync(() =>
            {
                SummaryText = "数据加载失败: " + ex.Message;
                return Task.CompletedTask;
            });
        }
        finally
        {
            IsLoading = false;
        }
    }

    public int Count { get; private set; }

    private async Task<string> LoadJsonOnBackgroundAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var items = await context.BrowserHistoryItems
            .AsNoTracking()
            .Select(x => new { x.Url, x.Title, x.VisitTime, x.VisitCount })
            .ToListAsync();

        Count = items.Count;

        var dto = items.Select(x => new
        {
            u = x.Url ?? "",
            t = x.Title ?? "",
            l = x.VisitTime,
            v = x.VisitCount > 0 ? x.VisitCount : 1
        }).ToList();

        var options = new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        return JsonSerializer.Serialize(dto, options);
    }
}
