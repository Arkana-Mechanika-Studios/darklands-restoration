using Darklands.Cards;
using Darklands.Cards.Scripts;

namespace Darklands.Cards.Tests;

/// <summary>
/// $Polit01 (card 0x37): a potion picked in the item box rolls against the word pointer-hide's display driver left in the handler's unset
/// result (0x0240, R057+0x1E5E..0x1E81 with MGRAPHIC drv+0x023D), so it always works; a Shift hint over the box keeps the other leftover.
/// </summary>
public sealed class HandbillThugsPotionTests
{
    /// <summary>Picks the potion of slot <paramref name="slot"/> in member 0's row of the item box, once; asks a Shift hint over it first.</summary>
    private sealed class PickingScreen(Func<CardContext> context, int slot) : ICardScreen
    {
        private int _menus;

        public List<int> Texts { get; } = [];

        public int? ShiftHintChance { get; private set; }

        public void Show(string cardName, string picture) { }

        public void ShowPicture(string picture) { }

        public void Text(int block, TextFrame frame = TextFrame.OnPicture) => Texts.Add(block);

        public int Choose(int block, IReadOnlyList<OptionState> options, Func<int, OptionHint>? hint = null) => Pick();

        public int Choose(int block, IReadOnlyList<OptionState> options, Func<int, OptionHint>? hint, Func<int, OptionHint>? boxHint, Action? afterOptions)
        {
            afterOptions?.Invoke();
            var c = context();
            c.MenuMember = 0;
            c.MenuSlot = slot;
            c.MenuBox = PopupBox.Items;
            ShiftHintChance ??= boxHint?.Invoke(4).Chance;
            return Pick();
        }

        private int Pick()
        {
            var c = context();
            if (++_menus > 1)
            {
                throw new InvalidOperationException("the card offered its menu again");
            }
            c.MenuPopup = true;
            c.MenuMember = 0;
            c.MenuSlot = slot;
            c.MenuBox = PopupBox.Items;
            return 4;
        }

        public void Tell(string message, int member = OptionHint.WholeParty) { }

        public void PassTime(int hours) { }
    }

    [Theory]
    [InlineData(0, 0x0B)]
    [InlineData(1, 0x0C)]
    [InlineData(2, 0x0D)]
    [InlineData(3, 0x0E)]
    public void APotionPickedInTheBoxAlwaysWorks(int slot, int text)
    {
        if (!CardHarness.Available)
        {
            return;
        }
        for (uint seed = 1; seed <= 8; seed++)
        {
            var state = CardHarness.NewGame(seed: seed, location: 3);
            CardContext? context = null;
            var screen = new PickingScreen(() => context!, slot);
            context = new CardContext(state, CardHarness.World!, screen);
            var battles = 0;
            context.Battle = (_, _, _) => { battles++; return 0; };

            new HandbillThugs_Polit01().Run(context);

            Assert.Equal([text], screen.Texts);
            Assert.Equal(0, battles);
        }
    }

    [Fact]
    public void AShiftHintOverTheBoxKeepsTheOtherLeftover()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var state = CardHarness.NewGame(seed: 1, location: 3);
        CardContext? context = null;
        var screen = new PickingScreen(() => context!, 0);
        context = new CardContext(state, CardHarness.World!, screen);
        context.Battle = (_, _, _) => 0;
        new HandbillThugs_Polit01().Run(context);
        if (screen.ShiftHintChance is { } chance)
        {
            Assert.Equal(-2, chance);
        }
    }
}
