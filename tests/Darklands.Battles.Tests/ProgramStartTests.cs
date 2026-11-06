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
}
