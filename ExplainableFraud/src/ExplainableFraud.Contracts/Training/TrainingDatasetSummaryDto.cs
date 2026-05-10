namespace ExplainableFraud.Contracts.Training;

public sealed class TrainingDatasetSummaryDto
{
    public required TrainingDatasetKind Kind { get; init; }

    public required string DisplayName { get; init; }

    public required string Description { get; init; }

    /// <summary>True when <c>creditcard.csv</c> exists on a resolved candidate path.</summary>
    public bool LocalCreditcardCsvDiscoverable { get; init; }

    /// <summary>Absolute path to the discovered CSV, when <see cref="LocalCreditcardCsvDiscoverable"/>.</summary>
    public string? LocalCreditcardCsvAbsolutePath { get; init; }

    /// <summary>Non-empty data lines after header (line scan); null if unknown or file is not Kaggle-shaped.</summary>
    public int? LocalCreditcardCsvApproximateDataRowCount { get; init; }
}
