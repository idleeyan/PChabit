using Microsoft.Extensions.DependencyInjection;
using Tai.Application.Aggregators;
using Tai.Core.Interfaces;
using Tai.Infrastructure.Data;

namespace Tai.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTaiApplication(this IServiceCollection services)
    {
        services.AddScoped<DailyAggregator>();
        services.AddScoped<SessionAggregator>();
        services.AddScoped<PatternDetector>();
        
        return services;
    }
}
