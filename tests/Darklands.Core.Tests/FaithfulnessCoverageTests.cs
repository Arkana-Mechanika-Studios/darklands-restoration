using System.Text.RegularExpressions;

namespace Darklands.Core.Tests;

/// <summary>
/// Nothing of the original's logic goes missing unnoticed. docs/faithfulness/coverage.csv
/// (tools/faithfulness/coverage.py) lists every original function, whether the game's entry
/// points reach it, and its status: implemented (cited by the C#), excluded (a reviewed row of
/// docs/faithfulness/exclusions.csv, or nothing reaches it) or MISSING. These tests re-check the
/// C# against it:
/// <list type="bullet">
/// <item>a reachable MISSING function must be on the shrinking allow-list
/// docs/faithfulness/missing-allowlist.txt;</item>
/// <item>the allow-list holds only functions that are still MISSING: once the C# cites one, it
/// must leave the list (and coverage.py be rerun);</item>
/// <item>a function the table counts as cited by its coordinate is still cited so in src/ or the
/// source map, and an exclusion the table applies is still in exclusions.csv.</item>
/// </list>
/// Functions are named by their coordinate (RNNN+0xPPPP, root SSSS:OOOO; docs/TRACEABILITY.md).
/// </summary>
public sealed partial class FaithfulnessCoverageTests
{
    private static readonly string Root = FindRepositoryRoot();
    private static readonly string Dir = Path.Combine(Root, "docs", "faithfulness");

    /// <summary>An entry coordinate as the C# cites it: RNNN+0xPPPP or root SSSS:OOOO.</summary>
    [GeneratedRegex(@"\bR\d{3}\+0x[0-9A-Fa-f]{1,5}\b|\broot [0-9A-Fa-f]{4}:[0-9A-Fa-f]{4}\b")]
    private static partial Regex Coordinate();

    private sealed record Row(string Function, string Linked, bool Reachable, string Status, string Via);

    [Fact]
    public void EveryReachableFunctionIsImplementedExcludedOrAllowListed()
    {
        var cited = CitedNow();
        var allow = AllowList();
        var problems = new List<string>();
        foreach (var row in Rows().Where(r => r.Reachable && r.Status == "MISSING"))
        {
            if (!cited.Contains(row.Function) && !allow.Contains(row.Function))
            {
                problems.Add($"{row.Function}: reachable original function neither implemented nor excluded (port it, or add a reviewed row to exclusions.csv)");
            }
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void TheAllowListOnlyShrinks()
    {
        var cited = CitedNow();
        // docs/faithfulness/not-ported.csv: named by the C#, behaviour not ported; they stay MISSING while listed there.
        cited.ExceptWith(File.ReadLines(Path.Combine(Dir, "not-ported.csv")).Skip(1).Select(l => l.Split(',')[0]));
        var rows = Rows().ToDictionary(r => r.Function, StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var name in AllowList())
        {
            if (cited.Contains(name))
            {
                problems.Add($"{name}: now cited by the C#: remove it from missing-allowlist.txt and rerun tools/faithfulness/coverage.py");
            }
            else if (!rows.TryGetValue(name, out var row))
            {
                problems.Add($"{name}: not an original function of coverage.csv");
            }
            else if (row.Status != "MISSING")
            {
                problems.Add($"{name}: {row.Status} in coverage.csv: remove it from missing-allowlist.txt");
            }
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void TheTableAgreesWithTheCodeAndTheExclusions()
    {
        var cited = CitedNow();
        var exclusions = File.ReadLines(Path.Combine(Dir, "exclusions.csv")).Skip(1)
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(l => l.Split(','))
            .ToDictionary(f => f[0], f => f[1], StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var row in Rows())
        {
            if (row.Status == "implemented" && row.Via == "cite:coordinate" &&!cited.Contains(row.Function))
            {
                problems.Add($"{row.Function}: coverage.csv counts it as cited, the C# no longer names it (rerun tools/faithfulness/coverage.py)");
            }
            if (row.Status.StartsWith("excluded:", StringComparison.Ordinal) && row.Via.Length > 0)
            {
                var reason = row.Status["excluded:".Length..];
                if (!exclusions.TryGetValue(row.Via, out var listed) || listed != reason || !Matches(row.Via, row.Function, row.Linked))
                {
                    problems.Add($"{row.Function}: excluded by '{row.Via}' ({reason}), which exclusions.csv no longer has (rerun tools/faithfulness/coverage.py)");
                }
            }
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void EveryExclusionSaysWhy()
    {
        var problems = new List<string>();
        foreach (var line in File.ReadLines(Path.Combine(Dir, "exclusions.csv")).Skip(1).Where(l => l.Length > 0 && !l.StartsWith('#')))
        {
            var fields = line.Split(',', 3);
            if (fields.Length < 3 || fields[1].Length == 0 || fields[2].Trim('"').Length < 20)
            {
                problems.Add($"exclusions.csv: '{line}' needs a pattern, a reason and a justification");
            }
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// An exclusions.csv pattern (`*` matches anything) against `root SSSS:OOOO`, or against
    /// `RNNN+0xPPPP (SSSS:OOOO)` for an overlay function; an exact coordinate matches too.
    /// </summary>
    private static bool Matches(string pattern, string coordinate, string linked)
    {
        var key = coordinate.StartsWith("root", StringComparison.Ordinal) ? coordinate : $"{coordinate} ({linked})";
        var regex = "^" + string.Join(".*", pattern.Split('*').Select(Regex.Escape)) + "$";
        return pattern == coordinate || Regex.IsMatch(key, regex);
    }

    private static IEnumerable<Row> Rows()
    {
        var lines = File.ReadLines(Path.Combine(Dir, "coverage.csv")).ToList();
        var header = lines[0].Split(',');
        int Col(string name) => Array.IndexOf(header, name);
        int fn = Col("coordinate"), linked = Col("linked"), reach = Col("reachable"), status = Col("status"), via = Col("via");
        foreach (var line in lines.Skip(1).Where(l => l.Length > 0))
        {
            var f = line.Split(',');
            yield return new Row(f[fn], f[linked], f[reach] == "yes", f[status], f[via]);
        }
    }

    private static HashSet<string> AllowList() =>
        File.ReadLines(Path.Combine(Dir, "missing-allowlist.txt"))
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Phrases that make a comment line a mention, not a trace (the line says the function is not
    /// ported); the same list as tools/faithfulness/coverage.py's NEGATIVE.
    /// </summary>
    private static readonly string[] Negative =
        ["not written", "not yet", "not ported", "not implemented", "not reproduced", "not modelled", "not modeled", "not needed"];

    /// <summary>
    /// The original coordinates the C# cites now (src/**/*.cs, lines that do not say "not ported", and the
    /// originals and notes of the source map, not its own coordinate column).
    /// </summary>
    private static HashSet<string> CitedNow()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        var sep = Path.DirectorySeparatorChar;
        var files = Directory.GetFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}", StringComparison.Ordinal) && !f.Contains($"{sep}bin{sep}", StringComparison.Ordinal));
        foreach (var file in files)
        {
            foreach (var line in File.ReadLines(file))
            {
                var low = line.ToLowerInvariant();
                if (Negative.Any(p => low.Contains(p, StringComparison.Ordinal)))
                {
                    continue;
                }
                foreach (Match m in Coordinate().Matches(line))
                {
                    set.Add(Normal(m.Value));
                }
            }
        }
        foreach (var line in File.ReadLines(Path.Combine(Root, "docs", "source-map", "map.csv")).Skip(1))
        {
            var fields = line.Split(',');
            var text = fields.Length > 1 ? fields[1] + " " + string.Join(",", fields.Skip(5)) : "";
            foreach (Match m in Coordinate().Matches(text))
            {
                set.Add(Normal(m.Value));
            }
        }
        return set;
    }

    /// <summary>A coordinate as coverage.csv writes it: RNNN+0x%04X, root SSSS:OOOO in upper case.</summary>
    private static string Normal(string coordinate) => coordinate.StartsWith("root", StringComparison.Ordinal)
        ? "root " + coordinate[5..].ToUpperInvariant()
        : $"{coordinate[..4]}+0x{Convert.ToInt32(coordinate[7..], 16):X4}";

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
