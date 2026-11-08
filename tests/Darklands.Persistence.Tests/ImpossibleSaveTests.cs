using System.Buffers.Binary;
using Darklands.Core.World;
using Darklands.Platform;

namespace Darklands.Persistence.Tests;

/// <summary>
/// A saved game whose header holds what no game has (<see cref="SaveFile.ImpossibleValue"/>: edited by hand) is told from every save play
/// writes: the player's game refuses it with OriginalBehaviour.FixImpossibleSavedGame (tests/Darklands.Battles.Tests/RcPlayTests); the
/// loader itself reads it as before (the Faithful mode, the testers).
/// </summary>
public sealed class ImpossibleSaveTests
{
    private const int Places = SaveFile.LocationCount;

    public static TheoryData<string> Saves() => DamagedSaveTests.Saves();

    [Theory]
    [MemberData(nameof(Saves))]
    public void NoRealSavedGameHoldsAnImpossibleValue(string name)
    {
        if (name.Length == 0)
        {
            return;
        }
        Assert.Null(SaveFile.ImpossibleValue(SaveFolderCopy.Read(name), Places));
    }

    /// <summary>The saved games the oracles' synthesiser and the community made (code/local/oracle-saves; not in a plain checkout).</summary>
    [Fact]
    public void NoSynthesisedOrCommunitySavedGameHoldsAnImpossibleValue()
    {
        if (OracleSaves() is not { } files)
        {
            return;
        }
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var sav = File.ReadAllBytes(file);
            if (!SaveFile.RunsPastTheEnd(sav))
            {
                Assert.True(SaveFile.ImpossibleValue(sav, Places) is null, $"{file}: {SaveFile.ImpossibleValue(sav, Places)}");
            }
        }
    }

    [Fact]
    public void EveryFileTheRewriteWritesHoldsNoImpossibleValue()
    {
        var state = OriginalSaveTests.SampleGame();
        var sav = OriginalSaves.Write(state).Sav;
        Assert.Null(SaveFile.ImpossibleValue(sav, SaveFile.Parse(sav, fixedLocationTable: true).Locations.Count));
    }

    /// <summary>DKSAVE0 with one header word poked (a hand edit): the month, day, hour and location-now.</summary>
    [Theory]
    [InlineData(0x6A, 12, "the month 12")]
    [InlineData(0x6A, -1, "the month -1")]
    [InlineData(0x6C, 32, "the day 32")]
    [InlineData(0x6C, -1, "the day -1")]
    [InlineData(0x6E, 24, "the hour 24")]
    [InlineData(0x7C, 414, "the location 414")]
    [InlineData(0x7C, -2, "the location -2")]
    [InlineData(0x7C, 30000, "the location 30000")]
    public void AHandEditedHeaderValueNoGameHasIsFound(int offset, int value, string found)
    {
        if (Poked(offset, value) is not { } sav)
        {
            return;
        }
        Assert.False(SaveFile.RunsPastTheEnd(sav));
        Assert.Equal(found, SaveFile.ImpossibleValue(sav, Places));
    }

    /// <summary>The edges play gives are kept: December, the 31st, a negative hour (date-plus-hours does not borrow), the wilderness, the last place.</summary>
    [Theory]
    [InlineData(0x6A, 11)]
    [InlineData(0x6A, 0)]
    [InlineData(0x6C, 31)]
    [InlineData(0x6C, 0)]
    [InlineData(0x6E, 23)]
    [InlineData(0x6E, -3)]
    [InlineData(0x7C, -1)]
    [InlineData(0x7C, 413)]
    public void TheValuesPlayGivesAreKept(int offset, int value)
    {
        if (Poked(offset, value) is not { } sav)
        {
            return;
        }
        Assert.Null(SaveFile.ImpossibleValue(sav, Places));
    }

    /// <summary>A location table shorter than the game's places, its count word saying so (the file whole as far as its counts go).</summary>
    [Fact]
    public void ALocationTableShorterThanTheGamesPlacesIsFound()
    {
        if (!SaveFolderCopy.Exists("DKSAVE0.SAV"))
        {
            return;
        }
        var file = SaveFile.Parse(SaveFolderCopy.Read("DKSAVE0.SAV"), fixedLocationTable: true);
        file.Locations.RemoveRange(10, file.Locations.Count - 10);
        file.Cache = [];
        BinaryPrimitives.WriteInt16LittleEndian(file.Header.AsSpan(0x7C), 3);
        var sav = file.ToBytes();
        Assert.False(SaveFile.RunsPastTheEnd(sav));
        Assert.Equal("10 places, not the game's 414", SaveFile.ImpossibleValue(sav, Places));
    }

    /// <summary>A record count past the file is a damaged file, refused already (FixDamagedSavedGame: SaveFile.RunsPastTheEnd).</summary>
    [Fact]
    public void ARecordCountPastTheFileIsADamagedFile()
    {
        if (!SaveFolderCopy.Exists("DKSAVE0.SAV"))
        {
            return;
        }
        var sav = SaveFolderCopy.Read("DKSAVE0.SAV");
        BinaryPrimitives.WriteUInt16LittleEndian(sav.AsSpan(SaveFile.HeaderSize + 2), 200);
        Assert.True(SaveFile.RunsPastTheEnd(sav));
    }

    /// <summary>The loader itself takes the values as they are (the Faithful mode; the original reads past its tables, not modelled).</summary>
    [Theory]
    [InlineData(0x6A, 12)]
    [InlineData(0x6E, 24)]
    [InlineData(0x7C, 414)]
    public void TheLoaderReadsAnImpossibleValueAsItIs(int offset, int value)
    {
        if (Poked(offset, value) is not { } sav)
        {
            return;
        }
        var state = OriginalSaves.Read(sav, [], "DKSAVE0.SAV", NoGameData.Instance).State;
        Assert.Equal(value, offset switch { 0x6A => state.Date.Month, 0x6E => state.Date.Hour, _ => state.CurrentLocation });
        // and writes it back as it was (the place name at +0x00 is the writer's own: OriginalSaveTests)
        Assert.Equal(sav[0x15..], OriginalSaves.Write(state).Sav[0x15..]);
    }

    private static byte[]? Poked(int offset, int value)
    {
        if (!SaveFolderCopy.Exists("DKSAVE0.SAV"))
        {
            return null;
        }
        var sav = SaveFolderCopy.Read("DKSAVE0.SAV");
        BinaryPrimitives.WriteInt16LittleEndian(sav.AsSpan(offset), (short)value);
        return sav;
    }

    private static List<string>? OracleSaves()
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
        return Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.sav", SearchOption.AllDirectories).ToList() : null;
    }
}
