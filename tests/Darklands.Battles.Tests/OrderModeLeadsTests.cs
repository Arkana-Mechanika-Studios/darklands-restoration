using System.Reflection;
using Darklands.Cards;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Tactics;
using Darklands.Core.Characters;

namespace Darklands.Battles.Tests;

/// <summary>Order mode and the keys outside it, as the original's R016 and R014 order functions run them (each test fails without its fix).</summary>
public sealed class OrderModeLeadsTests
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static TacticalBattle Start()
    {
        var (_, tactics) = BattleScreenTests.Start(GameFolder.Files!, new BattleRequest(0x5E, 4545, [(-1, 0, 0), (-1, 0, 0), (-1, 0, 0)]));
        tactics.PendingOrderMember = -1;
        if (tactics.Selected != -1)
        {
            tactics.CancelOrder();
        }
        return tactics;
    }

    private static T Get<T>(TacticalBattle t, string name) => (T)typeof(TacticalBattle).GetField(name, Any)!.GetValue(t)!;

    /// <summary>R016+0x101E / 0x1364: H with Halt on, travelling individually, calls R016+0x1590 with nobody selected; it has no test of DS:A6B2.</summary>
    [Fact]
    public void HaltWithNobodySelectedChoosesThePlaceAction()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var t = Start();
        Assert.Equal(-1, t.Selected);
        t.Menus.Set(OrdersItem.Halt, 1);
        typeof(TacticalBattle).GetProperty("MoveMode", Any)!.SetValue(t, 0);
        t.Halt(t.Battle.Leader);
        Assert.Equal(0x400, t.OrderAction);
        Assert.Equal((int)PlaceAction.Halt, t.PlaceOrder);
        Assert.Equal(-1, Get<int>(t, "_picker"));
    }

    /// <summary>R014+0xBCA8 has no test of DS:A6B2: with nobody selected its end (R014+0xC004..) still resets the order menu.</summary>
    [Fact]
    public void LeavingOrderModeWithNobodySelectedResetsTheMenu()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var t = Start();
        t.Menus.Set(OrdersItem.Flee, 1);
        t.Menus.Set(OrdersItem.Cancel, 1);
        t.LeaveOrderMode();
        Assert.False(t.Menus.On(OrdersItem.Flee));
        Assert.False(t.Menus.On(OrdersItem.Cancel));
        Assert.False(t.CursorKeysOn);
    }

    /// <summary>R016+0x0A7D: choosing a member stores its flee bit in DS:D6EA, which Esc (R016+0x13D4) gives back.</summary>
    [Fact]
    public void SelectingAMemberKeepsItsFleeBit()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var t = Start();
        var i = Enumerable.Range(0, Combatant.PartySlots).First(k => t.Battle[k].IsAble && t.Battle[k].Floor == t.Battle.FloorShown);
        t.Battle[i].Flags |= TacticalFlags.Following;
        typeof(TacticalBattle).GetField("_fleeBefore", Any)!.SetValue(t, false);
        t.SelectMember(i);
        Assert.True(Get<bool>(t, "_fleeBefore"));
    }

    /// <summary>R016+0x13D4 leaves DS:D7FE (the cursor keys) as it was.</summary>
    [Fact]
    public void EscKeepsTheCursorKeys()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var t = Start();
        var i = Enumerable.Range(0, Combatant.PartySlots).First(k => t.Battle[k].IsAble);
        t.EnterOrderMode(i);
        Assert.True(t.CursorKeysOn);
        t.CancelOrder();
        Assert.True(t.CursorKeysOn);
        Assert.Equal(-1, t.Selected);
    }

    /// <summary>
    /// R014+0x2256..0x2263: Dissolve Lock calls consume_slot (R014+0xB434) with the slot byte DS:C7CD[m] found when the order was given,
    /// whatever the pack holds then: past the last stack the stale entry's count goes down and, when it was 1, the pack's count (the last
    /// stack lost); slot 0xFF (the Eater-Water gone) is entry 255, for member 0 member 3's entry 63. With OriginalBehaviour.FixStaleDissolveSlot
    /// only an Eater-Water is used up.
    /// </summary>
    [Fact]
    public void ADissolveOrderUsesUpTheSlotFoundWhenTheOrderWasGiven()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var use = typeof(TacticalBattle).GetMethod("UseUpDissolveSlot", Any)!;
        foreach (var fix in new[] { false, true })
        {
            foreach (var slot in new[] { 3, -1 })
            {
                var t = Start();
                t.Battle.Behaviour = new Darklands.Core.OriginalBehaviour { FixStaleDissolveSlot = fix };
                var c = t.Battle[0];
                c.Items.Clear();
                c.Items.Add(new ItemStack(10, 0, 20, 1, 1));
                c.Items.Add(new ItemStack(99, 0, 30, 2, 1));
                c.Items.Add(new ItemStack(12, 0, 25, 1, 1));
                c.Items[3] = new ItemStack(99, 0, 30, 1, 1);
                var other = t.Battle[3].Items;
                other[63] = new ItemStack(5, 0, 5, 1, 1);
                c.DissolveSlot = slot;
                use.Invoke(t, [0]);
                if (fix)
                {
                    Assert.Equal(3, c.Items.Stacks);
                    Assert.Equal(1, c.Items[1].Count);
                    Assert.Equal(1, c.Items[3].Count);
                    Assert.Equal(1, other[63].Count);
                    continue;
                }
                Assert.Equal(2, c.Items.Stacks);
                Assert.Equal(2, c.Items[1].Count);
                Assert.Equal(slot == 3 ? 0 : 1, c.Items[3].Count);
                Assert.Equal(slot == 3 ? 1 : 0, other[63].Count);
            }
        }
    }

    /// <summary>combat_draw (R014+0x6D39) removes an arrived missile in the tick that drew it.</summary>
    [Fact]
    public void AnArrivedMissileGoesInTheTickThatDrawsIt()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var t = Start();
        var p = t.Battle[t.Battle.Leader].Position;
        var m = new Missile { Position = p, Goal = p, Target = -1, Shooter = 0, Done = 0x81, Sprite = 0, Speed = 4, Floor = t.Battle.FloorShown };
        t.Missiles.Add(m);
        t.Battle.Clock = t.NextTickAt;
        Assert.True(t.Tick());
        Assert.DoesNotContain(m, t.Missiles);
        Assert.Contains(m, t.MissilesDrawn);
    }
    private static void Set(TacticalBattle t, string name, object value)
    {
        var type = typeof(TacticalBattle);
        if (type.GetField(name, Any) is { } f)
        {
            f.SetValue(t, value);
        }
        else
        {
            type.GetProperty(name, Any)!.SetValue(t, value);
        }
    }

    /// <summary>
    /// R014+0xBF94..0xC000 with nobody selected (Enter travelling individually after the button held over a walking member): the walk is
    /// given to record -1, whose action state and repath entries are exchange[12] and give-up[12] (direct calls of R014+0xBCA8).
    /// </summary>
    [Fact]
    public void LeavingWithNobodySelectedGivesTheWalkToRecordMinusOne()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        foreach (var fix in new[] { false, true })
        {
            var t = Start();
            t.Battle.Behaviour = new Darklands.Core.OriginalBehaviour { FixLeaveOrderModeWithoutSelection = fix };
            var last = t.Battle[Combatant.Count - 1];
            last.Exchange = Exchange.Placed;
            last.GiveUpUntil = 5;
            var active = t.Battle.ActiveMember;
            Set(t, "CursorTarget", -2);
            Set(t, "OrderSpot", new MapPoint(300, 200));
            Set(t, "_orderClock", (ushort)1234);
            t.Battle.PartyFloor = t.Battle.FloorShown;
            t.LeaveOrderMode();
            Assert.Equal(new MapPoint(300, 200), t.CursorPoint);
            Assert.Equal(fix ? Exchange.Placed : Exchange.None, last.Exchange);
            Assert.Equal(fix ? 5 : 1234 + 30, last.GiveUpUntil);
            Assert.Equal(active, t.Battle.ActiveMember);      // DS:17B0 = 0xFF is not reproduced
            Assert.Equal(fix ? 0 : 1, Get<int>(t, "_actionBeforeRecords"));
        }
    }

    /// <summary>
    /// R014+0xBC6A (order mode's start) and R016+0x13DA (Esc) clear DS:C652, the spot's x, alone, and nothing but the ground click
    /// (R014+0xC6F3 / 0xC6FA) writes DS:C67D: a walk order given with no spot clicked goes to x 0 and the last spot's y
    /// (R014+0xBFC0..0xBFCC), in the next battle too (the battle's reset clears neither).
    /// </summary>
    [Fact]
    public void AWalkWithNoSpotClickedGoesToTheLastSpotsY()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var t = Start();
        var member = t.Battle.Leader;
        t.EnterOrderMode(member);
        Set(t, "OrderSpot", new MapPoint(300, 200));
        t.CancelOrder();
        Assert.Equal(new MapPoint(0, 200), t.ProgramMemory.OrderSpot);
        t.EnterOrderMode(member);
        Set(t, "OrderAction", 1);
        Set(t, "CursorTarget", -2);
        t.Battle.PartyFloor = t.Battle.FloorShown;
        t.LeaveOrderMode();
        Assert.Equal(CombatAction.Walk, t.Battle[member].Action);
        Assert.Equal(new MapPoint(0, 200), t.Battle[member].Destination);
    }

    /// <summary>R014+0xBCDC..0xBCEE and 0xBD48..0xBD5B: with nobody selected the Hurry bit of flags[-1] is the pause byte's 0x10.</summary>
    [Fact]
    public void LeavingWithNobodySelectedClearsThePauseBytesHurryBit()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var t = Start();
        t.PauseByte = 0x11;
        Set(t, "OrderAction", 0x400);
        t.LeaveOrderMode();
        Assert.Equal(1, t.PauseByte);
        t.PauseByte = 0x11;
        Set(t, "_actionBeforeRecords", 0x1BE);
        Set(t, "CursorTarget", -1);
        Set(t, "OrderSpot", new MapPoint(300, 200));
        t.LeaveOrderMode();
        Assert.Equal(1, t.PauseByte);
    }
}
