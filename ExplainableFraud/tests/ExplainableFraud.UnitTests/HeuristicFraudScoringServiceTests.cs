using ExplainableFraud.Domain.Transactions;
using ExplainableFraud.Infrastructure.Options;
using ExplainableFraud.Infrastructure.Scoring;
using Microsoft.Extensions.Options;

namespace ExplainableFraud.UnitTests;

public sealed class HeuristicFraudScoringServiceTests
{
    private static readonly float[] EmptyPc =
        Enumerable.Repeat(0f, ExplainableFraud.Application.Mapping.TransactionMapper.ExpectedPrincipalDimensions).ToArray();

    [Fact]
    public async Task ScoreAsync_returns_probability_in_unit_interval()
    {
        var options = Options.Create(new MlPipelineOptions { DecisionThreshold = 0.5f });
        var svc = new HeuristicFraudScoringService(options);

        var result = await svc.ScoreAsync(new TransactionFeatures(50f, 12_000f, EmptyPc));

        Assert.InRange(result.FraudProbability, 0f, 1f);
        Assert.Equal(0.5f, result.DecisionThreshold);
        Assert.NotEmpty(result.FeatureContributions);
        Assert.False(string.IsNullOrEmpty(result.ModelVersion));
    }
}
