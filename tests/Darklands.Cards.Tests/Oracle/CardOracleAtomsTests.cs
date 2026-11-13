using Darklands.Core.Characters;

namespace Darklands.Cards.Tests.Oracle;

/// <summary>The start-state atoms the directed-mutation runs name (tools/oracle/mutate_branch.py: members added, place facts, standing).</summary>
public sealed class CardOracleAtomsTests
{
    [Fact]
    public void MembersAreAddedAsCopiesOfTheLastUpToTheFiveSlots()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var state = CardOracleStates.New(3, 0);
        var before = state.Party.Slots.Count;
        Assert.True(CardOracleAtoms.Apply(state, "ad5"));
        Assert.Equal(Party.MaxMembers, state.Party.Slots.Count);
        Assert.Equal(state.Party.Slots[before - 1].Name, state.Party.Slots[^1].Name);
        Assert.NotSame(state.Party.Slots[before - 1], state.Party.Slots[^1]);
        Assert.True(CardOracleAtoms.Apply(state, "ad9"));
        Assert.Equal(Party.MaxMembers, state.Party.Slots.Count);
        var cut = CardOracleStates.FromKey("L3-Vsz2+ad3-O0");
        Assert.Equal(3, cut.Party.Slots.Count);
    }

    [Fact]
    public void AFactIsRemovedThenStartedWithTheValueAPlaceFlagQueryReads()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        // place-flag-value(2, 0, location-now) = -1: er28_0_2 + ev28_0_2_-1_-2 (mutate_branch.py fact_parts)
        var state = CardOracleStates.FromKey("L3-Ver28_0_2+ev28_0_2_-1_-2-O0");
        Assert.Equal(-1, state.Events.FactValue(2, 0, state.CurrentLocation));
        Assert.True(state.Events.Fact(2, 0, state.CurrentLocation));
        var none = CardOracleStates.FromKey("L3-Ver28_0_2-O0");
        Assert.Equal(0, none.Events.FactValue(2, 0, none.CurrentLocation));
        // a resolved quest of an owner (kind 0x24) and an open quest valued with the place (9999: location-now)
        var quests = CardOracleStates.FromKey("L3-Vev36_0_10_0_4+ev8_0_3_9999_5-O0");
        Assert.True(quests.Events.ResolvedQuestAt(10, 4, quests.CurrentLocation));
        Assert.True(quests.Events.QuestWithValue(3, 5, quests.CurrentLocation));
    }
}
