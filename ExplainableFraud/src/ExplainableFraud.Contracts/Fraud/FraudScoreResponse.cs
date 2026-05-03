namespace ExplainableFraud.Contracts.Fraud;

public sealed class FraudScoreResponse
{
    public float FraudProbability { get; init; }
    public bool IsFraudLikely { get; init; }
    public float DecisionThreshold { get; init; }
    public required IReadOnlyList<FeatureContributionDto> FeatureContributions { get; init; }
    public string ModelVersion { get; init; } = "";
}
