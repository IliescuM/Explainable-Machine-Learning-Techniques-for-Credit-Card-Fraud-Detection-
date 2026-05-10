using ExplainableFraud.Contracts.Training;

namespace ExplainableFraud.Infrastructure.Ml;

/// <summary>Stratified partitioning for imbalanced creditcard-shaped rows (avoids sequential splits on sorted CSVs).</summary>
public static class CreditcardStyleStratifiedSplitter
{
    /// <summary>XOR derivation kept in sync with <see cref="InProcessCreditcardStyleTrainer"/> so jobs can stratify rows before exposing split counts to the UI.</summary>
    public static int MatchingTrainerSplitSeed(int mlSeedPassedToTrainer) => unchecked(mlSeedPassedToTrainer ^ 713_409);

    /// <summary>Default 70 / 15 / 15 train-validation-test.</summary>
    public const double DefaultTrainFraction = 0.70;

    public const double DefaultValidationFraction = 0.15;

    public const double DefaultTestFraction = 0.15;

    public static StratifiedSplit Split(
        IReadOnlyList<FraudTrainingObservation> observations,
        int seed,
        double trainFraction = DefaultTrainFraction,
        double valFraction = DefaultValidationFraction,
        double testFraction = DefaultTestFraction)
    {
        ArgumentNullException.ThrowIfNull(observations);

        if (observations.Count < 128)
            throw new ArgumentException(
                $"Need sufficient rows for stratified training (minimum 128, got {observations.Count:N0}).",
                nameof(observations));

        var fracSum = trainFraction + valFraction + testFraction;
        if (Math.Abs(fracSum - 1.0) > 1e-6)
            throw new ArgumentException($"Split fractions must sum to 1.0 (got {fracSum:G}).");

        var positives = new List<FraudTrainingObservation>();
        var negatives = new List<FraudTrainingObservation>();

        foreach (var o in observations)
        {
            if (o.Label)
                positives.Add(o);
            else
                negatives.Add(o);
        }

        EnsureBinaryClassesOrThrow(observations.Count, positives.Count, negatives.Count);

        var rnd = new Random(seed);
        Shuffle(positives, rnd);
        Shuffle(negatives, rnd);

        AllocateThreeBuckets(positives.Count, trainFraction, valFraction, testFraction,
            out var pTrain, out var pVal, out var pTest);

        AllocateThreeBuckets(negatives.Count, trainFraction, valFraction, testFraction,
            out var nTrain, out var nVal, out var nTest);

        var train = ConcatSlices(positives, 0, pTrain, negatives, 0, nTrain);
        var validation = ConcatSlices(positives, pTrain, pVal, negatives, nTrain, nVal);
        var test = ConcatSlices(positives, pTrain + pVal, pTest, negatives, nTrain + nVal, nTest);

        Shuffle(train, rnd);
        Shuffle(validation, rnd);
        Shuffle(test, rnd);

        foreach (var (name, subset) in new[] { ("Training", train), ("Validation", validation), ("Test", test) })
        {
            var tp = CountPositives(subset);
            var tn = subset.Count - tp;
            if (tp == 0 || tn == 0)
            {
                throw new InvalidOperationException(
                    $"{name} split has only one class after stratified partition (positives={tp:N0}, negatives={tn:N0}). " +
                    $"Global row balance: positives={positives.Count:N0}, negatives={negatives.Count:N0}. " +
                    "This should not occur with a valid Kaggle creditcard.csv — report as a bug.");
            }
        }

        var distribution = new TrainingLabelDistributionDto
        {
            AfterLoad = Counts(positives.Count, negatives.Count),
            Train = Counts(train),
            Validation = Counts(validation),
            Test = Counts(test)
        };

        return new StratifiedSplit(train.ToArray(), validation.ToArray(), test.ToArray(), distribution);
    }

    /// <summary>Shared guard for empty single-class ingestion (mis-parsed CSV, wrong header, capped head-only slices with no fraud).</summary>
    public static void EnsureBinaryClassesOrThrow(long totalObservations, long positiveCount, long negativeCount)
    {
        if (positiveCount == 0 || negativeCount == 0)
        {
            throw new InvalidOperationException(
                $"Dataset has only one class after loading (total rows={totalObservations:N0}, positives={positiveCount:N0}, negatives={negativeCount:N0}). " +
                "For Kaggle creditcard.csv, confirm the numeric 'Class' column is present and parsed as 0/1 (comma decimals or wrong column order breaks this check).");
        }

        if (positiveCount < 3 || negativeCount < 3)
            throw new InvalidOperationException(
                $"Stratified 70/15/15 split needs at least three positives and negatives (positives={positiveCount:N0}, negatives={negativeCount:N0}).");
    }

    public sealed record StratifiedSplit(
        FraudTrainingObservation[] Train,
        FraudTrainingObservation[] Validation,
        FraudTrainingObservation[] Test,
        TrainingLabelDistributionDto LabelDistribution);

    private static void AllocateThreeBuckets(
        int total,
        double trainFraction,
        double valFraction,
        double testFraction,
        out int trainCount,
        out int valCount,
        out int testCount)
    {
        if (total < 3)
        {
            trainCount = valCount = testCount = 0;
            return;
        }

        trainCount = Math.Max(1, (int)Math.Floor(total * trainFraction));
        valCount = Math.Max(1, (int)Math.Floor(total * valFraction));
        testCount = total - trainCount - valCount;

        if (testCount < 1)
        {
            var deficit = 1 - testCount;
            var takeFromTrain = Math.Min(deficit, Math.Max(0, trainCount - 1));
            trainCount -= takeFromTrain;
            deficit -= takeFromTrain;
            if (deficit > 0)
            {
                var takeFromVal = Math.Min(deficit, Math.Max(0, valCount - 1));
                valCount -= takeFromVal;
                deficit -= takeFromVal;
            }

            testCount = total - trainCount - valCount;
        }

        if (testCount < 1)
        {
            trainCount = Math.Max(1, total - 2);
            valCount = 1;
            testCount = total - trainCount - valCount;
        }

        if (trainCount + valCount + testCount != total)
            throw new InvalidOperationException($"Internal split math error for total={total}.");

        if (trainCount < 1 || valCount < 1 || testCount < 1)
            throw new InvalidOperationException($"Could not form three non-empty buckets for class with total={total}.");
    }

    private static List<FraudTrainingObservation> ConcatSlices(
        IReadOnlyList<FraudTrainingObservation> pos,
        int posStart,
        int posLen,
        IReadOnlyList<FraudTrainingObservation> neg,
        int negStart,
        int negLen)
    {
        var list = new List<FraudTrainingObservation>(posLen + negLen);
        for (var i = 0; i < posLen; i++)
            list.Add(pos[posStart + i]);
        for (var i = 0; i < negLen; i++)
            list.Add(neg[negStart + i]);
        return list;
    }

    private static long CountPositives(IReadOnlyList<FraudTrainingObservation> rows)
    {
        long c = 0;
        foreach (var r in rows)
        {
            if (r.Label)
                c++;
        }

        return c;
    }

    private static TrainingBinaryLabelCountsDto Counts(IReadOnlyList<FraudTrainingObservation> rows)
    {
        var p = CountPositives(rows);
        return new TrainingBinaryLabelCountsDto { Positives = p, Negatives = rows.Count - p };
    }

    private static TrainingBinaryLabelCountsDto Counts(int positives, int negatives) =>
        new() { Positives = positives, Negatives = negatives };

    private static void Shuffle(IList<FraudTrainingObservation> list, Random rng)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
