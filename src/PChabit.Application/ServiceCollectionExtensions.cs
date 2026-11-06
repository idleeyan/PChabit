using Microsoft.Extensions.DependencyInjection;
using PChabit.Application.Aggregators;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Data;

namespace PChabit.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTaiApplication(this IServiceCollection services)
    {
        services.AddSingleton<SankeyAggregator>();
        // 仪表盘查询服务（D4 步骤 2b+3，3.71.0）：查库+聚合下沉应用层，VM 只做快照→绑定映射
        services.AddSingleton<Queries.DashboardQueryService>();
        // E3-1（3.74.0）：分析页/桑基图查询服务，VM 去 IDbContextFactory
        services.AddSingleton<Queries.AnalyticsQueryService>();
        services.AddSingleton<Queries.SankeyQueryService>();
        // E3-2（3.75.0）：时间线/流量页查询服务
        services.AddSingleton<Queries.TimelineQueryService>();
        services.AddSingleton<Queries.NetworkTrafficQueryService>();
        // E3-3（3.76.0）：应用统计/键鼠详情查询服务
        services.AddSingleton<Queries.AppStatsQueryService>();
        services.AddSingleton<Queries.KeyboardDetailsQueryService>();
        // E3-4（3.77.0）：应用详情卡片/详情弹窗查询服务
        services.AddSingleton<Queries.AppDetailQueryService>();
        services.AddSingleton<Queries.DetailDialogQueryService>();
        // E3-5（3.78.0）：网页浏览历史查询服务
        services.AddSingleton<Queries.WebHistoryQueryService>();
        // E3-6（3.79.0）：网页访问详情查询服务
        services.AddSingleton<Queries.WebDetailsQueryService>();
        // E3-7（3.80.0）：分类 CRUD 写操作服务（应用/网站各一份具体服务）
        services.AddSingleton<Application.UseCases.ProgramCategoryCrudService>();
        services.AddSingleton<Application.UseCases.WebsiteCategoryCrudService>();
        // E3-8（3.81.0）：数据管理概览查询服务
        services.AddSingleton<Queries.DataManagementQueryService>();

        return services;
    }
}
