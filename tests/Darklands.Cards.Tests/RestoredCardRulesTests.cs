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

    /// <summary>
    /// RestoreClothGuildShop (R-e0e0ff): the clothmakers' street by day never offers its shop line 0; restored, it is offered and opens
    /// the guild's shop (shop 8), an hour passes and the party is back on the street.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheClothmakersShopIsOfferedByDayOnlyWhenRestored(bool restored)
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var state = InCity(3, b => b.RestoreClothGuildShop = restored);
        var screen = new FirstMenu(0, 6);
        state.CurrentCard = ClothmakersStreet_Cloth00.CardNumber;
        var hour = state.Date.Hour;
        var next = new ClothmakersStreet_Cloth00().Run(Context(state, screen));
        Assert.Equal(restored ? OptionState.Enabled : OptionState.Hidden, screen.Menus[0][0]);
        // without a barter screen the shop says so (a Tell); an hour later the street's menu again (two menus), then leave (line 6)
        Assert.Equal(restored, screen.Log.Contains("tell"));
        Assert.Equal(restored ? 2 : 1, screen.Menus.Count);
        Assert.Equal(0x1A, next);
        Assert.Equal(restored ? hour + 1 : hour, state.Date.Hour);
    }

    /// <summary>The prison of city 3 with an imprisoned companion (event 0x4D here, its type the stored character 0) and 10 florins.</summary>
    private static GameState InPrison(bool restored)
    {
        var state = InCity(3, b => b.RestorePrisonRescue = restored);
        state.Events.Create(-2, 3, 0, 0x4D, 0, 0, 0, 9999, 0, 0);
        state.Party.Purse = Core.Economy.Money.From(10, 0, 0);
        state.CurrentCard = 286;
        return state;
    }

    private static bool Imprisoned(GameState state) => state.Events.Find(0x4D, -1, 3, -1, -1, -1) != -1;

    /// <summary>
    /// RestorePrisonRescue (R-b3f475): a bribe that works frees the companion on the spot; restored, it leads to card 288 (as 483.04-.06),
    /// which frees him there.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheRestoredBribeLeadsToTheFreedCompanionsCard(bool restored)
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var state = InPrison(restored);
        var screen = new FirstMenu(0, 4);
        var next = new PrisonCellsRescue().Run(Context(state, screen));
        Assert.Contains("text 1", screen.Log);
        Assert.Equal(Core.Economy.Money.From(5, 0, 0), state.Party.Purse);
        if (!restored)
        {
            Assert.False(Imprisoned(state));
            Assert.Equal(0x12, next);
            return;
        }
        Assert.True(Imprisoned(state));
        Assert.Equal(288, next);
        Assert.Equal(9, state.SavedCard);
        state.CurrentCard = 288;
        Assert.Equal(0x12, new CompanionReleased().Run(Context(state, new FirstMenu(0, 0))));
        Assert.False(Imprisoned(state));
    }

    /// <summary>
    /// RestorePrisonRescue (R-b3f475): the attack frees the companion before its fight, so even a lost fight frees him; restored, text 7
    /// (the guards of every floor) is shown, then the fight, and only a won fight (results 0, 1) leads to card 288; a flight (2) or a defeat
    /// (3, 4) frees nobody and goes where 483.07 goes.
    /// </summary>
    [Theory]
    [InlineData(false, 0, 0x12)]
    [InlineData(false, 2, 0x12)]
    [InlineData(false, 3, 0x0D)]
    [InlineData(true, 0, 288)]
    [InlineData(true, 1, 288)]
    [InlineData(true, 2, 0x12)]
    [InlineData(true, 3, 0x0D)]
    [InlineData(true, 4, 0x0D)]
    public void TheRestoredAttackFreesTheCompanionOnlyAfterAWonFight(bool restored, int result, int expected)
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var state = InPrison(restored);
        var screen = new FirstMenu(1, 4);
        var context = Context(state, screen);
        context.Battle = (_, _, _) => result;
        var next = new PrisonCellsRescue().Run(context);
        Assert.Equal(expected, next);
        Assert.Equal(restored, screen.Log.Contains("text 7"));
        // the original frees him first, whatever the fight; restored only card 288 does
        Assert.Equal(restored, Imprisoned(state));
    }

    private static int Teachers(GameState state) =>
        Enumerable.Range(0, EventLog.Capacity).Count(i => state.Events[i] is { Kind: EventKinds.Teacher } e && e.Location == state.CurrentLocation);
}
