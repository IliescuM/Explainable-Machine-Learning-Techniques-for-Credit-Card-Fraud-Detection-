namespace ExplainableFraud.Contracts.Training;

/// <summary>
/// Recognized training datasets (schema / source). The current API still runs a mock pipeline for all values.
/// </summary>
public enum TrainingDatasetKind
{
    Unspecified = 0,

    /// <summary>
    /// Kaggle-style creditcard.csv (Time, V1–V28, Amount, Class) — typical thesis demo path.
    /// </summary>
    CreditcardKaggleDemo = 1,

    /// <summary>
    /// Placeholder for a server-configured local CSV; same mock today, ready for CLI wiring.
    /// </summary>
    CreditcardLocalCsv = 2
}
