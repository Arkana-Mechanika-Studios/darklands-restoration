using Darklands.Cards.Scripts;
using Darklands.Cards.Tests.Oracle;
using Darklands.Core.Economy;

namespace Darklands.Cards.Tests;

/// <summary>
/// Card states the community's saved games reach (local/batch/cardsaves): a purse with groschen and pfennigs, a walled city whose
/// picture query is negative, a merchant's title, a prayer without the pop-up's hint.
/// </summary>
public sealed class SavedGameStateCardTests
{
    [Fact]
    public void FamineTakesOnlyTheFlorinsWords()
    {
        // R133+0x04D3: DS:906B = 0 and DS:9072 = 0
        if (!CardHarness.Available)
        {
            return;
        }
        var state = CardHarness.NewGame();
        state.Party.Purse = Money.From(100, 5, 7);
        state.Party.BankNotes = Money.From(30, 2, 3);
        var screen = new LineScreen(4);
        var c = new CardContext(state, CardHarness.World!, screen) { PersonalNames = CardHarness.PersonalNames };
        state.CurrentCard = 356;
        Assert.Equal(-2, new FamineHorseman().Run(c));
        Assert.Equal(Money.From(0, 5, 7), state.Party.Purse);
        Assert.Equal(Money.From(0, 2, 3), state.Party.BankNotes);
    }

    [Fact]
    public void ANegativePictureQueryKeepsTheLastPicture()
    {
        // R113+0x0687 / R072+0x14C5: location-now-query(0x21) % 3 < 0 matches no case; the card is drawn with image-buffer as it is
        if (!CardHarness.Available)
        {
            return;
        }
        foreach (Card card in new Card[] { new WalledCityApproach_CityE00(), new BeforeTheCityWalls() })
        {
            var state = CardHarness.NewGame(location: 3);
            // query 0x21 is (short)(city ordinal + party seed)
            state.Party.Seed = 0xFFFF - Core.Rules.LocationRules.Query(state, 3, 0x21) + state.Party.Seed;
            Assert.Equal(-1, Core.Rules.LocationRules.Query(state, 3, 0x21));
            var (_, screen) = CardHarness.Run(card, state, 4);
            Assert.Equal("show $CityE00 ", screen.Log[0]);
        }
        var oracle = new OracleScreen(0, false);
        oracle.Show("$Town00", "aproch1");
        oracle.ShowPicture("other");
        oracle.ShowWithTheLastPicture("$CityE00");
        Assert.Equal("show $CityE00 aproch1", oracle.Log[^2]);
    }

    [Fact]
    public void TradingAmongTheMerchantsNamesTheGoodsMerchant()
    {
        // R092+0x07B1: _fstrcpy($NamedOneName, the far pointer at 290E:2107)
        if (!CardHarness.Available)
        {
            return;
        }
        var state = CardHarness.NewGame(location: 3);
        var screen = new LineScreen(0);
        var c = new CardContext(state, CardHarness.World!, screen) { PersonalNames = CardHarness.PersonalNames };
        // an errand already offered here: the party stays among the merchants after trading (no new errand, no random name)
        c.Events.Create(-2, 3, 0x5F, 7, 0, 0, 0, 0x48, 0, 0);
        state.CurrentCard = 0xC1;
        new MerchantsOfTheCity().Run(c);
        Assert.Equal("goods merchant", c.Variables.Named[0]);
    }

    [Fact]
    public void APrayerLeavesTheWhispererAsTheCardChoseHim()
    {
        // R101+0x0E00 does not write text-slot-chosen-one (DS:EE8C); only the pop-up hint R101+0x10E0 does
        if (!CardHarness.Available)
        {
            return;
        }
        var state = CardHarness.NewGame();
        var probe = new CardContext(state, CardHarness.World!, new ScriptedScreen(0));
        probe.PartyMaxAttribute((int)Core.Characters.Attribute.Intelligence);
        var wisest = probe.BestMember;
        var screen = new LineScreen(2) { Member = (wisest + 1) % probe.PartySize };
        var c = new CardContext(state, CardHarness.World!, screen) { PersonalNames = CardHarness.PersonalNames, SaintPrayer = (_, _) => -1 };
        screen.Context = c;
        state.CurrentCard = 0xE6;
        new DwarfKingsRedCap_MineB03().Run(c);
        Assert.NotEqual(wisest, screen.Member);
        Assert.Equal(wisest, c.Variables.Chosen[0]);
    }

    /// <summary>The first menu answers a fixed line (with the pop-up member set), the next ones the last enabled line.</summary>
    private sealed class LineScreen(int line) : ICardScreen
    {
        private int _menus;

        public CardContext? Context { get; set; }

        public int Member { get; init; }

        public void Show(string cardName, string picture)
        {
        }

        public void ShowPicture(string picture)
        {
        }

        public void Text(int block, TextFrame frame = TextFrame.OnPicture)
        {
        }

        public int Choose(int block, IReadOnlyList<OptionState> options, Func<int, OptionHint>? hint = null)
        {
            if (++_menus > 20)
            {
                throw new InvalidOperationException("The card keeps asking.");
            }
            if (_menus == 1)
            {
                if (Context is { } c)
                {
                    c.MenuMember = Member;
                }
                _ = hint?.Invoke(line);
                return line;
            }
            var choice = Enumerable.Range(0, options.Count).Last(i => options[i] == OptionState.Enabled);
            _ = hint?.Invoke(choice);
            return choice;
        }

        public void Tell(string message, int member = OptionHint.WholeParty)
        {
        }

        public void PassTime(int hours)
        {
        }
    }
}
