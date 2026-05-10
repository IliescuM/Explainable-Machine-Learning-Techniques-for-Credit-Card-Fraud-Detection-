using ExplainableFraud.Contracts.Training;
using ExplainableFraud.Infrastructure.Ml;
using ExplainableFraud.Infrastructure.Options;
using Microsoft.Extensions.Hosting;

namespace ExplainableFraud.Infrastructure.Training;

internal static class TrainingRowsResolution
{
    internal sealed record Result(
        IReadOnlyList<FraudTrainingObservation> Rows,
        TrainingDataSourceKind DataSourceKind,
        string DataSourceSummary);

    public static Result Resolve(
        TrainingDatasetKind datasetKind,
        SimulatedTrainingJobOptions opts,
        IHostEnvironment host,
        int jobSeed)
    {
        var syntheticTarget = EffectiveSyntheticRowCount(datasetKind, opts);

        var candidates = CreditcardCsvPathResolution.BuildOrderedCandidatePaths(host, opts);
        foreach (var path in candidates)
        {
            if (!CreditcardCsvObservationLoader.TryLoad(path, opts.MaxCreditcardCsvRowsToLoad, jobSeed, out var fromCsv,
                    out var ingestDetail) ||
                fromCsv is null)
                continue;

            var summary =
                $"Observational CSV (offline) · {Path.GetFileName(path)} · {fromCsv.Count:N0} rows in memory — {ingestDetail}.";
            return new Result(fromCsv, TrainingDataSourceKind.LocalCreditcardCsv, summary);
        }

        var synthetic = SyntheticCreditcardStyleDataset
            .Generate(syntheticTarget, jobSeed, fraudFraction: Math.Clamp(opts.SyntheticFraudFraction, 0.01f, 0.4f))
            .ToArray();

        var fraudN = synthetic.Count(static r => r.Label);
        var synSummary =
            $"Synthetic ULB-shaped generator · {synthetic.Length:N0} rows (deterministic seed; {fraudN:N0} positives ≈ {(100d * fraudN / Math.Max(synthetic.Length, 1)):F1}%). "
            + "No creditcard.csv was found under the API content paths — place creditcard.csv at the repository root, next to appsettings.json, or list it in TrainingJobs:Simulation:CreditcardCsvCandidateRelativePaths (ancestor walk also applies).";

        return new Result(synthetic, TrainingDataSourceKind.SyntheticDeterministic, synSummary);
    }

    private static int EffectiveSyntheticRowCount(TrainingDatasetKind kind, SimulatedTrainingJobOptions opts)
    {
        var count = opts.SyntheticTrainingRowCount;

        if (kind is TrainingDatasetKind.CreditcardLocalCsv)
            count = (int)Math.Round(count * opts.LocalDatasetSyntheticRowMultiplier);

        var floor = Math.Max(512, opts.MinSyntheticTrainingRowCount);
        count = Math.Max(count, floor);
        return Math.Clamp(count, 512, 200_000);
    }
}
