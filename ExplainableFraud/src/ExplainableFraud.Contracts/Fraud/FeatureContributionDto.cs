namespace ExplainableFraud.Contracts.Fraud;

public sealed class FeatureContributionDto
{
    public required string FeatureName { get; init; }
    public float Contribution { get; init; }
    public FeatureContributionKind Kind { get; init; } = FeatureContributionKind.Unknown;
    public string Description { get; init; } = "";
}
