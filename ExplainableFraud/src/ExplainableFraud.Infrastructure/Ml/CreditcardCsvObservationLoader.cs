using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace ExplainableFraud.Infrastructure.Ml;

/// <summary>Loads Kaggle ULB-shaped creditcard.csv (comma-separated, header row).</summary>
public static class CreditcardCsvObservationLoader
{
    public const int MinimumUsefulRows = 128;

    /// <summary>Returns null when the path is missing/unreadable or fewer than <see cref="MinimumUsefulRows"/> usable rows parsed.</summary>
    public static bool TryLoad(string path, int maxRows, int stratifiedSamplingSeed,
        [NotNullWhen(true)] out IReadOnlyList<FraudTrainingObservation>? observations,
        out string loadDetail)
    {
        observations = null;
        loadDetail = "";

        if (maxRows < MinimumUsefulRows || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        try
        {
            var rows = ParseEntireFile(path);
            loadDetail =
                $"{Path.GetFileName(path)}: parsed {rows.Count:N0} valid data rows";

            if (rows.Count < MinimumUsefulRows)
                return false;

            List<FraudTrainingObservation> effective;
            if (rows.Count > maxRows)
            {
                effective = StratifiedSubset(rows, maxRows, stratifiedSamplingSeed ^ 911382323);
                var pos = effective.Count(static r => r.Label);
                loadDetail +=
                    $" · stratified subset to target cap {maxRows:N0}: using {effective.Count:N0} rows ({pos:N0} positives, {effective.Count - pos:N0} negatives); all fraud rows retained when feasible";
            }
            else
                effective = rows;

            observations = effective;
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Fast row estimate for API metadata: counts non-empty data lines after a Kaggle-style header (no full parse).
    /// </summary>
    public static bool TryEstimateDataRowCount(string path, out int dataRowCount)
    {
        dataRowCount = 0;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        try
        {
            using var reader = new StreamReader(path);
            var headerLine = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(headerLine))
                return false;

            if (headerLine.Length > 0 && headerLine[0] == '\uFEFF')
                headerLine = headerLine.TrimStart('\uFEFF');

            var headerCols = SplitCsv(headerLine).Select(static c => NormalizeHeader(c)).ToArray();
            var hasTime = headerCols.Any(static h => h.Equals("Time", StringComparison.OrdinalIgnoreCase));
            var hasClass = headerCols.Any(static h => h.Equals("Class", StringComparison.OrdinalIgnoreCase));
            if (!TryInferLayout(headerCols, out _) && !(hasTime && hasClass))
                return false;

            var n = 0;
            while (reader.ReadLine() is { } line)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    n++;
            }

            dataRowCount = n;
            return n > 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static List<FraudTrainingObservation> ParseEntireFile(string path)
    {
        var list = new List<FraudTrainingObservation>(capacity: 50_000);

        using var reader = new StreamReader(path);
        var headerLine = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(headerLine))
            return list;

        if (headerLine.Length > 0 && headerLine[0] == '\uFEFF')
            headerLine = headerLine.TrimStart('\uFEFF');

        var headerCols = SplitCsv(headerLine).Select(static c => NormalizeHeader(c)).ToArray();

        if (!TryInferLayout(headerCols, out var layout))
            layout = Layout.FixedKaggleOrdering();

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var parts = SplitCsv(line);
            if (!TryParseRow(parts, layout, out var row))
                continue;

            list.Add(row);
        }

        return list;
    }

    /// <remarks>
    /// Prefers retaining every fraud-positive row unless the fraud count exceeds maxRows − 2.
    /// Always keeps at least one negative so binary training remains feasible.
    /// </remarks>
    private static List<FraudTrainingObservation> StratifiedSubset(
        IReadOnlyList<FraudTrainingObservation> rows,
        int targetCount,
        int seed)
    {
        if (rows.Count <= targetCount)
            return rows.ToList();

        var positives = rows.Where(static r => r.Label).ToList();
        var negatives = rows.Where(static r => !r.Label).ToList();
        var rng = new Random(seed);

        Shuffle(positives, rng);
        Shuffle(negatives, rng);

        if (positives.Count + 2 > targetCount)
        {
            var posTake = Math.Max(2, Math.Min(positives.Count, targetCount - 2));
            var negTake = targetCount - posTake;
            if (negTake < 2)
                negTake = 2;

            posTake = targetCount - negTake;

            positives = positives.Take(posTake).ToList();
            negatives = negatives.Take(negTake).ToList();
        }
        else
        {
            var negTake = targetCount - positives.Count;
            negatives = negatives.Take(negTake).ToList();
        }

        var merged = new List<FraudTrainingObservation>(positives.Count + negatives.Count);
        merged.AddRange(positives);
        merged.AddRange(negatives);
        Shuffle(merged, rng);
        return merged;
    }

    private static void Shuffle(IList<FraudTrainingObservation> list, Random rng)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static string NormalizeHeader(string h) => NormalizeCsvToken(h);

    /// <summary>Trims whitespace/BOM and removes a single pair of surrounding ASCII quotes (Excel/Kaggle export style).</summary>
    private static string NormalizeCsvToken(string token)
    {
        var t = token.Trim().TrimStart('\uFEFF');
        if (t.Length >= 2 && t[0] == '"' && t[^1] == '"')
            return t[1..^1].Trim();
        return t;
    }

    private static bool TryInferLayout(string[] headerCols, out Layout layout)
    {
        layout = default;

        var classIdx = IndexOfHeader(headerCols, "Class");
        var amountIdx = IndexOfHeader(headerCols, "Amount");
        var timeIdx = IndexOfHeader(headerCols, "Time");
        if (classIdx < 0 || amountIdx < 0 || timeIdx < 0)
            return false;

        var vIdx = new int[28];
        for (var k = 0; k < 28; k++)
        {
            var name = k == 0 ? "V1" : $"V{k + 1}";
            var idx = IndexOfHeader(headerCols, name);
            if (idx < 0)
                return false;
            vIdx[k] = idx;
        }

        layout = new Layout(timeIdx, vIdx, amountIdx, classIdx);
        return true;
    }

    private static int IndexOfHeader(string[] headerCols, string name)
    {
        for (var i = 0; i < headerCols.Length; i++)
        {
            if (string.Equals(headerCols[i], name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static string[] SplitCsv(string line) => line.Split(',');

    private static bool TryParseRow(string[] parts, Layout layout, out FraudTrainingObservation row)
    {
        row = null!;
        if (parts.Length <= layout.MaxColumnIndex)
            return false;

        if (!float.TryParse(NormalizeCsvToken(parts[layout.Time]), NumberStyles.Float, CultureInfo.InvariantCulture, out var time))
            return false;

        if (!float.TryParse(NormalizeCsvToken(parts[layout.Amount]), NumberStyles.Float, CultureInfo.InvariantCulture, out var amount))
            return false;

        if (!TryParseBinaryLabel(parts[layout.Class], out var label))
            return false;

        var v = new float[28];
        for (var j = 0; j < 28; j++)
        {
            if (!float.TryParse(NormalizeCsvToken(parts[layout.V[j]]), NumberStyles.Float, CultureInfo.InvariantCulture, out v[j]))
                return false;
        }

        row = new FraudTrainingObservation
        {
            Time = time,
            Amount = amount,
            Label = label,
            V = v
        };
        return true;
    }

    private static bool TryParseBinaryLabel(string cell, out bool isPositive)
    {
        isPositive = false;
        var t = NormalizeCsvToken(cell);
        if (t.Length == 0)
            return false;

        if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var k))
        {
            if (k == 1)
            {
                isPositive = true;
                return true;
            }

            if (k == 0)
            {
                isPositive = false;
                return true;
            }

            return false;
        }

        if (bool.TryParse(t, out var b))
        {
            isPositive = b;
            return true;
        }

        if (string.Equals(t, "True", StringComparison.OrdinalIgnoreCase))
        {
            isPositive = true;
            return true;
        }

        if (string.Equals(t, "False", StringComparison.OrdinalIgnoreCase))
        {
            isPositive = false;
            return true;
        }

        return false;
    }

    private readonly struct Layout
    {
        public Layout(int time, int[] v, int amount, int @class)
        {
            Time = time;
            V = v;
            Amount = amount;
            Class = @class;
            MaxColumnIndex = new[] { time, amount, @class }.Concat(v).Max();
        }

        public int Time { get; }

        public int[] V { get; }

        public int Amount { get; }

        public int Class { get; }

        public int MaxColumnIndex { get; }

        public static Layout FixedKaggleOrdering() =>
            new(
                time: 0,
                v: Enumerable.Range(0, 28).Select(i => i + 1).ToArray(),
                amount: 29,
                @class: 30);
    }
}
