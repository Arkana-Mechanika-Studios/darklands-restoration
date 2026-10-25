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
}
