namespace ExplainableFraud.Contracts.Training;

public sealed class StartTrainingJobResponse
{
    public required Guid JobId { get; init; }
}
