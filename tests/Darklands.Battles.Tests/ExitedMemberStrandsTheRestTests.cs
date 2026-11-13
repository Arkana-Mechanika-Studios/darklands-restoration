using System.Reflection;
using Darklands.Cards;
using Darklands.Core;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Tactics;
using Darklands.Core.Characters;

namespace Darklands.Battles.Tests;

/// <summary>
/// OriginalBehaviour.FixExitedMemberStrandsTheRest (split audit row 10): one member gone through an exit (status 1, floor -99), the
/// others one by one down the stairs. Faithful: the last one does not bring the party together (use-exit's alone loop counts the one
/// gone) and the end test (R014+0x08FE) takes the empty enemies' floor for the party's flight: the battle ends as fled or left and the
/// members on the other level become Away. Fixed: the party comes together below and nobody becomes Away.
/// </summary>
public sealed class ExitedMemberStrandsTheRestTests
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static (TacticalBattle T, int Below, int[] Others) Run(OriginalBehaviour behaviour)
    {
        var (_, t) = BattleScreenTests.Start(GameFolder.Files!, new BattleRequest(0x7C, 3434, [(-1, 0, 0), (-1, 0, 0), (-1, 0, 0)]), s => s.Behaviour = behaviour);
        var party = t.Battle.LevelShown;
        var able = Enumerable.Range(0, Combatant.PartySlots).Where(i => t.Battle[i].IsAble).ToArray();
        Assert.True(able.Length >= 2);
        // the first one leaves through an exit (LeaveField's writes)
        var gone = t.Battle[able[0]];
        gone.Level = BattleEnd.LeftThroughExit;
        gone.Flags |= TacticalFlags.LeftField;
        var others = able.Skip(1).ToArray();
        foreach (var i in others)
        {
            typeof(TacticalBattle).GetMethod("TakeStairs", Any)!.Invoke(t, [0, i]);
        }
        var below = t.Battle[others[0]].Level;
        Assert.NotEqual(party, below);
        return (t, below, others);
    }

    [Fact]
    public void FaithfulEndsTheBattleAndTheOthersBecomeAway()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (t, below, others) = Run(OriginalBehaviour.Faithful);
        Assert.NotEqual(below, t.Battle.PartyLevel);
        Assert.True(BattleEnd.IsOver(t.Battle, t));
        Assert.All(others, i => Assert.Equal(CharacterStatus.Away, t.Battle[i].Status));
    }

    [Fact]
    public void TheFixBringsThePartyTogetherBelow()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (t, below, others) = Run(OriginalBehaviour.DataSafe);
        Assert.Equal(below, t.Battle.PartyLevel);
        Assert.Equal(below, t.Battle.LevelShown);
        Assert.False(BattleEnd.IsOver(t.Battle, t));
        Assert.All(others, i => Assert.Equal(CharacterStatus.Able, t.Battle[i].Status));
    }
    /// <summary>Split audit row 16: the party flees through the exit with an enemy left on its level and one member alone downstairs.
    /// Faithful: the fled branch of R014+0x08FE ends the battle and turns the lone member Away; fixed (the same switch): he stays.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ALoneMemberDownstairsIsNotLeftAwayByTheFlight(bool fix)
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (_, t) = BattleScreenTests.Start(GameFolder.Files!, new BattleRequest(0x7C, 3434, [(-1, 0, 0), (-1, 0, 0), (-1, 0, 0)]),
            s => s.Behaviour = fix ? OriginalBehaviour.DataSafe : OriginalBehaviour.Faithful);
        var party = t.Battle.LevelShown;
        var enemy = t.Battle[Combatant.PartySlots];
        enemy.Status = CharacterStatus.Able;
        enemy.Level = party;
        var able = Enumerable.Range(0, Combatant.PartySlots).Where(i => t.Battle[i].IsAble).ToArray();
        var lone = able[^1];
        typeof(TacticalBattle).GetMethod("TakeStairs", Any)!.Invoke(t, [0, lone]);
        Assert.NotEqual(party, t.Battle[lone].Level);
        foreach (var i in able[..^1])
        {
            t.Battle[i].Level = BattleEnd.LeftThroughExit;
            t.Battle[i].Flags |= TacticalFlags.LeftField;
        }
        var over = BattleEnd.IsOver(t.Battle, t);
        if (fix)
        {
            Assert.Equal(CharacterStatus.Able, t.Battle[lone].Status);
            Assert.NotEqual(BattleResult.Fled, t.Battle.Result);
        }
        else
        {
            Assert.True(over);
            Assert.Equal(CharacterStatus.Away, t.Battle[lone].Status);
        }
    }
}
