using System.Reflection;
using Darklands.Cards;
using Darklands.Core;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Tactics;

namespace Darklands.Battles.Tests;

/// <summary>
/// OriginalBehaviour.FixFollowersOnShownLevel (split audit row 9): the delayed placement of the enemies that followed the party down the
/// stairs, with the view on another level. Faithful: R025+0x0CFC places them on the level shown; fixed: on the party's level, the view
/// coming back.
/// </summary>
public sealed class FollowersOnShownLevelTests
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int Lone = 1;

    private static (TacticalBattle T, int Party, int Other, int Enemy) Place(OriginalBehaviour behaviour)
    {
        var (_, t) = BattleScreenTests.Start(GameFolder.Files!, new BattleRequest(0x7C, 3434, [(8, 3, 2), (-1, 0, 0), (-1, 0, 0)]), s => s.Behaviour = behaviour);
        var party = t.Battle.FloorShown;
        var enemy = Combatant.PartySlots;
        t.Battle[enemy].Status = Darklands.Core.Characters.CharacterStatus.Able;
        typeof(TacticalBattle).GetMethod("TakeStairs", Any)!.Invoke(t, [0, Lone]);
        var other = t.Battle[Lone].Floor;
        Assert.NotEqual(party, other);
        // the arrival spot: the party's leader's place on the party's level
        t.InitialEnemySpot = (t.Battle[t.Battle.Leader].Position, 4);
        t.SwitchLevel(other);
        typeof(TacticalBattle).GetMethod("PlaceInitialEnemies", Any)!.Invoke(t, []);
        return (t, party, other, enemy);
    }

    [Fact]
    public void FaithfulPlacesThemOnTheShownLevel()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (t, _, other, enemy) = Place(OriginalBehaviour.Faithful);
        Assert.Equal(other, t.Battle[enemy].Floor);
    }

    [Fact]
    public void TheFixPlacesThemOnThePartysLevel()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (t, party, other, enemy) = Place(OriginalBehaviour.DataSafe);
        Assert.Equal(party, t.Battle[enemy].Floor);
        Assert.Equal(other, t.Battle.FloorShown);
    }
}
