using Darklands.Core;
using Darklands.Core.World;
using Darklands.Core.Characters;
using Darklands.Platform;

namespace Darklands.Persistence.Tests;

/// <summary>
/// A saved game cut short is told from a whole one by its own counts (<see cref="SaveFile.RunsPastTheEnd"/>): the load screen refuses it
/// with OriginalBehaviour.FixDamagedSavedGame (RC-001, RC-002: tests/Darklands.Battles.Tests/RcPlayTests). Every save the original writes or
/// reads whole, real or synthesised, must not be taken for damaged.
/// </summary>
public sealed class DamagedSaveTests
{
    public static TheoryData<string> Saves()
    {
        var data = new TheoryData<string>();
        foreach (var name in SaveFolderCopy.Names.Where(n => n != "DEFAULT"))
        {
            data.Add(name);
        }
        if (data.Count == 0)
        {
            data.Add("");
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Saves))]
    public void ARealSavedGameIsNotDamaged(string name)
    {
        if (name.Length == 0)
        {
            return;
        }
        var sav = SaveFolderCopy.Read(name);
        // DKSAVE40 and DKSAVE41 are files another tool wrote: DKSAVE40 has a character count past its records and is read out of step (the original
        // loads it all the same), DKSAVE41 is one the load screen of 483.07 takes; both are whole as far as the length goes.
        Assert.False(SaveFile.RunsPastTheEnd(sav), $"{name} ({sav.Length} bytes) is taken for damaged");
    }

    /// <summary>The saved games the oracles' synthesiser and the community made (code/local/oracle-saves; not in a plain checkout).</summary>
    [Fact]
    public void EverySynthesisedAndCommunitySavedGameIsNotDamaged()
    {
        var repository = AppContext.BaseDirectory;
        for (var dir = new DirectoryInfo(repository); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Darklands.slnx")))
            {
                repository = dir.FullName;
                break;
            }
        }
        var folder = Path.Combine(LocalPaths.Resolve("code", repository, Environment.GetEnvironmentVariable(LocalPaths.RootVariable)), "local", "oracle-saves");
        if (!Directory.Exists(folder))
        {
            return;
        }
        var files = Directory.EnumerateFiles(folder, "*.sav", SearchOption.AllDirectories).ToList();
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            Assert.False(SaveFile.RunsPastTheEnd(File.ReadAllBytes(file)), $"{file} is taken for damaged");
        }
    }

    [Fact]
    public void EveryFileTheRewriteWritesIsNotDamaged()
    {
        var state = OriginalSaveTests.SampleGame();
        Assert.False(SaveFile.RunsPastTheEnd(OriginalSaves.Write(state).Sav));
        state.Party.Members[1].Status = CharacterStatus.Empty;
        state.Behaviour = Core.OriginalBehaviour.DataSafe;
        Assert.False(SaveFile.RunsPastTheEnd(OriginalSaves.Write(state).Sav));
    }

    [Theory]
    [InlineData("DKSAVE0.SAV", 0)]
    [InlineData("DKSAVE0.SAV", 96)]
    [InlineData("DKSAVE0.SAV", 0xEF)]
    [InlineData("DKSAVE0.SAV", 0x188)]
    [InlineData("DKSAVE0.SAV", 512)]
    [InlineData("DKSAVE0.SAV", 9374)]
    [InlineData("DKSAVE12.SAV", 3000)]
    public void AFileCutInsideItsHeaderRecordsOrLocationTableIsDamaged(string name, int length)
    {
        if (!SaveFolderCopy.Exists(name))
        {
            return;
        }
        var sav = SaveFolderCopy.Read(name);
        Assert.True(SaveFile.RunsPastTheEnd(sav[..length]));
    }

    [Theory]
    [InlineData("DKSAVE0.SAV")]
    [InlineData("DKSAVE12.SAV")]
    public void AFileCutOnlyInItsCacheIsNotDamagedAndOneCutInTheLocationsIs(string name)
    {
        if (!SaveFolderCopy.Exists(name))
        {
            return;
        }
        var sav = SaveFolderCopy.Read(name);
        var file = SaveFile.Parse(sav, fixedLocationTable: true);
        var cache = file.Cache.Length;
        // the cache is the rest of the file and has no length of its own: any cut of it is whole as far as the records go
        Assert.False(SaveFile.RunsPastTheEnd(sav[..^Math.Min(100, cache)]));
        Assert.False(SaveFile.RunsPastTheEnd(sav[..^cache]));
        Assert.True(SaveFile.RunsPastTheEnd(sav[..(sav.Length - cache - 1)]));
    }

    [Fact]
    public void TheFileTheOriginalsWriterLeavesShortOfItsRecordsIsLoadedAsBeforeNotTakenForDamaged()
    {
        // The writer's status-0 slot bug (FixSaveWriterSkipsEmptySlots) writes a count past its records; the original loads that file out of
        // step and goes on (the test in OriginalSaveTests), so it is not refused unless its own counts then run past the end.
        var name = SaveFolderCopy.Names.FirstOrDefault(n => n.Equals("DKSAVE40.SAV", StringComparison.OrdinalIgnoreCase));
        if (name is null)
        {
            return;
        }
        var first = OriginalSaves.Read(SaveFolderCopy.Read(name), SaveFolderCopy.Read(Path.ChangeExtension(name, ".BSV")), name, NoGameData.Instance).State;
        var shortSave = OriginalSaves.Write(first).Sav;
        Assert.Equal(8, BitConverter.ToInt16(shortSave, 0xF1));
        Assert.False(SaveFile.RunsPastTheEnd(shortSave));
    }
}
