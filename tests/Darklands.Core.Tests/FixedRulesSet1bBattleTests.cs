using Darklands.Core.Battles;
using Darklands.Core.Randomness;
using Darklands.Core.Rules;

namespace Darklands.Core.Tests;

/// <summary>
/// F-1 set 1b in a fight: the day-long protections of St. Patrick (missiles), St. Pantaleon (the four animals) and St. Dunstan (metal
/// armour) change the harm a blow does only with their flag on (the same prayer, the same blow, the same dice). The blows are strong
/// enough that the damage level stays above 7 (no table lookup): level 8..20 does roll / 2 + level - 6, above 20 level + roll - 10.
/// </summary>
public sealed class FixedRulesSet1bBattleTests
{
    // a member (party slot 0) hit by an enemy combatant: armour 2, quality 25 where it is hit; the blow strike quality 25, damage 30
    private static int Hit(GameState game, bool flagOn, Action<OriginalBehaviour> flag, Action<Combatant, Combatant> blow)
    {
        var behaviour = OriginalBehaviour.Of(RulePreset.Original);
        if (flagOn)
        {
            flag(behaviour);
        }
        var battle = new Battle(new CombatTables(new byte[CombatTables.Length], new byte[16]), new MscRandom(7)) { DifficultyOf = game, Behaviour = behaviour };
        var target = battle[0];
        target.Member = game.Party.Slot(0);
        target.ArmourValue[0] = 2;
        target.ArmourQuality[0] = 25;
        var attacker = battle[5];
        attacker.StrikeQuality = 25;
        attacker.WeaponDamage = 30;
        attacker.WeaponClass = 3;
        blow(attacker, target);
        return Attacks.HitDamage(battle, 5, 0, HitResult.Hit, 0);
    }

    private static GameState Prayed(int saint, Action<OriginalBehaviour> flag)
    {
        var s = TestGame.Create();
        flag(s.Behaviour);
        SaintBook.Help(s, 0, saint);
        return s;
    }

    /// <summary>St. Patrick (B-a19878): +2 armour against a missile only, with FixPatrickMissileArmour on.</summary>
    [Fact]
    public void PatricksArmourCountsAgainstMissilesOnly()
    {
        void Flag(OriginalBehaviour b) => b.FixPatrickMissileArmour = true;
        var game = Prayed(104, Flag);
        // penetration 4 against armour 2; +2 makes them equal: the bonus is halved (30 -> 15)
        void Missile(Combatant a, Combatant _)
        {
            a.WeaponClass = 4;
            a.Action = CombatAction.Missile;
        }
        void Melee(Combatant a, Combatant _) => a.WeaponClass = 4;
        Assert.True(Hit(game, true, Flag, Missile) < Hit(game, false, Flag, Missile));
        Assert.Equal(Hit(game, false, Flag, Melee), Hit(game, true, Flag, Melee));
    }

    /// <summary>St. Pantaleon (B-ce6144): +1 armour against the Wolf, Boar, Bear and Spider only, with FixPantaleonFireAndAnimals on.</summary>
    [Theory]
    [InlineData(44, true)]     // a wolf
    [InlineData(51, true)]     // a boar
    [InlineData(41, true)]     // a bear
    [InlineData(45, true)]     // a spider
    [InlineData(1, false)]     // a man with a weapon
    public void PantaleonsArmourCountsAgainstTheFourAnimals(int weapon, bool animal)
    {
        void Flag(OriginalBehaviour b) => b.FixPantaleonFireAndAnimals = true;
        var game = Prayed(103, Flag);
        // penetration 3 against armour 2; +1 makes them equal: the bonus is halved
        void Blow(Combatant a, Combatant _) => a.DefaultWeapon = (sbyte)weapon;
        var off = Hit(game, false, Flag, Blow);
        var on = Hit(game, true, Flag, Blow);
        Assert.Equal(animal, on < off);
        Assert.True(on <= off);
    }

    /// <summary>St. Dunstan (B-243faa): +20 quality on metal armour only (two damage levels less), with FixDunstanMetalArmour on.</summary>
    [Theory]
    [InlineData(73, true)]     // chainmail on the body
    [InlineData(84, true)]     // plate on a limb
    [InlineData(71, false)]    // leather
    public void DunstansQualityCountsOnMetalArmour(int armour, bool metal)
    {
        void Flag(OriginalBehaviour b) => b.FixDunstanMetalArmour = true;
        var game = Prayed(39, Flag);
        void Blow(Combatant _, Combatant t) => t.ArmourType[0] = (sbyte)armour;
        var off = Hit(game, false, Flag, Blow);
        var on = Hit(game, true, Flag, Blow);
        Assert.Equal(metal ? off - 2 : off, on);
    }

    /// <summary>FixCharityFire (B-315695) and FixErasmusFire (B-f32e0e) each act alone: the other saint keeps the original's charm.</summary>
    [Theory]
    [InlineData(20, 90)]
    [InlineData(45, 50)]
    public void CharityAndErasmusEachFollowTheirOwnFlag(int saint, int fire)
    {
        var charityOn = TestGame.Create();
        charityOn.Behaviour.FixCharityFire = true;
        SaintBook.Help(charityOn, 0, saint);
        var erasmusOn = TestGame.Create();
        erasmusOn.Behaviour.FixErasmusFire = true;
        SaintBook.Help(erasmusOn, 0, saint);
        var (fixedOne, otherOne) = saint == 20 ? (charityOn, erasmusOn) : (erasmusOn, charityOn);
        Assert.Equal(fire, fixedOne.Party.Slot(0).FirewallStrength);
        Assert.Equal(0, fixedOne.Party.Slot(0).BattleCharms & 0xF0);
        Assert.Equal(0, otherOne.Party.Slot(0).FirewallStrength);
        Assert.Equal(0xA0, otherOne.Party.Slot(0).BattleCharms & 0xF0);
    }
}
