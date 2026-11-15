using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Darklands.Core.Tests;

/// <summary>
/// The switch register (docs/faithfulness/switches.csv, explained in docs/faithfulness/SWITCHES.md) stays complete and true:
/// every <see cref="OriginalBehaviour"/> fix flag and every behaviour-changing settings key is in it, and every row names
/// something that exists. A new switch in the code fails here until it has a row; a row left behind by a removed switch fails too.
/// </summary>
public sealed class SwitchRegisterTests
{
    private static readonly string Root = FindRepositoryRoot();
    private static readonly string Docs = Path.Combine(Root, "docs", "faithfulness");

    private static readonly HashSet<string> Classes = ["data-safety", "visual", "gameplay", "restoration", "interface-comfort", "new-capability", "presentation", "content-mod"];
    private static readonly HashSet<string> Axes = ["Rules", "Presentation", "Interface"];
    private static readonly HashSet<string> Presets = ["classic", "fixed", "restored", "any"];

    /// <summary>Keys of docs/SETTINGS.md that are not switches (paths, the window, the hardware, the start-up, the developer console): each needs a reason to stay out of the register.</summary>
    private static readonly HashSet<string> NotSwitches = new(StringComparer.Ordinal)
    {
        "gameFolder", "savesFolder", "screenshotsFolder", "textFont", "sdl.library",   // where things are
        "display.scale", "display.resolution", "display.fullscreen",                      // the window
        "sound.card", "sound.mt32Roms", "sound.soundFont", "sound.configDrk",             // the player's sound hardware, as CONFIG.DRK has it
        "startup.banner", "startup.intro", "startup.commandLine",                         // the game's own start-up and command line
        "console.enabled", "console.font", "console.fontSize",                            // the developer console
    };

    [Fact]
    public void RowsAreWellFormed()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in Rows())
        {
            var id = row["id"];
            Assert.Matches(@"^[a-z]+(\.[a-z0-9]+(-[a-z0-9]+)*){1,3}$", id);
            Assert.True(ids.Add(id), $"duplicate id {id}");
            Assert.Contains(row["kind"], new[] { "flag", "setting" });
            Assert.True(Classes.Contains(row["class"]), $"{id}: unknown class '{row["class"]}'");
            Assert.True(Axes.Contains(row["axis"]), $"{id}: unknown axis '{row["axis"]}'");
            var presets = row["presets"].Split(';');
            Assert.All(presets, p => Assert.True(Presets.Contains(p), $"{id}: unknown preset '{p}'"));
            Assert.True(row["description"].Length >= 20, $"{id}: the description is empty or too short");
            Assert.True(row["key"].Length > 0, $"{id}: no key");
            foreach (var file in row["code"].Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                Assert.True(File.Exists(Path.Combine(Root, file)), $"{id}: code location {file} does not exist");
            }
            Assert.True(row["code"].Length > 0, $"{id}: no code location");
        }
    }

    [Fact]
    public void EveryFixFlagIsInTheRegisterAndNothingElse()
    {
        var inCode = typeof(OriginalBehaviour).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(bool) && p.CanWrite && p.GetSetMethod() is not null)
            .Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var inRegister = Rows().Where(r => r["kind"] == "flag").Select(r => r["key"]).ToList();
        Assert.Equal(inRegister.Count, inRegister.Distinct().Count());
        var missing = inCode.Except(inRegister).Order().ToList();
        var stale = inRegister.Except(inCode).Order().ToList();
        Assert.True(missing.Count == 0, "flags of OriginalBehaviour without a row in switches.csv: " + string.Join(", ", missing));
        Assert.True(stale.Count == 0, "rows of switches.csv naming no flag of OriginalBehaviour: " + string.Join(", ", stale));
    }

    /// <summary>The default column of a flag is its state in <see cref="OriginalBehaviour.DataSafe"/>, the set the player's game plays with; the presets follow its class.</summary>
    [Fact]
    public void FlagDefaultsAndPresetsFollowTheirClass()
    {
        var dataSafe = OriginalBehaviour.DataSafe;
        var problems = new List<string>();
        foreach (var row in Rows().Where(r => r["kind"] == "flag"))
        {
            var property = typeof(OriginalBehaviour).GetProperty(row["key"])!;
            var on = (bool)property.GetValue(dataSafe)!;
            if (row["default"] != (on ? "on" : "off"))
            {
                problems.Add($"{row["id"]}: default '{row["default"]}' but DataSafe has it {(on ? "on" : "off")}");
            }
            var everywhere = row["class"] is "data-safety" or "visual";
            if (everywhere != row["presets"].Split(';').Contains("classic"))
            {
                problems.Add($"{row["id"]}: class {row["class"]} and presets '{row["presets"]}' disagree about Classic (A4: only data-safety and visual fixes are on in Classic)");
            }
            if (everywhere && !on)
            {
                problems.Add($"{row["id"]}: a {row["class"]} fix that DataSafe leaves off");
            }
            if (!row["presets"].Split(';').Contains("fixed") || !row["presets"].Split(';').Contains("restored"))
            {
                problems.Add($"{row["id"]}: a fix is on in Fixed and Restored too: '{row["presets"]}'");
            }
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>The register ids of a flag are the original-bugs entries that name it as their fix flag, no more and no fewer.</summary>
    [Fact]
    public void RegisterIdsAgreeWithTheOriginalBugsRegister()
    {
        var bugs = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var bug in ReadCsv(Path.Combine(Docs, "original_bugs.csv")))
        {
            foreach (var flag in bug["fix_flag"].Split(';', StringSplitOptions.RemoveEmptyEntries).Where(f => f != "none"))
            {
                bugs.TryAdd(flag, new HashSet<string>(StringComparer.Ordinal));
                bugs[flag].Add(bug["id"]);
            }
        }
        var problems = new List<string>();
        foreach (var row in Rows().Where(r => r["kind"] == "flag"))
        {
            var listed = row["register"].Split(';', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
            var expected = bugs.GetValueOrDefault(row["key"]) ?? [];
            if (!listed.SetEquals(expected))
            {
                problems.Add($"{row["id"]} ({row["key"]}): register ids '{string.Join(';', listed.Order())}' but original_bugs.csv names '{string.Join(';', expected.Order())}'");
            }
        }
        foreach (var flag in bugs.Keys.Except(Rows().Where(r => r["kind"] == "flag").Select(r => r["key"])))
        {
            problems.Add($"original_bugs.csv names the fix flag {flag}, which has no row");
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>A flag's code locations really use it (by its name, or for the item-store flags through the derived CompactCache).</summary>
    [Fact]
    public void FlagCodeLocationsMentionTheFlag()
    {
        var problems = new List<string>();
        foreach (var row in Rows().Where(r => r["kind"] == "flag"))
        {
            var names = new List<string> { row["key"] };
            if (row["key"] is "FixInnCacheRewrite" or "FixCacheStoreLimit")
            {
                names.Add("CompactCache");
            }
            var files = row["code"].Split(';', StringSplitOptions.RemoveEmptyEntries);
            if (!files.Any(f => names.Any(n => File.ReadAllText(Path.Combine(Root, f)).Contains(n, StringComparison.Ordinal))))
            {
                problems.Add($"{row["id"]}: none of {string.Join(", ", files)} mentions {string.Join(" or ", names)}");
            }
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>Every key of docs/SETTINGS.md is either a row of the register or on the list of keys that are not switches, and every setting row is a key of that file.</summary>
    [Fact]
    public void EverySettingsKeyIsDecided()
    {
        var table = File.ReadAllLines(Path.Combine(Root, "docs", "SETTINGS.md"))
            .SkipWhile(l => l != "## Keys").Skip(1).TakeWhile(l => !l.StartsWith("### ", StringComparison.Ordinal))
            .Select(l => Regex.Match(l, @"^\| `([A-Za-z0-9.]+)` \|")).Where(m => m.Success).Select(m => m.Groups[1].Value).ToList();
        Assert.True(table.Count > 20, "the keys table of docs/SETTINGS.md was not read");
        var rows = Rows().Where(r => r["kind"] == "setting").Select(r => r["key"]).ToHashSet(StringComparer.Ordinal);
        var undecided = table.Where(k => !rows.Contains(k) && !NotSwitches.Contains(k)).ToList();
        Assert.True(undecided.Count == 0, "keys of docs/SETTINGS.md in neither switches.csv nor the list of non-switches: " + string.Join(", ", undecided));
        var unknown = rows.Except(table).Concat(NotSwitches.Except(table)).ToList();
        Assert.True(unknown.Count == 0, "keys in the register or the non-switch list that docs/SETTINGS.md does not have: " + string.Join(", ", unknown));
        Assert.Empty(rows.Intersect(NotSwitches));
    }

    private static List<Dictionary<string, string>> Rows() => ReadCsv(Path.Combine(Docs, "switches.csv"));

    /// <summary>A small CSV reader: quoted cells may hold commas and doubled quotes; no cell of these files holds a line break.</summary>
    private static List<Dictionary<string, string>> ReadCsv(string path)
    {
        var lines = File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var header = Split(lines[0]);
        return lines.Skip(1).Select(Split).Where(r => r.Count == header.Count)
            .Select(r => header.Zip(r).ToDictionary(p => p.First, p => p.Second, StringComparer.Ordinal)).ToList();
    }

    private static List<string> Split(string line)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted && c == '"' && i + 1 < line.Length && line[i + 1] == '"')
            {
                cell.Append('"');
                i++;
            }
            else if (c == '"')
            {
                quoted = !quoted;
            }
            else if (c == ',' && !quoted)
            {
                cells.Add(cell.ToString());
                cell.Clear();
            }
            else
            {
                cell.Append(c);
            }
        }
        cells.Add(cell.ToString());
        return cells;
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
