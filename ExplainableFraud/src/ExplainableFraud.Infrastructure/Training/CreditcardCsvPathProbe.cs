using ExplainableFraud.Infrastructure.Ml;
using ExplainableFraud.Infrastructure.Options;
using Microsoft.Extensions.Hosting;

namespace ExplainableFraud.Infrastructure.Training;

/// <summary>First-hit discovery of creditcard.csv for API metadata (datasets list).</summary>
public static class CreditcardCsvPathProbe
{
    public readonly record struct Result(bool IsDiscoverable, string? AbsolutePath, int? ApproximateDataRowCount);

    public static Result Probe(IHostEnvironment host, SimulatedTrainingJobOptions opts)
    {
        foreach (var path in CreditcardCsvPathResolution.BuildOrderedCandidatePaths(host, opts))
        {
            if (!File.Exists(path))
                continue;

            if (CreditcardCsvObservationLoader.TryEstimateDataRowCount(path, out var n))
                return new Result(true, path, n);

            return new Result(true, path, null);
        }

        return new Result(false, null, null);
    }
}
