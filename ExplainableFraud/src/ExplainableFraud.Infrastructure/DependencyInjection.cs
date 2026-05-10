using ExplainableFraud.Application.Abstractions;
using ExplainableFraud.Infrastructure.Options;
using ExplainableFraud.Infrastructure.Scoring;
using ExplainableFraud.Infrastructure.Training;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ExplainableFraud.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MlPipelineOptions>(configuration.GetSection(MlPipelineOptions.SectionName));
        services.Configure<SimulatedTrainingJobOptions>(configuration.GetSection(SimulatedTrainingJobOptions.SectionName));
        services.AddSingleton<ITrainingJobService, SimulatedTrainingJobService>();

        services.AddSingleton<IFraudScoringService>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<MlPipelineOptions>>();
            var host = sp.GetRequiredService<IHostEnvironment>();
            var configured = opts.Value.ModelPath;
            if (string.IsNullOrWhiteSpace(configured))
                return new HeuristicFraudScoringService(opts);

            var path = Path.IsPathRooted(configured)
                ? configured
                : Path.Combine(host.ContentRootPath, configured);

            return File.Exists(path)
                ? new MlNetFraudScoringService(opts, host)
                : new HeuristicFraudScoringService(opts);
        });

        return services;
    }
}
