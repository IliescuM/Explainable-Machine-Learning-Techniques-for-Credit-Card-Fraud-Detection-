namespace ExplainableFraud.Contracts.Training;

/// <summary>Label balance after ingest and after stratified train/validation/test partition.</summary>
public sealed class TrainingLabelDistributionDto
{
    public required TrainingBinaryLabelCountsDto AfterLoad { get; init; }

    public required TrainingBinaryLabelCountsDto Train { get; init; }

    public required TrainingBinaryLabelCountsDto Validation { get; init; }

    public required TrainingBinaryLabelCountsDto Test { get; init; }
}
