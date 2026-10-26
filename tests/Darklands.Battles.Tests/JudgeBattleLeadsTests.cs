using System.Reflection;
using Darklands.Cards;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Tactics;
using Darklands.Core.Characters;
using Darklands.Core.Randomness;
using Darklands.Platform;
using Darklands.Presentation.Battles;

namespace Darklands.Battles.Tests;

/// <summary>The battle leads of the direct-call rounds judged against the original (each test fails without its fix).</summary>
public sealed class JudgeBattleLeadsTests
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static (BattleScreen Screen, TacticalBattle Tactics) Start()
    {
        var (screen, tactics) = BattleScreenTests.Start(GameFolder.Files!, new BattleRequest(0x5E, 4545, [(-1, 0, 0), (-1, 0, 0), (-1, 0, 0)]));
        tactics.PendingOrderMember = -1;
        if (tactics.Selected != -1)
        {
            tactics.CancelOrder();
        }
        return (screen, tactics);
    }

    private static object? Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Any)!.Invoke(target, args);

    /// <summary>R024+0x0B2E..0x0C17: a member's random spots around stairs are 501, then 501 more with the furniture cleared (2004 draws).</summary>
    [Fact]
    public void AMembersRandomPlacementMakes501And501Tries()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (_, t) = Start();
        var spot = new MapPoint(20 * 32 + 16, 20 * 16 + 8);
        // every cell the tries can reach is void: no spot is free
        for (var x = 17; x <= 24; x++)
        {
            for (var y = 17; y <= 24; y++)
            {
                t.Level[x, y] = new BattlefieldCell(0x8000_0000);
            }
        }
        var random = (MscRandom)t.Battle.Random;
        random.Seed(12345);
        var placed = (bool)Call(t, "PlaceAround", 0, spot, 0)!;
        Assert.False(placed);
        var expected = new MscRandom(12345);
        for (var k = 0; k < 2004; k++)
        {
            expected.NextRaw();
        }
        Assert.Equal(expected.State, random.State);
    }

    /// <summary>R025+0x0314..0x035D: an enemy's pack gets its kind's stacks copied over it; the entries after them keep an earlier enemy's.</summary>
    [Fact]
    public void AnEnemysPackKeepsTheEntriesPastItsStacks()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (_, t) = Start();
        var kind = t.Field.Forces.Kinds.Where(l => l is not null).SelectMany(l => l).First(k => k.Items.Count < 16);
        var c = t.Battle[5];
        var marker = new ItemStack(0x77, 1, 2, 3, 4);
        c.Items.Clear();
        for (var k = 0; k < 16; k++)
        {
            c.Items.Add(marker);
        }
        CombatantSetup.FromEnemy(t.Battle, 5, kind, 0, 0, 5, true);
        Assert.Equal(kind.Items.Count, c.Items.Stacks);
        Assert.Equal(marker, c.Items.Entries[kind.Items.Count]);
    }

    /// <summary>R025+0x0264: the .enm record's head is copied over the slot, whose bytes +0x1E/+0x1F (the step and frame delays) are 8.</summary>
    [Fact]
    public void AnEnemysDelaysAreTheRecordsEight()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (_, t) = Start();
        var kind = t.Field.Forces.Kinds.Where(l => l is not null).SelectMany(l => l).First();
        var c = t.Battle[5];
        (c.StepDelay, c.FrameDelay) = (3, 5);
        CombatantSetup.FromEnemy(t.Battle, 5, kind, 0, 0, 5, true);
        Assert.Equal((8, 8), (c.StepDelay, c.FrameDelay));
        // and the record the loader writes has them at +0x1E / +0x1F
        var record = LevelEnemyFile.Write([kind]);
        Assert.Equal((8, 8), (record[1 + 0x1E], record[1 + 0x1F]));
    }

    /// <summary>R014+0x1E3F: the aim sequence's length is the caller's animation set's (DS:C8A7[set]), not one worked out again.</summary>
    [Fact]
    public void TheAimUsesTheCallersAnimationSet()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (_, t) = Start();
        var c = t.Battle[0];
        var other = Enumerable.Range(0, t.Slots.Length).First(s => (t.Slots[s].AimLength & 0x7F) != (t.Slots[t.AnimationSet(0)].AimLength & 0x7F));
        c.State = ActionState.Aiming;
        c.AimEndAt = (ushort)(t.Battle.Clock + 100);
        c.NextFrameAt = 0;
        c.AimSetUp = false;
        Call(t, "MissileAction", 0, other);
        Assert.Equal(t.Slots[other].AimLength & 0x7F, c.SequenceLength);
    }

    /// <summary>R016+0x00FC..: combat_keys matches whole key words: a keypad digit scrolls the view whatever Shift, a letter typed with another
    /// scan code (Alt and the keypad) is no case.</summary>
    [Fact]
    public void TheBattleKeysAreWholeWords()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (screen, t) = Start();
        var view = (BattlefieldView)typeof(BattleScreen).GetField("_view", Any)!.GetValue(screen)!;
        view.CentreColumn = 20;
        view.CentreRow = 20;
        var position = t.Battle[t.Battle.ActiveMember].Position;
        Call(screen, "Key", 0x4838);
        Assert.Equal((19, 19), (view.CentreColumn, view.CentreRow));
        Assert.Equal(position, t.Battle[t.Battle.ActiveMember].Position);
        t.Menus.Set(OrdersItem.Group, 1);
        var mode = t.MoveMode;
        Call(screen, "Key", 0x0047);
        Assert.Equal(mode, t.MoveMode);
        Call(screen, "Key", Keys.ForCharacter('g'));
        Assert.NotEqual(mode, t.MoveMode);
    }

    /// <summary>
    /// R014+0xF284 reads its intersection point from two stack words it never set when neither the box's bottom nor its top is crossed
    /// first: what the last clip test at that stack place left. A run of line tests made one after another from one frame (enemy_turn's
    /// missile tests, R018+0x0BE7) keeps it; a line test of its own starts from other leftovers (probed in the gate battle: outside the box),
    /// taken as a clean stack. The wall east of cell (21, 3) and the line (659, 61)-(897, 97) of that battle (round B1 tier 2).
    /// </summary>
    [Fact]
    public void AClipPointLeftInsideAWallBlocksOnlyTheRunThatLeftIt()
    {
        var battle = new Battle(new CombatTables(new byte[CombatTables.Length], new byte[16]), new MscRandom(1)) { FloorShown = 0 };
        var level = new BattlefieldLevel(new byte[BattlefieldLevel.ImageSize]);
        level[21, 3] = new BattlefieldCell(0x0A);
        var map = new BattleMap(battle, level, [], 0);
        var run = new BattleMap.ClipResidue { Point = (702, 64) };
        Assert.Equal(1, map.LineBlocked(new MapPoint(659, 61), new MapPoint(897, 97), residue: run));
        Assert.Equal((702, 64), run.Point);
        Assert.Equal(0, map.LineBlocked(new MapPoint(659, 61), new MapPoint(897, 97)));
        Assert.Equal(1, map.LineBlocked(new MapPoint(659, 61), new MapPoint(897, 97), residue: run));
    }

    /// <summary>
    /// R023+0x19BD..0x19C2: leave_level drops the enemy with the lowest 100 × endurance / maximum, the product kept in 16 bits
    /// (round B2 lead L01). Endurances 2/4, 1/10 and 20/20: the 10 % enemy goes; a product cut to a byte would drop the 2/4 one.
    /// </summary>
    [Fact]
    public void LeavingALevelDropsTheWeakestByTheWholePercent()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (_, t) = Start();
        for (var i = Combatant.PartySlots; i < Combatant.Count; i++)
        {
            t.Battle[i].Status = CharacterStatus.Empty;
        }
        (sbyte E, sbyte M)[] enemies = [(2, 4), (1, 10), (20, 20)];
        for (var k = 0; k < 3; k++)
        {
            var c = t.Battle[Combatant.PartySlots + k];
            c.Status = CharacterStatus.Able;
            c.AnimationSlot = 4 + k;
            c.EndsBattleWhenKilled = false;
            c.Endurance = enemies[k].E;
            c.Maximums[(int)Darklands.Core.Characters.Attribute.Endurance] = enemies[k].M;
            c.Position = new MapPoint((short)(t.Battle[0].Position.X + 64 * (k + 1)), t.Battle[0].Position.Y);
        }
        t.Battle.RealCombat = false;
        var groups = t.Activations[t.Battle.FloorShown];
        var before = groups.Count;
        Call(t, "LeaveLevel", 0, new byte[14]);
        Assert.Equal(before + 1, groups.Count);
        Assert.Equal(new sbyte[] { 1, 0, 1 }, groups[^1].Counts);
    }
}
