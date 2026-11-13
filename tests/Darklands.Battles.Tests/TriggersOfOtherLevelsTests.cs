using System.Reflection;
using Darklands.Cards;
using Darklands.Core;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Tactics;

namespace Darklands.Battles.Tests;

/// <summary>
/// OriginalBehaviour.FixTriggersOfOtherLevels (split audit row 4): a wall alarm of the lone member's level, shown, at the place where a
/// member of the party's level stands. Faithful: R014+0xD6DA has no floor test, the alarm takes its card (the battle ends with it);
/// fixed: a member on another level springs nothing.
/// </summary>
public sealed class TriggersOfOtherLevelsTests
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int TriggerCountAt = 0x2184, TriggersAt = 0x2186;
    private const int Lone = 1, Card = 0x1234;

    private static TacticalBattle Run(OriginalBehaviour behaviour)
    {
        var (_, t) = BattleScreenTests.Start(GameFolder.Files!, new BattleRequest(0x7C, 3434, [(-1, 0, 0), (-1, 0, 0), (-1, 0, 0)]), s => s.Behaviour = behaviour);
        typeof(TacticalBattle).GetMethod("TakeStairs", Any)!.Invoke(t, [0, Lone]);
        var his = t.Battle[Lone].Floor;
        var b = Enumerable.Range(0, Combatant.PartySlots).First(i => i != Lone && t.Battle[i].IsAble);
        t.SwitchLevel(his);
        var lv = t.Level;
        var k = lv.GetByte(TriggerCountAt);
        var at = TriggersAt + 14 * k;
        var p = t.Battle[b].Position;
        lv.SetWord(at, p.X - 8);
        lv.SetWord(at + 2, p.Y - 8);
        lv.SetByte(at + 4, 16);
        lv.SetByte(at + 5, 16);
        lv.SetWord(at + 6, (int)TriggerHandler.WallAlarm);
        lv.SetWord(at + 10, 0);
        lv.SetWord(at + 12, Card);
        lv.SetByte(TriggerCountAt, k + 1);
        t.Battle.ResultCard = 0;
        typeof(TacticalBattle).GetMethod("Triggers", Any)!.Invoke(t, [b]);
        return t;
    }

    [Fact]
    public void FaithfulSpringsTheShownLevelsAlarm()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        Assert.Equal(Card, Run(OriginalBehaviour.Faithful).Battle.ResultCard);
    }

    [Fact]
    public void TheFixSpringsNothing()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        Assert.Equal(0, Run(OriginalBehaviour.DataSafe).Battle.ResultCard);
    }
}
