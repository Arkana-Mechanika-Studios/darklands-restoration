using System.Text.RegularExpressions;

namespace Darklands.Core.Tests;

/// <summary>
/// The shared source map (docs/source-map/map.csv) stays honest: each original coordinate it names
/// exists in the original index (docs/source-map/original-functions.csv), is cited in the C# file
/// that defines the mapped type, and the row's status is the one the index gives it. Where the
/// machine-local mapping to the reference decompilation (local/reference-map.csv, git-ignored) is
/// present, the index agrees with it.
/// </summary>
public sealed partial class SourceMapTests
{
    private static readonly string Root = FindRepositoryRoot();

    /// <summary>A coordinate the index lists: RNNN+0xPPPP, root SSSS:OOOO, thunk 09C0:XXXX.</summary>
    [GeneratedRegex(@"^(?:R\d{3}\+0x[0-9A-F]{4,5}|root [0-9A-F]{4}:[0-9A-F]{4}|thunk 09C0:[0-9A-F]{4})$")]
    private static partial Regex IndexedCoordinate();

    [Fact]
    public void EveryMappedCoordinateIsKnownAndCitedInItsCode()
    {
        var known = Index().Keys.ToHashSet(StringComparer.Ordinal);
        var sources = Directory.GetFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(f => (Path: f, Text: File.ReadAllText(f)))
            .ToList();
        var problems = new List<string>();
        foreach (var row in MapRows())
        {
            var type = TypeName(row.CSharp);
            var files = sources.Where(s => s.Text.Contains($" class {type}", StringComparison.Ordinal)
                || s.Text.Contains($" record {type}", StringComparison.Ordinal)
                || s.Text.Contains($" struct {type}", StringComparison.Ordinal)
                || s.Text.Contains($" enum {type}", StringComparison.Ordinal)).ToList();
            if (files.Count == 0)
            {
                problems.Add($"{row.CSharp}: no C# type {type}");
                continue;
            }
            foreach (var original in row.Originals)
            {
                if (!known.Contains(original))
                {
                    problems.Add($"{row.CSharp}: {original} is not in original-functions.csv");
                }
                if (!files.Any(f => f.Text.Contains(original, StringComparison.Ordinal)))
                {
                    problems.Add($"{row.CSharp}: its code does not cite {original}");
                }
            }
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// A row about one original function states the status the index gives it: byte-matched C
    /// "matched", other C "unmatched", draft C "draft" (or "matched"/"unmatched" by the draft's own
    /// byte result, which the index notes), no C "asm".
    /// </summary>
    [Fact]
    public void EveryMappedStatusAgreesWithTheIndex()
    {
        var index = Index();
        var problems = new List<string>();
        foreach (var line in File.ReadLines(Path.Combine(Root, "docs", "source-map", "map.csv")).Skip(1).Where(l => l.Length > 0))
        {
            var fields = line.Split(',');
            var originals = Split(fields[1]).ToArray();   // a row about one function only (no data or driver part)
            var status = fields.Length > 3 ? fields[3].Trim() : "";
            if (originals.Length != 1 || !IndexedCoordinate().IsMatch(originals[0]) || status is not ("matched" or "unmatched" or "draft" or "asm")
                || !index.TryGetValue(originals[0], out var entry) || entry.Kind != "function")
            {
                continue;
            }
            var allowed = entry.Status switch
            {
                "draft" => entry.Note.Contains("draft bytes match", StringComparison.Ordinal) ? new[] { "draft", "matched" } : ["draft", "unmatched"],
                var s => [s],
            };
            if (!allowed.Contains(status))
            {
                problems.Add($"{fields[0]}: {originals[0]} is {entry.Status} in the index, the map says {status}");
            }
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>The index has one row per coordinate, of a known kind and status.</summary>
    [Fact]
    public void TheIndexIsKeyedByCoordinate()
    {
        var lines = File.ReadLines(IndexPath).ToList();
        Assert.Equal("coordinate,linked,size,kind,status,evidence,target,note", lines[0]);
        var problems = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (coordinate, entry) in Index())
        {
            if (!IndexedCoordinate().IsMatch(coordinate) || !seen.Add(coordinate))
            {
                problems.Add($"{coordinate}: not a coordinate, or repeated");
            }
            if (entry.Kind is not ("function" or "thunk" or "data" or "address") || entry.Status is not ("matched" or "unmatched" or "draft" or "asm" or ""))
            {
                problems.Add($"{coordinate}: kind '{entry.Kind}', status '{entry.Status}'");
            }
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// With the machine-local mapping present (local/reference-map.csv), the published index is exactly
    /// its neutral columns (tools/source-map/build_original_index.py); without it the test passes and says so.
    /// </summary>
    [Fact]
    public void TheIndexAgreesWithTheLocalMapping()
    {
        var mapping = Path.Combine(Root, "local", "reference-map.csv");
        if (!File.Exists(mapping))
        {
            Console.WriteLine("SKIPPED: local/reference-map.csv is absent (machine-local mapping to the reference decompilation)");
            return;
        }
        var local = File.ReadLines(mapping).Skip(1).Select(l => l.Split(',')[0]).ToList();
        var published = File.ReadLines(IndexPath).Skip(1).Select(l => l.Split(',')[0]).ToList();
        Assert.Equal(local, published);
    }

    private sealed record MapRow(string CSharp, string[] Originals);

    private sealed record Entry(string Kind, string Status, string Note);

    private static string IndexPath => Path.Combine(Root, "docs", "source-map", "original-functions.csv");

    private static Dictionary<string, Entry> Index()
    {
        var index = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(IndexPath).Skip(1).Where(l => l.Length > 0))
        {
            var f = line.Split(',', 8);
            index[f[0]] = new Entry(f[3], f[4], f.Length > 7 ? f[7] : "");
        }
        return index;
    }

    private static IEnumerable<string> Split(string cell) =>
        cell.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IEnumerable<MapRow> MapRows()
    {
        var path = Path.Combine(Root, "docs", "source-map", "map.csv");
        foreach (var line in File.ReadLines(path).Skip(1).Where(l => l.Length > 0))
        {
            var fields = line.Split(',');
            yield return new MapRow(fields[0].Trim(), Split(fields[1]).Where(o => IndexedCoordinate().IsMatch(o)).ToArray());
        }
    }

    /// <summary>`[Namespace.]Type` names a type; `[Namespace.]Type.Member()` a member of it.</summary>
    private static string TypeName(string symbol)
    {
        var parts = symbol.Split('.');
        return symbol.EndsWith("()", StringComparison.Ordinal) && parts.Length >= 2 ? parts[^2] : parts[^1];
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Darklands.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
