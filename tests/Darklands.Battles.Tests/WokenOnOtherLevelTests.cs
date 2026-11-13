using System.Reflection;
using Darklands.Cards;
using Darklands.Core;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Tactics;

namespace Darklands.Battles.Tests;

/// <summary>
/// OriginalBehaviour.FixWokenOnOtherLevel (split audit row 15): a member woken on a level not shown. Faithful: fight-recovery
/// R014+0x0DEE searches his free spot with the shown level's walls; fixed: with his own level's, the view coming back.
/// </summary>
public sealed class WokenOnOtherLevelTests
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int Lone = 1;

    private static (TacticalBattle T, int Party, int His) Wake(OriginalBehaviour behaviour)
    {
        var (_, t) = BattleScreenTests.Start(GameFolder.Files!, new BattleRequest(0x7C, 3434, [(-1, 0, 0), (-1, 0, 0), (-1, 0, 0)]), s => s.Behaviour = behaviour);
        var party = t.Battle.FloorShown;
        typeof(TacticalBattle).GetMethod("TakeStairs", Any)!.Invoke(t, [0, Lone]);
        var his = t.Battle[Lone].Floor;
        Assert.NotEqual(party, his);
        Assert.Equal(party, t.Battle.FloorShown);
        typeof(TacticalBattle).GetMethod("PlaceWoken", Any)!.Invoke(t, [Lone]);
        return (t, party, his);
    }

    private static bool FreeOn(TacticalBattle t, int floor, int who)
    {
        var shown = t.Battle.FloorShown;
        t.SwitchLevel(floor);
        var p = t.Battle[who].Position;
        t.Map.StrictWalls = true;
        var free = t.Map.Collides(who, p.X, p.Y).IsFree;
        t.Map.StrictWalls = false;
        t.SwitchLevel(shown);
        return free;
    }

    [Fact]
    public void FaithfulSearchesWithTheShownLevelsWalls()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (t, party, _) = Wake(OriginalBehaviour.Faithful);
        Assert.True(FreeOn(t, party, Lone));
    }

    [Fact]
    public void TheFixSearchesWithHisOwn()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (t, party, his) = Wake(OriginalBehaviour.DataSafe);
        Assert.True(FreeOn(t, his, Lone));
        Assert.Equal(party, t.Battle.FloorShown);
        Assert.Equal(his, t.Battle[Lone].Floor);
    }
}
