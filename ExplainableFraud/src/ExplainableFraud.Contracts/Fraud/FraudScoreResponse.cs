using System.Text.Json.Serialization;

namespace ExplainableFraud.Contracts.Fraud;

public sealed class FraudScoreResponse
{
    public float FraudProbability { get; init; }
    public bool IsFraudLikely { get; init; }
    public float DecisionThreshold { get; init; }
    public ExplanationMethod ExplanationMethod { get; init; } = ExplanationMethod.Unknown;
    public string ExplanationSummary { get; init; } = "";
    public required IReadOnlyList<FeatureContributionDto> FeatureContributions { get; init; }
    public string ModelVersion { get; init; } = "";

    /// <summary>Populated when scoring uses a trained pipeline with evaluation metadata.</summary>
    [JsonPropertyName("validationMetrics")]
    public ModelValidationMetricsDto? ValidationMetrics { get; init; }
}
