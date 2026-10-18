using Darklands.Cards;
using Darklands.Content;
using Darklands.Core;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Tactics;
using Darklands.Core.Characters;
using Darklands.Persistence;
using Darklands.Presentation;
using Darklands.Presentation.Battles;

namespace Darklands.Battles.Tests;

/// <summary>
/// Saving inside a battle and taking it up again (overlay 3's battle-state save and restore): a battle on a battlefield the
/// game can be saved on is saved as Alt-S saves it (.SAV with its battle bit and the members' combatant records, .BSV with the
/// battle's variables, levels, terrain.fil and loot file), read back, and started again from the battle file; the battle stands
/// as it was saved, and saved again it writes the same battle file but for what the start of a fight taken up again sets anew.
/// </summary>
public sealed class BattleSaveTests
{
    private static readonly (int, int, int)[] NoGroups = [(-1, 0, 0), (-1, 0, 0), (-1, 0, 0)];

    /// <summary>The battlefields tried: castles, cities, a mine, the Templar fortress (the main loop's battles persist).</summary>
    public static TheoryData<int, int> Battlefields() => new() { { 0x00, 1234 }, { 0x05, 4321 }, { 0x14, 863 }, { 0x0A, 777 }, { 0x29, 99 }, { 0x5E, 5 } };

    [Theory]
    [MemberData(nameof(Battlefields))]
    public void ABattleSavedInsideItIsTakenUpAsItStood(int battlefield, int seed)
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var world = WorldData.Load(files);
        var ui = new GameUi(new BattleScreenTests.SilentHost(), new GameAssets(files));
        var state = Simulation.NewGame.Quickstart(world, 7);
        var (screen, tactics) = StartBattle(ui, files, state, new BattleRequest(battlefield, seed, NoGroups, Persistent: true));
        var b = tactics.Battle;
        // The party walks a little and the clock runs: paths, timers and positions to save.
        var leader = b.Leader;
        Walk(tactics, leader, new MapPoint(b[leader].Position.X + 40, b[leader].Position.Y + 8));
        Run(tactics, 90);
        if (tactics.Menus.SaveGame != 1)
        {
            // The keep (its tile set) is never a place to save; nor is real combat (an encounter saw the party).
            Assert.True(b.RealCombat || tactics.IsKeep, $"no save offered: real combat {b.RealCombat}, keep {tactics.IsKeep}");
            return;
        }
        var (sav, bsv) = SaveInBattle(state, tactics);
        Assert.Equal(0x80, sav[0x96] & 0x80);
        Assert.NotNull(bsv);
        Assert.True(BattleSaveFile.Parse(bsv).SavedInBattle);

        var loaded = OriginalSaves.Read(sav, bsv, "dksave1.sav", world).State;
        Assert.True(loaded.ResumeBattle);
        var (_, resumed) = StartBattle(ui, files, loaded, new BattleRequest(battlefield, seed, NoGroups, Persistent: true, Resume: true));
        var r = resumed.Battle;

        Assert.Equal(b.Clock, r.Clock);
        Assert.Equal(b.FloorShown, r.FloorShown);
        Assert.Equal(b.PartyFloor, r.PartyFloor);
        Assert.Equal(b.RealCombat, r.RealCombat);
        Assert.Equal(tactics.MarchingOrder, resumed.MarchingOrder);
        Assert.Equal(tactics.LevelCount, resumed.LevelCount);
        for (var level = 0; level < tactics.LevelCount; level++)
        {
            Assert.Equal(TacticalBattle.SavedFloor(tactics.LevelAt(level).Image), TacticalBattle.SavedFloor(resumed.LevelAt(level).Image));
            Assert.Equal(tactics.Activations[level].Select(g => g.ToRecord()), resumed.Activations[level].Select(g => g.ToRecord()));
        }
        Assert.Equal(tactics.LootPiles.Count, resumed.LootPiles.Count);
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            Assert.Equal(b[i].Status, r[i].Status);
            if (b[i].Status == CharacterStatus.Empty)
            {
                continue;
            }
            Assert.Equal(b[i].Position, r[i].Position);
            Assert.Equal(b[i].Destination, r[i].Destination);
            Assert.Equal(b[i].Facing, r[i].Facing);
            Assert.Equal(b[i].Floor, r[i].Floor);
            Assert.Equal(b[i].Attributes, r[i].Attributes);
            Assert.Equal(b[i].Skills, r[i].Skills);
            Assert.Equal(b[i].Path, r[i].Path);
            Assert.Equal(b[i].PathIndex, r[i].PathIndex);
            Assert.Equal(b[i].Moving, r[i].Moving);
            Assert.Equal(b[i].Flags, r[i].Flags);
            Assert.Equal(b[i].NextStepAt, r[i].NextStepAt);
            Assert.Equal(b[i].RecoverAt, r[i].RecoverAt);
            Assert.Equal(b[i].Items, r[i].Items);
        }
        // The enemies of the battle are not saved (records 5..12 are emptied when a battle starts).
        Assert.All(Enumerable.Range(Combatant.PartySlots, 8), i => Assert.Equal(CharacterStatus.Empty, r[i].Status));

        // Saved again at once, the battle file is the one saved, but for the orders (dropped) and the two timers the start sets.
        var again = loaded.BattleFile!.Clone();
        resumed.WriteBattleFile(again, inBattle: true, loaded.Battlefield.ClearedLevels, loaded.LastCastleFlags);
        var first = state.BattleFile!;
        var mask = new bool[BattleFile.VariablesSize];
        Array.Fill(mask, true, BattleVariables.Orders, 13 * 6);
        Array.Fill(mask, true, BattleVariables.NextTickAt, 2);
        Array.Fill(mask, true, BattleVariables.ActivationsAt, 2);
        for (var k = 0; k < BattleFile.VariablesSize; k++)
        {
            if (!mask[k])
            {
                Assert.True(first.Variables[k] == again.Variables[k], $"battle variable +0x{k:X4}: saved {first.Variables[k]:X2}, taken up {again.Variables[k]:X2}");
            }
        }
        Assert.Equal(first.Levels.Count, again.Levels.Count);
        for (var level = 0; level < first.Levels.Count; level++)
        {
            Assert.Equal(first.Levels[level].Floor, again.Levels[level].Floor);
            Assert.Equal(first.Levels[level].Activations, again.Levels[level].Activations);
            Assert.Equal(first.Levels[level].Enemies, again.Levels[level].Enemies);
        }
        Assert.Equal(first.Terrain, again.Terrain);
        Assert.Equal(first.Loot, again.Loot);
    }

    /// <summary>The level files of every battle file of the player's saves go through this program's conversions unchanged.</summary>
    [Fact]
    public void EveryLevelOfTheSavesGoesThroughTheConversionsUnchanged()
    {
        var folder = Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") ?? "";
        var saves = Path.Combine(folder, "SAVES");
        if (!Directory.Exists(saves))
        {
            return;
        }
        foreach (var path in Directory.GetFiles(saves, "*.bsv", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive }))
        {
            var file = BattleSaveFile.Parse(File.ReadAllBytes(path)).ToBattleFile();
            foreach (var level in file.Levels)
            {
                // The enemy kinds (.enm) read and written over the same buffer, the floor's object numbers as the restore and the save set them.
                Assert.Equal(level.Enemies, LevelEnemyFile.Write(LevelEnemyFile.Read(level.Enemies), level.Enemies));
                Assert.Equal(level.Floor, TacticalBattle.SavedFloor(TacticalBattle.RestoredFloor(level.Floor)));
            }
        }
    }

    internal static (BattleScreen Screen, TacticalBattle Tactics) StartBattle(GameUi ui, GameFiles files, GameState state, BattleRequest request)
    {
        var screens = new BattleScreens(ui, () => state);
        var screen = new BattleScreen(ui, new BattleAssets(files), state, screens);
        screen.Start(request);
        var tactics = screen.Tactics!;
        if (tactics.Paused)
        {
            tactics.TogglePause();
        }
        tactics.PendingOrderMember = -1;
        return (screen, tactics);
    }

    /// <summary>What Alt-S does in a battle (SaveGameDialog with the battle): the party's modifiers end, the battle file and the saved game are written.</summary>
    internal static (byte[] Sav, byte[] Bsv) SaveInBattle(GameState state, TacticalBattle tactics)
    {
        tactics.EndPartyModifiersForSave();
        tactics.WriteBattleFile(state.BattleFile ??= new BattleFile(), inBattle: true, state.Battlefield.ClearedLevels, state.LastCastleFlags);
        var (sav, bsv) = OriginalSaves.Write(state, "in battle", true, true, tactics.Battle);
        return (sav, bsv!);
    }

    /// <summary>A walk order as the player gives it: order mode, Walk, a click on the ground, then the battle goes on.</summary>
    internal static void Walk(TacticalBattle tactics, int member, MapPoint to)
    {
        tactics.EnterOrderMode(member);
        tactics.ChooseOrder(1);
        tactics.OrderClick(-1, -1, to);
        if (tactics.Paused)
        {
            tactics.TogglePause();
        }
    }

    internal static void Run(TacticalBattle tactics, int ticks)
    {
        for (var t = 0; t < ticks; t++)
        {
            tactics.Battle.Clock++;
            if (tactics.Iterate())
            {
                return;
            }
        }
    }
}
