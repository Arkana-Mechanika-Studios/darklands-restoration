using Darklands.Cards.Scripts;
using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Core.Events;

namespace Darklands.Cards.Tests;

/// <summary>
/// The restorations of the Restored rule set that live in cards (restoration.csv, status built): the original's behaviour with the flag
/// off and the restored one with it on, on the same game (skipped without the game files).
/// </summary>
public sealed class RestoredCardRulesTests
{
    /// <summary>A card screen that records the first menu's lines and answers it with <paramref name="wanted"/> when that line is enabled, else with <paramref name="otherwise"/>; later menus get <paramref name="otherwise"/>.</summary>
    private sealed class FirstMenu(int wanted, int otherwise) : ICardScreen
    {
        public List<IReadOnlyList<OptionState>> Menus { get; } = [];

        public List<string> Log { get; } = [];

        public void Show(string cardName, string picture)
        {
        }

        public void ShowPicture(string picture)
        {
        }

        public void Text(int block, TextFrame frame = TextFrame.OnPicture) => Log.Add($"text {block}");

        public int Choose(int block, IReadOnlyList<OptionState> offered, Func<int, OptionHint>? hint = null)
        {
            Menus.Add(offered.ToArray());
            if (Menus.Count > 20)
            {
                throw new InvalidOperationException("the card keeps asking");
            }
            var choice = Menus.Count == 1 && offered[wanted] == OptionState.Enabled ? wanted : otherwise;
            _ = hint?.Invoke(choice);
            return choice;
        }

        public void Post(GameMessage message)
        {
        }

        public void Tell(GameText message, int member = OptionHint.WholeParty) => Log.Add("tell");

        public void PassTime(int hours)
        {
        }
    }

    private static CardContext Context(GameState state, ICardScreen screen) => new(state, CardHarness.World!, screen) { PersonalNames = CardHarness.PersonalNames };

    /// <summary>A new game standing in city <paramref name="city"/> (its working city record loaded), by day, with the flag set.</summary>
    private static GameState InCity(int city, Action<OriginalBehaviour> set, uint seed = 3)
    {
        var state = CardHarness.NewGame(seed, city);
        state.Data = CardHarness.World!;
        state.MapPosition = state.Locations[city].Position;
        state.City = state.Data.LoadCity(city);
        state.Date = state.Date with { Hour = 10 };
        var behaviour = OriginalBehaviour.Faithful;
        set(behaviour);
        state.Behaviour = behaviour;
        return state;
    }

    /// <summary>The cities in which <paramref name="test"/> holds (on a faithful game in that city).</summary>
    private static IEnumerable<int> Cities(Func<CardContext, bool> test) =>
        Enumerable.Range(0, Core.World.Location.CityCount).Where(city => test(Context(InCity(city, _ => { }), new FirstMenu(0, 0))));

    /// <summary>
    /// RestoreBusinessDistrictPhysician: the business district never offers its line 3 (the physician's house, card 0x18); restored, it is
    /// offered and leads there, except in the small towns and years its own test hides it in.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheBusinessDistrictOffersThePhysicianOnlyWhenRestored(bool restored)
    {
        if (!CardHarness.Available)
        {
            return;
        }
        static bool HidesIt(CardContext c) => c.HereQuery(2) <= 3 && (c.HereQuery(0x21) + c.State.Date.Year) % 3 == 0;
        var open = Cities(c => !HidesIt(c)).First();
        var shut = Cities(HidesIt).First();
        foreach (var city in new[] { open, shut })
        {
            var screen = new FirstMenu(3, 6);
            var state = InCity(city, b => b.RestoreBusinessDistrictPhysician = restored);
            state.CurrentCard = BusinessDistrict.CardNumber;
            var next = new BusinessDistrict().Run(Context(state, screen));
            var offered = restored && city == open;
            Assert.Equal(offered ? OptionState.Enabled : OptionState.Hidden, screen.Menus[0][3]);
            Assert.Equal(offered, next == PhysiciansHouse.CardNumber);
        }
    }

    /// <summary>
    /// RestoreJewellersGuild: the crafts quarter never offers its line 2 (the Jewelers' guild); restored, it is offered and leads to card
    /// 0x5D by day.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheCraftsQuarterOffersTheJewellersOnlyWhenRestored(bool restored)
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var screen = new FirstMenu(2, 9);
        var state = InCity(3, b => b.RestoreJewellersGuild = restored);
        state.CurrentCard = CraftsQuarter.CardNumber;
        var next = new CraftsQuarter().Run(Context(state, screen));
        Assert.Equal(restored ? OptionState.Enabled : OptionState.Hidden, screen.Menus[0][2]);
        Assert.Equal(restored, next == 0x5D);
    }

    /// <summary>
    /// RestoreJewellersGuild, the night card: a failed sneak names (and practises) the member whose NUMBER is the party's lowest Stealth;
    /// restored, the member who has it.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFailedSneakAtTheJewellersNamesTheLeastStealthyMemberWhenRestored(bool restored)
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var state = InCity(3, b => b.RestoreJewellersGuild = restored);
        state.Date = state.Date with { Hour = 23 };
        var able = state.Party.Slots.Select((m, i) => (m, i)).Where(x => x.m.Status == CharacterStatus.Able).Select(x => x.i).ToList();
        Assert.True(able.Count >= 2);
        // every able member a good picker (Artifice 60, so the lock is not what fails) and a fair sneak; the second the clumsiest (Stealth 3)
        foreach (var i in able)
        {
            state.Party.Slots[i].Skills[(int)Skill.Artifice] = 60;
            state.Party.Slots[i].Skills[(int)Skill.Stealth] = 40;
        }
        var clumsy = able[1];
        state.Party.Slots[clumsy].Skills[(int)Skill.Stealth] = 3;
        var context = Context(state, new FirstMenu(9, 9));
        JewelersGuildAtNight.FailedToGetIn(context);
        Assert.Equal(restored ? clumsy : 3, context.Variables.Chosen[0]);
    }

    /// <summary>
    /// RestoreGuardTraining: the council hall never offers line 5 (weapons training with the guard); restored, it is offered in a city that
    /// is not hostile, the guard's answer follows (an hour, the training offers or a loss of standing), and the line is greyed until the next day.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheGuardsTrainingIsOfferedOnceADayOnlyWhenRestored(bool restored)
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var state = InCity(3, b => b.RestoreGuardTraining = restored);
        var before = Teachers(state);
        var standing = Context(state, new FirstMenu(0, 0)).HereQuery(0);
        var screen = new FirstMenu(5, 8);
        state.CurrentCard = CouncilHall.CardNumber;
        var next = new CouncilHall().Run(Context(state, screen));
        Assert.Equal(restored ? OptionState.Enabled : OptionState.Hidden, screen.Menus[0][5]);
        if (!restored)
        {
            Assert.Equal(before, Teachers(state));
            return;
        }
        Assert.Equal(0x12, next);   // to the city square by day
        var trained = Teachers(state) == before + 5;
        Assert.True(trained ? screen.Log.Contains("text 12") : screen.Log.Contains("text 13") && Context(state, new FirstMenu(0, 0)).HereQuery(0) < standing);
        // asked today: greyed on the next visit
        var again = new FirstMenu(5, 8);
        state.CurrentCard = CouncilHall.CardNumber;
        new CouncilHall().Run(Context(state, again));
        Assert.Equal(OptionState.Shown, again.Menus[0][5]);
    }

    private static int Teachers(GameState state) =>
        Enumerable.Range(0, EventLog.Capacity).Count(i => state.Events[i] is { Kind: EventKinds.Teacher } e && e.Location == state.CurrentLocation);

    /// <summary>
    /// RestoreWelcomeAppointment: asking the council hall for an errand can end in "come back", an event that names the one-day welcome as
    /// its expiry handler but never runs it (flags 0); restored, it runs: when the appointment ends the party is welcome (event 0x3B) and
    /// the audience is granted at 99 per cent.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheComeBackAppointmentGivesTheWelcomeOnlyWhenRestored(bool restored)
    {
        if (!CardHarness.Available)
        {
            return;
        }
        for (var seed = 1u; seed < 200; seed++)
        {
            var state = InCity(3, b => b.RestoreWelcomeAppointment = restored, seed);
            var screen = new FirstMenu(1, 8);
            state.CurrentCard = CouncilHall.CardNumber;
            var context = Context(state, screen);
            new CouncilHall().Run(context);
            var appointment = Enumerable.Range(0, EventLog.Capacity).Select(i => state.Events[i]).FirstOrDefault(e => e is { Kind: 0x3A, ExpiryHandler: ExpiryHandler.FollowUp });
            if (appointment is null)
            {
                continue;   // no "come back" with this seed: a commission or a refusal
            }
            Assert.Equal(restored, appointment.Flags.HasFlag(EventFlags.CallExpiryHandler));
            // the evening comes (the appointment ends at 18:00): welcome or not
            context.PassHours(10);
            Assert.Equal(restored, context.EventAtHere(EventKinds.FollowUp));
            return;
        }
        Assert.Fail("no seed gave the come-back appointment");
    }
}
