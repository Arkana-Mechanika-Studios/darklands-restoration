using Darklands.Cards.Scripts;
using Darklands.Core;

namespace Darklands.Cards.Tests;

/// <summary>
/// The gameplay fixes of the Fixed rule set that live in cards (rules set A3): the original's behaviour with the flag off and the
/// fixed one with the flag on, on the same game (skipped without the game files).
/// </summary>
public sealed class FixedCardRulesTests
{
    private const int UniversityTried = 0x4E, SaintsLine = 0, FormulasLine = 1;

    /// <summary>A card screen that answers the menus with the given options in turn, then always the last.</summary>
    private sealed class Choices(params int[] options) : ICardScreen
    {
        private int _menus;

        public void Show(string cardName, string picture)
        {
        }

        public void ShowPicture(string picture)
        {
        }

        public void Text(int block, TextFrame frame = TextFrame.OnPicture)
        {
        }

        public int Choose(int block, IReadOnlyList<OptionState> offered, Func<int, OptionHint>? hint = null)
        {
            var choice = options[Math.Min(_menus++, options.Length - 1)];
            _ = hint?.Invoke(choice);
            return choice;
        }

        public void Post(GameMessage message)
        {
        }

        public void Tell(GameText message, int member = OptionHint.WholeParty)
        {
        }

        public void PassTime(int hours)
        {
        }
    }

    private static CardContext Context(GameState state, ICardScreen screen) => new(state, CardHarness.World!, screen) { PersonalNames = CardHarness.PersonalNames };

    /// <summary>
    /// FixUniversityFormulasTried: the university card records a failed formulas search as a tried saints search (the saints line
    /// shut for the week, the formulas line open again next visit); fixed, the formulas line is the one recorded.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFailedFormulasSearchShutsTheSaintsOrTheFormulasLine(bool fixedGame)
    {
        if (!CardHarness.Available)
        {
            return;
        }
        // a seed whose formulas search fails (the leader's chance is low: no fame, a poor scholar), by day; then to the main street
        for (var seed = 1u; seed < 40; seed++)
        {
            var state = CardHarness.NewGame(seed);
            state.Behaviour = new OriginalBehaviour { FixUniversityFormulasTried = fixedGame };
            state.Date = state.Date with { Hour = 10 };
            var context = Context(state, new Choices(FormulasLine, 8));
            state.CurrentCard = University_Unive00.CardNumber;
            var next = new University_Unive00().Run(context);
            if (next == 0x53)
            {
                continue;   // the search succeeded: to the alchemist's shop
            }
            Assert.Equal(fixedGame, context.EventTypeAtHere(UniversityTried, FormulasLine));
            Assert.Equal(!fixedGame, context.EventTypeAtHere(UniversityTried, SaintsLine));
            return;
        }
        Assert.Fail("no seed made the formulas search fail");
    }

    /// <summary>
    /// FixChurchAltarBoyStaleHour: the church-at-night card asked about Mass between 09:00 and 14:59 names a stale word (−1: Matins)
    /// as the hour; fixed, the hour named when no Mass is held (6: Prime's band).
    /// </summary>
    [Theory]
    [InlineData(false, 10)]
    [InlineData(true, 10)]
    [InlineData(false, 13)]
    [InlineData(true, 13)]
    public void TheAltarBoyNamesAStaleHourOrTheNoMassHour(bool fixedGame, int hour)
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var state = CardHarness.NewGame(3);
        state.Behaviour = new OriginalBehaviour { FixChurchAltarBoyStaleHour = fixedGame };
        state.Date = state.Date with { Hour = (short)hour };
        // ask the altar boy, then leave for the church
        var context = Context(state, new Choices(1, 3));
        state.CurrentCard = ChurchAtNight_CityC01.CardNumber;
        new ChurchAtNight_CityC01().Run(context);
        var expected = Context(CardHarness.NewGame(3), new Choices(0));
        expected.NameToSlot(3, fixedGame ? 6 : -1, 0);
        Assert.Equal(expected.Variables.Named[0], context.Variables.Named[0]);
        var other = Context(CardHarness.NewGame(3), new Choices(0));
        other.NameToSlot(3, fixedGame ? -1 : 6, 0);
        Assert.NotEqual(other.Variables.Named[0], context.Variables.Named[0]);
    }
}
