using Darklands.Cards;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Tactics;
using Darklands.Core.Characters;

namespace Darklands.Battles.Tests;

/// <summary>Order mode, the order menu's enables, the Throw list, surrender and the enemy information through the battle's own rules.</summary>
public sealed class OrderModeTests
{
    private static (Presentation.Battles.BattleScreen Screen, TacticalBattle Tactics) Start() =>
        BattleScreenTests.Start(GameFolder.Files!, new BattleRequest(40, 1234, [(34, 4, 3), (-1, 0, 0), (-1, 0, 0)]));

    /// <summary>Order mode pauses the game and opens the member's menu; Space leaves it and the clock runs again.</summary>
    [Fact]
    public void OrderModePausesAndSpaceResumes()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (_, tactics) = Start();
        if (tactics.Paused)
        {
            tactics.TogglePause();
        }
        Assert.Equal(-1, tactics.Selected);
        tactics.EnterOrderMode(1);
        Assert.True(tactics.Paused);
        Assert.Equal(1, tactics.Selected);
        Assert.True(tactics.Menus.On(OrdersItem.Resume));
        Assert.True(tactics.Menus.On(OrdersItem.Walk));
        Assert.True(tactics.Menus.On(OrdersItem.Halt));
        Assert.True(tactics.Menus.On(OrdersItem.Cancel));
        tactics.TogglePause();
        Assert.False(tactics.Paused);
        Assert.Equal(-1, tactics.Selected);
        Assert.False(tactics.Menus.On(OrdersItem.Resume));
    }

    /// <summary>Outside real combat the Attack menu stays off; in real combat Throw lists the throwable potions, one line per grade.</summary>
    [Fact]
    public void TheThrowListShowsThrowablePotions()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (_, tactics) = Start();
        var b = tactics.Battle;
        b.RealCombat = false;
        tactics.EnterOrderMode(0);
        Assert.NotEqual(1, tactics.Menus.AttackMenu);
        tactics.LeaveOrderMode();

        b.RealCombat = true;
        tactics.EnterOrderMode(0);
        Assert.False(tactics.Menus.On(AttackItem.Throw));
        tactics.LeaveOrderMode();

        b[0].Items.Add(new ItemStack(0x5F, 0, 30, 3, 1));
        b[0].Items.Add(new ItemStack(0x62, 0, 40, 2, 1));
        b[0].Items.Add(new ItemStack(0x61, 0, 40, 2, 1));   // Black Cloud: never thrown from the list
        b[0].Items.Add(new ItemStack(0x63, 0, 55, 1, 1));   // grade 50 and up: not listed
        tactics.EnterOrderMode(0);
        Assert.True(tactics.Menus.On(AttackItem.Throw));
        Assert.Equal([(0, 30, 3), (3, 40, 2)], tactics.Menus.ThrowLines);
    }

    /// <summary>Surrender ends the battle with result 4; a member who already left makes it a flight for those, the others are taken.</summary>
    [Fact]
    public void SurrenderEndsTheBattle()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (_, tactics) = Start();
        var b = tactics.Battle;
        b.RealCombat = true;
        tactics.Surrender();
        Assert.Equal(BattleResult.Surrendered, b.Result);

        (_, tactics) = Start();
        b = tactics.Battle;
        b.RealCombat = true;
        b[0].Floor = BattleEnd.LeftThroughExit;
        tactics.Surrender();
        Assert.Equal(BattleResult.Fled, b.Result);
        Assert.Equal(CharacterStatus.Able, b[0].Status);
        for (var i = 1; i < Combatant.PartySlots; i++)
        {
            if (b[i].Member is not null)
            {
                Assert.Equal(CharacterStatus.Away, b[i].Status);
            }
        }
    }

    /// <summary>Surrender outside real combat does nothing.</summary>
    [Fact]
    public void NoSurrenderOutsideRealCombat()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (_, tactics) = Start();
        tactics.Battle.RealCombat = false;
        tactics.Surrender();
        Assert.Equal(BattleResult.Running, tactics.Battle.Result);
    }

    /// <summary>The enemy information lists at most two kinds, by animation slot, with one combatant of each.</summary>
    [Fact]
    public void EnemyInfoTakesTwoKinds()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (_, tactics) = Start();
        var b = tactics.Battle;
        for (var i = Combatant.PartySlots; i < Combatant.Count; i++)
        {
            b[i].Status = CharacterStatus.Empty;
        }
        Assert.Empty(tactics.EnemyInfoKinds());
        int[] slots = [3, 3, 4, 5];
        for (var k = 0; k < slots.Length; k++)
        {
            var c = b[Combatant.PartySlots + k];
            c.Status = CharacterStatus.Able;
            c.AnimationSlot = slots[k];
        }
        Assert.Equal([Combatant.PartySlots, Combatant.PartySlots + 2], tactics.EnemyInfoKinds());
    }
}
