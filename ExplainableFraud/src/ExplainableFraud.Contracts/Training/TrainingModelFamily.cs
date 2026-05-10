namespace ExplainableFraud.Contracts.Training;

/// <summary>
/// Trainer family surfaced to the demo training API/UI. Neural networks for tabular data are NOT built into ML.NET
/// Core the same way as tree/linear trainers; Torch integration is deliberately out of scope for this dissertation build.
/// </summary>
public enum TrainingModelFamily
{
    Unspecified = 0,

    /// <summary>Lbfgs logistic regression — strong linear baseline, very fast.</summary>
    LogisticRegressionBaseline = 1,

    /// <summary>FastTree boosted trees — nonlinear, matches ExplainableFraud.Training CLI style.</summary>
    FastTreeGradientBoost = 2,

    /// <summary>FastForest bagged trees — another nonlinear ensemble without gradient boosting semantics.</summary>
    FastForestEnsemble = 3,

    /// <summary>
    /// Placeholder hook for ONNX/Torch/Python bridges. Not implemented in Infrastructure; API rejects training starts with a clear diagnostic.
    /// </summary>
    NeuralNetworkExperimentalPlaceholder = 4
}
