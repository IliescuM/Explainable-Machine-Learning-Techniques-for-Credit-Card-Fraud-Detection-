namespace ExplainableFraud.Contracts.Fraud;

/// <summary>Sidecar JSON next to the ML.NET model zip (training output, inference input).</summary>
public sealed class FraudModelMetadata
{
    public string ModelVersionLabel { get; set; } = "";

    public ModelValidationMetricsDto? Metrics { get; set; }

    /// <summary>Dataset statistics for local explanation weighting (Time, V1..V28, Amount).</summary>
    public Dictionary<string, float> FeatureMeans { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, float> FeatureStds { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Permutation feature importance means (global explainability).</summary>
    public List<FeatureImportanceEntry> GlobalImportance { get; set; } = [];
}

public sealed class FeatureImportanceEntry
{
    public string Name { get; set; } = "";
    public float Importance { get; set; }
}
