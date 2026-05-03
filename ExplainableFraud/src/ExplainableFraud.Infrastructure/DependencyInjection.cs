using ExplainableFraud.Application.Abstractions;
using ExplainableFraud.Infrastructure.Options;
using ExplainableFraud.Infrastructure.Scoring;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ExplainableFraud.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MlPipelineOptions>(configuration.GetSection(MlPipelineOptions.SectionName));
        services.AddSingleton<IFraudScoringService, HeuristicFraudScoringService>();

        return services;
    }
}
