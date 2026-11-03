using Darklands.Cards;
using Darklands.Cards.Scripts;
using Darklands.Core.Events;

namespace Darklands.Cards.Tests;

/// <summary>
/// $WildHunt (card 0x41): "outrun the Hunt" with not everybody mounted. The handler (R060+0x0FF4) leaves r.a unset on that path; rated
/// for the choice the word is what pointer-hide's display driver left (0x0240, R060+0x0B37..0x0B55), so outrunning always works (text 7).
/// </summary>
public sealed class WildHuntOutrunTests
{
    private sealed class OutrunScreen(Func<CardContext> context) : ICardScreen
    {
        private int _menus;

        public List<int> Texts { get; } = [];

        public void Show(string cardName, string picture) { }

        public void ShowPicture(string picture) { }

        public void Text(int block, TextFrame frame = TextFrame.OnPicture) => Texts.Add(block);

        public int Choose(int block, IReadOnlyList<OptionState> options, Func<int, OptionHint>? hint = null)
        {
            if (++_menus > 1)
            {
                throw new InvalidOperationException("the card offered its menu again");
            }
            context().MenuPopup = false;
            return 1;
        }

        public void Tell(string message, int member = OptionHint.WholeParty) { }

        public void PassTime(int hours) { }
    }

    [Fact]
    public void OutrunningOnFootAlwaysWorks()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var failures = 0;
        for (uint seed = 1; seed <= 12; seed++)
        {
            var state = CardHarness.NewGame(seed: seed, location: 3);
            CardContext? context = null;
            var screen = new OutrunScreen(() => context!);
            context = new CardContext(state, CardHarness.World!, screen);
            context.Battle = (_, _, _) => 0;
            // the Hunt's progress (fact 5 of quest group 7) at 4: the card offers its menu
            context.Events.Create(-2, context.Here, 0, EventKinds.LocationFact, 5, 4, 0, 100, 0, 7);
            Assert.Equal(0, context.PartyGet(5));

            new WildHunt().Run(context);

            Assert.DoesNotContain(8, screen.Texts);
            Assert.Contains(7, screen.Texts);
            failures += screen.Texts.Contains(8) ? 1 : 0;
        }
        Assert.Equal(0, failures);
    }
}
