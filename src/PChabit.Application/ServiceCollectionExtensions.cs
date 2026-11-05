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
        
        return services;
    }
}
