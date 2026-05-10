using System.Diagnostics;
using ExplainableFraud.Contracts.Fraud;
using ExplainableFraud.Contracts.Training;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers;
using Microsoft.ML.Trainers.FastTree;

namespace ExplainableFraud.Infrastructure.Ml;

/// <summary>Runs reproducible Fit/Evaluate on creditcard-shaped rows entirely in memory.</summary>
public static class InProcessCreditcardStyleTrainer
{
    /// <remarks>Includes Time+V1..V28+Amount flattened by ML.NET concatenate.</remarks>
    public const int FeatureCount = 30;

    public sealed record SyntheticRunResult(
        ModelValidationMetricsDto Metrics,
        int? TrainingIterationsReported,
        TrainingLabelDistributionDto LabelDistribution);

    /// <inheritdoc cref="FitAndEvaluateFromStratifiedSplit"/>
    public static SyntheticRunResult FitAndEvaluate(
        TrainingModelFamily family,
        IReadOnlyList<FraudTrainingObservation> observations,
        int mlSeed)
    {
        ArgumentNullException.ThrowIfNull(observations);
        if (observations.Count < 128)
            throw new ArgumentException("Need sufficient rows.", nameof(observations));

        EnsureTrainerFamilyRouting(family);

        var stratified = CreditcardStyleStratifiedSplitter.Split(
            observations,
            CreditcardStyleStratifiedSplitter.MatchingTrainerSplitSeed(mlSeed));
        return FitAndEvaluateFromStratifiedSplit(family, stratified, mlSeed);
    }

    /// <remarks>
    /// Use when callers already partitioned rows (allows UI/logging before Fit keeps identical folds via
    /// <see cref="CreditcardStyleStratifiedSplitter.MatchingTrainerSplitSeed"/>).
    /// </remarks>
    public static SyntheticRunResult FitAndEvaluateFromStratifiedSplit(
        TrainingModelFamily family,
        CreditcardStyleStratifiedSplitter.StratifiedSplit stratified,
        int mlSeed)
    {
        EnsureTrainerFamilyRouting(family);

        var trainRows = stratified.Train;
        var valRows = stratified.Validation;
        var testRows = stratified.Test;
        var labelDistribution = stratified.LabelDistribution;

        if (trainRows.Length < 32 || valRows.Length < 16 || testRows.Length < 16)
            throw new InvalidOperationException("Splits are too small — increase SyntheticTrainingRowCount.");

        var ml = new MLContext(mlSeed);

        var trainDv = ml.Data.LoadFromEnumerable(trainRows);
        var testDv = ml.Data.LoadFromEnumerable(testRows);

        var concatenate = ml.Transforms.Concatenate("Features",
            nameof(FraudMlInput.Time),
            nameof(FraudMlInput.V),
            nameof(FraudMlInput.Amount));

        FastTreeBinaryTrainer.Options boostedOptions = new()
        {
            NumberOfLeaves = 48,
            NumberOfTrees = 96,
            MinimumExampleCountPerLeaf = 14,
            Shrinkage = 0.12f,
            UnbalancedSets = true,
            FeatureFirstUsePenalty = 0f
        };
        FastForestBinaryTrainer.Options forestOptions = new()
        {
            NumberOfLeaves = 32,
            NumberOfTrees = 110,
            FeatureFraction = 0.75f,
            MinimumExampleCountPerLeaf = 10,
            Seed = mlSeed ^ 713
        };

        IEstimator<ITransformer> pipeline = family switch
        {
            TrainingModelFamily.LogisticRegressionBaseline =>
                concatenate
                    .Append(ml.Transforms.NormalizeMinMax("NormalizedFeatures", "Features"))
                    .Append(ml.BinaryClassification.Trainers.LbfgsLogisticRegression(new LbfgsLogisticRegressionBinaryTrainer.Options
                    {
                        LabelColumnName = nameof(FraudTrainingObservation.Label),
                        FeatureColumnName = "NormalizedFeatures",
                        L1Regularization = 0.1f,
                        L2Regularization = 2f,
                        OptimizationTolerance = 1e-4f,
                        HistorySize = 30,
                        EnforceNonNegativity = false,
                    })),
            TrainingModelFamily.FastTreeGradientBoost =>
                concatenate.Append(ml.BinaryClassification.Trainers.FastTree(boostedOptions)),
            TrainingModelFamily.FastForestEnsemble =>
                concatenate.Append(ml.BinaryClassification.Trainers.FastForest(forestOptions)),
            _ => throw new InvalidOperationException("Trainer routing failed.")
        };

        Stopwatch fittingSw = Stopwatch.StartNew();
        var model = pipeline.Fit(trainDv);
        fittingSw.Stop();

        var predictions = model.Transform(testDv);
        // FastTree/FastForest lack a calibrated Probability column; Evaluate() throws. Score-based EvaluateNonCalibrated works for every family here.
        var metrics = ml.BinaryClassification.EvaluateNonCalibrated(predictions, nameof(FraudTrainingObservation.Label));

        ExtractConfusion(metrics, out var tn, out var fp, out var fn, out var tp);

        var trainerLabel = DescribeTrainer(family, boostedOptions, forestOptions);

        var dto = new ModelValidationMetricsDto
        {
            AreaUnderRocCurve = metrics.AreaUnderRocCurve,
            AreaUnderPrecisionRecallCurve = metrics.AreaUnderPrecisionRecallCurve,
            F1Score = metrics.F1Score,
            Precision = metrics.PositivePrecision,
            Recall = metrics.PositiveRecall,
            Accuracy = metrics.Accuracy,
            PositivePrecision = metrics.PositivePrecision,
            PositiveRecall = metrics.PositiveRecall,
            NegativePrecision = metrics.NegativePrecision,
            NegativeRecall = metrics.NegativeRecall,
            TrainRows = trainRows.Length,
            ValidationRows = valRows.Length,
            TestRows = testRows.Length,
            FeatureCount = FeatureCount,
            TrueNegatives = tn,
            FalsePositives = fp,
            FalseNegatives = fn,
            TruePositives = tp,
            TrainerFamilyLabel = trainerLabel,
            FittingDurationMilliseconds = fittingSw.ElapsedMilliseconds
        };

        int? iterationsReported = family switch
        {
            TrainingModelFamily.FastTreeGradientBoost => boostedOptions.NumberOfTrees,
            TrainingModelFamily.FastForestEnsemble => forestOptions.NumberOfTrees,
            TrainingModelFamily.LogisticRegressionBaseline =>
                null, // LBFGS/inner IRLS pass count not exposed as an epoch analogue.
            _ => null
        };

        return new SyntheticRunResult(dto, iterationsReported, labelDistribution);

        static void ExtractConfusion(
            BinaryClassificationMetrics scored,
            out long tn,
            out long fp,
            out long fn,
            out long tp)
        {
            tn = fp = fn = tp = 0;
            try
            {
                var confusion = scored.ConfusionMatrix;
                if (confusion is null || confusion.NumberOfClasses < 2)
                    return;

                // ML.NET exposes counts as Counts[classPred][classActual].
                // GetCountForClassPair(predictedLabel, actualLabel); Key order for Bool labels puts False before True,
                // while ML.NET evaluates with fraud=true as positive class — empirically the fraud/fraud counts live on (0,0).
                tp = (long)confusion.GetCountForClassPair(0, 0);
                fn = (long)confusion.GetCountForClassPair(1, 0);
                fp = (long)confusion.GetCountForClassPair(0, 1);
                tn = (long)confusion.GetCountForClassPair(1, 1);
            }
            catch
            {
                // Leave zeros rather than crashing the UX if API surface shifts slightly.
            }
        }

        static string DescribeTrainer(
            TrainingModelFamily family,
            FastTreeBinaryTrainer.Options boost,
            FastForestBinaryTrainer.Options forest) =>
            family switch
            {
                TrainingModelFamily.LogisticRegressionBaseline =>
                    "LbfgsLogisticRegression (quasi-Newton IRLS, min-max normalized features)",
                TrainingModelFamily.FastTreeGradientBoost =>
                    $"FastTree ({boost.NumberOfTrees} trees × {boost.NumberOfLeaves} leaves, unbalanced sets)",
                TrainingModelFamily.FastForestEnsemble =>
                    $"FastForest ({forest.NumberOfTrees} trees, frac {forest.FeatureFraction:0.###})",
                _ => family.ToString()
            };
    }

    private static void EnsureTrainerFamilyRouting(TrainingModelFamily family)
    {
        switch (family)
        {
            case TrainingModelFamily.NeuralNetworkExperimentalPlaceholder:
                throw new InvalidOperationException(
                    "Tabular neural networks are not wired in this ML.NET Core stack. ONNX runtime, Torch (preview packages), or a Python training/export path would be needed.");

            case TrainingModelFamily.Unspecified:
                throw new ArgumentException("Select a trainer family.", nameof(family));

            case TrainingModelFamily.LogisticRegressionBaseline:
            case TrainingModelFamily.FastTreeGradientBoost:
            case TrainingModelFamily.FastForestEnsemble:
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(family), family.ToString());
        }
    }
}
