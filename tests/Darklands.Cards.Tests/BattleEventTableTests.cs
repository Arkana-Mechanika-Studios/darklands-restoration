using Darklands.Core.Events;

namespace Darklands.Cards.Tests;

/// <summary>
/// A card's battle (run_battle): cards_shutdown writes the events to EVENTS.TMP and empties the table before it,
/// cards_startup's world_load reads them back into the first slots after it, over what the battle added there.
/// </summary>
public sealed class BattleEventTableTests
{
    [Fact]
    public void ACardsBattleRunsOnAnEmptyTableAndItsBoostIsLost()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var state = CardHarness.NewGame();
        var context = new CardContext(state, CardHarness.World!, new ScriptedScreen(-1)) { PersonalNames = CardHarness.PersonalNames };
        state.Events.Put(5, new TimedEvent { Kind = 0x0B, Owner = -2, End = EventLog.ForeverEnd });
        var before = state.Events.All().Select(x => x.Event.Kind).ToList();
        var seen = -1;
        context.Battle = (_, _, _) =>
        {
            seen = state.Events.All().Count();
            // A saint's boost called in the battle: the first slot, which the reload takes back.
            state.Events.Create(0, -1, 0, EventKinds.AttributeBoost, 1, 5, 0, 24, 0, 0);
            return 0;
        };

        context.RunBattle(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        Assert.Equal(0, seen);
        Assert.Equal(before, state.Events.All().Select(x => x.Event.Kind));
        Assert.Equal(Enumerable.Range(0, before.Count), state.Events.All().Select(x => x.Slot));
        Assert.Equal(0, state.Events.Count(EventKinds.AttributeBoost));
    }
}
