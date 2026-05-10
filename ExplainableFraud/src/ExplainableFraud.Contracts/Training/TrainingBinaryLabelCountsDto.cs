namespace ExplainableFraud.Contracts.Training;

/// <summary>Positive (fraud) vs negative counts for binary creditcard-style training rows.</summary>
public sealed class TrainingBinaryLabelCountsDto
{
    public long Negatives { get; init; }

    public long Positives { get; init; }
}
