using ExplainableFraud.Application.Abstractions;
using ExplainableFraud.Contracts.Fraud;
using ExplainableFraud.Domain.Transactions;
using ExplainableFraud.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace ExplainableFraud.Infrastructure.Scoring;

/// <summary>
/// Deterministic baseline until a trained ML.NET model is loaded from <see cref="MlPipelineOptions.ModelPath"/>.
/// </summary>
public sealed class HeuristicFraudScoringService(IOptions<MlPipelineOptions> options) : IFraudScoringService
{
    private readonly MlPipelineOptions _options = options.Value;

    public Task<FraudScoreResponse> ScoreAsync(TransactionFeatures transaction, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var amount = SafeLog1p(transaction.Amount);
        var time = transaction.TimeSeconds / 86_400f;
        var pcEnergy = 0f;
        for (var i = 0; i < transaction.PrincipalComponents.Count; i++)
            pcEnergy += Math.Abs(transaction.PrincipalComponents[i]);

        var logit = -2.5f + 2.8f * amount + 1.6f * time + 0.05f * pcEnergy;
        var p = Sigmoid(logit);
        var contributions = BuildContributions(amount, time, pcEnergy);
        var threshold = _options.DecisionThreshold;

        var response = new FraudScoreResponse
        {
            FraudProbability = p,
            DecisionThreshold = threshold,
            IsFraudLikely = p >= threshold,
            FeatureContributions = contributions,
            ModelVersion = string.IsNullOrWhiteSpace(_options.ModelPath)
                ? _options.ModelVersionLabel
                : $"mlnet:{_options.ModelVersionLabel}"
        };

        return Task.FromResult(response);
    }

    private static IReadOnlyList<FeatureContributionDto> BuildContributions(float amountNorm, float timeNorm, float pcEnergy)
    {
        var raw = new[]
        {
            ("Amount (log-scaled contribution)", 2.8f * amountNorm),
            ("Time (normalized contribution)", 1.6f * timeNorm),
            ("PCA aggregate magnitude proxy", 0.05f * pcEnergy)
        };

        var scale = raw.Max(x => Math.Abs(x.Item2));
        if (scale < 1e-6f)
            scale = 1f;

        return raw
            .Select(x => new FeatureContributionDto { FeatureName = x.Item1, Contribution = x.Item2 / scale })
            .OrderByDescending(x => Math.Abs(x.Contribution))
            .ToList();
    }

    private static float SafeLog1p(float x)
    {
        var v = x < 0 ? 0f : x;
        return MathF.Log(1f + v);
    }

    private static float Sigmoid(float x)
    {
        var z = MathF.Exp(Math.Clamp(-x, -20f, 20f));
        return 1f / (1f + z);
    }
}
