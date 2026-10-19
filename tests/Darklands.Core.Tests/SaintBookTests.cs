using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Core.Events;
using Darklands.Core.Rules;
using Darklands.Core.World;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Core.Tests;

public sealed class SaintBookTests
{
    private const int Adrian = 0, Agatha = 1, Agnes = 2, Cosmas = 27, Crispin = 28, Damian = 30, Edward = 41,
        GilesOfPortugal = 59, Januarius = 74, Matthew = 94, Polycarp = 110;

    private static List<TimedEvent> Boosts(GameState s, int kind) =>
        s.Events.All().Select(e => e.Event).Where(e => e.Kind == kind).ToList();

    [Fact]
    public void TheTableListsEachSaintFunctionOnce()
    {
        var functions = Enumerable.Range(0, SaintTable.Count).Select(i => SaintBook.Of(i).Original).ToList();
        Assert.Equal(SaintTable.Count, functions.Distinct().Count());
        Assert.All(functions, f => Assert.StartsWith("R039+0x", f));
        Assert.DoesNotContain(SaintBook.Unlisted[0].Original, functions);
        Assert.DoesNotContain(SaintBook.Unlisted[1].Original, functions);
        Assert.Equal("R039+0x1FD4", SaintBook.Of(Adrian).Coordinate);
        Assert.Equal("R039+0x9674", SaintBook.Of(52).Original);   // St.Francis: out of file order
    }

    [Fact]
    public void BoostsDrawInTheOriginalOrder()
    {
        var s = TestGame.Create();
        var twin = TestGame.Twin(s);
        SaintBook.Help(s, 0, Adrian);

        // End: rnd 8 + 8 then the duration's rnd 3; Chr: rnd 6 + 6, rnd 3; then the seven weapon skills.
        var endurance = twin.Below(8) + 8;
        twin.Below(3);
        var charisma = twin.Below(6) + 6;
        var attributes = Boosts(s, EventKinds.AttributeBoost);
        Assert.Equal([endurance, charisma], attributes.Select(e => e.Value));
        Assert.Equal([(int)Attribute.Endurance, (int)Attribute.Charisma], attributes.Select(e => e.Type));
        Assert.Equal(7, Boosts(s, EventKinds.SkillBoost).Count);
        Assert.All(Boosts(s, EventKinds.SkillBoost), e => Assert.InRange(e.Value, 0, 29));
    }

    [Fact]
    public void StAgathaGivesWomenMoreEndurance()
    {
        var woman = TestGame.Create();
        SaintBook.Help(woman, 0, Agatha);   // Gretchen
        var man = TestGame.Create();
        SaintBook.Help(man, 1, Agatha);
        Assert.InRange(Boosts(woman, EventKinds.AttributeBoost)[0].Value, 12, 23);
        Assert.InRange(Boosts(man, EventKinds.AttributeBoost)[0].Value, 6, 11);
    }

    [Fact]
    public void StAgnesHelpsOnlyWithAnAbleWoman()
    {
        var s = TestGame.Create();
        s.Party.Members[0].Status = CharacterStatus.Unconscious;
        var before = ((Randomness.MscRandom)s.Random).State;
        SaintBook.Help(s, 1, Agnes);
        Assert.Empty(s.Events.All());
        Assert.Equal(before, ((Randomness.MscRandom)s.Random).State);
        Assert.Equal(0, Saints.Chance(s, 1, Agnes, 50));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StCeciliaInTheOpenCountryRaisesNoStandingUnlessFixed(bool fixedGame)
    {
        const int cecilia = 18, instrument = 50;
        var s = TestGame.Create();
        var items = ((TestData)s.Data).Items;
        items[instrument] = items[instrument] with { Flags = 0x2000_0000 };
        Inventory.Add(s, 0, instrument);
        s.CurrentLocation = GameState.Wilderness;
        s.MapPosition = new MapPosition(0, 0);
        s.Behaviour.FixSaintStandingInOpenCountry = fixedGame;
        var nearest = LocationRules.NearestCity(s, 0, 0);
        SaintBook.Help(s, 0, cecilia);
        if (fixedGame)
        {
            Assert.InRange(s.Locations[nearest].Reputation, 20, 59);   // standing 0: the chance is 100
        }
        else
        {
            Assert.All(s.Locations, l => Assert.Equal(0, l.Reputation));
        }
    }

    [Fact]
    public void StCosmasAndStDamianTeachEachOther()
    {
        var s = TestGame.Create();
        SaintBook.Help(s, 1, Cosmas);
        SaintBook.Help(s, 2, Damian);
        Assert.Contains(Damian, s.Party.Members[1].Saints);
        Assert.Contains(Cosmas, s.Party.Members[2].Saints);
    }

    [Fact]
    public void StCrispinMendsEveryonesArmourInASignedByte()
    {
        var s = TestGame.Create();
        s.Party.Members[0].VitalArmor = new EquippedItem(74, 120);
        s.Party.Members[2].Status = CharacterStatus.Empty;
        var limb = s.Party.Members[2].LimbArmor.Quality;
        SaintBook.Help(s, 0, Crispin);
        Assert.Equal(-126, s.Party.Members[0].VitalArmor.Quality);
        Assert.Equal(limb, s.Party.Members[2].LimbArmor.Quality);
        Assert.Equal(35, s.Party.Members[3].LimbArmor.Quality);
    }

    [Fact]
    public void StEdwardRaisesVirtueAndTakesAQuarterOfTheLow16BitsOfThePurse()
    {
        var s = TestGame.Create();
        s.Party.Members[1][Skill.Virtue] = 3;
        s.Party.Purse = new Money(300, 0, 0);   // 72000 pfennigs: 6464 in 16 bits, a quarter 1616
        SaintBook.Help(s, 0, Edward);
        Assert.Equal(20, s.Party.Members[1][Skill.Virtue]);
        Assert.Equal(52, s.Party.Members[3][Skill.Virtue]);
        Assert.Equal(72000 - 1616, s.Party.Purse.TotalPfennigs);
    }

    [Fact]
    public void StGilesOfPortugalCostsStrengthAndVirtueForGood()
    {
        var s = TestGame.Create();
        var m = s.Party.Members[3];
        var (strength, virtue) = (m[Attribute.Strength], m[Skill.Virtue]);
        SaintBook.Help(s, 3, GilesOfPortugal);
        Assert.Equal(strength - 1, m[Attribute.Strength]);
        Assert.Equal(virtue - 1, m[Skill.Virtue]);
    }

    [Fact]
    public void StMatthewAndStPolycarpChangeTheirOwnState()
    {
        var s = TestGame.Create();
        SaintBook.Help(s, 0, Matthew);
        Assert.InRange(s.FuggerStanding, 20, 39);
        Assert.InRange(s.MediciStanding, 20, 39);
        s.Party.Members[1].BattleCharms = 0x13;
        SaintBook.Help(s, 0, Polycarp);
        Assert.Equal(0xB3, s.Party.Members[1].BattleCharms);
        Assert.Equal(90, s.Party.Members[1].CharmStrength);
    }

    [Fact]
    public void StJanuariusDoesNothingAndTheLeaderIsMinusOne()
    {
        var s = TestGame.Create();
        SaintBook.Help(s, Who.Leader, Januarius);
        Assert.Empty(s.Events.All());
        SaintBook.Help(s, Who.Leader, Adrian);
        Assert.All(s.Events.All(), e => Assert.Equal(s.Party.LeaderIndex, e.Event.Owner));
    }
}
