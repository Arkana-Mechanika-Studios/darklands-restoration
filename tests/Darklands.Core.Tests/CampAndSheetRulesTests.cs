using Darklands.Core.Time;
using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Core.Events;
using Darklands.Core.Items;
using Darklands.Core.Rules;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Core.Tests;

/// <summary>The rules behind the character sheet, the alchemy screen, the camp and the party information screen.</summary>
public sealed class CampAndSheetRulesTests
{
    private static TestData Data(GameState s) => (TestData)s.Data;

    private static int Give(GameState s, int member, int code, int quality, int count = 1)
    {
        var d = s.Item(code);
        Inventory.AddStack(s, member, new ItemStack(code, d.Type, quality, count, d.Weight));
        return s.Party.Members[member].Pack.FindIndex(st => st.Code == code && st.Quality == quality);
    }

    // ---- the pack -------------------------------------------------------------------

    [Fact]
    public void ReadyingAHandWeaponTakesTheStacksTypeAndQualityAndItsWeight()
    {
        var s = TestGame.Create();
        var m = s.Party.Members[0];
        m.Pack.Clear();
        var index = Give(s, 0, 3, 30);
        PackRules.Equip(s, 0, index);
        Assert.Equal(new EquippedItem(3, 30), m.Weapon);
        Assert.Equal(4, m.Encumbrance);
    }

    [Theory]
    [InlineData(0x45, EquipmentSlot.VitalArmor)]
    [InlineData(0x50, EquipmentSlot.LimbArmor)]
    [InlineData(0x60, EquipmentSlot.Shield)]
    public void ArmourAndShieldsGoByItemType(int code, EquipmentSlot slot)
    {
        var s = TestGame.Create();
        s.Party.Members[0].Pack.Clear();
        PackRules.Equip(s, 0, Give(s, 0, code, 25));
        Assert.Equal(code, Inventory.EquippedType(s, 0, slot));
    }

    [Fact]
    public void MissileWeaponsAreReadiedInTheirOwnSlot()
    {
        var s = TestGame.Create();
        Data(s).Items[20] = Data(s).Items[20] with { Flags = 0x20 };
        s.Party.Members[0].Pack.Clear();
        PackRules.Equip(s, 0, Give(s, 0, 20, 25));
        Assert.Equal(20, s.Party.Members[0].MissileWeapon.Type);
    }

    [Fact]
    public void AnythingElseIsNotReadied()
    {
        var s = TestGame.Create();
        var m = s.Party.Members[0];
        m.Pack.Clear();
        var before = (m.Weapon, m.VitalArmor, m.LimbArmor, m.Shield, m.MissileWeapon);
        PackRules.Equip(s, 0, Give(s, 0, 0x30, 25));
        Assert.Equal(before, (m.Weapon, m.VitalArmor, m.LimbArmor, m.Shield, m.MissileWeapon));
    }

    [Fact]
    public void GivingOneMovesASingleItem()
    {
        var s = TestGame.Create();
        s.Party.Members[0].Pack.Clear();
        s.Party.Members[1].Pack.Clear();
        PackRules.GiveOne(s, 0, 1, Give(s, 0, 50, 25, 3));
        Assert.Equal(2, s.Party.Members[0].Pack[0].Count);
        Assert.Equal(1, s.Party.Members[1].Pack.Single().Count);
    }

    [Fact]
    public void GivingTheStackMovesItAll()
    {
        var s = TestGame.Create();
        s.Party.Members[0].Pack.Clear();
        s.Party.Members[1].Pack.Clear();
        PackRules.GiveStack(s, 0, 1, Give(s, 0, 50, 25, 3));
        Assert.Empty(s.Party.Members[0].Pack);
        Assert.Equal(3, s.Party.Members[1].Pack.Single().Count);
    }

    [Fact]
    public void NobodyGivesToThemselvesOrToAFullPack()
    {
        var s = TestGame.Create();
        s.Party.Members[0].Pack.Clear();
        var index = Give(s, 0, 50, 25, 3);
        PackRules.GiveOne(s, 0, 0, index);
        Assert.Equal(3, s.Party.Members[0].Pack[index].Count);
        var full = s.Party.Members[1].Pack;
        full.Clear();
        for (var i = 0; i < Inventory.MaxStacks; i++)
        {
            full.Add(new ItemStack(60, 60, i, 1, 1));
        }
        PackRules.GiveStack(s, 0, 1, index);
        Assert.Equal(3, s.Party.Members[0].Pack[index].Count);
    }

    [Fact]
    public void MovingAStackShiftsTheOthers()
    {
        var s = TestGame.Create();
        var pack = s.Party.Members[0].Pack;
        pack.Clear();
        Give(s, 0, 50, 25);
        Give(s, 0, 51, 25);
        Give(s, 0, 52, 25);
        PackRules.Move(s, 0, 2, 0);
        Assert.Equal([52, 50, 51], pack.Select(st => st.Code));
        PackRules.Move(s, 0, 0, 2);
        Assert.Equal([50, 51, 52], pack.Select(st => st.Code));
    }

    // ---- potions --------------------------------------------------------------------

    private static GameState WithPotions()
    {
        var s = TestGame.Create();
        for (var code = 0x69; code <= 0x74; code++)
        {
            Data(s).Items[code] = Data(s).Items[code] with { Flags = 0x800 };
        }
        s.Party.Members[0].Pack.Clear();
        return s;
    }

    private static void Drink(GameState s, int code, int quality) => PotionEffects.DrinkFromPack(s, 0, Give(s, 0, code, quality));

    [Fact]
    public void OnlyPotionsThatAreNotThrownCanBeDrunk()
    {
        var s = WithPotions();
        Data(s).Items[0x60] = Data(s).Items[0x60] with { Flags = 0x0100_0800 };
        Assert.True(PotionEffects.CanDrink(s, 0x69));
        Assert.False(PotionEffects.CanDrink(s, 0x60));
        Assert.False(PotionEffects.CanDrink(s, 50));
    }

    [Theory]
    [InlineData(25, 0x30)]
    [InlineData(35, 0x40)]
    [InlineData(60, 0x50)]
    public void DeadlyBladeRaisesTheDamageNibble(int quality, int expected)
    {
        var s = WithPotions();
        Drink(s, 0x69, quality);
        Assert.Equal(expected, s.Party.Members[0].WeaponPotions);
        Assert.Contains(s.Events.All(), e => e.Event.Kind == EventKinds.PotionBonus && e.Event.Type == 0x69);
    }

    [Fact]
    public void StrongedgeCarriesIntoTheDamageNibble()
    {
        var s = WithPotions();
        s.Party.Members[0].WeaponPotions = 0x0E;
        Drink(s, 0x6A, 20);
        Assert.Equal(0x11, s.Party.Members[0].WeaponPotions);
    }

    [Fact]
    public void GreatPowerAddsToTheWholeByteAndWraps()
    {
        var s = WithPotions();
        s.Party.Members[0].WeaponPotions = 0xF0;
        Drink(s, 0x6B, 30);
        Assert.Equal((0xF0 + 22) & 0xFF, s.Party.Members[0].WeaponPotions);
    }

    [Theory]
    [InlineData(20, 1)]
    [InlineData(40, 3)]
    [InlineData(46, 5)]
    public void HardarmorAboveFortyFiveIsTheBestBandByAccident(int quality, int bonus)
    {
        var s = WithPotions();
        Drink(s, 0x6D, quality);
        Assert.Equal(bonus, s.Party.Members[0].ArmourPotions & 0xF);
    }

    [Fact]
    public void TruefightAboveFortyFiveIsTheWeakest()
    {
        var s = WithPotions();
        var bows = s.Party.Members[0][Skill.Bows];
        Drink(s, 0x6C, 50);
        Assert.Equal(Math.Min(99, bows + 5), s.Party.Members[0][Skill.Bows]);
    }

    [Fact]
    public void TransformationAddsAFlorin()
    {
        var s = WithPotions();
        s.Party.Purse = Money.From(2, 3, 4);
        Drink(s, 0x6E, 25);
        Assert.Equal(Money.From(3, 3, 4), s.Party.Purse);
    }

    [Fact]
    public void FirewallCountsAFirewallAndSetsItsStrength()
    {
        var s = WithPotions();
        s.Party.Members[0].ArmourPotions = 0x12;
        Drink(s, 0x74, 36);
        Assert.Equal(0x22, s.Party.Members[0].ArmourPotions);
        Assert.Equal(90, s.Party.Members[0].FirewallStrength);
    }

    [Fact]
    public void IronarmDrawsItsBoostBeforeTheBoostsHours()
    {
        var s = WithPotions();
        var index = Give(s, 0, 0x71, 25);
        var twin = TestGame.Twin(s);
        var boost = twin.Below(6) + 6;
        PotionEffects.DrinkFromPack(s, 0, index);
        Assert.Contains(s.Events.All(), e => e.Event.Kind == EventKinds.AttributeBoost && e.Event.Owner == 0
            && e.Event.Type == (int)Attribute.Strength && e.Event.Value == boost);
    }

    [Fact]
    public void DrinkingTakesOnePotionAndAnythingElseStays()
    {
        var s = WithPotions();
        var index = Give(s, 0, 0x6E, 25, 2);
        PotionEffects.DrinkFromPack(s, 0, index);
        Assert.Equal(1, s.Party.Members[0].Pack[index].Count);
        var other = Give(s, 0, 50, 25);
        PotionEffects.DrinkFromPack(s, 0, other);
        Assert.Equal(1, s.Party.Members[0].Pack[other].Count);
    }

    // ---- brewing --------------------------------------------------------------------

    [Theory]
    [InlineData(20, 30, 40, 0, 1, 99)]
    [InlineData(20, 30, 140, 0, 2, 10)]
    [InlineData(20, 30, 140, 0, 4, 0)]
    [InlineData(20, 30, 80, 5, 6, 8)]
    [InlineData(20, 30, 0, 0, 0, 0)]
    public void TheChanceOfABrew(int intelligence, int alchemy, int mystic, int stone, int batches, int chance) =>
        Assert.Equal(chance, Brewing.SuccessChance(intelligence, alchemy, mystic, stone, batches));

    [Theory]
    [InlineData(1, 100)]
    [InlineData(2, 100)]
    [InlineData(3, 100)]
    [InlineData(4, 50)]
    [InlineData(7, 33)]
    public void BigBrewsStartLower(int batches, int start) => Assert.Equal(start, Brewing.BaseChance(batches));

    private static Ingredient[] Slots(params Ingredient[] used) => [.. used, .. Enumerable.Repeat(new Ingredient(0, 0), 5 - used.Length)];

    private static readonly Formula[] Book =
    [
        new(4, "Galen's Eyeburn", "Ga Eyebrn", "burns", 0, 1, Slots(new Ingredient(2, 0x48), new Ingredient(1, 0x49))),
        new(7, "Rufinus' Black Cloud", "RG BlkCld", "cloud", 10, 0, Slots(new Ingredient(1, 0x4A))),
        new(10, "Nicolas F's Fleadust", "NF Fleadst", "fleas", 20, 2, Slots(new Ingredient(1, 0x48))),
    ];

    private static GameState Alchemists()
    {
        var s = TestGame.Create();
        s.ClockSeed = null;
        foreach (var m in s.Party.Members)
        {
            m.Pack.Clear();
            m.Formulas.Clear();
        }
        s.Party.Members[0].Formulas.UnionWith([10, 4]);
        return s;
    }

    [Fact]
    public void TheBenchListsTheKnownFormulasAndCountsTheIngredients()
    {
        var s = Alchemists();
        Give(s, 0, 0x48, 25, 3);
        Give(s, 1, 0x48, 10, 2);
        Give(s, 0, 0x49, 25);
        var bench = new BrewingBench(s, Book, 0);
        Assert.Equal([4, 10], bench.Batches.Select(b => b.Formula.Index));
        Assert.Equal(5, bench.Stock(0x48));
        Assert.True(bench.Reserve(Book[0]));
        Assert.Equal(3, bench.Stock(0x48));
        Assert.False(bench.HasIngredients(Book[0]));
        Assert.False(bench.Reserve(Book[0]));
        bench.Release(Book[0]);
        Assert.Equal(5, bench.Stock(0x48));
        Assert.Equal(1, bench.Stock(0x49));
    }

    [Fact]
    public void BrewingTakesTheIngredientsFromTheAlchemistFirst()
    {
        var s = Alchemists();
        Give(s, 1, 0x48, 25, 2);
        Give(s, 0, 0x48, 25, 3);
        Give(s, 0, 0x49, 25, 2);
        var bench = new BrewingBench(s, Book, 0);
        var batch = bench.Batches[0];
        batch.Count = 2;
        bench.Brew(batch, 1, 0x1F73);
        Assert.Empty(s.Party.Members[0].Pack);
        Assert.Equal(1, s.Party.Members[1].Pack.Single().Count);
        var brew = s.Events.All().Single(e => e.Event.Kind == EventKinds.Brew).Event;
        Assert.Equal((0, 4, 2, 1, 1), (brew.Owner, brew.Type, brew.Extra, brew.Data1, brew.Data2));
        Assert.Equal(0x1F73, brew.Tag);
        Assert.Equal(Brewing.SuccessChance(s.Party.Members[0][Attribute.Intelligence], s.Party.Members[0][Skill.Alchemy], 0, 0, 2), brew.Value);
        Assert.Equal(2, Brewing.BatchesBrewing(s, 0));
    }

    [Fact]
    public void OpeningTheBenchAgainTakesBackAnUnfinishedBrew()
    {
        var s = Alchemists();
        Give(s, 0, 0x48, 25, 4);
        Give(s, 0, 0x49, 25, 2);
        var bench = new BrewingBench(s, Book, 0);
        bench.Batches[0].Count = 2;
        bench.Brew(bench.Batches[0], 0, 0x1F73);
        var again = new BrewingBench(s, Book, 0);
        Assert.Equal(2, again.Batches[0].Count);
        Assert.Equal(4, again.Stock(0x48));
        Assert.Equal(0, Brewing.BatchesBrewing(s, 0));
        Assert.All(s.Party.Members[0].Pack, st => Assert.Equal(25, st.Quality));
    }

    [Fact]
    public void ABrewIsDoneWhenTimeNextPasses()
    {
        var s = Alchemists();
        Give(s, 0, 0x48, 25, 2);
        var bench = new BrewingBench(s, Book, 0);
        bench.Batches[1].Count = 2;
        bench.Brew(bench.Batches[1], 0, 0x1F73);
        var roll = TestGame.Twin(s).Below(100);
        TimePassage.Advance(s, 1);
        var potions = s.Party.Members[0].Pack.Where(st => st.Code == Potions.FirstPotion + 10 / 3).ToList();
        var chance = bench.Chance(bench.Batches[1]);
        if (chance <= roll)
        {
            Assert.Empty(potions);
        }
        else
        {
            Assert.Equal(2, potions.Single().Count);
            Assert.Equal(10 % 3 * 10 + 20, potions.Single().Quality);
        }
        Assert.Equal(0, Brewing.BatchesBrewing(s, 0));
    }

    [Fact]
    public void CallingOffABrewGivesBackEveryIngredientSlotEvenEmptyOnes()
    {
        var s = Alchemists();
        s.Behaviour.FixCallOffScansEveryEvent = true;
        Give(s, 0, 0x4A, 25, 3);
        s.Party.Members[0].Formulas.Add(7);
        var bench = new BrewingBench(s, Book, 0);
        var batch = bench.Batches.Single(b => b.Formula.Index == 7);
        batch.Count = 3;
        bench.Brew(batch, 1, 0x1F73);
        Brewing.CallOff(s, Book, 0);
        var pack = s.Party.Members[0].Pack;
        Assert.Equal(3, pack.Where(st => st.Code == 0x4A).Sum(st => st.Count));
        Assert.Contains(pack, st => st.Code == 0 && st.Count == 0);
        Assert.Equal(0, Brewing.BatchesBrewing(s, 0));
    }

    // orig: R036+0x1EC2: the scan starts at the slot numbered as many as there are formulas (1462:1F71)
    [Fact]
    public void CallingOffABrewMissesEveryBrewInAnEventSlotBelowTheFormulaCount()
    {
        var s = Alchemists();
        Give(s, 0, 0x4A, 25, 3);
        s.Party.Members[0].Formulas.Add(7);
        var bench = new BrewingBench(s, Book, 0);
        var batch = bench.Batches.Single(b => b.Formula.Index == 7);
        batch.Count = 3;
        bench.Brew(batch, 1, 0x1F73);
        Brewing.CallOff(s, Book, 0);
        Assert.NotEqual(0, Brewing.BatchesBrewing(s, 0));
        Assert.Equal(0, s.Party.Members[0].Pack.Where(st => st.Code == 0x4A).Sum(st => st.Count));
    }

    [Fact]
    public void FixedCallingOffGivesBackOnlyTheIngredients()
    {
        var s = Alchemists();
        s.Behaviour.FixUnbrewEmptyIngredientSlots = true;
        s.Behaviour.FixCallOffScansEveryEvent = true;
        Give(s, 0, 0x4A, 25, 1);
        s.Party.Members[0].Formulas.Add(7);
        var bench = new BrewingBench(s, Book, 0);
        var batch = bench.Batches.Single(b => b.Formula.Index == 7);
        batch.Count = 1;
        bench.Brew(batch, 1, 0x1F73);
        Brewing.CallOff(s, Book, 0);
        Assert.DoesNotContain(s.Party.Members[0].Pack, st => st.Code == 0);
    }

    [Fact]
    public void LeavingTheCampLosesEveryBrew()
    {
        var s = Alchemists();
        Give(s, 0, 0x48, 25, 1);
        var bench = new BrewingBench(s, Book, 0);
        bench.Batches[1].Count = 1;
        bench.Brew(bench.Batches[1], 1, 0x1F73);
        Brewing.AbandonAll(s);
        Assert.DoesNotContain(s.Events.All(), e => e.Event.Kind == EventKinds.Brew);
    }

    // ---- the camp -------------------------------------------------------------------

    [Theory]
    [InlineData(CampPlace.Inn, 4)]
    [InlineData(CampPlace.OtherLodging, 12)]
    [InlineData(CampPlace.SleepingRough, 1)]
    [InlineData(CampPlace.Wilderness, 0)]
    public void ADaysLodgingIsPricedForThePlace(CampPlace place, int price)
    {
        var s = TestGame.Create();
        Assert.Equal(price, new CampStay(s, place).DayCost());
    }

    [Theory]
    // the original's unit run of R036+0x1E80 (local/batch/evid1 t_alch.py): one formula byte 0x80 -> 1, 0x08 -> 1, 0x07 -> 1, 0 -> 0
    [InlineData(0x80, true)]
    [InlineData(0x08, true)]
    [InlineData(0x07, true)]
    [InlineData(0x00, false)]
    public void AlchemyIsOfferedForAnyBitOfAFormulaByte(int value, bool offered)
    {
        var s = TestGame.Create();
        var m = s.Party.Members[0];
        Array.Clear(m.Formulas.Bytes);
        m.Formulas.Bytes[5] = (byte)value;
        Assert.Equal(offered, new CampStay(s, CampPlace.Inn).CanDoAlchemy(0));
    }

    [Fact]
    public void WithoutTheMoneyNoDayPasses()
    {
        var s = TestGame.Create();
        s.Party.Purse = Money.Zero;
        var date = s.Date;
        new CampStay(s, CampPlace.Inn).PassTheDay();
        Assert.Contains("Not Enough Money", ((GameMessageLog)s.Messages).Lines);
        Assert.Equal(date, s.Date);
    }

    [Fact]
    public void ADayAtTheInnRestsPaysAndEndsAtFiveInTheMorning()
    {
        var s = TestGame.Create();
        s.ClockSeed = null;
        s.Party.Purse = Money.From(1, 0, 0);
        var m = s.Party.Members[0];
        m[Attribute.Endurance] = 3;
        new CampStay(s, CampPlace.Inn).PassTheDay();
        Assert.Equal(m.Maximum(Attribute.Endurance), m[Attribute.Endurance]);
        Assert.Equal(Money.From(1, 0, 0).Subtract(0, 0, 4), s.Party.Purse);
        Assert.Equal((5, 11), (s.Date.Hour, (int)s.Date.Day));
    }

    /// <summary>The day passes through advance_hours, so the hourglass shows once, before the clock moves (R036+0x147A).</summary>
    [Fact]
    public void TheDayShowsTheHourglassBeforeTheClockMoves()
    {
        var s = TestGame.Create();
        s.ClockSeed = null;
        s.Party.Purse = Money.From(1, 0, 0);
        var before = s.Date;
        var shown = new List<(int Hours, int Hour, int Day)>();
        new CampStay(s, CampPlace.Inn) { Hourglass = hours => shown.Add((hours, s.Date.Hour, s.Date.Day)) }.PassTheDay();
        var hours = TimeOfDay.HoursUntil(before.Hour, 5);
        Assert.Equal([(hours < 9 ? hours + 24 : hours, before.Hour, (int)before.Day)], shown);
    }

    [Fact]
    public void WithoutTheMoneyNoHourglassShows()
    {
        var s = TestGame.Create();
        s.Party.Purse = Money.Zero;
        var shown = 0;
        new CampStay(s, CampPlace.Inn) { Hourglass = _ => shown++ }.PassTheDay();
        Assert.Equal(0, shown);
    }

    [Fact]
    public void RegainingStrengthAddsTheBestHealersShare()
    {
        var s = TestGame.Create();
        s.ClockSeed = null;
        s.Party.Purse = Money.From(1, 0, 0);
        var m = s.Party.Members[0];
        m[Attribute.Strength] = m.Maximum(Attribute.Strength) - 10;
        var stay = new CampStay(s, CampPlace.Inn);
        var gain = stay.StrengthGain;
        stay.RegainStrength(0, Book);
        Assert.Equal(CampActivity.RegainStrength, stay.Activity[0]);
        stay.PassTheDay();
        Assert.True(m[Attribute.Strength] >= m.Maximum(Attribute.Strength) - 10 + gain);
        Assert.Equal(Math.Clamp(PartyQueries.MaxSkill(s, (int)Skill.Healing) / 15, 1, 99), gain);
    }

    [Fact]
    public void PrayingAddsReligionAndVirtueOverTwelve()
    {
        var s = TestGame.Create();
        s.ClockSeed = null;
        s.Date = s.Date with { Hour = 1 };   // four hours to 05:00 is too short: the next 05:00 but one
        s.Party.Purse = Money.From(1, 0, 0);
        var m = s.Party.Members[0];
        m[Attribute.DivineFavor] = 1;
        var stay = new CampStay(s, CampPlace.Inn);
        stay.Pray(0, Book);
        var expected = 1 + (m[Skill.Religion] + m[Skill.Virtue]) / 12 + 1;
        stay.PassTheDay();
        Assert.Equal((5, 11), (s.Date.Hour, (int)s.Date.Day));
        Assert.True(m[Attribute.DivineFavor] >= expected);   // the night adds a little more
    }

    [Fact]
    public void SleepingRoughCostsAPfennigAndStanding()
    {
        var s = TestGame.Create();
        s.ClockSeed = null;
        s.Party.Purse = Money.From(0, 1, 0);
        new CampStay(s, CampPlace.SleepingRough).PassTheDay();
        Assert.Equal(Money.From(0, 0, 11), s.Party.Purse);
        Assert.Equal(-1, s.Locations[5].Reputation);
    }

    [Fact]
    public void GuardsLowerTheDailyRisk()
    {
        var s = TestGame.Create();
        var m = s.Party.Members[0];
        Array.Fill(m.Skills, 0);
        m[Skill.Woodwise] = 80;
        m[Skill.Stealth] = 40;
        m[Skill.EdgedWeapons] = 30;
        var stay = new CampStay(s, CampPlace.Wilderness) { DailyRisk = 10 };
        stay.Guard(0, Book);
        stay.GrowRisk();
        Assert.Equal(10 - 3 - 1 - 1, stay.Risk);
    }

    [Fact]
    public void AGuardsImpactWeaponSkillIsNotCounted()
    {
        var s = TestGame.Create();
        var m = s.Party.Members[0];
        Array.Fill(m.Skills, 0);
        m[Skill.ImpactWeapons] = 50;
        var stay = new CampStay(s, CampPlace.Wilderness) { DailyRisk = 10 };
        stay.Guard(0, Book);
        stay.GrowRisk();
        Assert.Equal(10 - 1, stay.Risk);
    }

    [Fact]
    public void TheDailyRiskIsAtLeastOneAndNoneWhileCampingIsSafe()
    {
        var s = TestGame.Create();
        var stay = new CampStay(s, CampPlace.Wilderness) { DailyRisk = 0, Risk = 7 };
        stay.GrowRisk();
        Assert.Equal(8, stay.Risk);
        s.Events.CreateHere(EventKinds.SafeCamping, 24);
        stay.GrowRisk();
        Assert.Equal(0, stay.Risk);
    }

    [Fact]
    public void ADiscoveredCampEndsTheStayAndRemembersTheFirstGuard()
    {
        var s = TestGame.Create();
        var stay = new CampStay(s, CampPlace.Wilderness) { Risk = 200 };
        stay.Guard(2, Book);
        stay.Guard(3, Book);
        Assert.Equal(StayEnd.Over, stay.SpendDay());
        Assert.Equal(2, s.SavedCard);
        Assert.True(s.Events.At(EventKinds.CampDiscovered, s.CurrentLocation));
    }

    [Fact]
    public void AnUndiscoveredCampSpendsTheDay()
    {
        var s = TestGame.Create();
        s.ClockSeed = null;
        var twin = TestGame.Twin(s);
        var roll = twin.Below(50) + twin.Below(50);
        var stay = new CampStay(s, CampPlace.Wilderness) { Risk = roll - 1 };
        Assert.Equal(StayEnd.None, stay.SpendDay());
        Assert.Equal(1, stay.Days);
    }

    // ---- day labour -----------------------------------------------------------------

    [Theory]
    [InlineData(-5, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 2)]
    [InlineData(15, 3)]
    [InlineData(16, 4)]
    [InlineData(81, 9)]
    [InlineData(99, 9)]
    public void ASkillCountsByTheSquareRootOfItsExcess(int excess, int term) => Assert.Equal(term, DayJobs.SkillTerm(excess));

    [Fact]
    public void WithoutAnyGiftsAMemberIsADayLaborer()
    {
        var s = TestGame.Create();
        var m = s.Party.Members[0];
        Array.Fill(m.Skills, 0);
        Array.Fill(m.Attributes, 0);
        Assert.Equal((2, "Day Laborer"), DayJobs.Best(s, 0));
    }

    [Fact]
    public void JobsNeedingASecondSetOfBuildingsAreNeverOpen() =>
        Assert.All(DayJobs.All.Where(j => j.OtherContents != 0), j => Assert.Contains("Friar", j.Name));

    // ---- party information ----------------------------------------------------------

    [Theory]
    [InlineData(-10, "Unknown")]
    [InlineData(49, "Unknown")]
    [InlineData(50, "Barely Known")]
    [InlineData(5000, "Legendary Heroes")]
    public void FameTitles(int fame, string title) => Assert.Equal(title, PartyStanding.FameTitle(fame));

    [Theory]
    [InlineData(4, "Small-Sized")]
    [InlineData(6, "Moderate-Sized")]
    [InlineData(7, "Large-Sized")]
    public void TownSizes(int size, string word) => Assert.Equal(word, PartyStanding.SizeWord(size));

    [Theory]
    [InlineData(100, 50, 0)]
    [InlineData(150, 100, 2)]
    [InlineData(150, 40, 2)]
    [InlineData(130, 10, 1)]
    [InlineData(70, 190, 5)]
    [InlineData(100, 190, 4)]
    public void DirectionsCountYAsAThird(int x, int y, int direction) =>
        Assert.Equal(direction, PartyStanding.Direction(100, 100, x, y));

    [Fact]
    public void TheNearestTownOnTheSmallMapIsWithinTen()
    {
        var s = TestGame.Create();
        Assert.Equal(10, PartyStanding.NearestTown(s, 10, 20));
        Assert.Equal(-1, PartyStanding.NearestTown(s, 299, 1000));
    }
}
