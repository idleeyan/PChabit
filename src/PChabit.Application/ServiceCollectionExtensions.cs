using Microsoft.Extensions.DependencyInjection;
using PChabit.Application.Aggregators;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Data;

namespace PChabit.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTaiApplication(this IServiceCollection services)
    {
        services.AddScoped<DailyAggregator>();
        services.AddScoped<SessionAggregator>();
        services.AddScoped<PatternDetector>();
        services.AddScoped<HeatmapAggregator>();
        services.AddScoped<SankeyAggregator>();
        
        return services;
    }
}
