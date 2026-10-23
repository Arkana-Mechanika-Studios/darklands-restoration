using System.Reflection;
using Darklands.Cards;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Tactics;

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
}
