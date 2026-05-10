namespace ExplainableFraud.Infrastructure.Options;

public sealed class SimulatedTrainingJobOptions
{
    public const string SectionName = "TrainingJobs:Simulation";

    /// <summary>When true (default), training runs as an in-memory mock suitable for demos.</summary>
    public bool UseSimulation { get; set; } = true;

    public int StepDelayMilliseconds { get; set; } = 120;

    /// <summary>Micro-steps used to interpolate progress inside each coarse phase.</summary>
    public int ProgressStepsPerPhase { get; set; } = 6;

    /// <summary>
    /// When true together with <see cref="UseSimulation"/>, executes a real ML.NET Fit/Evaluate on deterministic synthetic rows
    /// and surfaces evaluation metrics instead of pretending training happened.
    /// </summary>
    public bool RunSyntheticInProcessTraining { get; set; } = true;

    /// <summary>Synthetic workload size when no CSV is found (Kaggle full set is much larger).</summary>
    public int SyntheticTrainingRowCount { get; set; } = 12_800;

    /// <summary>Floor for synthetic runs so demos stay statistically stable (also applied when config is lower).</summary>
    public int MinSyntheticTrainingRowCount { get; set; } = 10_000;

    /// <summary>Fraud prevalence injected into synthesis (fraction in (0,1)).</summary>
    public float SyntheticFraudFraction { get; set; } = 0.06f;

    /// <summary>Applied on top when the local CSV dataset kind is selected.</summary>
    public float LocalDatasetSyntheticRowMultiplier { get; set; } = 1.35f;

    /// <summary>Displayed in legacy timelines as an educational hint (not asserted against real ingestion yet).</summary>
    public int ReferenceKaggleCardinality { get; set; } = 284_807;

    /// <summary>
    /// Relative paths checked under the API content root and base directory (in order) for creditcard.csv.
    /// When a file exists it is preferred over synthetic data for in-process training.
    /// </summary>
    public string[] CreditcardCsvCandidateRelativePaths { get; set; } =
    [
        "creditcard.csv",
        "Data/creditcard.csv",
        "data/creditcard.csv",
        "datasets/creditcard.csv",
        "../creditcard.csv",
        "../../creditcard.csv",
        "../../../creditcard.csv",
        "../../../Data/creditcard.csv",
        "../../../../creditcard.csv",
        "../../../../Data/creditcard.csv",
        "../../../../../creditcard.csv"
    ];

    /// <summary>
    /// From each of content root and base directory, walk up to this many ancestor folders and test
    /// <c>creditcard.csv</c> in each (after applying <see cref="CreditcardCsvCandidateRelativePaths"/> for that base).
    /// </summary>
    public int CreditcardCsvParentDirectoryWalkDepth { get; set; } = 12;

    /// <summary>Optional hard cap when loading large local CSVs (entire file if smaller).</summary>
    public int MaxCreditcardCsvRowsToLoad { get; set; } = 300_000;
}
