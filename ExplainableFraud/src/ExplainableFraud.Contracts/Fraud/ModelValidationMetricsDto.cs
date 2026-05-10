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

    [JsonPropertyName("validationRows")]
    public int ValidationRows { get; init; }

    [JsonPropertyName("testRows")]
    public int TestRows { get; init; }

    [JsonPropertyName("featureCount")]
    public int FeatureCount { get; init; }

    [JsonPropertyName("precision")]
    public double Precision { get; init; }

    [JsonPropertyName("recall")]
    public double Recall { get; init; }

    [JsonPropertyName("areaUnderPrecisionRecallCurve")]
    public double AreaUnderPrecisionRecallCurve { get; init; }

    [JsonPropertyName("accuracy")]
    public double Accuracy { get; init; }

    [JsonPropertyName("positivePrecision")]
    public double PositivePrecision { get; init; }

    [JsonPropertyName("positiveRecall")]
    public double PositiveRecall { get; init; }

    [JsonPropertyName("negativePrecision")]
    public double NegativePrecision { get; init; }

    [JsonPropertyName("negativeRecall")]
    public double NegativeRecall { get; init; }

    [JsonPropertyName("trueNegatives")]
    public long TrueNegatives { get; init; }

    [JsonPropertyName("falsePositives")]
    public long FalsePositives { get; init; }

    [JsonPropertyName("falseNegatives")]
    public long FalseNegatives { get; init; }

    [JsonPropertyName("truePositives")]
    public long TruePositives { get; init; }

    /// <summary>Trainer label for audit/UI (not persisted in older artifact JSON).</summary>
    [JsonPropertyName("trainerFamilyLabel")]
    public string TrainerFamilyLabel { get; init; } = "";

    /// <summary>Elapsed training fit + evaluate for this snapshot (milliseconds).</summary>
    [JsonPropertyName("fittingDurationMilliseconds")]
    public long FittingDurationMilliseconds { get; init; }
}
