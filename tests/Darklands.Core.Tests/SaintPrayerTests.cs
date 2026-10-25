using Darklands.Core.Characters;
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
    /// <summary>saint_pray R034+0x0166..0x01F5: the message before the saint's help, after the extra loss, before the offer is spent.</summary>
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

    private sealed class OrderRecorder(List<string> events) : ISaintEffects
    {
        public void Help(GameState state, int who, int saint, int favor) => events.Add("help");
    }
}
