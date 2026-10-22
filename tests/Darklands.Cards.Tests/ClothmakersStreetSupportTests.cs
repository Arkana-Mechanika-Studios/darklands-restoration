using Darklands.Cards.Scripts;

namespace Darklands.Cards.Tests;

/// <summary>
/// Card 0x25: asking about the guild (R053+0x120D) and revealing the leader's secret (R053+0x17D6)
/// put the guild's side into the shared $Support (DS:ED5E), which the texts read.
/// </summary>
public sealed class ClothmakersStreetSupportTests
{
    private const int GuildBit = 0x40;

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void AskingAboutTheGuildSetsTheSharedSupport(int side)
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var state = CardHarness.NewGame(seed: 7, location: 3);
        var c = new CardContext(state, CardHarness.World!, new ForcedScreen(1)) { PersonalNames = CardHarness.PersonalNames };
        c.SetSide(GuildBit, side);
        c.Support[0] = 0x55;
        state.CurrentCard = ClothmakersStreet_Cloth00.CardNumber;
        new ClothmakersStreet_Cloth00().Run(c);
        Assert.Equal(c.SideOf(GuildBit), c.Support[0]);
        Assert.Equal(side, c.Support[0]);
    }

    [Fact]
    public void RevealingTheSecretSetsTheSharedSupportToTheNewSide()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var successes = 0;
        for (uint seed = 1; seed <= 60 && successes < 3; seed++)
        {
            var state = CardHarness.NewGame(seed: seed, location: 3);
            var screen = new ForcedScreen(3);
            var c = new CardContext(state, CardHarness.World!, screen) { PersonalNames = CardHarness.PersonalNames };
            c.Support[0] = 0x55;
            state.CurrentCard = ClothmakersStreet_Cloth00.CardNumber;
            new ClothmakersStreet_Cloth00().Run(c);
            if (!screen.Texts.Contains(0x0B))
            {
                Assert.Equal(0x55, c.Support[0]);
                continue;
            }
            successes++;
            Assert.InRange(c.Support[0], -1, 1);
            Assert.Equal(c.SideOf(GuildBit), c.Support[0]);
        }
        Assert.True(successes > 0, "no seed revealed the secret successfully");
    }

    /// <summary>Answers the first menu with a fixed line even when the card does not offer it (as the oracle's forced lines do), then leaves.</summary>
    private sealed class ForcedScreen(int line) : ICardScreen
    {
        private int _menus;

        public List<int> Texts { get; } = [];

        public void Show(string cardName, string picture)
        {
        }

        public void ShowPicture(string picture)
        {
        }

        public void Text(int block, TextFrame frame = TextFrame.OnPicture) => Texts.Add(block);

        public int Choose(int block, IReadOnlyList<OptionState> options, Func<int, OptionHint>? hint = null)
        {
            var choice = _menus++ == 0 ? line : 6;
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
