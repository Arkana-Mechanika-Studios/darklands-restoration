using System.Reflection;
using Darklands.Cards;
using Darklands.Core;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Tactics;

namespace Darklands.Battles.Tests;

/// <summary>
/// OriginalBehaviour.FixFloorsOfResumedBattle (split audit row 17): a battle taken up again in mode 1 after one that ended with a
/// member alone on another level. The start puts every member on the level shown (fight-main-loop R014+0x0000: DS:A6B4 = DS:11CA) and
/// mode 1 keeps the x/y; Faithful: the lone member is on the shown level at his own level's coordinates; fixed: on his own level.
/// </summary>
public sealed class FloorsOfResumedBattleTests
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int Lone = 1;

    private static (TacticalBattle T, int Shown, int His) Resume(OriginalBehaviour behaviour)
    {
        var (_, t) = BattleScreenTests.Start(GameFolder.Files!, new BattleRequest(0x7C, 3434, [(-1, 0, 0), (-1, 0, 0), (-1, 0, 0)]), s => s.Behaviour = behaviour);
        var shown = t.Battle.LevelShown;
        typeof(TacticalBattle).GetMethod("TakeStairs", Any)!.Invoke(t, [0, Lone]);
        var his = t.Battle[Lone].Level;
        Assert.NotEqual(shown, his);
        // the end, as BattleScreen keeps it for the battlefield
        var store = t.Field.Store;
        var kept = new (MapPoint, int, int)?[Combatant.PartySlots];
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            kept[i] = (t.Battle[i].Position, t.Battle[i].Facing, t.Battle[i].SpriteFacing);
            store.PartyLevels[i] = t.Battle[i].Level;
        }
        // the next start: everybody on the level shown, then mode 1's places
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            t.Battle[i].Level = t.Battle.LevelShown;
        }
        typeof(TacticalBattle).GetMethod("KeepPlaces", Any)!.Invoke(t, [kept]);
        return (t, shown, his);
    }

    [Fact]
    public void FaithfulPutsHimOnTheShownLevel()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (t, shown, _) = Resume(OriginalBehaviour.Faithful);
        Assert.Equal(shown, t.Battle[Lone].Level);
    }

    [Fact]
    public void TheFixKeepsHisOwnLevel()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (t, _, his) = Resume(OriginalBehaviour.DataSafe);
        Assert.Equal(his, t.Battle[Lone].Level);
    }
}
