using Darklands.Core.Battles;
using Darklands.Core.Characters;
using Darklands.Core.Events;
using Darklands.Core.Items;
using Darklands.Core.Randomness;
using Darklands.Core.Rules;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Core.Tests;

/// <summary>
/// F-1 set 2 (the user's choices of 2026-11-27), the rules: healing leaves a boost alone, Great Power raises the weapon's quality, and
/// St. Dismas, St. Christopher, St. Michael, St. Anthony and St. Thomas Aquinas give what their texts say. Each test shows the original's
/// behaviour with the flag off and the fixed one with it on, on the same situation.
/// </summary>
public sealed class FixedRulesSet2Tests
{
    private const int GreatPower = 0x6B;
    private const int Anthony = 9, Christopher = 22, Dismas = 35, Michael = 96, Aquinas = 125;

    private static GameState Game(Action<OriginalBehaviour> fix, bool fixedGame)
    {
        var s = TestGame.Create();
        if (fixedGame)
        {
            fix(s.Behaviour);
        }
        var items = ((TestData)s.Data).Items;
        items[GreatPower] = items[GreatPower] with { Flags = 0x800, Type = 0x90 };
        items[5] = items[5] with { Flags = 0x2, Weight = 4 };         // an impact weapon (type 5); item 3 is an edged one
        return s;
    }

    private static void Drink(GameState s, int code, int quality)
    {
        var d = s.Item(code);
        Inventory.AddStack(s, 0, new ItemStack(code, d.Type, quality, 1, d.Weight));
        PotionEffects.DrinkFromPack(s, 0, s.Party.Slots[0].Pack.FindIndex(st => st.Code == code && st.Quality == quality));
    }

    private static void HoursLater(GameState s, int hours)
    {
        s.Date = s.Date.AddHours(hours);
        EventExpiry.Run(s);
    }

    private static int Skill(GameState s, int slot, Skill k) => s.Party.Slot(slot).Skills[(int)k];

    private static int Attr(GameState s, int slot, Attribute a) => s.Party.Slot(slot).Attributes[(int)a];

    // ---- B-308359 -------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// FixHealingKeepsBoosts (B-308359): a heal pulls an attribute above its maximum down (the replay: Strength 55 of 45, Essence of Grace
    /// grade 20's 40 % -> 51); fixed, it is left alone; below the maximum both heal alike, and the fixed heal never goes past it.
    /// </summary>
    [Theory]
    [InlineData(false, 55, 40, 51)]
    [InlineData(true, 55, 40, 55)]
    [InlineData(false, 30, 40, 36)]
    [InlineData(true, 30, 40, 36)]
    [InlineData(false, 40, 150, 47)]
    [InlineData(true, 40, 150, 45)]
    public void HealingAnAttributeAboveItsMaximum(bool fixedGame, int strength, int percent, int expected)
    {
        var s = Game(b => b.FixHealingKeepsBoosts = true, fixedGame);
        var m = s.Party.Slot(0);
        m.AttributeMaximums[(int)Attribute.Strength] = 45;
        m.Attributes[(int)Attribute.Strength] = strength;
        MemberRules.HealAttribute(s, 0, (int)Attribute.Strength, percent);
        Assert.Equal(expected, m.Attributes[(int)Attribute.Strength]);
    }

    // ---- B-555b7a -------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// FixGreatPowerQuality (B-555b7a): Great Power adds 14 to the bonus byte at each dose (the replay: 0x00 -> 0x0E -> 0x1C); fixed, the
    /// byte is untouched, a running quality of 14 that a second dose replaces, gone after about a day.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GreatPowerIsTheByteOrADaysQuality(bool fixedGame)
    {
        var s = Game(b => b.FixGreatPowerQuality = true, fixedGame);
        var m = s.Party.Slot(0);
        Drink(s, GreatPower, 20);
        Drink(s, GreatPower, 20);
        Assert.Equal(fixedGame ? 0x00 : 0x1C, m.WeaponPotions);
        Assert.Equal(fixedGame ? 14 : 0, DayProtections.Amount(s, 0, DayProtections.GreatPowerQuality));
        HoursLater(s, 36);
        Assert.Equal(0, DayProtections.Amount(s, 0, DayProtections.GreatPowerQuality));
        Assert.Equal(fixedGame ? 0x00 : 0x1C, m.WeaponPotions);
    }

    // a member (slot 0) strikes an enemy, or an enemy strikes the member: quality 25 against armour quality 25, damage 30 (level above 7)
    private static int Blow(GameState game, OriginalBehaviour behaviour, bool memberStrikes, int weapon, int armourType = 73)
    {
        var battle = new Battle(new CombatTables(new byte[CombatTables.Length], new byte[16]), new MscRandom(7)) { DifficultyOf = game, Behaviour = behaviour };
        var (a, t) = memberStrikes ? (0, 5) : (5, 0);
        battle[0].Member = game.Party.Slot(0);
        battle[a].StrikeQuality = 25;
        battle[a].WeaponDamage = 30;
        battle[a].WeaponClass = 3;
        battle[a].DefaultWeapon = (sbyte)weapon;
        battle[t].ArmourValue[0] = 2;
        battle[t].ArmourQuality[0] = 25;
        battle[t].ArmourType[0] = (sbyte)armourType;
        return Attacks.HitDamage(battle, a, t, HitResult.Hit, 0);
    }

    private static OriginalBehaviour With(Action<OriginalBehaviour> fix, bool on)
    {
        var b = OriginalBehaviour.Of(RulePreset.Original);
        if (on)
        {
            fix(b);
        }
        return b;
    }

    /// <summary>FixGreatPowerQuality in a fight: +14 quality (one damage level) with an impact weapon only.</summary>
    [Theory]
    [InlineData(5, true)]      // an impact weapon
    [InlineData(3, false)]     // an edged weapon
    public void GreatPowersQualityCountsWithImpactWeapons(int weapon, bool counts)
    {
        void Fix(OriginalBehaviour b) => b.FixGreatPowerQuality = true;
        var game = Game(Fix, true);
        Drink(game, GreatPower, 20);
        var off = Blow(game, With(Fix, false), true, weapon);
        var on = Blow(game, With(Fix, true), true, weapon);
        Assert.Equal(counts ? off + 1 : off, on);
    }

    // ---- the saints ---------------------------------------------------------------------------------------------------------------

    /// <summary>FixDismasForTheParty (B-5eb6aa): the one prayed for alone; fixed, the party's Agility, Woodwise, Stealth and his Charisma and Speak Common.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DismasHelpsOneOrTheParty(bool fixedGame)
    {
        var s = Game(b => b.FixDismasForTheParty = true, fixedGame);
        var (woodwise, agility, stealth) = (Skill(s, 1, Characters.Skill.Woodwise), Attr(s, 1, Attribute.Agility), Skill(s, 1, Characters.Skill.Stealth));
        var (charisma, speak, streetwise) = (Attr(s, 0, Attribute.Charisma), Skill(s, 0, Characters.Skill.SpeakCommon), Skill(s, 0, Characters.Skill.Streetwise));
        var otherStreetwise = Skill(s, 1, Characters.Skill.Streetwise);
        SaintBook.Help(s, 0, Dismas);
        Assert.True(Skill(s, 0, Characters.Skill.Streetwise) >= streetwise + 20);
        Assert.Equal(fixedGame, Skill(s, 1, Characters.Skill.Woodwise) > woodwise);
        Assert.Equal(fixedGame, Attr(s, 1, Attribute.Agility) > agility);
        Assert.Equal(fixedGame, Skill(s, 1, Characters.Skill.Stealth) > stealth);
        Assert.Equal(fixedGame, Attr(s, 0, Attribute.Charisma) > charisma);
        Assert.Equal(fixedGame, Skill(s, 0, Characters.Skill.SpeakCommon) > speak);
        Assert.Equal(otherStreetwise, Skill(s, 1, Characters.Skill.Streetwise));
    }

    /// <summary>FixChristopherTravelAndHealing (B-623452): no heal, no travel; fixed, Strength 20 % and Endurance 30 % of what is lost, and a week of faster travel.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChristopherHealsAndSpeedsTravelForAWeek(bool fixedGame)
    {
        var s = Game(b => b.FixChristopherTravelAndHealing = true, fixedGame);
        var m = s.Party.Slot(0);
        m.AttributeMaximums[(int)Attribute.Strength] = 40;
        m.Attributes[(int)Attribute.Strength] = 20;
        m.AttributeMaximums[(int)Attribute.Endurance] = 40;
        m.Attributes[(int)Attribute.Endurance] = 10;
        SaintBook.Help(s, 0, Christopher);
        Assert.Equal(fixedGame ? 24 : 20, m.Attributes[(int)Attribute.Strength]);
        Assert.Equal(fixedGame ? 19 : 10, m.Attributes[(int)Attribute.Endurance]);
        Assert.Equal(fixedGame, DayProtections.RunsOnTheParty(s, DayProtections.ChristopherTravel));
        HoursLater(s, 6 * 24);
        Assert.Equal(fixedGame, DayProtections.RunsOnTheParty(s, DayProtections.ChristopherTravel));
        HoursLater(s, 2 * 24);
        Assert.False(DayProtections.RunsOnTheParty(s, DayProtections.ChristopherTravel));
    }

    /// <summary>FixMichaelArmsQuality (B-723764): his gifts alone; fixed, every member's weapons and armour +20 quality in a fight, about a day.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MichaelImprovesThePartysWeaponsAndArmour(bool fixedGame)
    {
        void Fix(OriginalBehaviour b) => b.FixMichaelArmsQuality = true;
        var s = Game(Fix, fixedGame);
        SaintBook.Help(s, 0, Michael);
        Assert.All(Enumerable.Range(0, s.Party.Slots.Count).Where(i => s.Party.OccupiedSlot(i) is not null),
            i => Assert.Equal(fixedGame ? 20 : 0, DayProtections.Amount(s, i, DayProtections.MichaelArmourQuality)));
        // two damage levels more dealt (any weapon), two less taken (any armour worn), with the flag on
        Assert.Equal(Blow(s, With(Fix, false), true, 3) + (fixedGame ? 2 : 0), Blow(s, With(Fix, true), true, 3));
        Assert.Equal(Blow(s, With(Fix, false), false, 3, armourType: 71) - (fixedGame ? 2 : 0), Blow(s, With(Fix, true), false, 3, armourType: 71));
        HoursLater(s, 36);
        Assert.Equal(0, DayProtections.Amount(s, 0, DayProtections.MichaelWeaponQuality));
    }

    /// <summary>FixAnthonyHealingOnly (B-747355): Strength and Endurance boosted past the maximum and taken back; fixed, healed up to the maximum, kept.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnthonyBoostsOrHeals(bool fixedGame)
    {
        var s = Game(b => b.FixAnthonyHealingOnly = true, fixedGame);
        var full = s.Party.Slot(0);
        full.AttributeMaximums[(int)Attribute.Strength] = 40;
        full.Attributes[(int)Attribute.Strength] = 40;
        SaintBook.Help(s, 0, Anthony);
        Assert.Equal(fixedGame, full.Attributes[(int)Attribute.Strength] == 40);
        Assert.Equal(!fixedGame, full.Attributes[(int)Attribute.Strength] > 40);
        var hurt = s.Party.Slot(1);
        hurt.AttributeMaximums[(int)Attribute.Endurance] = 40;
        hurt.Attributes[(int)Attribute.Endurance] = 30;
        SaintBook.Help(s, 1, Anthony);
        var healed = hurt.Attributes[(int)Attribute.Endurance];
        Assert.InRange(healed, 34, 39);
        HoursLater(s, 36);
        Assert.Equal(fixedGame ? healed : 30, hurt.Attributes[(int)Attribute.Endurance]);
    }

    /// <summary>FixAquinasReligion (B-78a26c): no Religion, no standing; fixed, Religion +20..39 and the standing where the city has a monastery.</summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public void AquinasGivesReligionAndStandingAtPlacesOfLearning(bool fixedGame, bool monastery)
    {
        var s = Game(b => b.FixAquinasReligion = true, fixedGame);
        ((TestData)s.Data).CityBytes[0x5E] = monastery ? (byte)0x80 : (byte)0;
        var religion = Skill(s, 0, Characters.Skill.Religion);
        var standing = s.Locations[s.CurrentLocation].Reputation;
        SaintBook.Help(s, 0, Aquinas);
        var gain = Skill(s, 0, Characters.Skill.Religion) - religion;
        if (fixedGame)
        {
            Assert.InRange(gain, 1, 39);
        }
        else
        {
            Assert.Equal(0, gain);
        }
        Assert.Equal(fixedGame && monastery, s.Locations[s.CurrentLocation].Reputation > standing);
    }
}
