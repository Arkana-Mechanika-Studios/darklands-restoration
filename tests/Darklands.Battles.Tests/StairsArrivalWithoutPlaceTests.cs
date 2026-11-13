using System.Reflection;
using Darklands.Cards;
using Darklands.Core;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Tactics;

namespace Darklands.Battles.Tests;

/// <summary>
/// OriginalBehaviour.FixStairsArrivalWithoutPlace: mine 0x0F, seed 4885 (party-split audit fuzz). Member 1 alone down level 0's stairs to
/// level 1, then back up by his record: its arrival spot on level 0 gives no place, and use-exit keeps his level-1 coordinates (336, 280)
/// on level 0, a void cell; the original does the same (direct calls of use-exit, R023+0x1618, from a state of that battle: down to
/// (336, 280), up at (336, 280) on level 0). Fixed: he is placed around level 0's stairs back to level 1.
/// </summary>
public sealed class StairsArrivalWithoutPlaceTests
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int Who = 1;

    private static TacticalBattle UpAgain(OriginalBehaviour behaviour)
    {
        var (_, t) = BattleScreenTests.Start(GameFolder.Files!, new BattleRequest(0x0F, 4885, [(8, 3, 8), (2, 2, 4), (-1, 0, 0)]), s => s.Behaviour = behaviour);
        var exit = typeof(TacticalBattle).GetMethod("TakeStairs", Any)!;
        exit.Invoke(t, [0, Who]);
        Assert.Equal(1, t.Battle[Who].Level);
        Assert.Equal(new MapPoint(336, 280), t.Battle[Who].Position);
        t.SwitchLevel(1);
        exit.Invoke(t, [t.Battle[Who].StairsBack, Who]);
        Assert.Equal(0, t.Battle[Who].Level);
        if (t.Battle.LevelShown != 0)
        {
            t.SwitchLevel(0);
        }
        return t;
    }

    [Fact]
    public void FaithfulLeavesHimInTheVoid()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var t = UpAgain(OriginalBehaviour.Faithful);
        Assert.Equal(new MapPoint(336, 280), t.Battle[Who].Position);
        Assert.True(t.Map.Collides(Who, 336, 280).IsVoid);
    }

    [Fact]
    public void TheFixPlacesHimByTheStairs()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var t = UpAgain(OriginalBehaviour.DataSafe);
        var p = t.Battle[Who].Position;
        Assert.NotEqual(new MapPoint(336, 280), p);
        Assert.False(t.Map.Collides(Who, p.X, p.Y).IsVoid);
    }
}
