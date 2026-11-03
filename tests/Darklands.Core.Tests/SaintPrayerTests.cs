using Darklands.Core.Characters;
using Darklands.Core.Events;
using Darklands.Core.Randomness;
using Darklands.Core.Rules;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Core.Tests;

public sealed class SaintPrayerTests
{
    private const int Aidan = 3;     // favor 15..99, Virtue 23, base 25
    private const int Charity = 20; // the one saint usable for the party

    private sealed class Recorder : ISaintEffects
    {
        public List<(int Who, int Saint, int Favor)> Calls { get; } = [];

        public void Help(GameState state, int who, int saint, int favor) => Calls.Add((who, saint, favor));
    }

    [Fact]
    public void TheOfferStartsAtTheSaintsLeastOrAtZero()
    {
        var s = TestGame.Create();
        Assert.Equal(15, new SaintPrayer(s, 3, Aidan, PrayerMode.ForParty).Favor);
        s.Party.Members[3][Attribute.DivineFavor] = 10;
        var poor = new SaintPrayer(s, 3, Aidan, PrayerMode.ForParty);
        Assert.Equal(0, poor.Favor);
        Assert.False(poor.CanPray);
    }

    [Fact]
    public void MoreStopsAtTheDivineFavorOrAtAChanceOf99AndLessAtZero()
    {
        var s = TestGame.Create();
        s.Party.Members[3][Attribute.DivineFavor] = 40;
        var p = new SaintPrayer(s, 3, Aidan, PrayerMode.ForParty);
        // Ebhard: Virtue 52 → (52 − 23) / 2 − 15 + 25 + favor.
        Assert.Equal(14 - 15 + 25 + 15, p.Chance);
        for (var k = 0; k < 120; k++)
        {
            p.OfferMore();
        }
        Assert.Equal(40, p.Favor);
        Assert.Equal(0, p.FavorRemaining);
        Assert.Equal(64, p.Chance);

        s.Party.Members[3][Attribute.DivineFavor] = 99;
        var rich = new SaintPrayer(s, 3, Aidan, PrayerMode.ForParty);
        while (rich.OfferMore())
        {
        }
        Assert.Equal(75, rich.Favor);
        Assert.Equal(99, rich.Chance);   // stops at 99: the saint's most (99) is not what holds it
        Assert.Equal(99, rich.ShownChance);
        for (var k = 0; k < 120; k++)
        {
            rich.OfferLess();
        }
        Assert.Equal(0, rich.Favor);
        Assert.Equal(0, rich.Chance);
    }

    [Fact]
    public void OnlyStCharityLetsACardPrayerChooseAMember()
    {
        var s = TestGame.Create();
        Assert.False(new SaintPrayer(s, 0, Aidan, PrayerMode.ForParty).ChoosesMember);
        Assert.True(new SaintPrayer(s, 0, Charity, PrayerMode.ForParty).ChoosesMember);
        Assert.True(new SaintPrayer(s, 0, Aidan, PrayerMode.ForMember).ChoosesMember);
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(2u)]
    [InlineData(7u)]
    [InlineData(1234u)]
    public void APrayerRollsForSuccessThenForTheExtraLossAndAlwaysSpendsTheOffer(uint seed)
    {
        var s = TestGame.Create(seed);
        var p = new SaintPrayer(s, 3, Aidan, PrayerMode.ForParty);
        for (var k = 0; k < 60 && p.Chance < 90; k++)
        {
            p.OfferMore();
        }
        var (favor, chance) = (p.Favor, p.Chance);
        var twin = TestGame.Twin(s);
        var effects = new Recorder();
        var success = p.Pray(Who.Leader, effects);

        var expected = twin.Below(100) <= chance;
        Assert.Equal(expected, success);
        var lost = favor + (!expected && twin.Below(99) < chance - 66 ? favor / 2 : 0);
        Assert.Equal(Math.Max(1, 99 - lost), s.Party.Members[3][Attribute.DivineFavor]);
        Assert.Equal(expected ? [(Who.Leader, Aidan, favor)] : [], effects.Calls);
        Assert.Equal(expected ? PrayerOutcome.Succeeded : PrayerOutcome.Failed, p.Outcome(success));
        Assert.Equal(PrayerOutcome.Failed, new SaintPrayer(s, 3, Aidan, PrayerMode.ForMember).Outcome(true));
    }
    /// <summary>saint-prayer R034+0x0166..0x01F5: the message before the saint's help, after the extra loss, before the offer is spent.</summary>
    [Theory]
    [InlineData(1u)]
    [InlineData(2u)]
    [InlineData(7u)]
    [InlineData(1234u)]
    [InlineData(99u)]
    public void TheMessageComesBeforeTheHelpAndAfterTheExtraLoss(uint seed)
    {
        var s = TestGame.Create(seed);
        var p = new SaintPrayer(s, 3, Aidan, PrayerMode.ForMember);
        while (p.Chance < 70 && p.OfferMore())
        {
        }
        var favor = p.Favor;
        var events = new List<string>();
        var effects = new OrderRecorder(events);
        var success = p.Pray(Who.Leader, effects, ok => events.Add($"message {ok} favor {s.Party.Members[3][Attribute.DivineFavor]}"));
        Assert.Equal(success ? 2 : 1, events.Count);
        // the offer is spent after the message: it still shows the favor before (less the extra loss on a failure)
        Assert.StartsWith($"message {success}", events[0]);
        var shown = int.Parse(events[0].Split(' ')[3]);
        Assert.True(shown == 99 || !success && shown == Math.Max(1, 99 - favor / 2));
        if (success)
        {
            Assert.Equal("message True favor 99", events[0]);
            Assert.Equal("help", events[1]);
        }
        Assert.Equal(Math.Max(1, shown - favor), s.Party.Members[3][Attribute.DivineFavor]);
    }

    /// <summary>
    /// The empty saint slot -1 (a card's Enter on its saint line after a Shift hint on option 3, local/batch/b32/a6-saint): the original's
    /// screen reads the saint tables at -1, whose function (R029+0x4626) returns what it is given: no member list, no offer, P prays with
    /// chance 2 (the font word), nothing helps on land, the travel blessing is renewed on terrain 1 or 2.
    /// </summary>
    [Theory]
    [InlineData(1u, 0)]
    [InlineData(2u, 0)]
    [InlineData(7u, 0)]
    [InlineData(1234u, 1)]
    [InlineData(99u, 2)]
    public void AnEmptySaintSlotPraysWithTheFontWordAsTheChance(uint seed, int terrain)
    {
        var s = TestGame.Create(seed);
        s.PartyTerrain = terrain;
        var p = new SaintPrayer(s, 0, -1, PrayerMode.ForParty);
        Assert.False(p.Listed);
        Assert.False(p.ChoosesMember);
        Assert.Equal(0, p.Favor);
        Assert.True(p.CanPray);
        Assert.False(p.PraysByMouse);
        Assert.Equal(99, p.ShownChance);
        var df = s.Party.Members[0][Attribute.DivineFavor];
        p.OfferMore();
        p.OfferMore();
        Assert.Equal(Math.Min(2, df), p.Favor);
        var favor = p.Favor;
        var twin = TestGame.Twin(s);
        var effects = new Recorder();
        var success = p.Pray(Who.Leader, effects);
        var expected = twin.Below(100) <= SaintPrayer.UnlistedChance;
        Assert.Equal(expected, success);
        if (!expected)
        {
            twin.Below(99);
        }
        Assert.Equal(twin.State, ((MscRandom)s.Random).State);
        Assert.Empty(effects.Calls);
        Assert.Equal(Math.Max(1, df - favor), s.Party.Members[0][Attribute.DivineFavor]);
        Assert.Equal(expected && terrain is 1 or 2 ? 1 : 0, s.Events.Count(EventKinds.TravelBlessing));
    }

    [Fact]
    public void AnEmptySaintSlotSucceedsOnARollOfTwoOrLess()
    {
        var hits = 0;
        for (uint seed = 1; seed < 400; seed++)
        {
            var s = TestGame.Create(seed);
            var roll = TestGame.Twin(s).Below(100);
            var success = new SaintPrayer(s, 0, -1, PrayerMode.ForParty).Pray(Who.Leader, new Recorder());
            Assert.Equal(roll <= 2, success);
            hits += success ? 1 : 0;
        }
        Assert.InRange(hits, 1, 30);
    }

    private sealed class OrderRecorder(List<string> events) : ISaintEffects
    {
        public void Help(GameState state, int who, int saint, int favor) => events.Add("help");
    }
}
