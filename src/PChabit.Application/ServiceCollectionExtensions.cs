using Microsoft.Extensions.DependencyInjection;
using PChabit.Application.Aggregators;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Data;

namespace PChabit.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTaiApplication(this IServiceCollection services)
    {
        services.AddSingleton<DailyAggregator>();
        services.AddSingleton<SessionAggregator>();
        services.AddSingleton<PatternDetector>();
        services.AddSingleton<HeatmapAggregator>();
        services.AddSingleton<SankeyAggregator>();
        
        return services;
    }
}
