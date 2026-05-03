namespace ExplainableFraud.Contracts.Fraud;

public sealed class FeatureContributionDto
{
    public required string FeatureName { get; init; }
    public float Contribution { get; init; }
}
