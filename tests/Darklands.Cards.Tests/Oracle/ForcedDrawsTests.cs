using Darklands.Core.Randomness;

namespace Darklands.Cards.Tests.Oracle;

/// <summary>The forced draws of the directed-mutation runs (run key parts R&lt;k&gt;_&lt;v&gt;; tools/oracle/card_oracle.py draws_of / draw_state).</summary>
public sealed class ForcedDrawsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(99)]
    [InlineData(12345)]
    [InlineData(32767)]
    public void TheStateForAValueDrawsThatValue(int raw)
    {
        var random = new MscRandom(ForcedDraws.StateFor(raw));
        Assert.Equal(raw, random.NextRaw());
        Assert.Equal((uint)raw << 16, random.State);
    }

    [Fact]
    public void TheStateIsTheOneThePythonSideComputes()
    {
        // card_oracle.py draw_state(37) = ((37 << 16) - 2531011) * pow(214013, -1, 2**32) mod 2**32
        Assert.Equal(1u, unchecked(214013u * ForcedDraws.MultiplierInverse));
        Assert.Equal(unchecked(((37u << 16) - 2531011u) * ForcedDraws.MultiplierInverse), ForcedDraws.StateFor(37));
    }

    [Fact]
    public void TheKeyPartsNameTheDrawsAndChangeNoState()
    {
        Assert.Equal(new Dictionary<int, int> { [0] = 5, [3] = 99 }, CardOracleStates.DrawsOf("L3-Vsk3_40+R0_5+R3_99-O2"));
        Assert.Empty(CardOracleStates.DrawsOf("L3-Vsk3_40-O2"));
        Assert.True(CardOracleStates.IsDraw("R12_0"));
        Assert.False(CardOracleStates.IsDraw("R12_40000"));
        Assert.False(CardOracleStates.IsDraw("rn5"));
        Assert.Equal("L3-Vsk3_40-O2", CardAimSearch.StateKey("L3-Vsk3_40+R0_5-O2"));
    }

    [Fact]
    public void TheKthDrawOfARunIsForcedAndTheOthersAreNot()
    {
        var plain = new MscRandom(8);
        var expected = new[] { plain.NextRaw(), plain.NextRaw() };
        var random = new MscRandom(8);
        ForcedDraws.Begin(random, new Dictionary<int, int> { [2] = 4321 });
        try
        {
            Assert.Equal(expected[0], random.NextRaw());
            Assert.Equal(expected[1], random.NextRaw());
            Assert.Equal(4321, random.NextRaw());
            var after = new MscRandom(4321u << 16);
            Assert.Equal(after.NextRaw(), random.NextRaw());
        }
        finally
        {
            ForcedDraws.End();
        }
        var unforced = new MscRandom(8);
        unforced.NextRaw();
        unforced.NextRaw();
        var third = unforced.NextRaw();
        Assert.Equal(third, new MscRandom(unforced.PreviousState()).NextRaw());
    }
}
