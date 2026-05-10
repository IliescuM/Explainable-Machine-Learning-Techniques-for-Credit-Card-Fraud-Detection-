namespace ExplainableFraud.Contracts.Training;

/// <summary>Where training rows originated for the in-process demo trainer.</summary>
public enum TrainingDataSourceKind
{
    Unspecified = 0,

    /// <summary>Deterministic creditcard-shaped synthetic generator (no external CSV).</summary>
    SyntheticDeterministic = 1,

    /// <summary>Rows loaded from a local creditcard.csv (Kaggle-style columns).</summary>
    LocalCreditcardCsv = 2
}
