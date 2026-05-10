using ExplainableFraud.Contracts.Training;

namespace ExplainableFraud.Web.Services;

public interface ITrainingApi
{
    Uri? BaseAddress { get; }

    Task<IReadOnlyList<TrainingDatasetSummaryDto>> ListDatasetsAsync(CancellationToken cancellationToken = default);

    Task<StartTrainingJobResponse> StartJobAsync(StartTrainingJobRequest request, CancellationToken cancellationToken = default);

    Task<TrainingJobDto?> GetJobAsync(Guid jobId, CancellationToken cancellationToken = default);
}
