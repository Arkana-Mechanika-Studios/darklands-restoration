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
    private static readonly HashSet<string> Presets = ["original", "fixed", "restored", "any"];

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
            Assert.Contains(row["kind"], new[] { "flag", "setting", "planned" });
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

    /// <summary>
    /// The default column of a flag is its state in a new game at the default preset (<see cref="RulePresets.Default"/>, Fixed); the
    /// presets follow its class, and the data-safety and visual fixes are on in <see cref="OriginalBehaviour.DataSafe"/> (Original) too.
    /// </summary>
    [Fact]
    public void FlagDefaultsAndPresetsFollowTheirClass()
    {
        var newGame = OriginalBehaviour.Of(RulePresets.Default);
        var dataSafe = OriginalBehaviour.DataSafe;
        var problems = new List<string>();
        foreach (var row in Rows().Where(r => r["kind"] == "flag"))
        {
            var property = typeof(OriginalBehaviour).GetProperty(row["key"])!;
            var inNewGame = (bool)property.GetValue(newGame)!;
            if (row["default"] != (inNewGame ? "on" : "off"))
            {
                problems.Add($"{row["id"]}: default '{row["default"]}' but a new game ({RulePresets.Default}) has it {(inNewGame ? "on" : "off")}");
            }
            var on = (bool)property.GetValue(dataSafe)!;
            var everywhere = row["class"] is "data-safety" or "visual";
            if (everywhere != row["presets"].Split(';').Contains("original"))
            {
                problems.Add($"{row["id"]}: class {row["class"]} and presets '{row["presets"]}' disagree about Original (A4: only data-safety and visual fixes are on in Original)");
            }
            if (everywhere && !on)
            {
                problems.Add($"{row["id"]}: a {row["class"]} fix that DataSafe leaves off");
            }
            if (row["class"] == "restoration")
            {
                // a restoration is in the Restored rule set only (restoration.csv), never in Original or Fixed
                if (row["presets"] != "restored")
                {
                    problems.Add($"{row["id"]}: a restoration is on in Restored only: '{row["presets"]}'");
                }
            }
            else if (!row["presets"].Split(';').Contains("fixed") || !row["presets"].Split(';').Contains("restored"))
            {
                problems.Add($"{row["id"]}: a fix is on in Fixed and Restored too: '{row["presets"]}'");
            }
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// The rule table (RuleTable.Generated.cs) is the CSV's flag rows, in order: id, property, class and presets (the column's
    /// "original" is the Original preset); each property's index constant is its place. Run tools/faithfulness/rule_table.py after
    /// changing a flag row.
    /// </summary>
    [Fact]
    public void TheRuleTableIsGeneratedFromTheRegister()
    {
        var rows = Rows().Where(r => r["kind"] == "flag").ToList();
        Assert.Equal(rows.Count, RuleTable.Flags.Count);
        var problems = new List<string>();
        for (var i = 0; i < rows.Count; i++)
        {
            var (row, rule) = (rows[i], RuleTable.Flags[i]);
            var presets = row["presets"].Split(';');
            var expected = new RuleTable.Rule(row["id"], row["key"], row["class"], presets.Contains("original"), presets.Contains("fixed"), presets.Contains("restored"));
            if (rule != expected)
            {
                problems.Add($"row {i}: the table has {rule}, the CSV {expected}");
            }
            var index = typeof(RuleTable).GetField(row["key"], BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue();
            if (index is not int at || at != i)
            {
                problems.Add($"{row["id"]}: RuleTable.{row["key"]} is {index ?? "missing"}, not {i}");
            }
            if (RuleTable.IndexOf(row["id"]) != i)
            {
                problems.Add($"{row["id"]}: IndexOf gives {RuleTable.IndexOf(row["id"])}, not {i}");
            }
        }
        Assert.True(problems.Count == 0, "RuleTable.Generated.cs differs from switches.csv (run tools/faithfulness/rule_table.py):" + Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    /// <summary>Each preset turns on exactly the flags whose presets column names it; Faithful turns none on; DataSafe is Original.</summary>
    [Fact]
    public void PresetsComeFromTheRegister()
    {
        var rows = Rows().Where(r => r["kind"] == "flag").ToList();
        var problems = new List<string>();
        foreach (var (preset, word) in new[] { (RulePreset.Faithful, "(none)"), (RulePreset.Original, "original"), (RulePreset.Fixed, "fixed"), (RulePreset.Restored, "restored") })
        {
            var behaviour = OriginalBehaviour.Of(preset);
            foreach (var row in rows)
            {
                var on = (bool)typeof(OriginalBehaviour).GetProperty(row["key"])!.GetValue(behaviour)!;
                if (on != row["presets"].Split(';').Contains(word))
                {
                    problems.Add($"{preset}: {row["id"]} is {(on ? "on" : "off")}, presets '{row["presets"]}'");
                }
                if (on != behaviour.IsOn(row["id"]))
                {
                    problems.Add($"{preset}: {row["id"]}: the property and IsOn disagree");
                }
            }
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        Assert.Empty(OriginalBehaviour.Faithful.On);
        Assert.Equal(OriginalBehaviour.Original.On, OriginalBehaviour.DataSafe.On);
        Assert.Equal(31, OriginalBehaviour.DataSafe.On.Count());
    }

    /// <summary>Every gameplay flag is on in Fixed (and Restored) and none is on in Original; the data-safety and visual flags are on in every player set.</summary>
    [Fact]
    public void EveryGameplayFlagIsOnInFixedAndNoneInOriginal()
    {
        var (original, fixedSet, restored) = (OriginalBehaviour.Of(RulePreset.Original), OriginalBehaviour.Of(RulePreset.Fixed), OriginalBehaviour.Of(RulePreset.Restored));
        var gameplay = RuleTable.Flags.Where(r => r.Class == "gameplay").ToList();
        Assert.Equal(30, gameplay.Count);
        Assert.All(gameplay, r => Assert.True(fixedSet.IsOn(r.Id) && restored.IsOn(r.Id) && !original.IsOn(r.Id), r.Id));
        Assert.All(RuleTable.Flags.Where(r => r.Class is "data-safety" or "visual"), r => Assert.True(original.IsOn(r.Id) && fixedSet.IsOn(r.Id) && restored.IsOn(r.Id), r.Id));
        Assert.Equal(RuleTable.Flags.Count(r => r.Class != "restoration"), fixedSet.On.Count());
        Assert.Equal(RuleTable.Flags.Count, restored.On.Count());
    }

    /// <summary>
    /// A restoration flag is on in Restored only, and its register ids are the rows of restoration.csv it restores (status <c>built</c>, the
    /// note naming the switch); every built row has a flag. Original and Fixed turn none on.
    /// </summary>
    [Fact]
    public void RestorationFlagsAreTheBuiltRowsOfTheRestorationRegister()
    {
        var built = ReadCsv(Path.Combine(Docs, "restoration.csv")).Where(r => r["status"] == "built").ToList();
        var flags = Rows().Where(r => r["kind"] == "flag" && r["class"] == "restoration").ToList();
        var problems = new List<string>();
        foreach (var row in flags)
        {
            foreach (var id in row["register"].Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                if (built.FirstOrDefault(r => r["id"] == id) is not { } item)
                {
                    problems.Add($"{row["id"]}: {id} is not a built row of restoration.csv");
                }
                else if (!item["note"].Contains(row["id"], StringComparison.Ordinal))
                {
                    problems.Add($"{id}: its note does not name its switch {row["id"]}");
                }
            }
            if (row["register"].Length == 0)
            {
                problems.Add($"{row["id"]}: a restoration names the row of restoration.csv it restores");
            }
            if (OriginalBehaviour.Of(RulePreset.Original).IsOn(row["id"]) || OriginalBehaviour.Of(RulePreset.Fixed).IsOn(row["id"]) || !OriginalBehaviour.Of(RulePreset.Restored).IsOn(row["id"]))
            {
                problems.Add($"{row["id"]}: a restoration is on in Restored and nowhere else");
            }
        }
        foreach (var item in built.Where(r => !flags.Any(f => f["register"].Split(';').Contains(r["id"]))))
        {
            problems.Add($"{item["id"]} is built but no restoration flag names it");
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>Each property reads and writes its own flag of the table, by its register id.</summary>
    [Fact]
    public void EachPropertyIsItsKeyedFlag()
    {
        foreach (var rule in RuleTable.Flags)
        {
            var behaviour = OriginalBehaviour.Faithful;
            behaviour.Set(rule.Id, true);
            Assert.Equal([rule.Id], behaviour.On);
            var property = typeof(OriginalBehaviour).GetProperty(rule.Property)!;
            Assert.True((bool)property.GetValue(behaviour)!, rule.Id);
            property.SetValue(behaviour, false);
            Assert.False(behaviour.IsOn(rule.Id), rule.Id);
        }
    }

    /// <summary>A key the table does not know (from a newer save or a typo in the settings) is reported once and ignored: it turns nothing on.</summary>
    [Fact]
    public void UnknownKeysAreReportedOnceAndIgnored()
    {
        var reports = new List<string>();
        var before = OriginalBehaviour.ReportUnknown;
        OriginalBehaviour.ReportUnknown = reports.Add;
        try
        {
            var key = "fix.test.unknown-" + Guid.NewGuid().ToString("N");
            var behaviour = OriginalBehaviour.Faithful;
            Assert.False(behaviour.IsOn(key));
            behaviour.Set(key, true);
            Assert.False(behaviour.IsOn(key));
            Assert.Empty(behaviour.On);
            Assert.Single(reports, r => r.Contains(key, StringComparison.Ordinal));
            Assert.Equal(-1, RuleTable.IndexOf(key));
        }
        finally
        {
            OriginalBehaviour.ReportUnknown = before;
        }
    }

    /// <summary>The players' page of the Fixed rules (docs/FIXED.md) has a line for each gameplay fix: one list item a flag.</summary>
    [Fact]
    public void ThePlayersPageListsEveryGameplayFix()
    {
        var items = File.ReadAllLines(Path.Combine(Root, "docs", "FIXED.md")).Count(l => l.StartsWith("- ", StringComparison.Ordinal));
        Assert.Equal(Rows().Count(r => r["kind"] == "flag" && r["class"] == "gameplay"), items);
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
        foreach (var row in Rows().Where(r => r["kind"] == "flag" && r["class"] != "restoration"))
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

    /// <summary>
    /// A planned switch (a fix decided for a later step, not in the code yet) names the flag it will be, which <see cref="OriginalBehaviour"/>
    /// does not have yet; it is off, its presets follow its class, and its register ids are bugs kept as the original (no fix flag, decision
    /// "later") whose note in one of its code files names the planned flag. When the flag is added, its row becomes a `flag` row.
    /// </summary>
    [Fact]
    public void PlannedSwitchesAreDecidedBugsNotYetInTheCode()
    {
        var properties = typeof(OriginalBehaviour).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var bugs = ReadCsv(Path.Combine(Docs, "original_bugs.csv")).ToDictionary(b => b["id"], StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var row in Rows().Where(r => r["kind"] == "planned"))
        {
            var id = row["id"];
            if (!Regex.IsMatch(row["key"], "^Fix[A-Z][A-Za-z]+$") || properties.Contains(row["key"]))
            {
                problems.Add($"{id}: the planned flag {row["key"]} is not a new Fix… name (a flag in the code has a `flag` row)");
            }
            if (row["default"] != "off")
            {
                problems.Add($"{id}: a planned switch is off ('{row["default"]}')");
            }
            var presets = row["presets"].Split(';');
            if ((row["class"] is "data-safety" or "visual") != presets.Contains("original") || !presets.Contains("fixed") || !presets.Contains("restored"))
            {
                problems.Add($"{id}: class {row["class"]} and presets '{row["presets"]}' disagree");
            }
            var ids = row["register"].Split(';', StringSplitOptions.RemoveEmptyEntries);
            if (ids.Length == 0)
            {
                problems.Add($"{id}: a planned switch names the bugs it will fix");
            }
            foreach (var bug in ids)
            {
                if (!bugs.TryGetValue(bug, out var b))
                {
                    problems.Add($"{id}: {bug} is not in original_bugs.csv");
                    continue;
                }
                if (b["fix_flag"] != "none" || !b["decision"].StartsWith("later", StringComparison.Ordinal))
                {
                    problems.Add($"{id}: {bug} has fix flag '{b["fix_flag"]}' and decision '{b["decision"]}' (a planned fix is for a bug kept as the original, decision later)");
                }
                if (!b["note"].Contains(row["key"], StringComparison.Ordinal))
                {
                    problems.Add($"{id}: the note of {bug} does not name the planned flag {row["key"]}");
                }
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
