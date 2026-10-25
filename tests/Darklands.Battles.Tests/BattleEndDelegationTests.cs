using Darklands.Battles.Tests.BattleCalls;
using Darklands.Cards;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Tactics;
using Darklands.Core.Characters;
using Darklands.Core.Randomness;
using Darklands.Presentation.Battles;

namespace Darklands.Battles.Tests;

/// <summary>
/// The parts of combat_mode_leave (R014+0x0D86), combat_recover (R014+0x0DEE) and the end of the battle (R014+0x0694) that BattleEnd hands to the
/// tactical battle (IBattleEndActions, TacticalBattle.AfterBattle) happen there. They need the game files and pass vacuously without.
/// </summary>
public sealed class BattleEndDelegationTests
{
    private static readonly (int, int, int)[] Groups = [(34, 4, 8), (18, 5, 2), (34, 1, 2)];

    private static (BattleScreen Screen, TacticalBattle Tactics)? Begin() =>
        GameFolder.Files is { } files ? BattleScreenTests.Start(files, new BattleRequest(0x14, 863, Groups)) : null;

    [Fact]
    public void LeavingCombatModeResetsWhatTheOriginalResets()
    {
        if (Begin() is not var (_, tactics))
        {
            return;
        }
        var b = tactics.Battle;
        b.RealCombat = true;
        b.Alarm = 7;
        foreach (var c in b.Combatants)
        {
            c.Flags |= TacticalFlags.InFight | TacticalFlags.CombatBox;
        }
        typeof(TacticalBattle).GetProperty("WavesEnabled")!.SetValue(tactics, true);
        BattleEnd.LeaveCombatMode(b, tactics);
        Assert.False(b.RealCombat);
        Assert.Equal(0, b.Alarm);
        Assert.False(tactics.WavesEnabled);   // DS:D6EB = 0
        Assert.All(b.Combatants, c => Assert.Equal((TacticalFlags)0, c.Flags & (TacticalFlags.InFight | TacticalFlags.CombatBox)));   // &= 0x9FFF
    }

    [Fact]
    public void RecoveringWakesTheUnconsciousOnAFreeSpotWithTheSearchsDraws()
    {
        if (Begin() is not var (_, tactics))
        {
            return;
        }
        var b = tactics.Battle;
        var member = Enumerable.Range(0, Combatant.PartySlots).First(i => b[i].Status == CharacterStatus.Able);
        var m = b[member];
        m.Status = CharacterStatus.Unconscious;
        m.Endurance = 1;
        var before = ((MscRandom)b.Random).State;
        BattleEnd.Recover(b, tactics);
        Assert.Equal(CharacterStatus.Able, m.Status);
        Assert.True(m.Endurance >= (sbyte)(m.MaximumEndurance * 3 / 4));
        Assert.NotEqual(before, ((MscRandom)b.Random).State);   // the search drew random numbers
        Assert.True(tactics.Map.Collides(member, m.Position.X, m.Position.Y).IsFree);
    }

    [Fact]
    public void AfterABattleTheLevelIsSavedAndTheModifiersEnd()
    {
        if (Begin() is not var (_, tactics))
        {
            return;
        }
        tactics.LevelChanged = true;
        BattleCallsTests.Call(tactics, "AfterBattle");
        Assert.False(tactics.LevelChanged);   // DS:D4E4 cleared by the save of the level
    }
}
