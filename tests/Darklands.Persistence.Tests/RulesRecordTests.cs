using System.Text.Json.Nodes;
using Darklands.Core;

namespace Darklands.Persistence.Tests;

/// <summary>
/// A saved game records its rules (rules.preset): a game whose rules are not Original gets a companion file with a <c>rules</c>
/// record (the preset and the flags that differ from it), the .SAV stays as it is, and loading gives the recorded rules back; a
/// save without a record has none (the game loads as Original); what a record names that this program does not know is reported
/// and left out.
/// </summary>
public sealed class RulesRecordTests
{
    private static SaveFolder Folder(bool record = true) => new(TempCopies.Create("darklands-rules-")) { RecordRules = record };

    private static string Companion(SaveFolder folder, string name) => Path.Combine(folder.Path, ModdedSaves.CompanionOf(name));

    // a saved game's file as it is on disk: the game names it as the original does, in small letters ("dksave0.sav", DS:A680),
    //   and writes it in capitals ("DKSAVE0.SAV"); on a file system where case matters (Linux) only the name on disk opens it
    private static string OnDisk(SaveFolder folder, string name)
    {
        var files = Directory.GetFiles(folder.Path).Where(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Single(files);
        return files[0];
    }

    // the names on disk, compared with their case (so a name written in the wrong case fails on Windows too)
    private static List<string> Names(SaveFolder folder) => [.. Directory.GetFiles(folder.Path).Select(f => Path.GetFileName(f)!).Order(StringComparer.Ordinal)];

    [Fact]
    public void AFixedGameSavedAndLoadedKeepsFixed()
    {
        var folder = Folder();
        var state = OriginalSaveTests.SampleGame();
        state.Behaviour = OriginalBehaviour.Of(RulePreset.Fixed);
        var name = folder.Save(state, "fixed", true, true);
        // the files on disk in capitals, the companion's name made from the save's (found by the same name wherever case matters)
        Assert.Equal(["DKSAVE0.BSV", "DKSAVE0.MODS.JSON", "DKSAVE0.SAV"], Names(folder));
        var record = JsonNode.Parse(File.ReadAllText(Companion(folder, name)))!["rules"]!;
        Assert.Equal("fixed", (string?)record["preset"]);
        Assert.Empty(record["flags"]!.AsObject());

        var loaded = folder.Load(name, new OriginalSaveTests.FakeCities());
        Assert.Equal(RulePreset.Fixed, loaded.Mods.Rules!.Preset);
        Assert.True(loaded.Mods.Rules.SameFlags(OriginalBehaviour.Of(RulePreset.Fixed)));
        Assert.Empty(loaded.Mods.RulesProblems);
        // a companion with rules only marks no mods (the load screen's mark is for mods)
        Assert.Empty(ModdedSaves.ModsUsed(loaded.State));
        Assert.False(ModdedSaves.IsCompanionOf(File.ReadAllText(Companion(folder, name)), File.ReadAllBytes(OnDisk(folder, name))));
    }

    [Fact]
    public void AFlagChangedFromItsPresetIsRecordedAndComesBack()
    {
        var folder = Folder();
        var state = OriginalSaveTests.SampleGame();
        state.Behaviour = OriginalBehaviour.Of(RulePreset.Original);
        var gameplay = RuleTable.Flags.First(r => r.Class == "gameplay").Id;
        state.Behaviour.Set(gameplay, true);
        var name = folder.Save(state, "custom", true, true);
        var record = JsonNode.Parse(File.ReadAllText(Companion(folder, name)))!["rules"]!;
        Assert.Equal("original", (string?)record["preset"]);
        Assert.True((bool)record["flags"]![gameplay]!);
        var rules = folder.Load(name, new OriginalSaveTests.FakeCities()).Mods.Rules!;
        Assert.Equal(RulePreset.Original, rules.Preset);
        Assert.Equal([(gameplay, true)], rules.Differences());
    }

    [Fact]
    public void TheSavedGameIsTheSameWhateverItsRulesRecord()
    {
        // the record lives only in the companion: Original and Fixed games write the same .SAV (no gameplay fix acted here), but for
        //   the bank standings Fixed keeps in the header's spare words +0xA7..+0xAE (F-1 set 1b, B-047d71: the marker "BK", then the
        //   Fugger's, the Medici's and the Hanse's standing), where Original writes zeros as the original does
        var original = Folder();
        var fixedRules = Folder();
        var a = OriginalSaveTests.SampleGame();
        a.Behaviour = OriginalBehaviour.Of(RulePreset.Original);
        var b = OriginalSaveTests.SampleGame();
        b.Behaviour = OriginalBehaviour.Of(RulePreset.Fixed);
        var nameA = original.Save(a, "same", true, true);
        var nameB = fixedRules.Save(b, "same", true, true);
        var bytesA = File.ReadAllBytes(OnDisk(original, nameA));
        var bytesB = File.ReadAllBytes(OnDisk(fixedRules, nameB));
        Assert.Equal(new byte[8], bytesA[0xA7..0xAF]);
        Assert.Equal("BK"u8.ToArray(), bytesB[0xA7..0xA9]);
        bytesB.AsSpan(0xA7, 8).Clear();
        Assert.Equal(bytesA, bytesB);
        Assert.False(File.Exists(Companion(original, nameA)));
        Assert.True(File.Exists(Companion(fixedRules, nameB)));
    }

    [Fact]
    public void ASaveWithoutARecordHasNoRules()
    {
        // an Original game writes no companion; loaded, it has no recorded rules (the game plays Original: DarklandsGame)
        var folder = Folder();
        var state = OriginalSaveTests.SampleGame();
        state.Behaviour = OriginalBehaviour.DataSafe;
        var name = folder.Save(state, "original", true, true);
        Assert.False(File.Exists(Companion(folder, name)));
        var loaded = folder.Load(name, new OriginalSaveTests.FakeCities());
        Assert.Null(loaded.Mods.Rules);
        Assert.Empty(loaded.Mods.RulesProblems);
    }

    [Fact]
    public void TheTestersGameRecordsNothing()
    {
        // without RecordRules (the faithful game of the replays and the headless host) no companion is written, whatever the flags
        var folder = Folder(record: false);
        var state = OriginalSaveTests.SampleGame();
        state.Behaviour = OriginalBehaviour.Of(RulePreset.Fixed);
        var name = folder.Save(state, "tester", true, true);
        Assert.False(File.Exists(Companion(folder, name)));
        state.Behaviour = OriginalBehaviour.Faithful;
        name = folder.Save(state, "faithful", true, true);
        Assert.False(File.Exists(Companion(folder, name)));
    }

    [Fact]
    public void UnknownKeysOfARecordAreReportedAndLeftOut()
    {
        var reports = new List<string>();
        var before = OriginalBehaviour.ReportUnknown;
        OriginalBehaviour.ReportUnknown = reports.Add;
        try
        {
            var folder = Folder();
            var state = OriginalSaveTests.SampleGame();
            state.Behaviour = OriginalBehaviour.Of(RulePreset.Fixed);
            var name = folder.Save(state, "newer", true, true);
            // a record from a newer version: a flag this program does not have, and one it has turned off
            var unknown = "fix.test.from-a-newer-version-" + Guid.NewGuid().ToString("N");
            var gameplay = RuleTable.Flags.First(r => r.Class == "gameplay").Id;
            var file = JsonNode.Parse(File.ReadAllText(Companion(folder, name)))!;
            file["rules"]!["flags"] = new JsonObject { [unknown] = true, [gameplay] = false, ["fix.not-a-flag"] = "yes" };
            File.WriteAllText(Companion(folder, name), file.ToJsonString());

            var loaded = folder.Load(name, new OriginalSaveTests.FakeCities());
            var rules = loaded.Mods.Rules!;
            Assert.Equal(RulePreset.Fixed, rules.Preset);
            Assert.False(rules.IsOn(gameplay));
            Assert.Equal([(gameplay, false)], rules.Differences());
            Assert.Contains(loaded.Mods.RulesProblems, p => p.Contains(unknown, StringComparison.Ordinal));
            Assert.Contains(reports, r => r.Contains(unknown, StringComparison.Ordinal));
        }
        finally
        {
            OriginalBehaviour.ReportUnknown = before;
        }
    }

    [Fact]
    public void AnUnknownPresetLoadsAsOriginalAndIsReported()
    {
        var folder = Folder();
        var state = OriginalSaveTests.SampleGame();
        state.Behaviour = OriginalBehaviour.Of(RulePreset.Fixed);
        var name = folder.Save(state, "unknown preset", true, true);
        var file = JsonNode.Parse(File.ReadAllText(Companion(folder, name)))!;
        file["rules"]!["preset"] = "legendary";
        File.WriteAllText(Companion(folder, name), file.ToJsonString());
        var loaded = folder.Load(name, new OriginalSaveTests.FakeCities());
        Assert.Equal(RulePreset.Original, loaded.Mods.Rules!.Preset);
        Assert.True(loaded.Mods.Rules.SameFlags(OriginalBehaviour.Original));
        Assert.Contains(loaded.Mods.RulesProblems, p => p.Contains("legendary", StringComparison.Ordinal));
    }

    [Fact]
    public void AnOriginalGameAfterAFixedOneRemovesTheOldCompanion()
    {
        var folder = Folder();
        var state = OriginalSaveTests.SampleGame();
        state.Behaviour = OriginalBehaviour.Of(RulePreset.Fixed);
        var name = folder.Save(state, "fixed", true, true);
        Assert.True(File.Exists(Companion(folder, name)));
        // the original game deletes DKSAVE0.SAV and the name is saved again by an Original game: the stale companion goes
        File.Delete(OnDisk(folder, name));
        state.Behaviour = OriginalBehaviour.Original;
        Assert.Equal(name, folder.Save(state, "original", true, true));
        Assert.False(File.Exists(Companion(folder, name)));
        Assert.Equal(["DKSAVE0.BSV", "DKSAVE0.SAV"], Names(folder));
    }
}
