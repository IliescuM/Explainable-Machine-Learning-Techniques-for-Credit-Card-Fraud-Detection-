using ExplainableFraud.Contracts.Training;
using ExplainableFraud.Infrastructure.Options;
using ExplainableFraud.Infrastructure.Training;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExplainableFraud.UnitTests;

public sealed class SimulatedTrainingJobServiceTests
{
    [Fact]
    public async Task Synthetic_in_process_training_surfaces_real_metrics()
    {
        var opts = Options.Create(new SimulatedTrainingJobOptions
        {
            UseSimulation = true,
            RunSyntheticInProcessTraining = true,
            StepDelayMilliseconds = 0,
            ProgressStepsPerPhase = 1,
            SyntheticTrainingRowCount = 3584,
            CreditcardCsvCandidateRelativePaths = [],
            CreditcardCsvParentDirectoryWalkDepth = 0
        });

        var service = new SimulatedTrainingJobService(opts, CreateIsolateHostEnvironment(),
            NullLogger<SimulatedTrainingJobService>.Instance);

        var outcome =
            await service.StartJobAsync(TrainingDatasetKind.CreditcardKaggleDemo, TrainingModelFamily.FastTreeGradientBoost,
                CancellationToken.None);

        Assert.True(outcome.Success);

        TrainingJobDto? snapshot = null;
        for (var attempt = 0; attempt < 600; attempt++)
        {
            snapshot = service.GetJob(outcome.JobId);
            Assert.NotNull(snapshot);

            if (snapshot!.Status == TrainingJobStatus.Succeeded)
                break;

            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }

        Assert.NotNull(snapshot);
        Assert.Equal(TrainingJobStatus.Succeeded, snapshot!.Status);
        Assert.Equal(100, snapshot.ProgressPercent);
        Assert.False(snapshot.IsSimulated);
        Assert.True(snapshot.UsedInProcessMlTrainer);
        Assert.True(snapshot.UsesSyntheticRows);
        Assert.Equal(TrainingDataSourceKind.SyntheticDeterministic, snapshot.DataSourceKind);
        Assert.Contains("Synthetic ULB-shaped", snapshot.DataSourceSummary ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.True(snapshot.Metrics!.TrainRows >= 6_800);
        Assert.NotNull(snapshot.CompletedAt);
        Assert.NotNull(snapshot.Stages);
        Assert.Equal(7, snapshot.Stages!.Count);
        Assert.NotNull(snapshot.Metrics);
        Assert.InRange(snapshot.Metrics!.AreaUnderRocCurve, 0.65, 1.01);
        Assert.InRange(snapshot.Metrics.AreaUnderPrecisionRecallCurve, 0d, 1.01);
        Assert.True(snapshot.Metrics.TruePositives + snapshot.Metrics.FalseNegatives +
                    snapshot.Metrics.FalsePositives + snapshot.Metrics.TrueNegatives > 100);
    }

    [Fact]
    public async Task Timeline_only_mode_keeps_simulated_flag_without_fit()
    {
        var opts = Options.Create(new SimulatedTrainingJobOptions
        {
            UseSimulation = true,
            RunSyntheticInProcessTraining = false,
            StepDelayMilliseconds = 1,
            ProgressStepsPerPhase = 1,
            CreditcardCsvCandidateRelativePaths = [],
            CreditcardCsvParentDirectoryWalkDepth = 0
        });

        var service = new SimulatedTrainingJobService(opts, CreateIsolateHostEnvironment(),
            NullLogger<SimulatedTrainingJobService>.Instance);

        var outcome = await service.StartJobAsync(
            TrainingDatasetKind.CreditcardKaggleDemo,
            TrainingModelFamily.LogisticRegressionBaseline,
            CancellationToken.None);

        Assert.True(outcome.Success);

        TrainingJobDto? snapshot = null;
        for (var attempt = 0; attempt < 600; attempt++)
        {
            snapshot = service.GetJob(outcome.JobId);
            Assert.NotNull(snapshot);

            if (snapshot!.Status == TrainingJobStatus.Succeeded)
                break;

            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }

        Assert.True(snapshot!.IsSimulated);
        Assert.False(snapshot.UsedInProcessMlTrainer);
        Assert.False(snapshot.UsesSyntheticRows);
        Assert.NotNull(snapshot.Metrics);
        Assert.Equal(0, snapshot.Metrics!.TrainRows);
    }

    [Fact]
    public async Task Neural_placeholder_is_rejected_without_enqueue()
    {
        var opts = Options.Create(new SimulatedTrainingJobOptions
        {
            UseSimulation = true,
            CreditcardCsvCandidateRelativePaths = [],
            CreditcardCsvParentDirectoryWalkDepth = 0
        });

        var service = new SimulatedTrainingJobService(opts, CreateIsolateHostEnvironment(),
            NullLogger<SimulatedTrainingJobService>.Instance);

        var outcome = await service.StartJobAsync(
            TrainingDatasetKind.CreditcardKaggleDemo,
            TrainingModelFamily.NeuralNetworkExperimentalPlaceholder,
            CancellationToken.None);

        Assert.False(outcome.Success);
        Assert.Equal(Guid.Empty, outcome.JobId);
        Assert.Contains("Torch", outcome.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UseSimulation_false_blocks_start()
    {
        var opts = Options.Create(new SimulatedTrainingJobOptions
        {
            UseSimulation = false,
            StepDelayMilliseconds = 1,
            ProgressStepsPerPhase = 1,
            CreditcardCsvCandidateRelativePaths = [],
            CreditcardCsvParentDirectoryWalkDepth = 0
        });

        var service = new SimulatedTrainingJobService(opts, CreateIsolateHostEnvironment(),
            NullLogger<SimulatedTrainingJobService>.Instance);

        var outcome =
            await service.StartJobAsync(
                TrainingDatasetKind.CreditcardKaggleDemo,
                TrainingModelFamily.LogisticRegressionBaseline,
                CancellationToken.None);

        Assert.False(outcome.Success);
        Assert.Equal(Guid.Empty, outcome.JobId);
        Assert.Contains("orchestration", outcome.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static IHostEnvironment CreateIsolateHostEnvironment()
    {
        var root = Path.Combine(Path.GetTempPath(), "ExplainableFraudTests-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        return new StubTempHost(root);
    }

    private sealed class StubTempHost(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "ExplainableFraud.Tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
