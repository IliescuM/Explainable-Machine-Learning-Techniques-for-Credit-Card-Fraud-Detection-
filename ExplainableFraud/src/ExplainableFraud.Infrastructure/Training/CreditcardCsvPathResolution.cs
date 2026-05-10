using ExplainableFraud.Infrastructure.Options;
using Microsoft.Extensions.Hosting;

namespace ExplainableFraud.Infrastructure.Training;

/// <summary>
/// Builds ordered absolute paths for locating creditcard.csv from the API content root and app base directory,
/// including configured relatives and an optional walk up ancestor directories (repository-root style layouts).
/// </summary>
public static class CreditcardCsvPathResolution
{
    private const string DefaultFileName = "creditcard.csv";

    /// <summary>
    /// Ordered, de-duplicated candidate paths. Checks <paramref name="host"/> content root first, then <see cref="AppContext.BaseDirectory"/>.
    /// </summary>
    public static IReadOnlyList<string> BuildOrderedCandidatePaths(IHostEnvironment host, SimulatedTrainingJobOptions opts) =>
        BuildOrderedCandidatePaths(
            host,
            opts.CreditcardCsvCandidateRelativePaths,
            opts.CreditcardCsvParentDirectoryWalkDepth,
            DefaultFileName);

    /// <summary>Test hook: same logic with explicit relatives and walk depth.</summary>
    public static IReadOnlyList<string> BuildOrderedCandidatePaths(
        IHostEnvironment host,
        IReadOnlyList<string> relativePaths,
        int parentDirectoryWalkDepth,
        string fileName = DefaultFileName)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();

        void TryAdd(string absolute)
        {
            var full = Path.GetFullPath(absolute);
            if (seen.Add(full))
                list.Add(full);
        }

        var bases = new[] { host.ContentRootPath, AppContext.BaseDirectory }
            .Where(static s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var depth = Math.Clamp(parentDirectoryWalkDepth, 0, 64);
        var safeName = string.IsNullOrWhiteSpace(fileName) ? DefaultFileName : fileName.Trim();

        foreach (var root in bases)
        {
            var trimmed = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            foreach (var rel in relativePaths)
            {
                if (string.IsNullOrWhiteSpace(rel))
                    continue;

                TryAdd(Path.Combine(trimmed, rel));
            }

            var current = trimmed;
            for (var i = 0; i < depth; i++)
            {
                TryAdd(Path.Combine(current, safeName));
                var parent = Directory.GetParent(current);
                if (parent is null)
                    break;

                current = parent.FullName;
            }
        }

        return list;
    }
}
