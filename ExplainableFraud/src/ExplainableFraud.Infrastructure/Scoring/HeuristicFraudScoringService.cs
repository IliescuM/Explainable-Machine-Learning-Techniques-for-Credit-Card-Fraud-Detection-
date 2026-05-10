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
            ExplanationMethod = ExplanationMethod.HeuristicWeights,
            ExplanationSummary = "Deterministic baseline: relative emphasis is derived from fixed feature weights and normalized for comparison.",
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
            ("Amount", 2.8f * amountNorm, FeatureContributionKind.Scalar, "Log-scaled transaction amount in the baseline risk formula."),
            ("Time", 1.6f * timeNorm, FeatureContributionKind.Scalar, "Kaggle Time column normalized to days in the baseline risk formula."),
            ("PCA aggregate magnitude", 0.05f * pcEnergy, FeatureContributionKind.PrincipalComponentAggregate, "Aggregate absolute magnitude across V1-V28 used as a coarse PCA signal.")
        };

        var scale = raw.Max(x => Math.Abs(x.Item2));
        if (scale < 1e-6f)
            scale = 1f;

        return raw
            .Select(x => new FeatureContributionDto
            {
                FeatureName = x.Item1,
                Contribution = x.Item2 / scale,
                Kind = x.Item3,
                Description = x.Item4
            })
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
