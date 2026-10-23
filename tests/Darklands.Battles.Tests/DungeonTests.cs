using Darklands.Cards;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Tactics;
using Darklands.Core.Characters;

namespace Darklands.Battles.Tests;

/// <summary>Doors, secret doors, chests, locks and stairs of generated battlefields, driven through the battle's own rules.</summary>
public sealed class DungeonTests
{
    private const int DoorsAt = 0x1F54, DoorOpenAt = 0x33D3, TriggersAt = 0x2186, RegionsAt = 0x300E, StairsAt = 0x2FB8;

    private static (Presentation.Battles.BattleScreen Screen, TacticalBattle Tactics) Start(int battlefield, int seed)
    {
        var (screen, tactics) = BattleScreenTests.Start(GameFolder.Files!, new BattleRequest(battlefield, seed, [(-1, 0, 0), (-1, 0, 0), (-1, 0, 0)]));
        if (tactics.Paused)
        {
            tactics.TogglePause();
        }
        // The frame drawn after the first tick would open the leader's order mode (and pause): these tests give their orders themselves.
        tactics.PendingOrderMember = -1;
        return (screen, tactics);
    }

    private static void Run(TacticalBattle tactics, int ticks)
    {
        for (var t = 0; t < ticks; t++)
        {
            tactics.Battle.Clock++;
            tactics.Iterate();
        }
    }

    /// <summary>With DUNGEON_SHOTS set to a folder, the screen as it stands is saved there (to look at the doors, chests and stairs).</summary>
    private static void Shot(Presentation.Battles.BattleScreen screen, string name)
    {
        if (Environment.GetEnvironmentVariable("DUNGEON_SHOTS") is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
            var p = screen.Tactics!.Battle[screen.Tactics.Battle.Leader].Position;
            screen.Tactics.ViewOrigin = (p.Column, p.Row);
            screen.Draw();
            screen.SaveScreen(Path.Combine(folder, name + ".png"));
        }
    }

    private static MapPoint Centre(BattlefieldLevel level, int at) =>
        new(level.GetWord(at) + level.GetByte(at + 4) / 2, level.GetWord(at + 2) + level.GetByte(at + 5) / 2);

    /// <summary>The level's wall lists were relocated: each starts with a cell word (x &lt;&lt; 8 | y) and ends with −1.</summary>
    [Fact]
    public void TheWallListsAreRelocated()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (_, tactics) = Start(0x0A, 777);
        var level = tactics.Level;
        Assert.True(level.GetByte(0x2488) > 0);
        for (var k = 0; k < level.GetByte(0x2488); k++)
        {
            var at = (ushort)level.GetWord(0x248A + 2 * k);
            Assert.InRange(at, 0x24C6, 0x2FB6);
            var w = (ushort)level.GetWord(at);
            Assert.True(w == 0xFFFF || (w >> 8) < 40 && (w & 0xFF) < 40, $"list {k} starts with {w:X4}");
        }
    }

    /// <summary>A member at a shut door: Use Door is offered in order mode; ordered, the door opens (its flag and the wall's door bit).</summary>
    [Fact]
    public void AMemberOpensADoor()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (screen, tactics) = Start(0x00, 1234);
        var b = tactics.Battle;
        var level = tactics.Level;
        var k = Enumerable.Range(0, level.GetByte(0x1F52)).First(d => level.GetWord(DoorsAt + 14 * d) > 0 && level.GetByte(DoorsAt + 14 * d + 7) == 0);
        var leader = b.Leader;
        b[leader].Position = Centre(level, DoorsAt + 14 * k);
        Run(tactics, 16);
        Assert.True((b[leader].Flags & TacticalFlags.InDoorway) != 0, "stands in the doorway");
        Assert.Equal(k, b[leader].Door);
        tactics.EnterOrderMode(leader);
        Assert.True(tactics.Menus.On(OrdersItem.UseDoor));
        Shot(screen, "door-shut");
        var opens = b[leader].DoorOpens;
        tactics.OrderPlace(leader, PlaceAction.UseDoor);
        tactics.TogglePause();
        Run(tactics, 16);
        Shot(screen, "door-open");
        Assert.Equal(opens ? 1 : 0, level.GetByte(DoorOpenAt + k));
    }

    /// <summary>R023+0x0362..0x0368: a door wakes an encounter of its wall group only when the encounter's X (+0x00) is above 0; a disabled one (X negative) stays.</summary>
    [Fact]
    public void ADoorLeavesADisabledEncounterWaiting()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (_, tactics) = Start(0x5E, 4545);
        var level = tactics.Level;
        var door = Enumerable.Range(0, level.GetByte(0x1F52)).First(d => (sbyte)level.GetByte(DoorsAt + 14 * d + 6) >= 0);
        var wall = (sbyte)level.GetByte(DoorsAt + 14 * door + 6);
        typeof(TacticalBattle).GetProperty(nameof(TacticalBattle.ActivationsEnabled))!.SetValue(tactics, true);
        var list = tactics.Activations[tactics.Battle.FloorShown];
        list.RemoveAll(g => g.Room == wall);
        var disabled = new EnemyGroup { X = -528, Y = 200, Spread = 16, Room = wall };
        list.Add(disabled);
        void Toggle() => typeof(TacticalBattle).GetMethod("ToggleDoor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(tactics, [door, tactics.Battle.Leader]);
        Toggle();
        Assert.Contains(disabled, list);
        disabled.X = 528;
        Toggle();
        Assert.DoesNotContain(disabled, list);
    }

    /// <summary>R014+0xDC4E..0xDC54: a member stepping into a door gets the place-changed bit 0x08; after the row redraw (R014+0xDD09) the
    /// common tail R014+0xDD17 clears it again, as it does on every pass of a member.</summary>
    [Fact]
    public void EnteringADoorLeavesThePlaceChangedBitClear()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (_, tactics) = Start(0x5E, 4545);
        var level = tactics.Level;
        var door = Enumerable.Range(0, level.GetByte(0x1F52)).First(d => (short)level.GetWord(DoorsAt + 14 * d) > 0
            && level.GetByte(DoorsAt + 14 * d + 4) > 0 && level.GetByte(DoorsAt + 14 * d + 5) > 0);
        var at = DoorsAt + 14 * door;
        var c = tactics.Battle[Enumerable.Range(0, Combatant.PartySlots).First(i => tactics.Battle[i].IsAble)];
        c.Position = new MapPoint(level.GetWord(at), level.GetWord(at + 2));
        c.Flags &= ~(TacticalFlags.InDoorway | TacticalFlags.PlaceChanged);
        typeof(TacticalBattle).GetMethod("PartyLocations", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(tactics, []);
        Assert.True((c.Flags & TacticalFlags.InDoorway) != 0);
        Assert.Equal(door, c.Door);
        Assert.True((c.Flags & TacticalFlags.PlaceChanged) == 0);
    }

    /// <summary>R014+0x48CA: an enemy's path search whose goal is blocked still leaves through R014+0x4B5A, which puts the doors back to the
    /// flags DS:E0C0 kept at the last search that opened them (here none: every door shuts again).</summary>
    [Fact]
    public void AnEnemySearchWithABlockedGoalPutsTheDoorsBack()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (_, tactics) = Start(0x00, 1234);
        var level = tactics.Level;
        const System.Reflection.BindingFlags any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var twins = (bool[])typeof(TacticalBattle).GetField("_twinDoor", any)!.GetValue(tactics)!;
        var door = Enumerable.Range(0, level.GetByte(0x1F52)).First(d => !twins[d] && level.GetWord(DoorsAt + 14 * d) > 0 && level.GetByte(DoorsAt + 14 * d + 7) == 0);
        typeof(TacticalBattle).GetMethod("SetDoor", any)!.Invoke(tactics, [door, true, false]);
        Assert.Equal(1, level.GetByte(DoorOpenAt + door));
        var b = tactics.Battle;
        var enemy = b[Combatant.PartySlots];
        enemy.Status = CharacterStatus.Able;
        enemy.Floor = b.FloorShown;
        enemy.Target = -1;
        enemy.Position = new MapPoint(b[b.Leader].Position.X + 40, b[b.Leader].Position.Y);
        enemy.Destination = b[b.Leader].Position;
        var found = (bool)typeof(TacticalBattle).GetMethod("FindPath", any)!.Invoke(tactics, [Combatant.PartySlots])!;
        Assert.False(found);
        Assert.Equal(0, level.GetByte(DoorOpenAt + door));
    }

    /// <summary>A perceptive member steps on a secret door's trigger: the trigger goes off and the door's wall is cleared.</summary>
    [Fact]
    public void ASecretDoorIsFound()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (screen, tactics) = Start(0x0A, 777);
        var b = tactics.Battle;
        var level = tactics.Level;
        var t = Enumerable.Range(0, level.GetByte(0x2184)).First(k => level.GetWord(TriggersAt + 14 * k + 6) == 0 && level.GetWord(TriggersAt + 14 * k) > 0);
        var leader = b.Leader;
        b[leader].Attributes[3] = 99;
        b[leader].Position = Centre(level, TriggersAt + 14 * t);
        var door = level.GetWord(TriggersAt + 14 * t + 10) & 0xFF;
        Assert.True(level.GetWord(DoorsAt + 14 * door) < 0, "the secret door is hidden");
        Shot(screen, "secret-before");
        Run(tactics, 16);
        Shot(screen, "secret-found");
        Assert.True(level.GetWord(TriggersAt + 14 * t) < 0, "the trigger went off");
    }

    /// <summary>A member with lock picks at a locked chest picks it (with enough skill) and the chest opens to the loot screen.</summary>
    [Fact]
    public void AChestIsPickedAndOpened()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (screen, tactics) = Start(0x0A, 777);
        var b = tactics.Battle;
        var level = tactics.Level;
        var r = Enumerable.Range(0, level.GetByte(0x300C)).First(k => (level.GetByte(RegionsAt + 0x2C * k + 8) & 0x7F) != 0);
        var leader = b.Leader;
        b[leader].Skills[14] = 99;
        b[leader].Items.Add(new ItemStack(0x40, 0x69, 40, 1, 1));
        b[leader].Position = Centre(level, RegionsAt + 0x2C * r);
        Run(tactics, 16);
        Assert.True((b[leader].Flags & TacticalFlags.AtChest) != 0);
        tactics.EnterOrderMode(leader);
        Assert.True(tactics.Menus.On(OrdersItem.PickLock));
        Shot(screen, "chest");
        tactics.OrderPlace(leader, PlaceAction.PickChestLock);
        tactics.TogglePause();
        Run(tactics, 16);
        Assert.Equal(0, level.GetByte(RegionsAt + 0x2C * r + 8));
        Assert.True((b[leader].SkillsUsed & SkillUse.ArtificeSucceeded) != 0);
    }

    /// <summary>The whole party on stairs takes them: the party is on the other level, the level shown.</summary>
    [Fact]
    public void ThePartyTakesTheStairs()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (screen, tactics) = Start(0x0B, 4242);
        var b = tactics.Battle;
        var level = tactics.Level;
        Assert.True(level.GetByte(0x2FB6) > 0, "level 0 has stairs");
        var to = (sbyte)level.GetByte(StairsAt + 6);
        var spot = Centre(level, StairsAt);
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            if (b[i].IsAble)
            {
                b[i].Position = spot;
            }
        }
        Run(tactics, 16);
        Shot(screen, "stairs");
        Assert.True(tactics.Menus.On(OrdersItem.UseStairs));
        tactics.ToggleGroup();
        // G ends with the order menu reset (R016+0x0BD6): the next tick's party_locations turns Use Stairs on again
        Assert.False(tactics.Menus.On(OrdersItem.UseStairs));
        Run(tactics, 8);
        Assert.True(tactics.Menus.On(OrdersItem.UseStairs));
        tactics.UseKey(-1);
        Run(tactics, 4);
        Shot(screen, "stairs-taken");
        Assert.Equal(to, b.FloorShown);
        Assert.Equal(to, b.PartyFloor);
        Assert.All(Enumerable.Range(0, Combatant.PartySlots).Where(i => b[i].IsAble), i => Assert.Equal(to, b[i].Floor));
    }

    /// <summary>
    /// G and Q end with the order menu reset (R016+0x0BD6 / R016+0x0CDC): the stairs and exit entries say "(All)" while the party moves
    /// together and "(Ldr)" again after, and in order mode Resume stays on.
    /// </summary>
    [Fact]
    public void TheMoveModeTogglesResetTheOrderMenu()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (_, tactics) = Start(0x0B, 4242);
        var m = tactics.Menus;
        Run(tactics, 2);
        Assert.Equal(BattleMenus.Leader, m.StairsSuffix);
        tactics.ToggleGroup();
        Assert.Equal(1, tactics.MoveMode);
        Assert.True(tactics.CursorKeysOn);
        Assert.Equal(BattleMenus.All, m.StairsSuffix);
        Assert.Equal(BattleMenus.All, m.ExitSuffix);
        Assert.False(m.On(OrdersItem.Resume));
        tactics.ToggleGroup();
        Assert.Equal(0, tactics.MoveMode);
        Assert.False(tactics.CursorKeysOn);
        Assert.Equal(BattleMenus.Leader, m.StairsSuffix);
        tactics.TogglePause();
        Assert.True(tactics.Paused);
        tactics.ToggleSingleFile();
        Assert.Equal(2, tactics.MoveMode);
        Assert.Equal(BattleMenus.All, m.StairsSuffix);
        Assert.True(m.On(OrdersItem.Resume));
        tactics.ToggleSingleFile();
        Assert.Equal(0, tactics.MoveMode);
        Assert.True(m.On(OrdersItem.Resume));
    }

    /// <summary>
    /// The fortress varies its furniture: each level shown puts its own record of the set named at level byte 0x33C8 (byte 0x33D2) in
    /// furniture tile byte 0x33D1 (level_switch R024+0x059A → R024+0x07A2), its footprint and height with it.
    /// </summary>
    [Fact]
    public void EachFortressLevelShowsItsOwnFurniture()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var (_, tactics) = Start(0x5E, 4242);
        var field = tactics.Field;
        Assert.NotEqual(0, field.Terrain[0xB4]);
        var data = Content.Formats.Graphics.CatMemberDecoder.Decode(new Presentation.Battles.BattleAssets(files).TileSets.ReadStored("fortmons.frc"), 0x10000);
        var records = new HashSet<int>();
        for (var f = 0; f < field.Levels.Count; f++)
        {
            tactics.SwitchLevel(f);
            var image = field.Levels[f].Image;
            int tile = (sbyte)image[0x33D1], record = (sbyte)image[0x33D2];
            Assert.Equal(11, tile);
            records.Add(record);
            var shape = tactics.Map.Furniture[tile];
            Assert.NotNull(shape);
            Assert.Equal((sbyte)data[record * 0x28 + 0x20], shape.Width);
            Assert.Equal((sbyte)data[record * 0x28 + 0x21], shape.Height);
        }
        Assert.Contains(0x17, records);
        Assert.Contains(0x1D, records);
    }
}
