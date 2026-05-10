using ExplainableFraud.Contracts.Training;
using ExplainableFraud.Infrastructure.Ml;

namespace ExplainableFraud.UnitTests;

public sealed class CreditcardTrainingIngestAndSplitTests
{
    [Fact]
    public void TryLoad_parses_quoted_kaggle_style_header_and_cells()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cc_q_{Guid.NewGuid():N}.csv");
        try
        {
            using (var writer = new StreamWriter(path))
            {
                writer.WriteLine(
                    "\"Time\",\"V1\",\"V2\",\"V3\",\"V4\",\"V5\",\"V6\",\"V7\",\"V8\",\"V9\",\"V10\",\"V11\",\"V12\",\"V13\",\"V14\",\"V15\",\"V16\",\"V17\",\"V18\",\"V19\",\"V20\",\"V21\",\"V22\",\"V23\",\"V24\",\"V25\",\"V26\",\"V27\",\"V28\",\"Amount\",\"Class\"");
                writer.WriteLine($"0,{string.Join(",", Enumerable.Repeat("0", 28))},0.5,\"1\"");
                for (var i = 1; i < CreditcardCsvObservationLoader.MinimumUsefulRows; i++)
                    writer.WriteLine($"{i},{string.Join(",", Enumerable.Repeat("0", 28))},0,\"0\"");
            }

            Assert.True(CreditcardCsvObservationLoader.TryLoad(path, maxRows: 10_000, stratifiedSamplingSeed: 1,
                out var observations, out _));
            Assert.NotNull(observations);
            Assert.Contains(observations!, static r => r.Label);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryLoad_parses_class_from_header_and_detects_fraud()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cc_{Guid.NewGuid():N}.csv");
        try
        {
            {
                using var writer = new StreamWriter(path);
                writer.WriteLine(
                    "Time,V1,V2,V3,V4,V5,V6,V7,V8,V9,V10,V11,V12,V13,V14,V15,V16,V17,V18,V19,V20,V21,V22,V23,V24,V25,V26,V27,V28,Amount,Class");
                writer.WriteLine($"0,{string.Join(",", Enumerable.Repeat("0", 28))},0.5,1");
                for (var i = 1; i < CreditcardCsvObservationLoader.MinimumUsefulRows; i++)
                    writer.WriteLine($"{i},{string.Join(",", Enumerable.Repeat("0", 28))},0,0");
            }

            Assert.True(CreditcardCsvObservationLoader.TryLoad(path, maxRows: 10_000, stratifiedSamplingSeed: 1,
                out var observations, out var detail));
            Assert.NotNull(observations);
            Assert.Contains("parsed", detail, StringComparison.OrdinalIgnoreCase);

            var fraud = Assert.Single(observations!, static r => r.Label);
            Assert.Equal(0.5f, fraud.Amount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Stratified_split_keeps_both_classes_when_negatives_precede_positives()
    {
        var negatives = Enumerable.Range(0, 9000).Select(_ => Row(false)).ToList();
        var positives = Enumerable.Range(0, 300).Select(_ => Row(true)).ToList();
        var stacked = negatives.Concat(positives).ToList();

        var split = CreditcardStyleStratifiedSplitter.Split(stacked, seed: 90210);

        foreach (var fold in new[] { split.Train, split.Validation, split.Test })
        {
            var p = fold.Count(r => r.Label);
            var n = fold.Length - p;
            Assert.True(p > 0, "expected positives in every fold");
            Assert.True(n > 0, "expected negatives in every fold");
        }

        var r = InProcessCreditcardStyleTrainer.FitAndEvaluate(
            TrainingModelFamily.FastTreeGradientBoost,
            stacked,
            mlSeed: 7331);

        Assert.InRange(r.Metrics.AreaUnderRocCurve, 0d, 1.02);
    }

    [Fact]
    public void TryLoad_stratified_subset_retains_fraud_when_capping()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cc_cap_{Guid.NewGuid():N}.csv");
        try
        {
            using (var writer = new StreamWriter(path))
            {
                writer.WriteLine(
                    "Time,V1,V2,V3,V4,V5,V6,V7,V8,V9,V10,V11,V12,V13,V14,V15,V16,V17,V18,V19,V20,V21,V22,V23,V24,V25,V26,V27,V28,Amount,Class");
                writer.WriteLine($"0,{string.Join(",", Enumerable.Repeat("0", 28))},1,0");
                for (var i = 0; i < 400; i++)
                    writer.WriteLine($"{i},{string.Join(",", Enumerable.Repeat("0", 28))},1,{(i % 97 == 0 ? 1 : 0)}");
            }

            Assert.True(CreditcardCsvObservationLoader.TryLoad(path, maxRows: 200, stratifiedSamplingSeed: 77,
                out var rows, out _));
            Assert.InRange(rows!.Count, 128, 201);
            Assert.Contains(rows, static r => r.Label);
            Assert.Contains(rows, static r => !r.Label);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static FraudTrainingObservation Row(bool fraud) =>
        new()
        {
            Time = 0,
            Amount = 1,
            V = new float[28],
            Label = fraud
        };
}
