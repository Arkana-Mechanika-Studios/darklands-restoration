using Darklands.Cards;
using Darklands.Cards.Scripts;
using Darklands.Core.Economy;

namespace Darklands.Cards.Tests;

public sealed class AlchemistFormulaSellerTests
{
    /// <summary>Buys the formula in slot 1 through the item pop-up, then asks the alchemist to take leave.</summary>
    private sealed class BuyingScreen(Func<CardContext> context) : ICardScreen
    {
        private int _menus;

        public List<int> Texts { get; } = [];

        public void Show(string cardName, string picture) { }

        public void ShowPicture(string picture) { }

        public void Text(int block, TextFrame frame = TextFrame.OnPicture) => Texts.Add(block);

        public int Choose(int block, IReadOnlyList<OptionState> options, Func<int, OptionHint>? hint = null)
        {
            _menus++;
            var c = context();
            if (_menus == 1)
            {
                c.MenuPopup = true;
                c.MenuMember = 0;
                c.MenuSlot = 1;
                return 0;
            }
            c.MenuPopup = false;
            return 3;
        }

        public void Tell(string message, int member = OptionHint.WholeParty) { }

        public void PassTime(int hours) { }
    }

    [Fact]
    public void BuyingAFormulaPaysForItAndTeachesIt()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var state = CardHarness.NewGame(seed: 3, location: 3);
        state.Party.Purse = Money.From(500, 0, 0);
        CardContext? context = null;
        var screen = new BuyingScreen(() => context!);
        context = new CardContext(state, CardHarness.World!, screen);
        var before = state.Party.Purse;

        var next = new AlchemistFormulaSeller().Run(context);

        Assert.Equal(6, next);
        Assert.True(state.Party.Purse.CompareTo(before) < 0);
        Assert.Single(state.Party.Slot(0).Formulas, f => f / 3 + 0x5F == context.CardItems[1]);
        Assert.Empty(screen.Texts);
    }
}
