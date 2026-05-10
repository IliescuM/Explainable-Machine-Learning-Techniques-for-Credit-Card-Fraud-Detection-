namespace ExplainableFraud.Contracts.Training;

/// <summary>One labelled step in an in-job training timeline (fine-grained; separate from coarse <see cref="TrainingJobPhase"/>).</summary>
public sealed class TrainingPipelineStageDto
{
    /// <summary>Stable key such as <c>validate</c>, <c>split</c>.</summary>
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required TrainingPipelineStageStatus Status { get; init; }

    /// <summary>Brief rationale or machine-readable outcome for auditing.</summary>
    public string Detail { get; init; } = "";
}
