using System.Reflection;
using Darklands.Cards;
using Darklands.Core;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Tactics;
using Darklands.Core.Characters;

namespace Darklands.Battles.Tests;

/// <summary>
/// OriginalBehaviour.FixCombatAcrossLevels (split audit rows 6 and 7): a member alone on another level, his level shown, the party and an
/// enemy on the party's level. Faithful: the enemy turn drops the enemy's target on the party's level (R018+0x0706: not on DS:11CA), and
/// a missile is made on the level shown (R018+0x0000); fixed: the target is kept, the missile flies on the shooter's level.
/// </summary>
public sealed class CombatAcrossLevelsTests
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int Lone = 1;

    private static (TacticalBattle T, int Party, int His, int Enemy, int Target) Split(OriginalBehaviour behaviour)
    {
        var (_, t) = BattleScreenTests.Start(GameFolder.Files!, new BattleRequest(0x7C, 3434, [(-1, 0, 0), (-1, 0, 0), (-1, 0, 0)]), s => s.Behaviour = behaviour);
        var party = t.Battle.FloorShown;
        typeof(TacticalBattle).GetMethod("TakeExit", Any)!.Invoke(t, [0, Lone]);
        var his = t.Battle[Lone].Floor;
        Assert.NotEqual(party, his);
        var target = Enumerable.Range(0, Combatant.PartySlots).First(i => i != Lone && t.Battle[i].IsAble);
        var enemy = Combatant.PartySlots;
        var en = t.Battle[enemy];
        en.Status = CharacterStatus.Able;
        en.Floor = party;
        en.Position = new MapPoint(t.Battle[target].Position.X + 24, t.Battle[target].Position.Y);
        en.Target = (sbyte)target;
        t.SwitchLevel(his);
        return (t, party, his, enemy, target);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheEnemyKeepsItsTargetOnItsOwnLevelOnlyWithTheFix(bool fix)
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (t, _, _, enemy, target) = Split(fix ? OriginalBehaviour.DataSafe : OriginalBehaviour.Faithful);
        typeof(TacticalBattle).GetMethod("EnemyTurn", Any)!.Invoke(t, []);
        if (fix)
        {
            Assert.Equal(target, t.Battle[enemy].Target);
        }
        else
        {
            Assert.NotEqual(target, t.Battle[enemy].Target);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheMissileFliesOnTheShootersLevelOnlyWithTheFix(bool fix)
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (t, party, his, enemy, target) = Split(fix ? OriginalBehaviour.DataSafe : OriginalBehaviour.Faithful);
        t.LaunchMissile(enemy, target, 0);
        Assert.Equal(fix ? party : his, t.Missiles[^1].Floor);
    }
}
