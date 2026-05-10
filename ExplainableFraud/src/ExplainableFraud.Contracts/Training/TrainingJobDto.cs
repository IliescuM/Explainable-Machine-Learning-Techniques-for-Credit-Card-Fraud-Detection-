using ExplainableFraud.Contracts.Fraud;

namespace ExplainableFraud.Contracts.Training;

/// <summary>
/// Full training job snapshot for polling (progress, phase, outcome).
/// </summary>
public sealed class TrainingJobDto
{
    public required Guid JobId { get; init; }

    public required TrainingDatasetKind DatasetKind { get; init; }

    /// <summary>Trainer family requested for this run.</summary>
    public TrainingModelFamily ModelFamily { get; init; }

    public required TrainingJobStatus Status { get; init; }

    public required TrainingJobPhase Phase { get; init; }

    /// <summary>
    /// When <see cref="IsSimulated"/> is true because of timeline-only mocks; when <see cref="UsedInProcessMlTrainer"/> is true we still evaluated a real ML.NET pipeline on bundled synthetic rows.
    /// </summary>
    public bool UsesSyntheticRows { get; init; }

    /// <summary>True when Fit/Evaluate executed in-memory (ML.NET).</summary>
    public bool UsedInProcessMlTrainer { get; init; }

    /// <summary>Whether rows came from the synthetic generator or a discovered local CSV file.</summary>
    public TrainingDataSourceKind DataSourceKind { get; init; }

    /// <summary>One-line explanation for the UI (path, row counts, fallbacks).</summary>
    public string DataSourceSummary { get; init; } = "";

    /// <summary>In-process jobs record class balance after load (and after any stratified sampling) and per split.</summary>
    public TrainingLabelDistributionDto? LabelDistribution { get; init; }

    /// <summary>Approximate epochs for iterative trainers; boosted trees expose tree pass count when available.</summary>
    public int? TrainingIterationsReported { get; init; }

    /// <summary>Wall-clock elapsed for the job from queue to terminal state.</summary>
    public long DurationMilliseconds { get; init; }

    /// <summary>0–100 inclusive while running or after success.</summary>
    public int ProgressPercent { get; init; }

    public required string Message { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public IReadOnlyList<TrainingPipelineStageDto> Stages { get; init; } = [];

    public ModelValidationMetricsDto? Metrics { get; init; }

    /// <summary>
    /// When true, delays/progress smoothing may include non-persisting steps; combining with <see cref="UsedInProcessMlTrainer"/> shows real-fit demo runs.
    /// </summary>
    public bool IsSimulated { get; init; }

    public string? ErrorDetail { get; init; }
}
