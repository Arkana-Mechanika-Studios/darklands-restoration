using Darklands.Core.Characters;
using Darklands.Core.Events;
using Darklands.Core.Items;
using Darklands.Core.Rules;

namespace Darklands.Core.Tests;

/// <summary>
/// F-1 set 1b (the user's choices of 2026-11-25): the potions' and saints' protections last about a day and replace each other, the
/// fire ones guard against flame weapons only, St. Patrick, St. Pantaleon and St. Dunstan give what their texts say. Each test shows
/// the original's behaviour with the flag off and the fixed one with it on, on the same situation.
/// </summary>
public sealed class FixedRulesSet1bTests
{
    private static int Give(GameState s, int code, int quality)
    {
        var d = s.Item(code);
        Inventory.AddStack(s, 0, new ItemStack(code, d.Type, quality, 1, d.Weight));
        return s.Party.Slots[0].Pack.FindIndex(st => st.Code == code && st.Quality == quality);
    }

    private static void Drink(GameState s, int code, int quality) => PotionEffects.DrinkFromPack(s, 0, Give(s, code, quality));

    // a day and a half passes, and the events that ended run their ends
    private static void ADayLater(GameState s)
    {
        s.Date = s.Date.AddHours(36);
        EventExpiry.Run(s);
    }

    private static GameState Game(Action<OriginalBehaviour> fixes, bool fixedGame)
    {
        var s = TestGame.Create();
        if (fixedGame)
        {
            fixes(s.Behaviour);
        }
        foreach (var code in new[] { 0x69, 0x6A, 0x6B, 0x6D, 0x74 })
        {
            ((TestData)s.Data).Items[code] = ((TestData)s.Data).Items[code] with { Flags = 0x800, Type = 0x90 };
        }
        return s;
    }

    /// <summary>FixWeaponPotionsWearOff (B-11a75a): Deadly Blade adds up and stays; fixed, a second dose replaces the first and both go after a day.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeadlyBladeAddsUpAndStaysOrLastsADay(bool fixedGame)
    {
        var s = Game(b => b.FixWeaponPotionsWearOff = true, fixedGame);
        var m = s.Party.Slot(0);
        m.WeaponPotions = 0x01;
        Drink(s, 0x69, 20);                      // quality up to 25: +3 damage (high nibble)
        Assert.Equal(0x31, m.WeaponPotions);
        Drink(s, 0x69, 20);
        Assert.Equal(fixedGame ? 0x31 : 0x61, m.WeaponPotions);
        ADayLater(s);
        Assert.Equal(fixedGame ? 0x01 : 0x61, m.WeaponPotions);
    }

    /// <summary>FixArmourPotionsWearOff (B-d0b264): Firewall sets the charm nibble and stays; fixed, its strength alone, back after a day.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FirewallIsALastingCharmOrADaysFireStrength(bool fixedGame)
    {
        var s = Game(b => b.FixArmourPotionsWearOff = true, fixedGame);
        var m = s.Party.Slot(0);
        Drink(s, 0x74, 40);                      // quality 36..45: strength 90
        Assert.Equal(fixedGame ? 0x00 : 0x10, m.ArmourPotions & 0xF0);
        Assert.Equal(90, m.FirewallStrength);
        ADayLater(s);
        Assert.Equal(fixedGame ? 0 : 90, m.FirewallStrength);
        Assert.Equal(fixedGame ? 0x00 : 0x10, m.ArmourPotions & 0xF0);
    }

    /// <summary>FixArmourPotionsWearOff: Hardarmor's levels pile up and stay; fixed, one dose at a time, gone after a day.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HardarmorPilesUpOrLastsADay(bool fixedGame)
    {
        var s = Game(b => b.FixArmourPotionsWearOff = true, fixedGame);
        var m = s.Party.Slot(0);
        Drink(s, 0x6D, 30);                      // quality 26..35: 2 levels
        Drink(s, 0x6D, 30);
        Assert.Equal(fixedGame ? 2 : 4, m.ArmourPotions & 0x0F);
        ADayLater(s);
        Assert.Equal(fixedGame ? 0 : 4, m.ArmourPotions & 0x0F);
    }

    /// <summary>An old save's lasting bonus stays: a fixed dose taken back leaves what was there before (the user's choice).</summary>
    [Fact]
    public void AnOldSavesLastingBonusStays()
    {
        var s = Game(b => { b.FixWeaponPotionsWearOff = true; b.FixArmourPotionsWearOff = true; }, true);
        var m = s.Party.Slot(0);
        (m.WeaponPotions, m.FirewallStrength) = (0x40, 70);
        Drink(s, 0x69, 20);
        Drink(s, 0x74, 20);                      // strength 50, under the old 70
        Assert.Equal(0x70, m.WeaponPotions);
        Assert.Equal(70, m.FirewallStrength);
        ADayLater(s);
        Assert.Equal(0x40, m.WeaponPotions);
        Assert.Equal(70, m.FirewallStrength);
    }

    /// <summary>FixPolycarpFireForADay (B-44772a): the charm 0xB0 and 90 for good; fixed, 90 for each member for a day, no charm.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PolycarpsFireLastsADay(bool fixedGame)
    {
        var s = Game(b => b.FixPolycarpFireForADay = true, fixedGame);
        SaintBook.Help(s, 0, 110);
        foreach (var m in s.Party.Occupied)
        {
            Assert.Equal(90, m.FirewallStrength);
            Assert.Equal(fixedGame ? 0 : 0xB0, m.BattleCharms & 0xF0);
        }
        ADayLater(s);
        Assert.All(s.Party.Occupied, m => Assert.Equal(fixedGame ? 0 : 90, m.FirewallStrength));
    }

    /// <summary>FixCharityFire (B-315695) and FixErasmusFire (B-f32e0e): the charm nibble only; fixed, fire 90 / 50 and 50 for the party.</summary>
    [Theory]
    [InlineData(20, 90, 50)]
    [InlineData(45, 50, 50)]
    public void CharityAndErasmusGiveTheirFire(int saint, int prayedFor, int others)
    {
        var original = Game(_ => { }, false);
        SaintBook.Help(original, 1, saint);
        Assert.Equal(0xA0, original.Party.Slot(1).BattleCharms & 0xF0);
        Assert.Equal(0, original.Party.Slot(1).FirewallStrength);
        var s = Game(b => { b.FixCharityFire = true; b.FixErasmusFire = true; }, true);
        SaintBook.Help(s, 1, saint);
        Assert.Equal(prayedFor, s.Party.Slot(1).FirewallStrength);
        Assert.Equal(0, s.Party.Slot(1).BattleCharms & 0xF0);
        Assert.Equal(others, s.Party.Slot(0).FirewallStrength);
        ADayLater(s);
        Assert.All(s.Party.Occupied, m => Assert.Equal(0, m.FirewallStrength));
    }

    /// <summary>FixPantaleonFireAndAnimals (B-ce6144): the charm nibble; fixed, fire 30 and +1 against the four animals for the party, a day.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PantaleonGivesFireAndArmourAgainstAnimals(bool fixedGame)
    {
        var s = Game(b => b.FixPantaleonFireAndAnimals = true, fixedGame);
        SaintBook.Help(s, 0, 103);
        Assert.Equal(fixedGame ? 0 : 0xA0, s.Party.Slot(0).BattleCharms & 0xF0);
        Assert.Equal(fixedGame ? 30 : 0, s.Party.Slot(0).FirewallStrength);
        Assert.Equal(fixedGame ? 1 : 0, DayProtections.Amount(s, 1, DayProtections.AnimalArmour));
        ADayLater(s);
        Assert.Equal(0, DayProtections.Amount(s, 1, DayProtections.AnimalArmour));
    }

    /// <summary>FixPatrickMissileArmour (B-a19878): +2 to the charm byte for good; fixed, +2 against missiles for the one prayed for, a day.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PatrickGivesArmourAgainstMissiles(bool fixedGame)
    {
        var s = Game(b => b.FixPatrickMissileArmour = true, fixedGame);
        SaintBook.Help(s, 2, 104);
        Assert.Equal(fixedGame ? 0 : 2, s.Party.Slot(2).BattleCharms & 0x0F);
        Assert.Equal(fixedGame ? 2 : 0, DayProtections.Amount(s, 2, DayProtections.MissileArmour));
        Assert.Equal(0, DayProtections.Amount(s, 0, DayProtections.MissileArmour));
        ADayLater(s);
        Assert.Equal(0, DayProtections.Amount(s, 2, DayProtections.MissileArmour));
    }

    /// <summary>FixDunstanMetalArmour (B-243faa): +1 to the charm byte for good; fixed, +20 metal armour quality for the party, a day.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DunstanImprovesMetalArmour(bool fixedGame)
    {
        var s = Game(b => b.FixDunstanMetalArmour = true, fixedGame);
        SaintBook.Help(s, 0, 39);
        Assert.Equal(fixedGame ? 0 : 1, s.Party.Slot(0).BattleCharms & 0x0F);
        Assert.Equal(fixedGame ? 20 : 0, DayProtections.Amount(s, 1, DayProtections.MetalQuality));
        Assert.True(DayProtections.IsMetalArmour(73) && DayProtections.IsMetalArmour(84) && !DayProtections.IsMetalArmour(71) && !DayProtections.IsMetalArmour(80));
    }

    /// <summary>The weapon potions count with the weapons the manual names (FixWeaponPotionsWearOff).</summary>
    [Fact]
    public void TheWeaponPotionsCountWithTheirWeapons()
    {
        var s = Game(b => b.FixWeaponPotionsWearOff = true, true);
        var items = ((TestData)s.Data).Items;
        // weapon types 1 edged, 2 impact, 4 polearm, 5 flail, 6 bow, 32 a handgun (flags as DARKLAND.LST)
        foreach (var (code, flags) in new[] { (1, 1u), (2, 2u), (4, 4u), (5, 8u), (6, 0x20u), (32, 0x0800_0000u) })
        {
            items[code] = items[code] with { Type = code, Flags = flags };
        }
        Drink(s, 0x69, 20);                      // Deadly Blade +3 (0x30)
        Drink(s, 0x6A, 20);                      // Strongedge +3 (0x03)
        Drink(s, 0x6B, 20);                      // Great Power +14 (0x0E)
        var all = (byte)s.Party.Slot(0).WeaponPotions;
        Assert.Equal(0x30 + 0x03 + 0x0E, all);
        byte For(int weapon) => DayProtections.WeaponBonusFor(s, 0, all, weapon);
        Assert.Equal(0x30 + 0x03, For(1));       // edged: Deadly Blade, Strongedge
        Assert.Equal(0x0E, For(2));              // impact: Great Power
        Assert.Equal(0x30 + 0x03, For(4));       // polearm: Deadly Blade, Strongedge
        Assert.Equal(0x0E, For(5));              // flail: Great Power
        Assert.Equal(0x30, For(6));              // bow: Deadly Blade
        Assert.Equal(0x30 + 0x0E, For(32));      // handgun: Deadly Blade, Great Power
        Assert.Equal(0x00, For(-1));             // no weapon: none
    }
}
