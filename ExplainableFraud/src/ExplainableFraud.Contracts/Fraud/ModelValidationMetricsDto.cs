using System.Text.Json.Serialization;

namespace ExplainableFraud.Contracts.Fraud;

/// <summary>Hold-out metrics from training (same schema as persisted model metadata).</summary>
public sealed class ModelValidationMetricsDto
{
    [JsonPropertyName("areaUnderRocCurve")]
    public double AreaUnderRocCurve { get; init; }

    [JsonPropertyName("f1Score")]
    public double F1Score { get; init; }

    [JsonPropertyName("trainRows")]
    public int TrainRows { get; init; }

    [JsonPropertyName("testRows")]
    public int TestRows { get; init; }
}
