using Darklands.Core.Characters;
using Darklands.Core.Events;
using Darklands.Core.Items;
using Darklands.Core.Rules;
using Darklands.Core.Time;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Core.Tests;

public sealed class PracticeAndItemsTests
{
    private const int Alchemy = (int)Skill.Alchemy;

    [Fact]
    public void PracticeRollsTheChanceFirstThenTheGain()
    {
        var s = TestGame.Create(seed: 12345);
        var twin = TestGame.Twin(s);
        var before = s.Party.Members[2][Skill.Alchemy];   // Hans: 48

        Practice.PracticeSkill(s, 2, Alchemy, 1, 10, 2);

        // chance 10 x 3.3 = 33 %; gain improve_roll(48, 3, 8): chance |100 - 48| %, then 3 + rnd 5
        var expected = before;
        if (twin.Below(100) <= 33)
        {
            var gain = 52 >= twin.Below(100) ? twin.Below(5) + 3 : 3;
            expected += gain;
        }
        Assert.Equal(expected, s.Party.Members[2][Skill.Alchemy]);
        Assert.Equal(twin.State, ((Randomness.MscRandom)s.Random).State);
    }

    [Theory]
    [InlineData(1, 10, 33)]   // success: x 3.3
    [InlineData(0, 10, 11)]   // failure: x 1.1
    [InlineData(1, 40, 132)]  // always
    public void PracticeSucceedsWithTheOriginalChance(int mode, int percent, int chance)
    {
        var s = TestGame.Create(seed: 99);
        var improved = 0;
        const int trials = 4000;
        for (var k = 0; k < trials; k++)
        {
            s.Party.Members[0].Skills[Alchemy] = 50;
            Practice.PracticeSkill(s, 0, Alchemy, mode, percent, 0);
            if (s.Party.Members[0].Skills[Alchemy] > 50)
            {
                improved++;
            }
        }
        var expected = Math.Min(chance + 1, 100) / 100.0;   // roll 0..99 <= chance
        Assert.InRange(improved / (double)trials, expected - 0.03, expected + 0.03);
    }

    [Fact]
    public void WholePartyPracticeRollsForEachAbleMember()
    {
        var s = TestGame.Create(seed: 5);
        s.Party.Members[1].Status = CharacterStatus.Unconscious;
        var twin = TestGame.Twin(s);
        var before = s.Party.Members.Select(m => m[Skill.Alchemy]).ToArray();

        Practice.PracticeSkill(s, Who.Everybody, Alchemy, 0, 20, 0);   // 20 x 0.5 = 10 %

        foreach (var i in new[] { 0, 2, 3 })
        {
            var expected = before[i];
            if (twin.Below(100) <= 10)
            {
                expected += Math.Abs(100 - before[i]) >= twin.Below(100) ? twin.Below(3) + 1 : 1;
            }
            Assert.Equal(expected, s.Party.Members[i][Skill.Alchemy]);
        }
        Assert.Equal(before[1], s.Party.Members[1][Skill.Alchemy]);
    }

    [Fact]
    public void ItemsStackUpToTwoHundredFiftyFive()
    {
        var s = TestGame.Create();
        var hans = s.Party.Members[2];
        var stacks = hans.Pack.Count;
        Inventory.AddMany(s, 2, 50, 200);
        Inventory.AddMany(s, 2, 50, 55);
        Inventory.AddMany(s, 2, 50, 1);

        Assert.Equal(stacks + 2, hans.Pack.Count);
        Assert.Equal(256, Inventory.Count(s, 2, 50));
        Assert.Equal(stacks + 2, Inventory.Take(s, 2, 50));   // from the first stack
        Assert.Equal(254, hans.Pack.First(st => st.Code == 50).Count);
        Assert.Equal(stacks + 1, Inventory.RemoveStack(s, 2, 50));
        Assert.Equal(-1, Inventory.Take(s, 0, 50));
    }

    // count_items R029+0x0C76 with who >= 5 counts the last battle's enemy items (not modelled; no caller passes it)
    [Fact]
    public void CountingForANumberPastTheMembersIsZero()
    {
        var s = TestGame.Create();
        Inventory.AddMany(s, 2, 50, 3);
        Assert.Equal(0, Inventory.Count(s, 5, 50));
        Assert.Equal(0, Inventory.Count(s, 9, 50));
        Assert.Equal(3, Inventory.Count(s, 2, 50));
    }

    [Fact]
    public void GivingTheWholePartyAnItemGivesItTwice()
    {
        var s = TestGame.Create();
        Inventory.Add(s, Who.Everybody, 60);
        Assert.All(s.Party.Members, m => Assert.Single(m.Pack, st => st.Code == 60));
        Inventory.Add(s, Who.Everybody, 60);
        Assert.All(s.Party.Members, m => Assert.Equal(3, m.Pack.Where(st => st.Code == 60).Sum(st => st.Count)));

        s.Behaviour.FixPartyItemsAddedTwice = true;
        Inventory.Add(s, Who.Everybody, 60);
        Assert.All(s.Party.Members, m => Assert.Equal(4, m.Pack.Where(st => st.Code == 60).Sum(st => st.Count)));
    }

    [Fact]
    public void KeepingItemsStartsOnePastTheLastStack()
    {
        var s = TestGame.Create(seed: 1);
        var gretchen = s.Party.Members[0];
        var codes = gretchen.Pack.Select(st => st.Code).ToList();   // three stacks
        var twin = TestGame.Twin(s);

        Inventory.KeepEach(s, 0, 50);

        // index 3 (past the end) drops the last stack, then 2, 1, 0 roll for themselves
        var expected = codes.ToList();
        for (var k = 3; k >= 0; k--)
        {
            if (twin.Below(100) > 50)
            {
                expected.RemoveAt(Math.Min(k, expected.Count - 1));
            }
        }
        Assert.Equal(expected, gretchen.Pack.Select(st => st.Code));
    }

    [Fact]
    public void TakingTheLastEquippedItemUnequipsIt()
    {
        var s = TestGame.Create();
        var gretchen = s.Party.Members[0];
        gretchen.Pack.Clear();
        gretchen.Pack.Add(new ItemStack(3, 3, 25, 1, 4));
        gretchen.Weapon = new EquippedItem(3, 25);

        Assert.Equal(3, Inventory.Equipped(s, 0, EquipmentSlot.Weapon));
        Assert.Equal(0, Inventory.Take(s, 0, 3));
        Assert.True(gretchen.Weapon.IsEmpty);
        Assert.Equal(0, gretchen.Encumbrance);
    }

    [Fact]
    public void HasItemAlsoMeansKnowingTheFormula()
    {
        var s = TestGame.Create();
        s.Party.Members[0].Formulas.Add(31);           // Gretchen: potion group 10
        Assert.Equal(2, Inventory.Has(s, 0, Potions.FirstPotion + 10));
        Assert.Equal(0, Inventory.Has(s, 0, Potions.FirstPotion + 11));
        // a lower code reads the previous member's table: member 1, code 0x5F - 22 + 10 -> Gretchen's group 10
        Assert.Equal(2, Inventory.Has(s, 1, Potions.FirstPotion - Potions.Count + 10));
        Assert.Equal(1, Inventory.Has(s, 2, 102));     // Hans carries it
    }

    [Fact]
    public void AllMountedNeedsEveryPresentMemberAbleWithAHorse()
    {
        var s = TestGame.Create();
        Assert.Equal(0, PartyQueries.Get(s, 5));
        foreach (var i in Enumerable.Range(0, 4))
        {
            Inventory.Add(s, i, 40);
        }
        Assert.Equal(1, PartyQueries.Get(s, 5));
        Assert.Equal(30, Inventory.BestQualityWithFlags(s, Who.Everybody, ItemFlags.Mount));
    }

    [Fact]
    public void SaintChancesFollowTheTable()
    {
        var s = TestGame.Create();
        var ebhard = s.Party.Members[3];            // Virtue 52, Divine Favor 99
        var t = SaintTable.Of(0);                   // 335, 2, 15, 99, 43, 25
        Assert.Equal((52 - 43) / 2 + 25, Saints.ChanceLow(s, 3, 0));
        Assert.Equal((52 - 43) / 2 - t.MinFavor + 25 + 99, Saints.ChanceHigh(s, 3, 0));
        ebhard[Attribute.DivineFavor] = 10;
        Assert.Equal(0, Saints.ChanceLow(s, 3, 0));
        Assert.Equal(0, Saints.ChanceHigh(s, 3, 0));     // 10 favor is below the least of 15
        Assert.Equal(0, Saints.Chance(s, 2, 0, 20));     // Hans's Virtue 19 is too low
        Assert.True(Saints.Knows(s, Who.Everybody, 50));
        Assert.False(Saints.Knows(s, 0, 50));
    }

    [Fact]
    public void SaintTwoNeedsAnAbleWoman()
    {
        var s = TestGame.Create();
        Assert.True(Saints.ChanceLow(s, 3, SaintTable.NeedsAWoman) > 0);
        s.Party.Members[0].Status = CharacterStatus.Unconscious;
        Assert.Equal(0, Saints.ChanceLow(s, 3, SaintTable.NeedsAWoman));
    }

    [Fact]
    public void ABrewEndsWithPotionsOrFailure()
    {
        var s = TestGame.Create(seed: 4);
        var messages = new GameMessageLog();
        s.Messages = messages;
        var formula = 2 * 3 + 1;                    // potion group 2, grade 1
        var i = s.Events.Create(2, 5, 3, 0x50, formula, 100, (int)ExpiryHandler.AlchemyResult, 5, 1, 0);
        Assert.NotEqual(-1, i);

        TimePassage.Advance(s, 5);

        Assert.Contains("Alchemy Success", messages.Lines);
        var potion = Assert.Single(s.Party.Members[2].Pack, st => st.Code == TestGame.Potion);
        Assert.Equal((0x90, 25, 3, 1), (potion.Type, potion.Quality, potion.Count, potion.Weight));

        s.Events.Create(2, 5, 3, 0x50, formula, -1, (int)ExpiryHandler.AlchemyResult, 5, 1, 0);
        TimePassage.Advance(s, 5);
        Assert.Contains("Alchemy Failure", messages.Lines);
    }

    [Fact]
    public void AHirelingsTermEndsWithANote()
    {
        var s = TestGame.Create();
        var slot = PartyRules.AddMember(s, "Wolf Bauer", "Wolf", "F01", 10, 3);
        Assert.Equal(4, slot);
        Assert.Equal(3, s.Party.Members[4].Pack.Count);
        Assert.Equal(1, s.Events.Count(EventKinds.MemberTerm));

        TimePassage.Advance(s, 10);

        Assert.Equal(0, s.Events.Count(EventKinds.MemberTerm));
        Assert.True(s.Events.HasType(EventKinds.MemberLeft, 4));
        Assert.Equal(-1, PartyRules.AddMember(s, "Anna", "Anna", "F01", 10, 3));
    }

    [Fact]
    public void TheTravelBlessingRenewsOnlyOnWater()
    {
        var s = TestGame.Create();
        s.Events.Create(-2, 0, 0x63, EventKinds.TravelBlessing, 0, 0, (int)ExpiryHandler.RenewTravelBlessing, 2, 1, 0);
        s.PartyTerrain = 1;
        TimePassage.Advance(s, 2);
        Assert.Equal(1, s.Events.Count(EventKinds.TravelBlessing));
        s.PartyTerrain = 0;
        TimePassage.Advance(s, 6);
        Assert.Equal(0, s.Events.Count(EventKinds.TravelBlessing));
    }

    [Fact]
    public void ANewMonthAgesTheBirthdayMemberAndStartsTheSabbat()
    {
        var s = TestGame.Create();
        s.Date = new GameDate(12, 30, 5, 1402);          // 30 June; the next day is 1 July (month 6)
        var birthday = (7 + 6) % 12;                      // seed 7: member (seed + month) mod 12
        var ages = s.Party.Members.Select(m => m.Age).ToArray();
        s.Locations[300].Icon = Icons.WitchSabbat;

        TimePassage.Advance(s, 24);

        Assert.Equal(6, s.Date.Month);
        ages[birthday]++;                                 // Gunther
        Assert.Equal(ages, s.Party.Members.Select(m => m.Age));
        Assert.True(s.Events.QuestExists(Quests.Sabbat));
        Assert.Equal(300, Quests.SabbatSite(s));
    }

    [Fact]
    public void BirthdaysFollowTheSeed()
    {
        var s = TestGame.Create();
        s.Party.Seed = 2;                                  // month 0 (January as 12): (2 + 12) mod 12 = 2
        MonthlyUpdate.Run(s, 12);
        Assert.Equal(41, s.Party.Members[2].Age);
        Assert.Equal(35, s.Party.Members[0].Age);
    }

    [Fact]
    public void EveryThirdMonthARuinedCastleIsTakenOver()
    {
        var s = TestGame.Create();
        s.Locations[120].Icon = Icons.RuinedRaubritterCastle;
        MonthlyUpdate.Run(s, 9);
        Assert.Equal(Icons.RaubritterCastle, s.Locations[120].Icon);
        Assert.Equal((0x93, 0x92, 4, 0), (s.Locations[120].ArrivalCard, s.Locations[120].EncounterCard, s.Locations[120].NoticeRange, s.Locations[120].Flags));
    }
}
