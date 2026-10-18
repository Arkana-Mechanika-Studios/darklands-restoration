using Darklands.Cards;
using Darklands.Cards.Scripts;
using Darklands.Core.Characters;

namespace Darklands.Cards.Tests;

/// <summary>Focused checks of the group C card API and of a few tricky cards.</summary>
public sealed class GroupCTests
{
    [Theory]
    [InlineData(1, 99, 50, 50)]
    [InlineData(1, 99, -5, 1)]
    [InlineData(1, 99, 120, 99)]
    // clamp_range tests the upper bound first: crossed bounds give the upper one for large values.
    [InlineData(1, 0, 5, 0)]
    [InlineData(1, 0, -3, 1)]
    public void ClampRangeTestsTheUpperBoundFirst(int low, int high, int value, int expected) =>
        Assert.Equal(expected, CardContext.ClampRange(low, high, value));

    [Fact]
    public void RandomNameIsTheSameForTheSameSeed()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var state = CardHarness.NewGame(seed: 5, location: 3);
        var c = new CardContext(state, CardHarness.World!, new ScriptedScreen(0));
        c.PersonalNames = new TableNames();
        c.RandomName(100, 0, 0);
        var first = c.Variables.Named[0];
        c.RandomName(7, 1, 1);
        c.RandomName(100, 0, 2);
        Assert.Equal(first, c.Variables.Named[2]);
        Assert.StartsWith("MenFirstNames:", first);
        Assert.Contains(" Surnames:", first);
        Assert.StartsWith("WomenFirstNames:", c.Variables.Named[1]);
    }

    [Fact]
    public void DeleteMemberPassesTheLeadershipOn()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var state = CardHarness.NewGame(seed: 5, location: 3);
        var c = new CardContext(state, CardHarness.World!, new ScriptedScreen(0));
        if (state.Party.Members.Count < 2)
        {
            return;
        }
        state.Party.LeaderIndex = 0;
        c.DeleteMember(0);
        Assert.Equal(CharacterStatus.Empty, state.Party.Slot(0).Status);
        Assert.NotEqual(0, state.Party.LeaderIndex);
    }

    [Fact]
    public void TheExecutionCardHasNoTableNameAndShowsItsOwn()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var state = CardHarness.NewGame(seed: 3, location: 3);
        var (_, screen) = CardHarness.Run(new PublicExecution(), state, 4);
        Assert.Contains(screen.Log, line => line.StartsWith("show $Execu01", StringComparison.Ordinal));
    }

    private sealed class TableNames : IPersonalNames
    {
        public string Name(NameTable table, int index) => $"{table}:{index}";

        public int Count(NameTable table) => 0x100;
    }
}
