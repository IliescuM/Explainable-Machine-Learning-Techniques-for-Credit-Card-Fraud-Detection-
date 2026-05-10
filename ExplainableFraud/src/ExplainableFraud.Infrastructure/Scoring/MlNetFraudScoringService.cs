using ExplainableFraud.Application.Abstractions;
using ExplainableFraud.Contracts.Fraud;
using ExplainableFraud.Domain.Transactions;
using ExplainableFraud.Infrastructure.Ml;
using ExplainableFraud.Infrastructure.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.ML;
using System.Text.Json;

namespace ExplainableFraud.Infrastructure.Scoring;

/// <summary>Loads an ML.NET binary classifier + metadata produced by <c>ExplainableFraud.Training</c>.</summary>
public sealed class MlNetFraudScoringService : IFraudScoringService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly string[] FeatureNameOrder =
    [
        "Time",
        "V1", "V2", "V3", "V4", "V5", "V6", "V7", "V8", "V9", "V10",
        "V11", "V12", "V13", "V14", "V15", "V16", "V17", "V18", "V19", "V20",
        "V21", "V22", "V23", "V24", "V25", "V26", "V27", "V28",
        "Amount"
    ];

    private readonly PredictionEngine<FraudMlInput, FraudMlOutput> _engine;
    private readonly FraudModelMetadata _metadata;
    private readonly MlPipelineOptions _options;
    private readonly Dictionary<string, float> _importanceByName;
    private readonly bool _useUniformImportance;
    private readonly object _predictionLock = new();

    public MlNetFraudScoringService(IOptions<MlPipelineOptions> options, IHostEnvironment hostEnvironment)
    {
        _options = options.Value;
        var modelPath = ResolveModelPath(options.Value.ModelPath, hostEnvironment.ContentRootPath);
        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
            throw new InvalidOperationException($"ML model file not found: '{modelPath}'. Train with ExplainableFraud.Training or clear MlPipeline:ModelPath to use the heuristic.");

        var ml = new MLContext(seed: 42);
        var model = ml.Model.Load(modelPath, out _);
        _engine = ml.Model.CreatePredictionEngine<FraudMlInput, FraudMlOutput>(model);

        var metaPath = MetadataPath(modelPath);
        if (File.Exists(metaPath))
        {
            var json = File.ReadAllText(metaPath);
            _metadata = JsonSerializer.Deserialize<FraudModelMetadata>(json, JsonOptions)
                        ?? new FraudModelMetadata();
        }
        else
            _metadata = new FraudModelMetadata();

        _importanceByName = _metadata.GlobalImportance.ToDictionary(x => x.Name, x => x.Importance, StringComparer.Ordinal);
        _useUniformImportance = _importanceByName.Count == 0;
    }

    public Task<FraudScoreResponse> ScoreAsync(TransactionFeatures transaction, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var input = FraudMlInputMapper.FromDomain(transaction);
        FraudMlOutput prediction;
        lock (_predictionLock)
        {
            prediction = _engine.Predict(input);
        }

        var p = Math.Clamp(prediction.Probability, 0f, 1f);
        var threshold = _options.DecisionThreshold;

        var contributions = BuildContributions(input);
        var label = string.IsNullOrWhiteSpace(_metadata.ModelVersionLabel)
            ? _options.ModelVersionLabel
            : _metadata.ModelVersionLabel;
        var modelVersion = $"mlnet:{label}";

        var response = new FraudScoreResponse
        {
            FraudProbability = p,
            DecisionThreshold = threshold,
            IsFraudLikely = p >= threshold,
            ExplanationMethod = ExplanationMethod.MetadataWeightedDeviation,
            ExplanationSummary = "Approximate local emphasis combines global training importance with each feature's deviation from training statistics.",
            FeatureContributions = contributions,
            ModelVersion = modelVersion,
            ValidationMetrics = _metadata.Metrics
        };

        return Task.FromResult(response);
    }

    private IReadOnlyList<FeatureContributionDto> BuildContributions(FraudMlInput input)
    {
        var raw = new List<(string Name, float Value)>(FeatureNameOrder.Length);
        foreach (var name in FeatureNameOrder)
        {
            var x = GetFeature(input, name);
            _metadata.FeatureMeans.TryGetValue(name, out var mean);
            _metadata.FeatureStds.TryGetValue(name, out var std);
            var imp = _useUniformImportance ? 1f : _importanceByName.GetValueOrDefault(name);

            var z = std > 1e-8f ? MathF.Abs((x - mean) / std) : MathF.Abs(x - mean);
            raw.Add((name, imp * z));
        }

        var scale = raw.Max(t => Math.Abs(t.Value));
        if (scale < 1e-8f)
            scale = 1f;

        return raw
            .Select(t => new FeatureContributionDto
            {
                FeatureName = FormatFeatureLabel(t.Name),
                Contribution = t.Value / scale,
                Kind = GetFeatureKind(t.Name),
                Description = FormatFeatureDescription(t.Name)
            })
            .OrderByDescending(x => Math.Abs(x.Contribution))
            .ToList();
    }

    private static string FormatFeatureLabel(string name) =>
        name is "Time" or "Amount" ? name : $"PCA {name}";

    private static FeatureContributionKind GetFeatureKind(string name) =>
        name is "Time" or "Amount" ? FeatureContributionKind.Scalar : FeatureContributionKind.PrincipalComponent;

    private static string FormatFeatureDescription(string name) =>
        name switch
        {
            "Time" => "Seconds elapsed in the Kaggle transaction window.",
            "Amount" => "Transaction amount as provided to the model.",
            _ => "An anonymized principal component from the public credit-card fraud dataset."
        };

    private static float GetFeature(FraudMlInput input, string name)
    {
        if (name == "Time")
            return input.Time;
        if (name == "Amount")
            return input.Amount;
        if (name.Length > 1 && name[0] == 'V' && int.TryParse(name.AsSpan(1), out var idx) && idx is >= 1 and <= 28)
            return input.V[idx - 1];
        return 0f;
    }

    private static string ResolveModelPath(string? configured, string contentRoot)
    {
        if (string.IsNullOrWhiteSpace(configured))
            return "";
        return Path.IsPathRooted(configured)
            ? configured
            : Path.GetFullPath(Path.Combine(contentRoot, configured));
    }

    private static string MetadataPath(string modelPath)
    {
        var dir = Path.GetDirectoryName(modelPath)!;
        var baseName = Path.GetFileNameWithoutExtension(modelPath);
        return Path.Combine(dir, $"{baseName}-metadata.json");
    }
}
