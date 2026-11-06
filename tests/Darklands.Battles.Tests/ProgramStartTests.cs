using Darklands.Content;
using Darklands.Core.Battles;
using Darklands.Persistence;
using Darklands.Presentation;
using Darklands.Simulation;

namespace Darklands.Battles.Tests;

/// <summary>
/// What a program holds before its first battle (the battle file of a new game: the start values and the folder's work files), and the
/// start screen shown again after a game, which stands for a new run of the program (the original has no other way to a new party).
/// </summary>
public sealed class ProgramStartTests
{
    private static DarklandsGame Game(GameFiles files, RcPlayTests.RcHost host, string saves, int? card = null) =>
        new(host, files)
        {
            Seed = () => 27,
            PartyTicks = () => 27,
            PlayOpening = false,
            ShowStartUp = false,
            SaveDirectory = saves,
            QuickstartCard = card,
        };

    private static string TempFolder(string name)
    {
        var root = Path.Combine(Path.GetTempPath(), "darklands-programstart-" + name + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        return root;
    }

    [Fact]
    public void AFreshProgramsBattleVariablesAreZerosButTheLevelShownAndTheTimerRate()
    {
        var v = new BattleFile().Variables;
        Assert.Equal(0xFF, v[BattleVariables.FloorShown]);
        Assert.Equal(1, v[BattleVariables.TimerRate] | v[BattleVariables.TimerRate + 1] << 8);
        Assert.Equal(2, v.Count(b => b != 0));
    }

    /// <summary>
    /// Lockstep session tester-newgame-reload: a new game saved before any battle writes +0x16D1 = 0xFF, +0x16DB = 1 and the folder's
    /// terrain.fil and cfile.dat (the original's battle-state save packs the work files it finds).
    /// </summary>
    [Fact]
    public void ANewGameSavedBeforeAnyBattleWritesTheStartValuesAndTheFoldersWorkFiles()
    {
        if (GameFolder.Files is not { } shared)
        {
            return;
        }
        var files = new GameFiles(shared.Root);
        byte[] terrain = [.. Enumerable.Range(0, 0xC4).Select(i => (byte)(i * 7))];
        byte[] loot = [4, 0, 2, 0x14, 1, 4, 0x29, 0, 0x48, 0x19, 1, 0x15, 0x2E, 0, 0x4D, 0x19, 1, 6];
        files.Replace("TERRAIN.FIL", terrain);
        files.Replace("CFILE.DAT", loot);
        var saves = TempFolder("save");
        try
        {
            Game(files, new RcPlayTests.RcHost(["k:q", "w:80", "c:1F00", "w:30", "k:enter", "w:60", "k:esc", "w:40"]), saves).Run();
            var bsv = BattleSaveFile.Parse(File.ReadAllBytes(Directory.GetFiles(saves, "DKSAVE0.BSV").Single()));
            Assert.Equal(0xFF, bsv.Variables[BattleVariables.FloorShown]);
            Assert.Equal(1, bsv.Variables[BattleVariables.TimerRate]);
            Assert.Equal(terrain, bsv.File1164);
            Assert.Equal(loot, bsv.File1162);
        }
        finally
        {
            Directory.Delete(saves, recursive: true);
        }
    }

    /// <summary>
    /// The original ends the program after a game (exit, or main's return on 0x62 / -0x63) and shows the start screen once per run: a game
    /// begun from the start screen shown again gets the program's globals at their start values, not the last game's.
    /// </summary>
    [Fact]
    public void AGameAfterAnEndedOneStartsFromTheProgramsStartGlobals()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var saves = TempFolder("restart");
        try
        {
            var host = new RcPlayTests.RcHost(["w:5", "k:q", "w:40", "k:q", "w:40"]);
            var game = Game(files, host, saves, card: CardDispatcher.EndOfGame);
            var memories = new HashSet<object>(ReferenceEqualityComparer.Instance);
            var keys = new HashSet<object>(ReferenceEqualityComparer.Instance);
            host.EveryFrame = _ =>
            {
                if (game.Battles is { } battles)
                {
                    memories.Add(battles.GenerationMemory);
                    keys.Add(battles.CursorKeys);
                }
            };
            game.Run();
            Assert.Equal(2, memories.Count);
            Assert.Equal(2, keys.Count);
        }
        finally
        {
            Directory.Delete(saves, recursive: true);
        }
    }

    /// <summary>
    /// The interface's globals of a new run: the music and sound options are 1 in the EXE's data image (DS:01BE / DS:01BF), the card
    /// window's font 3, the kept piece (DS:9C3D), the barter listing (DS:86A2), the panel's selection (DS:9064) and order mode (DS:A6B3)
    /// 0; a game after an ended one finds them so, not as the last game left them.
    /// </summary>
    [Fact]
    public void AGameAfterAnEndedOneStartsWithTheInterfacesStartValues()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var saves = TempFolder("restart-ui");
        try
        {
            var host = new RcPlayTests.RcHost(["w:5", "k:q", "w:40", "k:q", "w:40"]);
            var game = Game(files, host, saves, card: CardDispatcher.EndOfGame);
            var ui = game.Ui;
            var firstRun = true;
            (bool Music, bool Sound, int Font, int Kept, int Listing, bool Selected, bool OrderMode, bool Cycling)? second = null;
            host.EveryFrame = _ =>
            {
                if (!game.OnStartScreen)
                {
                    return;
                }
                // a game has been played once the battles' screens exist (the ended game is played at once: card 0x62)
                if (game.Battles is null)
                {
                    // the first run leaves them changed (Alt-M / Alt-F off, a listing, a lit frame, order mode, the cycles running)
                    ui.Options.Music = false;
                    ui.Options.SoundEffects = false;
                    ui.SavedMusic.Value = 5;
                    ui.BarterListing[3] = 9;
                    ui.PanelSelection[1] = true;
                    ui.PanelOrderMode = (true, 1);
                    ui.Cycles.Running = true;
                }
                else if (firstRun)
                {
                    firstRun = false;
                    second = (ui.Options.Music, ui.Options.SoundEffects, ui.CardWindowFont, ui.SavedMusic.Value, ui.BarterListing[3],
                        ui.PanelSelection[1], ui.PanelOrderMode.On, ui.Cycles.Running);
                }
            };
            game.Run();
            Assert.False(firstRun);
            Assert.Equal((true, true, 3, 0, 0, false, false, false), second);
        }
        finally
        {
            Directory.Delete(saves, recursive: true);
        }
    }

    /// <summary>
    /// A new run reads CONFIG.DRK again (R000+0x0114): a game after an ended one takes the sound driver of the file as it is then, not as
    /// it was when the program started.
    /// </summary>
    [Fact]
    public void AGameAfterAnEndedOneReadsConfigDrkAgain()
    {
        if (GameFolder.Files is not { } shared)
        {
            return;
        }
        var files = new GameFiles(shared.Root);
        files.Replace("CONFIG.DRK", [5, 0, 0x20, 2, 5, 0, 1, 0]);
        var saves = TempFolder("restart-config");
        try
        {
            var host = new RcPlayTests.RcHost(["w:300", "k:q", "w:40", "k:q", "w:40"]);
            var game = Game(files, host, saves, card: CardDispatcher.EndOfGame);
            game.ShowStartUp = true;
            var drivers = new List<string?>();
            host.EveryFrame = _ =>
            {
                if (!game.OnStartScreen)
                {
                    return;
                }
                if (game.Battles is null && drivers.Count == 0)
                {
                    drivers.Add(game.Started?.SoundDriver);
                    // the player runs the set-up between the two runs: card 0, a Sound Blaster
                    files.Replace("CONFIG.DRK", [0, 0, 0x20, 2, 5, 0, 1, 0]);
                }
                else if (game.Battles is not null && drivers.Count == 1)
                {
                    drivers.Add(game.Started?.SoundDriver);
                }
            };
            game.Run();
            Assert.Equal(["psound.dlc", "asound.dlc"], drivers);
        }
        finally
        {
            Directory.Delete(saves, recursive: true);
        }
    }
    /// <summary>
    /// A saved game whose slots 0 and 1 name one record: the load screen's shield pictures ask for "pics\shield.pic" for the slot the
    /// reader left without its record and the program ends (exit 99; lockstep session carry-dup-resave); fixed (DataSafe), the game goes on.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASavedGameWithTwoSlotsOfOneRecordEndsTheProgramAtTheLoadUnlessFixed(bool fixedGame)
    {
        if (GameFolder.Files is not { } files || DarklandsGame.DefaultSaveDirectory(files.Root) is not { } folder
            || Directory.GetFiles(folder).FirstOrDefault(f => Path.GetFileName(f).Equals("DKSAVE0.SAV", StringComparison.OrdinalIgnoreCase)) is not { } source)
        {
            return;
        }
        var saves = TempFolder("dup");
        try
        {
            var sav = File.ReadAllBytes(source);
            BitConverter.GetBytes(BitConverter.ToInt16(sav, 0xF3)).CopyTo(sav, 0xF5);
            File.WriteAllBytes(Path.Combine(saves, "DKSAVE0.SAV"), sav);
            var game = Game(files, new RcPlayTests.RcHost(["w:400"]), saves);
            game.Behaviour = fixedGame ? Core.OriginalBehaviour.DataSafe : Core.OriginalBehaviour.Faithful;
            game.StartWithSave = "DKSAVE0.SAV";
            game.Run();
            Assert.Equal(fixedGame ? null : "pics\\shield.pic: No such file or directory\n", game.ProgramExit?.Printed);
        }
        finally
        {
            Directory.Delete(saves, recursive: true);
        }
    }
    /// <summary>
    /// The travel map's own Alt-Q (the second of two keys read in one pass) frees the map's buffers before "Quit to DOS?"; after "no" a
    /// second one, or the camp (C), frees one again and the original ends ("M - Buffer dealloc error" / "S - ...", exit 0; local/batch/mapquit);
    /// fixed, the map goes on.
    /// </summary>
    [Theory]
    [InlineData(false, "4D00,1000", "M - Buffer dealloc error")]
    [InlineData(true, "4D00,1000", null)]
    [InlineData(false, "2E63", "S - Buffer dealloc error")]
    [InlineData(true, "2E63", null)]
    public void ASecondAltQOnTheMapAfterNoEndsTheProgramUnlessFixed(bool fixedGame, string then, string? printed)
    {
        if (GameFolder.Files is not { } files || DarklandsGame.DefaultSaveDirectory(files.Root) is not { } folder
            || Directory.GetFiles(folder).FirstOrDefault(f => Path.GetFileName(f).Equals("DKSAVE17.SAV", StringComparison.OrdinalIgnoreCase)) is not { } source)
        {
            return;
        }
        var saves = TempFolder("mapquit");
        try
        {
            File.Copy(source, Path.Combine(saves, "DKSAVE17.SAV"));
            var game = Game(files, new RcPlayTests.RcHost(["w:300", "c:4D00,1000", "w:60", "k:n", "w:60", "c:" + then, "w:60", "k:n", "w:60", "c:011B", "w:60"]), saves);
            game.Behaviour = fixedGame ? Core.OriginalBehaviour.DataSafe : Core.OriginalBehaviour.Faithful;
            game.StartWithSave = "DKSAVE17.SAV";
            game.Run();
            Assert.Equal(printed, game.ProgramExit?.Printed);
        }
        finally
        {
            Directory.Delete(saves, recursive: true);
        }
    }
}
