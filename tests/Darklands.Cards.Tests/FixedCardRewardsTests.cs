using Darklands.Cards.Scripts;
using Darklands.Core;
using Darklands.Core.Events;

namespace Darklands.Cards.Tests;

/// <summary>
/// Three gameplay fixes of the Fixed rule set that keep a card's promise (decided 2026-11-24 from the restoration register): the
/// "come back" appointment, the fortress's commission pay and the village Schulz's reward. The original's behaviour with the flag off,
/// the fixed one with it on, on the same game (skipped without the game files).
/// </summary>
public sealed class FixedCardRewardsTests
{
    private const int RewardCard = 0x91;

    /// <summary>A card screen that answers the first menu with <paramref name="first"/>, the later ones with <paramref name="then"/>.</summary>
    private sealed class Choices(int first, int then) : ICardScreen
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
            if (++_menus > 20)
            {
                throw new InvalidOperationException("the card keeps asking");
            }
            var choice = _menus == 1 ? first : then;
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

    /// <summary>A new game standing at place <paramref name="place"/> by day (the working city record of <paramref name="city"/>), with the flags set.</summary>
    private static GameState At(int place, int city, Action<OriginalBehaviour> set, uint seed = 3)
    {
        var state = CardHarness.NewGame(seed, place);
        state.Data = CardHarness.World!;
        state.MapPosition = state.Locations[place].Position;
        state.City = state.Data.LoadCity(city);
        state.Date = state.Date with { Hour = 10 };
        var behaviour = OriginalBehaviour.Faithful;
        set(behaviour);
        state.Behaviour = behaviour;
        return state;
    }

    /// <summary>A finished robber-knight task (a resolved quest 3) of <paramref name="owner"/> for this place.</summary>
    private static void FinishedTask(GameState state, int owner) =>
        state.Events.Create(owner, state.CurrentLocation, 0, EventKinds.ResolvedQuest, 0, 0x0F, 0, 999, 0, 3);

    /// <summary>
    /// FixWelcomeAppointment: asking the council hall for an errand can end in "come back", an event that names the one-day welcome as its
    /// expiry handler but never runs it (flags 0); fixed, it runs: when the appointment ends the party is welcome (event 0x3B).
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheComeBackAppointmentGivesTheWelcomeOnlyWhenFixed(bool fixedGame)
    {
        if (!CardHarness.Available)
        {
            return;
        }
        for (var seed = 1u; seed < 200; seed++)
        {
            var state = At(3, 3, b => b.FixWelcomeAppointment = fixedGame, seed);
            state.CurrentCard = CouncilHall.CardNumber;
            var context = Context(state, new Choices(1, 8));
            new CouncilHall().Run(context);
            var appointment = Enumerable.Range(0, EventLog.Capacity).Select(i => state.Events[i]).FirstOrDefault(e => e is { Kind: 0x3A, ExpiryHandler: ExpiryHandler.FollowUp });
            if (appointment is null)
            {
                continue;   // no "come back" with this seed: a commission or a refusal
            }
            Assert.Equal(fixedGame, appointment.Flags.HasFlag(EventFlags.CallExpiryHandler));
            context.PassHours(10);   // the evening comes (the appointment ends at 18:00)
            Assert.Equal(fixedGame, context.EventAtHere(EventKinds.FollowUp));
            return;
        }
        Assert.Fail("no seed gave the come-back appointment");
    }

    /// <summary>
    /// FixFortressCommissionPay: a finished commission of the City Lord (owner 0x0A) is paid only by the town hall; at the fortress the next
    /// commission simply starts. Fixed, the fortress pays it first (seven florins a size of the city) and goes to the reward card with itself
    /// as the patron.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheFortressPaysAFinishedCommissionOnlyWhenFixed(bool fixedGame)
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var state = At(3, 3, b => b.FixFortressCommissionPay = fixedGame);
        FinishedTask(state, 0x0A);
        var before = state.Party.Purse;
        var context = Context(state, new Choices(0, 0));
        state.CurrentCard = FortressByDay.CardNumber;
        FortressByDay.ReceiveACommission(context, () => 6);
        var size = context.HereQuery(2);
        Assert.Equal(fixedGame ? before.Add(size * 7, 0, 0) : before, state.Party.Purse);
        Assert.Equal(fixedGame ? RewardCard : 0x90, context.Next);
        Assert.Equal(fixedGame ? FortressByDay.CardNumber : 6, state.SavedCard);
    }

    /// <summary>
    /// FixVillageSchulzReward: the Schulz's robber-knight task (owner 9) is never paid: asking him for information goes on as ever. Fixed, a
    /// finished task is paid first (five florins a size of the village) and the party goes to the reward card with the Schulz as its patron.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheSchulzPaysAFinishedTaskOnlyWhenFixed(bool fixedGame)
    {
        if (!CardHarness.Available)
        {
            return;
        }
        // a village: the first place whose arrival card is the hamlet's square
        var places = CardHarness.NewGame(3).Locations;
        var village = Enumerable.Range(0, places.Count).First(l => places[l].ArrivalCard == HamletSquare.CardNumber);
        var state = At(village, 0, b => b.FixVillageSchulzReward = fixedGame);
        FinishedTask(state, 9);
        var before = state.Party.Purse;
        var context = Context(state, new Choices(0, 4));
        state.CurrentCard = VillageSchulz.CardNumber;
        var next = new VillageSchulz().Run(context);
        var pay = context.LocationQuery(village, 2) * 5;
        Assert.Equal(fixedGame ? before.Add(pay, 0, 0) : before, state.Party.Purse);
        Assert.Equal(fixedGame, next == RewardCard);
        if (fixedGame)
        {
            Assert.Equal(VillageSchulz.CardNumber, state.SavedCard);
        }
    }
}
