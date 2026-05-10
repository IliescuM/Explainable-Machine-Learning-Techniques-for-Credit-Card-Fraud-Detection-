using ExplainableFraud.Contracts.Training;

namespace ExplainableFraud.Application.Abstractions;

public interface ITrainingJobService
{
    IReadOnlyList<TrainingDatasetSummaryDto> GetAvailableDatasets();

    Task<TrainingJobStartOutcome> StartJobAsync(
        TrainingDatasetKind datasetKind,
        TrainingModelFamily modelFamily,
        CancellationToken cancellationToken);

    TrainingJobDto? GetJob(Guid jobId);
}

public readonly record struct TrainingJobStartOutcome(bool Success, Guid JobId, string? ErrorMessage)
{
    public static TrainingJobStartOutcome Ok(Guid jobId) => new(true, jobId, null);

    public static TrainingJobStartOutcome Fail(string errorMessage) => new(false, Guid.Empty, errorMessage);
}
