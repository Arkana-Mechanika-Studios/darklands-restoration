using System.Reflection;
using Darklands.Cards;
using Darklands.Core;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Tactics;

namespace Darklands.Battles.Tests;

/// <summary>
/// OriginalBehaviour.FixWalkingOnOwnLevel (split audit row 5): a member on the party's level while another level is shown, at a point
/// where the two levels differ in wall or void. Faithful: fighter-collision-pass R014+0x5ECA tests the shown level's cells; fixed: his own
/// level's.
/// </summary>
public sealed class WalkingOnOwnLevelTests
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int Lone = 1;

    private static bool Blocked(Blocking b) => b.IsVoid || (b.Mask & Blocking.WallBit) != 0;

    /// <summary>A point where the walker's own level and the level shown differ in wall or void; whether the battle's own test then
    /// finds it blocked, and whether his own level does.</summary>
    private static (bool Battle, bool Own, bool Found) Test(OriginalBehaviour behaviour)
    {
        var (_, t) = BattleScreenTests.Start(GameFolder.Files!, new BattleRequest(0x7C, 3434, [(-1, 0, 0), (-1, 0, 0), (-1, 0, 0)]), s => s.Behaviour = behaviour);
        var party = t.Battle.FloorShown;
        typeof(TacticalBattle).GetMethod("TakeExit", Any)!.Invoke(t, [0, Lone]);
        var his = t.Battle[Lone].Floor;
        var walker = Enumerable.Range(0, Combatant.PartySlots).First(i => i != Lone && t.Battle[i].IsAble);
        t.SwitchLevel(his);
        var resolver = t.Map.OwnLevel;
        for (var py = 40; py < BattlefieldLevel.Size * 16 - 40; py += 4)
        {
            for (var px = 80; px < BattlefieldLevel.Size * 32 - 80; px += 8)
            {
                t.Map.OwnLevel = null;
                var shown = Blocked(t.Map.Collides(walker, px, py));
                t.Map.OwnLevel = _ => t.LevelAt(party);
                var own = Blocked(t.Map.Collides(walker, px, py));
                if (shown == own)
                {
                    continue;
                }
                t.Map.OwnLevel = resolver;
                return (Blocked(t.Map.Collides(walker, px, py)), own, true);
            }
        }
        return (false, false, false);
    }

    [Fact]
    public void FaithfulTestsTheShownLevelsCells()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (battle, own, found) = Test(OriginalBehaviour.Faithful);
        Assert.True(found);
        Assert.NotEqual(own, battle);
    }

    [Fact]
    public void TheFixTestsHisOwn()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (battle, own, found) = Test(OriginalBehaviour.DataSafe);
        Assert.True(found);
        Assert.Equal(own, battle);
    }
}
