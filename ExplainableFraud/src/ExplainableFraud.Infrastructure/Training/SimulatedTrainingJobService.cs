using System.Collections.Concurrent;
using System.Diagnostics;
using ExplainableFraud.Application.Abstractions;
using ExplainableFraud.Contracts.Fraud;
using ExplainableFraud.Contracts.Training;
using ExplainableFraud.Infrastructure.Ml;
using ExplainableFraud.Infrastructure.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExplainableFraud.Infrastructure.Training;

/// <summary>
/// In-memory training jobs. Default path runs a truthful ML.NET Fit/Evaluate on deterministic synthetic ULB-shaped rows so metrics are real despite the microscopic workload.
/// </summary>
public sealed class SimulatedTrainingJobService(
    IOptions<SimulatedTrainingJobOptions> options,
    IHostEnvironment hostEnvironment,
    ILogger<SimulatedTrainingJobService> logger)
    : ITrainingJobService
{
    private readonly ConcurrentDictionary<Guid, JobState> _jobs = new();

    private static readonly TrainingDatasetKind[] SupportedDatasetKinds =
    [
        TrainingDatasetKind.CreditcardKaggleDemo,
        TrainingDatasetKind.CreditcardLocalCsv
    ];

    public IReadOnlyList<TrainingDatasetSummaryDto> GetAvailableDatasets() => BuildDatasetCatalog();

    private IReadOnlyList<TrainingDatasetSummaryDto> BuildDatasetCatalog()
    {
        var opts = options.Value;
        var probe = CreditcardCsvPathProbe.Probe(hostEnvironment, opts);

        var discoveryNote = probe.IsDiscoverable
            ? $" Local CSV: discoverable at {probe.AbsolutePath}" +
              (probe.ApproximateDataRowCount is { } n ? $" (~{n:N0} data rows by line scan)." : " (row count not estimated).")
            : " Local CSV: not found from content root / base directory, configured relatives, or ancestor walk — synthetics will be used unless you add creditcard.csv or paths under TrainingJobs:Simulation.";

        return
        [
            new TrainingDatasetSummaryDto
            {
                Kind = TrainingDatasetKind.CreditcardKaggleDemo,
                DisplayName = "Creditcard workflow (demo)",
                Description =
                    "Same column semantics as the ULB/Kaggle CSV (Time, V1–V28, Amount, Class). If creditcard.csv is discovered (configured paths, next to appsettings, or by walking up from the API folder), that file is loaded offline; otherwise ≥10k deterministic synthetic rows are generated."
                    + discoveryNote,
                LocalCreditcardCsvDiscoverable = probe.IsDiscoverable,
                LocalCreditcardCsvAbsolutePath = probe.AbsolutePath,
                LocalCreditcardCsvApproximateDataRowCount = probe.ApproximateDataRowCount
            },
            new TrainingDatasetSummaryDto
            {
                Kind = TrainingDatasetKind.CreditcardLocalCsv,
                DisplayName = "Creditcard (local CSV emphasis)",
                Description =
                    "Identical ingestion path with a higher synthetic multiplier when no CSV is discovered. Discovery uses the same search as the demo workflow (including repository-root relatives such as ../../../../creditcard.csv)."
                    + discoveryNote,
                LocalCreditcardCsvDiscoverable = probe.IsDiscoverable,
                LocalCreditcardCsvAbsolutePath = probe.AbsolutePath,
                LocalCreditcardCsvApproximateDataRowCount = probe.ApproximateDataRowCount
            }
        ];
    }

    public TrainingJobDto? GetJob(Guid jobId) =>
        _jobs.TryGetValue(jobId, out var state) ? Snapshot(state) : null;

    public Task<TrainingJobStartOutcome> StartJobAsync(
        TrainingDatasetKind datasetKind,
        TrainingModelFamily modelFamily,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        if (datasetKind is TrainingDatasetKind.Unspecified)
            return Task.FromResult(TrainingJobStartOutcome.Fail("Select a dataset."));

        if (modelFamily is TrainingModelFamily.Unspecified)
            return Task.FromResult(TrainingJobStartOutcome.Fail("Select a model family."));

        if (modelFamily is TrainingModelFamily.NeuralNetworkExperimentalPlaceholder)
        {
            return Task.FromResult(TrainingJobStartOutcome.Fail(
                "Neural/tabular stacks are disabled in this build: ML.NET Core ships tree/linear trainers; deep networks would require Torch/ONNX or an external Python service."));
        }

        if (SupportedDatasetKinds.All(k => k != datasetKind))
            return Task.FromResult(TrainingJobStartOutcome.Fail("Unknown dataset."));

        var opts = options.Value;
        if (!opts.UseSimulation)
            return Task.FromResult(TrainingJobStartOutcome.Fail("Real training orchestration is not wired yet — enable simulation in configuration."));

        var jobId = Guid.NewGuid();
        var utcNow = DateTimeOffset.UtcNow;
        var job = new JobState(jobId, datasetKind, modelFamily, utcNow);
        if (!_jobs.TryAdd(jobId, job))
            return Task.FromResult(TrainingJobStartOutcome.Fail("Could not enqueue job — retry."));

        _ = RunJobAsync(job, opts, CancellationToken.None);
        logger.LogInformation("Queued training job {JobId} dataset {DatasetKind} model {ModelFamily}", jobId, datasetKind,
            modelFamily);

        return Task.FromResult(TrainingJobStartOutcome.Ok(jobId));
    }

    private async Task RunJobAsync(JobState job, SimulatedTrainingJobOptions opts, CancellationToken cancellationToken)
    {
        var delay = Math.Clamp(opts.StepDelayMilliseconds, 0, 60_000);
        var steps = Math.Clamp(opts.ProgressStepsPerPhase, 1, 50);
        var wall = Stopwatch.StartNew();

        try
        {
            if (opts.RunSyntheticInProcessTraining)
                await RunSyntheticTrainerAsync(job, opts, delay, steps, wall, cancellationToken).ConfigureAwait(false);
            else
                await RunLegacyTimelineAsync(job, opts, delay, steps, wall, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            lock (job.Sync)
            {
                job.DurationMilliseconds = wall.ElapsedMilliseconds;
                job.Status = TrainingJobStatus.Failed;
                job.CompletedAt = DateTimeOffset.UtcNow;
                job.ErrorDetail = "Cancelled.";
                job.Message = job.ErrorDetail;
                MarkAllPendingStagesFailed(job, "Cancelled.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Training job {JobId} failed", job.JobId);

            lock (job.Sync)
            {
                job.DurationMilliseconds = wall.ElapsedMilliseconds;
                job.Status = TrainingJobStatus.Failed;
                job.CompletedAt = DateTimeOffset.UtcNow;
                job.ErrorDetail = ex.Message;
                job.Message = "Training failed — inspect ErrorDetail for ML pipeline diagnostics.";
                MarkAllPendingStagesFailed(job, ex.Message);
            }
        }
    }

    /// <remarks>Historical delay-only UX for regression testing / teaching.</remarks>
    private async Task RunLegacyTimelineAsync(JobState job, SimulatedTrainingJobOptions opts, int delay, int steps, Stopwatch wall,
        CancellationToken cancellationToken)
    {
        await AdvanceAsync(job, TrainingJobPhase.LoadingDataset, "Synthetic dataset ingestion (timeline-only)...", delay, steps,
            fromPercent: 0, toPercent: 23, coarseOnly: false, cancellationToken).ConfigureAwait(false);

        await AdvanceAsync(job, TrainingJobPhase.ModelTraining,
            $"Training {DescribeTrainerFamilyStatic(job.ModelFamily)} classifier (timeline-only)...", delay, steps, fromPercent: 24,
            toPercent: 68, coarseOnly: false, cancellationToken).ConfigureAwait(false);

        await AdvanceAsync(job, TrainingJobPhase.Evaluating, "Computing hold-out metrics (timeline-only)...", delay, steps,
            fromPercent: 69, toPercent: 88, coarseOnly: false, cancellationToken).ConfigureAwait(false);

        await AdvanceAsync(job, TrainingJobPhase.WritingArtifacts,
            $"Saving artifact plan for {DescribeTrainerFamilyStatic(job.ModelFamily)}... (timeline-only)...", delay, steps,
            fromPercent: 89, toPercent: 100, coarseOnly: false, cancellationToken).ConfigureAwait(false);

        lock (job.Sync)
        {
            if (job.StagesMutable is not null)
            {
                foreach (var s in job.StagesMutable)
                {
                    s.Status = TrainingPipelineStageStatus.Completed;
                    s.Detail = "Timeline-only surrogate.";
                }
            }

            job.Status = TrainingJobStatus.Succeeded;
            job.ProgressPercent = 100;
            job.Phase = TrainingJobPhase.WritingArtifacts;
            job.Message =
                "Timeline simulation finished — no estimator was Fit. Toggle TrainingJobs:Simulation:RunSyntheticInProcessTraining to surface real ML.NET metrics on synthetic rows.";
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.DurationMilliseconds = wall.ElapsedMilliseconds;
            job.Metrics ??= MinimalHintMetrics(job, legacy: true);
        }
    }

    private async Task RunSyntheticTrainerAsync(JobState job, SimulatedTrainingJobOptions opts, int delayMs, int steps,
        Stopwatch wall, CancellationToken cancellationToken)
    {
        lock (job.Sync)
        {
            job.Status = TrainingJobStatus.Running;
            foreach (var s in job.StagesMutable)
            {
                s.Status = TrainingPipelineStageStatus.Pending;
                s.Detail = "";
            }
        }

        async Task BreathAsync(TrainingJobPhase coarse, CancellationToken ct)
        {
            if (delayMs <= 0)
                await Task.CompletedTask.ConfigureAwait(false);
            else
                await PulseStageAsync(job, coarse, delayMs, Math.Max(steps / 4, 1), ct).ConfigureAwait(false);
        }

        async Task PulseStageAsync(
            JobState j,
            TrainingJobPhase coarse,
            int d,
            int microSteps,
            CancellationToken ct)
        {
            for (var i = 1; i <= microSteps; i++)
            {
                ct.ThrowIfCancellationRequested();
                var pct = RecalculateAggregatePercentUnsafe(j);

                lock (j.Sync)
                {
                    j.Status = TrainingJobStatus.Running;
                    j.Phase = coarse;
                    j.ProgressPercent = pct;
                }

                if (d > 0)
                    await Task.Delay(d, ct).ConfigureAwait(false);
            }
        }

        // --- Stage: validate -------------------------------------------------
        SetStage(job, TrainingJobPhase.LoadingDataset, "validate", TrainingPipelineStageStatus.Running,
            "Checking schema expectations (Time, V1..V28, Amount) and locating creditcard.csv or falling back to deterministic synthetics.");
        await BreathAsync(TrainingJobPhase.LoadingDataset, cancellationToken).ConfigureAwait(false);

        var resolution =
            TrainingRowsResolution.Resolve(job.DatasetKind, opts, hostEnvironment, CombinedSeed(job.JobId));

        lock (job.Sync)
        {
            job.DataSourceKind = resolution.DataSourceKind;
            job.DataSourceSummary = resolution.DataSourceSummary;
        }

        SetStage(job, TrainingJobPhase.LoadingDataset, "validate", TrainingPipelineStageStatus.Completed,
            resolution.DataSourceSummary);

        // --- Stage: split ----------------------------------------------------
        SetStage(job, TrainingJobPhase.LoadingDataset, "split", TrainingPipelineStageStatus.Running,
            "Applying per-class stratified 70 / 15 / 15 partition (shuffle within class; deterministic seed).");
        await BreathAsync(TrainingJobPhase.LoadingDataset, cancellationToken).ConfigureAwait(false);

        var rows = resolution.Rows.ToArray();

        var fraudCount = rows.Count(static r => r.Label);
        var legitimateCount = rows.Length - fraudCount;
        CreditcardStyleStratifiedSplitter.EnsureBinaryClassesOrThrow(rows.Length, fraudCount, legitimateCount);

        var trainerMlSeed = CombinedSeed(job.JobId) ^ 404;
        var stratified = CreditcardStyleStratifiedSplitter.Split(rows,
            CreditcardStyleStratifiedSplitter.MatchingTrainerSplitSeed(trainerMlSeed));

        lock (job.Sync)
        {
            job.LabelDistribution = stratified.LabelDistribution;
        }

        SetStage(job, TrainingJobPhase.LoadingDataset, "split", TrainingPipelineStageStatus.Completed,
            SplitDetail(stratified.LabelDistribution, opts.ReferenceKaggleCardinality));

        // --- Stage: preprocess ----------------------------------------------
        SetStage(job, TrainingJobPhase.ModelTraining, "preprocess", TrainingPipelineStageStatus.Running,
            $"Building ML.NET concatenate feature vector ×{InProcessCreditcardStyleTrainer.FeatureCount}; linear models add min-max scaling.");
        await BreathAsync(TrainingJobPhase.ModelTraining, cancellationToken).ConfigureAwait(false);
        SetStage(job, TrainingJobPhase.ModelTraining, "preprocess", TrainingPipelineStageStatus.Completed,
            job.ModelFamily is TrainingModelFamily.LogisticRegressionBaseline
                ? "Concatenate + NormalizeMinMax for LBFGS logistic regression baseline."
                : "Concatenate only — tree ensembles learn scale-aware splits.");

        // --- Stage: imbalance --------------------------------------------------
        SetStage(job, TrainingJobPhase.ModelTraining, "imbalance", TrainingPipelineStageStatus.Running,
            "Mitigating imbalance through trainer-specific safeguards (e.g., FastTree UnbalancedSets).");
        await BreathAsync(TrainingJobPhase.ModelTraining, cancellationToken).ConfigureAwait(false);
        SetStage(job, TrainingJobPhase.ModelTraining, "imbalance", TrainingPipelineStageStatus.Completed,
            "Class priors encoded in tree hyperparameters; logistic baseline relies on calibrated evaluation on rare positives.");

        // --- Stage: fit ------------------------------------------------------
        SetStage(job, TrainingJobPhase.ModelTraining, "fit", TrainingPipelineStageStatus.Running,
            $"Executing ML.NET Fit for {DescribeTrainerFamilyStatic(job.ModelFamily)}.");
        PulsePercentOnly(job);

        InProcessCreditcardStyleTrainer.SyntheticRunResult result;
        result = await Task.Run(() =>
                InProcessCreditcardStyleTrainer.FitAndEvaluateFromStratifiedSplit(job.ModelFamily, stratified,
                    trainerMlSeed), cancellationToken).ConfigureAwait(false);

        lock (job.Sync)
        {
            job.TrainingIterationsReported = result.TrainingIterationsReported;
        }

        SetStage(job, TrainingJobPhase.ModelTraining, "fit", TrainingPipelineStageStatus.Completed,
            $"{result.Metrics.TrainerFamilyLabel} finished in {result.Metrics.FittingDurationMilliseconds:N0} ms (ML.NET Fit + transform).");

        // --- Stage: evaluate --------------------------------------------------
        SetStage(job, TrainingJobPhase.Evaluating, "evaluate", TrainingPipelineStageStatus.Running,
            "Scoring withheld test partition; metrics use ROC/PR/F1 from raw classification outputs (trees lack native Probability scores in this evaluator).");
        await BreathAsync(TrainingJobPhase.Evaluating, cancellationToken).ConfigureAwait(false);

        var metricsPayload = CloneMetrics(job.ModelFamily, result.Metrics);

        SetStage(job, TrainingJobPhase.Evaluating, "evaluate", TrainingPipelineStageStatus.Completed,
            $"ROC-AUC={metricsPayload.AreaUnderRocCurve:F4}, PR-AUC={metricsPayload.AreaUnderPrecisionRecallCurve:F4}, Precision={metricsPayload.Precision:F4}, Recall={metricsPayload.Recall:F4}, F1={metricsPayload.F1Score:F4}.");

        lock (job.Sync)
        {
            job.Metrics = metricsPayload;
            job.LabelDistribution = result.LabelDistribution;
        }

        // --- Stage: artifacts --------------------------------------------------
        SetStage(job, TrainingJobPhase.WritingArtifacts, "artifact", TrainingPipelineStageStatus.Running,
            "Preparing deployment notes (no disk write in demo mode).");
        await BreathAsync(TrainingJobPhase.WritingArtifacts, cancellationToken).ConfigureAwait(false);
        SetStage(job, TrainingJobPhase.WritingArtifacts, "artifact", TrainingPipelineStageStatus.Completed,
            "Demo path keeps models in memory only. Promote to disk via ExplainableFraud.Training --data creditcard.csv for production artifacts.");

        lock (job.Sync)
        {
            job.Status = TrainingJobStatus.Succeeded;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.ProgressPercent = 100;
            job.Phase = TrainingJobPhase.WritingArtifacts;
            job.Message =
                "ML.NET training finished — metrics are computed on withheld test rows (not scripted constants).";
            job.DurationMilliseconds = wall.ElapsedMilliseconds;
            job.IsSimulated = false;
            job.UseSyntheticRows = resolution.DataSourceKind is TrainingDataSourceKind.SyntheticDeterministic;
            job.UseInProcessMlTrainer = true;
        }
    }

    private static int CombinedSeed(Guid jobId) => jobId.GetHashCode() ^ unchecked((int)0x1357beef);

    private async Task AdvanceAsync(JobState job,
        TrainingJobPhase phase,
        string messageTemplate,
        int delayMs,
        int steps,
        int fromPercent,
        int toPercent,
        bool coarseOnly,
        CancellationToken cancellationToken)
    {
        for (var step = 1; step <= steps; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fraction = steps == 1 ? 1 : (step / (float)steps);
            var percent = Math.Clamp((int)Math.Round(fromPercent + (toPercent - fromPercent) * fraction), fromPercent, toPercent);

            lock (job.Sync)
            {
                job.Status = TrainingJobStatus.Running;
                job.Phase = phase;
                job.ProgressPercent = percent;
                job.Message = messageTemplate;
                job.CompletedAt = null;
                job.ErrorDetail = null;
                if (!coarseOnly)
                {
                    job.IsSimulated = true;
                    job.UseSyntheticRows = false;
                    job.UseInProcessMlTrainer = false;
                }

                PulseLegacyStages(job, phase, coarseOnly ? "Timeline-only coarse phase" : "Timeline pulse");
            }

            if (delayMs > 0)
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
        }
    }

    private static readonly string[] StageOrder =
    [
        "validate", "split", "preprocess", "imbalance", "fit", "evaluate", "artifact"
    ];

    private static int StageWireIndex(string id)
    {
        var idx = Array.IndexOf(StageOrder, id);
        return idx < 0 ? int.MaxValue : idx;
    }

    private static void PulseLegacyStages(JobState job, TrainingJobPhase phase, string detailSuffix)
    {
        if (job.StagesMutable is null)
            return;

        string MapId() =>
            phase switch
            {
                TrainingJobPhase.LoadingDataset => "validate",
                TrainingJobPhase.ModelTraining => "fit",
                TrainingJobPhase.Evaluating => "evaluate",
                TrainingJobPhase.WritingArtifacts => "artifact",
                _ => "validate"
            };

        var focus = MapId();
        var focusIndex = StageWireIndex(focus);
        foreach (var stage in job.StagesMutable)
        {
            var idx = StageWireIndex(stage.Id);
            if (idx < focusIndex)
            {
                stage.Status = TrainingPipelineStageStatus.Completed;
                stage.Detail = "Completed in legacy mode.";
            }
            else if (idx == focusIndex)
            {
                stage.Status = TrainingPipelineStageStatus.Running;
                stage.Detail = detailSuffix;
            }
            else
            {
                stage.Status = TrainingPipelineStageStatus.Pending;
                stage.Detail = "";
            }
        }
    }

    private static ModelValidationMetricsDto CloneMetrics(TrainingModelFamily family, ModelValidationMetricsDto source) =>
        new()
        {
            AreaUnderRocCurve = source.AreaUnderRocCurve,
            AreaUnderPrecisionRecallCurve = source.AreaUnderPrecisionRecallCurve,
            F1Score = source.F1Score,
            Precision = source.Precision,
            Recall = source.Recall,
            Accuracy = source.Accuracy,
            PositivePrecision = source.PositivePrecision,
            PositiveRecall = source.PositiveRecall,
            NegativePrecision = source.NegativePrecision,
            NegativeRecall = source.NegativeRecall,
            TrainRows = source.TrainRows,
            ValidationRows = source.ValidationRows,
            TestRows = source.TestRows,
            FeatureCount = source.FeatureCount,
            TrueNegatives = source.TrueNegatives,
            FalsePositives = source.FalsePositives,
            FalseNegatives = source.FalseNegatives,
            TruePositives = source.TruePositives,
            TrainerFamilyLabel = $"{source.TrainerFamilyLabel} · Selected={DescribeTrainerFamilyStatic(family)}",
            FittingDurationMilliseconds = source.FittingDurationMilliseconds
        };

    private static ModelValidationMetricsDto MinimalHintMetrics(JobState job, bool legacy) =>
        new()
        {
            TrainRows = 0,
            ValidationRows = 0,
            TestRows = 0,
            FeatureCount = InProcessCreditcardStyleTrainer.FeatureCount,
            TrainerFamilyLabel = legacy ? $"Timeline-only reminder · {DescribeTrainerFamilyStatic(job.ModelFamily)}" : ""
        };

    private static string SplitDetail(
        TrainingLabelDistributionDto distribution,
        int referenceKaggleCardinality)
    {
        static string Fmt(TrainingBinaryLabelCountsDto c) =>
            $"positive={c.Positives:N0}, negative={c.Negatives:N0}";

        return
            $"After load → {Fmt(distribution.AfterLoad)}. Stratified partitions → Train: {Fmt(distribution.Train)}; Validation: {Fmt(distribution.Validation)}; Test: {Fmt(distribution.Test)} (reference ULB cardinality ≈ {referenceKaggleCardinality:N0}).";
    }

    private static TrainingPipelineStageDto SnapshotStage(JobStage mutable) =>
        new()
        {
            Id = mutable.Id,
            Title = mutable.Title,
            Status = mutable.Status,
            Detail = mutable.Detail
        };

    private static int RecalculateAggregatePercentUnsafe(JobState job)
    {
        if (job.StagesMutable is null)
            return job.ProgressPercent;

        var denom = Math.Max(job.StagesMutable.Count, 1);
        var acc = job.StagesMutable.Sum(stage => stage.Status switch
        {
            TrainingPipelineStageStatus.Completed => 1f,
            TrainingPipelineStageStatus.Running => 0.65f,
            TrainingPipelineStageStatus.Failed => 0f,
            TrainingPipelineStageStatus.Skipped => 1f,
            _ => 0f
        });

        return Math.Clamp((int)Math.Round(acc / denom * 100f), 0, 99);
    }

    private static void PulsePercentOnly(JobState job)
    {
        lock (job.Sync)
        {
            job.ProgressPercent = RecalculateAggregatePercentUnsafe(job);
        }
    }

    private static void SetStage(JobState job, TrainingJobPhase coarse, string id, TrainingPipelineStageStatus status, string detail)
    {
        lock (job.Sync)
        {
            var match = job.StagesMutable.First(s => s.Id == id);
            match.Status = status;
            match.Detail = detail;
            job.Phase = coarse;
            job.Status = TrainingJobStatus.Running;
            job.Message = $"{match.Title}: {detail}";
            job.ProgressPercent = RecalculateAggregatePercentUnsafe(job);
            job.LastStageRefreshUtc = DateTimeOffset.UtcNow;
        }
    }

    private static void MarkAllPendingStagesFailed(JobState job, string detail)
    {
        if (job.StagesMutable is null)
            return;

        foreach (var s in job.StagesMutable.Where(static s =>
                     s.Status is TrainingPipelineStageStatus.Pending or TrainingPipelineStageStatus.Running))
        {
            s.Status = TrainingPipelineStageStatus.Failed;
            s.Detail = detail;
        }
    }

    private static TrainingJobDto Snapshot(JobState job)
    {
        lock (job.Sync)
        {
            return new TrainingJobDto
            {
                JobId = job.JobId,
                DatasetKind = job.DatasetKind,
                ModelFamily = job.ModelFamily,
                Status = job.Status,
                Phase = job.Phase,
                ProgressPercent = job.ProgressPercent,
                Message = job.Message,
                CreatedAt = job.CreatedAt,
                CompletedAt = job.CompletedAt,
                IsSimulated = job.IsSimulated,
                ErrorDetail = job.ErrorDetail,
                Stages = job.StagesMutable?.Select(SnapshotStage).ToArray() ?? [],
                Metrics = job.Metrics,
                UsedInProcessMlTrainer = job.UseInProcessMlTrainer,
                UsesSyntheticRows = job.UseSyntheticRows,
                DataSourceKind = job.DataSourceKind,
                DataSourceSummary = job.DataSourceSummary,
                LabelDistribution = job.LabelDistribution,
                TrainingIterationsReported = job.TrainingIterationsReported,
                DurationMilliseconds = job.DurationMilliseconds > 0
                    ? job.DurationMilliseconds
                    : job.CompletedAt is { } end
                        ? (long)(end - job.CreatedAt).TotalMilliseconds
                        : (long)(DateTimeOffset.UtcNow - job.CreatedAt).TotalMilliseconds
            };
        }
    }

    private static string DescribeTrainerFamilyStatic(TrainingModelFamily family) =>
        family switch
        {
            TrainingModelFamily.LogisticRegressionBaseline => "logistic regression (LBFGS)",
            TrainingModelFamily.FastTreeGradientBoost => "FastTree gradient boosting",
            TrainingModelFamily.FastForestEnsemble => "FastForest bagging",
            TrainingModelFamily.NeuralNetworkExperimentalPlaceholder => "experimental neural (disabled)",
            _ => family.ToString()
        };

    private sealed class JobState
    {
        public JobState(Guid jobId, TrainingDatasetKind datasetKind, TrainingModelFamily modelFamily, DateTimeOffset createdAt)
        {
            JobId = jobId;
            DatasetKind = datasetKind;
            ModelFamily = modelFamily;
            CreatedAt = createdAt;
            Status = TrainingJobStatus.Pending;
            Phase = TrainingJobPhase.NotStarted;
            Message = "Job queued.";
            IsSimulated = true;
            UseSyntheticRows = false;
            UseInProcessMlTrainer = false;
            DataSourceKind = TrainingDataSourceKind.Unspecified;
            DataSourceSummary = "";

            StagesMutable =
            [
                new JobStage("validate", "Dataset validation"),
                new JobStage("split", "Stratified train / validation / test split"),
                new JobStage("preprocess", "Preprocessing pipeline"),
                new JobStage("imbalance", "Class imbalance safeguards"),
                new JobStage("fit", "Model fitting"),
                new JobStage("evaluate", "Evaluation & calibration"),
                new JobStage("artifact", "Artifact publication plan"),
            ];

            Metrics = null;
        }

        public object Sync { get; } = new();

        public Guid JobId { get; }

        public TrainingDatasetKind DatasetKind { get; }

        public TrainingModelFamily ModelFamily { get; }

        public DateTimeOffset CreatedAt { get; }

        public TrainingJobStatus Status { get; set; }

        public TrainingJobPhase Phase { get; set; }

        public int ProgressPercent { get; set; }

        public string Message { get; set; }

        public DateTimeOffset? CompletedAt { get; set; }

        public string? ErrorDetail { get; set; }

        public bool IsSimulated { get; set; }

        public bool UseSyntheticRows { get; set; }

        public bool UseInProcessMlTrainer { get; set; }

        public TrainingDataSourceKind DataSourceKind { get; set; }

        public string DataSourceSummary { get; set; } = "";

        public TrainingLabelDistributionDto? LabelDistribution { get; set; }

        public ModelValidationMetricsDto? Metrics { get; set; }

        public DateTimeOffset? LastStageRefreshUtc { get; set; }

        public int? TrainingIterationsReported { get; set; }

        public long DurationMilliseconds { get; set; }

        public List<JobStage> StagesMutable { get; }
    }

    private sealed class JobStage(string id, string title)
    {
        public string Id { get; } = id;

        public string Title { get; } = title;

        public TrainingPipelineStageStatus Status { get; set; }

        public string Detail { get; set; } = "";
    }
}
