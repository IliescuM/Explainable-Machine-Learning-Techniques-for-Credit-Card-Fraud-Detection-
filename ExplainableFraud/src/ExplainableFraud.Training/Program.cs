using System.Globalization;
using System.Text.Json;
using ExplainableFraud.Contracts.Fraud;
using ExplainableFraud.Infrastructure.Ml;
using Microsoft.ML;
using Microsoft.ML.Trainers.FastTree;

var slotNames = new[]
{
    "Time",
    "V1", "V2", "V3", "V4", "V5", "V6", "V7", "V8", "V9", "V10",
    "V11", "V12", "V13", "V14", "V15", "V16", "V17", "V18", "V19", "V20",
    "V21", "V22", "V23", "V24", "V25", "V26", "V27", "V28",
    "Amount"
};

var argv = Environment.GetCommandLineArgs().Skip(1).ToArray();
string? dataPath = null;
var outDir = Path.Combine(Environment.CurrentDirectory, "artifacts");
int? maxRows = null;
for (var i = 0; i < argv.Length; i++)
{
    if (argv[i] == "--data" && i + 1 < argv.Length)
        dataPath = argv[++i];
    else if (argv[i] == "--out" && i + 1 < argv.Length)
        outDir = Path.GetFullPath(argv[++i]);
    else if (argv[i] == "--max-rows" && i + 1 < argv.Length && int.TryParse(argv[++i], out var mr))
        maxRows = mr;
}

if (argv.Contains("--help") || argv.Contains("-h"))
{
    Console.WriteLine("""
        Train FastTree binary classifier on Kaggle-style creditcard.csv (Time, V1..V28, Amount, Class).

        Download: https://www.kaggle.com/datasets/mlg-ulb/creditcardfraud

        Usage:
          dotnet run --project ExplainableFraud.Training -- --data <path-to-creditcard.csv> [--out <dir>] [--max-rows N]

        Writes fraud-model.zip and fraud-model-metadata.json into --out (default: ./artifacts).
        Copy both files to ExplainableFraud.Api/Models/ and set MlPipeline:ModelPath to Models/fraud-model.zip
        """);
    return;
}

if (string.IsNullOrWhiteSpace(dataPath) || !File.Exists(dataPath))
{
    Console.Error.WriteLine("Provide an existing file: --data <creditcard.csv>");
    Environment.Exit(1);
}

Directory.CreateDirectory(outDir);
var modelZip = Path.Combine(outDir, "fraud-model.zip");
var metaJson = Path.Combine(outDir, "fraud-model-metadata.json");

Console.WriteLine($"Loading CSV (max rows: {maxRows?.ToString() ?? "all"})...");
var rows = LoadRows(dataPath, maxRows);
Console.WriteLine($"Rows: {rows.Count}");

var rnd = new Random(42);
rows = rows.OrderBy(_ => rnd.Next()).ToList();

var ml = new MLContext(seed: 42);
var fullData = ml.Data.LoadFromEnumerable(rows);
var split = ml.Data.TrainTestSplit(fullData, testFraction: 0.2);

var trainer = ml.BinaryClassification.Trainers.FastTree(new FastTreeBinaryTrainer.Options
{
    NumberOfLeaves = 56,
    NumberOfTrees = 100,
    MinimumExampleCountPerLeaf = 10,
    Shrinkage = 0.15f,
    UnbalancedSets = true,
    FeatureFirstUsePenalty = 0f,
});

var pipeline = ml.Transforms.Concatenate("Features",
        nameof(FraudMlInput.Time),
        nameof(FraudMlInput.V),
        nameof(FraudMlInput.Amount))
    .Append(trainer);

Console.WriteLine("Training...");
var model = pipeline.Fit(split.TrainSet);

Console.WriteLine("Evaluating...");
var predictions = model.Transform(split.TestSet);
var metrics = ml.BinaryClassification.Evaluate(predictions, labelColumnName: nameof(FraudTrainingRow.Label));

var trainEnum = ml.Data.CreateEnumerable<FraudTrainingRow>(split.TrainSet, reuseRowObject: false).ToList();
var metadata = new FraudModelMetadata
{
    ModelVersionLabel = "fasttree-kaggle-creditcard-v1",
    Metrics = new ModelValidationMetricsDto
    {
        AreaUnderRocCurve = metrics.AreaUnderRocCurve,
        F1Score = metrics.F1Score,
        TrainRows = trainEnum.Count,
        TestRows = (int)(split.TestSet.GetRowCount() ?? 0L)
    }
};

FillDatasetStats(trainEnum, metadata, slotNames);
Console.WriteLine("Computing global feature ranking (Pearson |r| vs label, train split)...");
FillGlobalImportanceFromCorrelations(trainEnum, slotNames, metadata);

ml.Model.Save(model, split.TrainSet.Schema, modelZip);

var jsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
await File.WriteAllTextAsync(metaJson, JsonSerializer.Serialize(metadata, jsonOptions));

Console.WriteLine($"AUC: {metrics.AreaUnderRocCurve:F4}, F1: {metrics.F1Score:F4}");
Console.WriteLine($"Saved model: {modelZip}");
Console.WriteLine($"Saved metadata: {metaJson}");

static List<FraudTrainingRow> LoadRows(string path, int? maxRows)
{
    var list = new List<FraudTrainingRow>();
    using var reader = new StreamReader(path);
    reader.ReadLine(); // header
    string? line;
    while ((line = reader.ReadLine()) != null)
    {
        if (maxRows is { } m && list.Count >= m)
            break;

        var parts = line.Split(',');
        if (parts.Length < 31)
            continue;

        var row = new FraudTrainingRow
        {
            Time = float.Parse(parts[0], CultureInfo.InvariantCulture),
            Amount = float.Parse(parts[29], CultureInfo.InvariantCulture),
            Label = parts[30].Trim() == "1",
            V = new float[28]
        };
        for (var j = 0; j < 28; j++)
            row.V[j] = float.Parse(parts[1 + j], CultureInfo.InvariantCulture);

        list.Add(row);
    }

    return list;
}

static void FillDatasetStats(IReadOnlyList<FraudTrainingRow> trainRows, FraudModelMetadata metadata, string[] slotNames)
{
    foreach (var name in slotNames)
    {
        var values = trainRows.Select(r => GetFeature(r, name)).ToArray();
        metadata.FeatureMeans[name] = values.Length == 0 ? 0f : values.Average();
        metadata.FeatureStds[name] = StdDev(values);
    }
}

static float GetFeature(FraudTrainingRow row, string name)
{
    if (name == "Time")
        return row.Time;
    if (name == "Amount")
        return row.Amount;
    if (name.Length > 1 && name[0] == 'V' && int.TryParse(name.AsSpan(1), out var idx) && idx is >= 1 and <= 28)
        return row.V[idx - 1];
    return 0f;
}

static float StdDev(float[] values)
{
    if (values.Length == 0)
        return 1f;
    var mean = values.Average();
    var sum = values.Sum(v => (v - mean) * (v - mean));
    return MathF.Max(1e-6f, MathF.Sqrt((float)(sum / values.Length)));
}

static void FillGlobalImportanceFromCorrelations(IReadOnlyList<FraudTrainingRow> rows, string[] slotNames, FraudModelMetadata metadata)
{
    var labels = rows.Select(r => r.Label ? 1f : 0f).ToArray();
    foreach (var name in slotNames)
    {
        var xs = rows.Select(r => GetFeature(r, name)).ToArray();
        var r = MathF.Abs(Pearson(xs, labels));
        metadata.GlobalImportance.Add(new FeatureImportanceEntry { Name = name, Importance = float.IsNaN(r) ? 0f : r });
    }
}

static float Pearson(float[] x, float[] y)
{
    if (x.Length == 0 || x.Length != y.Length)
        return 0f;
    var mx = x.Average();
    var my = y.Average();
    double num = 0, dx = 0, dy = 0;
    for (var i = 0; i < x.Length; i++)
    {
        var vx = x[i] - mx;
        var vy = y[i] - my;
        num += vx * vy;
        dx += vx * vx;
        dy += vy * vy;
    }

    var den = Math.Sqrt(dx * dy);
    return den < 1e-12 ? 0f : (float)(num / den);
}

public sealed class FraudTrainingRow : FraudMlInput
{
    public bool Label { get; set; }
}
