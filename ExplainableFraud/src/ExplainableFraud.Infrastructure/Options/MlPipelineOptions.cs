namespace ExplainableFraud.Infrastructure.Options;

public sealed class MlPipelineOptions
{
    public const string SectionName = "MlPipeline";

    /// <summary>Optional path to serialized ML.NET model. When empty, infrastructure uses heuristic placeholder.</summary>
    public string? ModelPath { get; init; }

    public float DecisionThreshold { get; init; } = 0.5f;

    /// <summary>Published model label for auditing and reproducibility.</summary>
    public string ModelVersionLabel { get; init; } = "heuristic-v1";
}
