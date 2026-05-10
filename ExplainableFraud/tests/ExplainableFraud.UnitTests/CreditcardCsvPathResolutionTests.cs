using ExplainableFraud.Infrastructure.Ml;
using ExplainableFraud.Infrastructure.Options;
using ExplainableFraud.Infrastructure.Training;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace ExplainableFraud.UnitTests;

public sealed class CreditcardCsvPathResolutionTests
{
    [Fact]
    public void Relative_path_three_levels_up_resolves_dissertation_root_csv_from_api_content_root()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "CreditcardPath-" + Guid.NewGuid().ToString("N")[..10]);
        var repoRoot = Path.Combine(tmp, "DissertationRoot");
        var apiRoot = Path.Combine(repoRoot, "ExplainableFraud", "src", "ExplainableFraud.Api");
        Directory.CreateDirectory(apiRoot);
        var csvPath = Path.Combine(repoRoot, "creditcard.csv");
        WriteMinimalCreditcardCsv(csvPath);

        var host = new StubHost(apiRoot);
        var opts = new SimulatedTrainingJobOptions
        {
            // DissertationRoot/ExplainableFraud/src/ExplainableFraud.Api -> three levels up to dissertation root.
            CreditcardCsvCandidateRelativePaths = ["../../../creditcard.csv"],
            CreditcardCsvParentDirectoryWalkDepth = 0
        };

        var candidates = CreditcardCsvPathResolution.BuildOrderedCandidatePaths(host, opts);
        var expected = Path.GetFullPath(csvPath);
        Assert.Contains(expected, candidates.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase));

        var probe = CreditcardCsvPathProbe.Probe(host, opts);
        Assert.True(probe.IsDiscoverable);
        Assert.Equal(Path.GetFullPath(csvPath), Path.GetFullPath(probe.AbsolutePath!));
        Assert.Equal(CreditcardCsvObservationLoader.MinimumUsefulRows, probe.ApproximateDataRowCount);
    }

    [Fact]
    public void Ancestor_walk_finds_csv_without_explicit_relative_when_depth_sufficient()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "CreditcardWalk-" + Guid.NewGuid().ToString("N")[..10]);
        var repoRoot = Path.Combine(tmp, "DissertationRoot");
        var apiRoot = Path.Combine(repoRoot, "ExplainableFraud", "src", "ExplainableFraud.Api");
        Directory.CreateDirectory(apiRoot);
        var csvPath = Path.Combine(repoRoot, "creditcard.csv");
        WriteMinimalCreditcardCsv(csvPath);

        var host = new StubHost(apiRoot);
        var opts = new SimulatedTrainingJobOptions
        {
            CreditcardCsvCandidateRelativePaths = [],
            CreditcardCsvParentDirectoryWalkDepth = 8
        };

        var probe = CreditcardCsvPathProbe.Probe(host, opts);
        Assert.True(probe.IsDiscoverable);
        Assert.Equal(Path.GetFullPath(csvPath), Path.GetFullPath(probe.AbsolutePath!));
    }

    [Fact]
    public void TryEstimateDataRowCount_counts_non_empty_data_lines_after_kaggle_header()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cc_est_{Guid.NewGuid():N}.csv");
        try
        {
            WriteMinimalCreditcardCsv(path);
            Assert.True(CreditcardCsvObservationLoader.TryEstimateDataRowCount(path, out var n));
            Assert.Equal(CreditcardCsvObservationLoader.MinimumUsefulRows, n);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void WriteMinimalCreditcardCsv(string path)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine(
            "Time,V1,V2,V3,V4,V5,V6,V7,V8,V9,V10,V11,V12,V13,V14,V15,V16,V17,V18,V19,V20,V21,V22,V23,V24,V25,V26,V27,V28,Amount,Class");
        for (var i = 0; i < CreditcardCsvObservationLoader.MinimumUsefulRows; i++)
            writer.WriteLine($"{i},{string.Join(",", Enumerable.Repeat("0", 28))},1,0");
    }

    private sealed class StubHost(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "ExplainableFraud.Tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
