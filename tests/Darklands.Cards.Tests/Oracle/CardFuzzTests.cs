using Darklands.Core.Randomness;

namespace Darklands.Cards.Tests.Oracle;

/// <summary>The fuzzer's parts of the card harness (CardFuzz): its run keys, the saved games held in memory, the draw count.</summary>
public sealed class CardFuzzTests
{
    [Fact]
    public void AnInputsKeyIsAnSKeyWithItsKeptPartsThenThePathThenTheDraws()
    {
        Assert.Equal("Sfzab12-VT$Alche01+rn7+P1_0_19+R0_5+R3_32767-O4",
            CardFuzz.KeyOf("fzab12", 4, ["T$Alche01", "rn7"], [1, 0, 19], new Dictionary<int, int> { [3] = 32767, [0] = 5 }));
        Assert.Equal("Sfzab12-O0", CardFuzz.KeyOf("fzab12", 0, [], [], new Dictionary<int, int>()));
        var key = CardFuzz.KeyOf("fzab12", 2, ["Ya"], [3], new Dictionary<int, int> { [1] = 9 });
        // the harness reads the parts back as any run key's
        Assert.Equal([3], CardOracleStates.PathOf(key)!);
        Assert.Equal(new Dictionary<int, int> { [1] = 9 }, CardOracleStates.DrawsOf(key));
        Assert.Equal([1], CardOracleStates.PrayerAnswersOf(key)!);
        Assert.Equal("Sfzab12-O2", CardAimSearch.StateKey(key));
    }

    [Fact]
    public void CountingCountsTheDrawsOfARunWithoutForcingAny()
    {
        // the count is the thread's (every generator's draws count), so the expected values are drawn before it starts
        var reference = new MscRandom(42);
        var expected = Enumerable.Range(0, 3).Select(_ => reference.NextRaw()).ToArray();
        var random = new MscRandom(42);
        try
        {
            ForcedDraws.Counting = true;
            ForcedDraws.Begin(random, []);
            for (var i = 0; i < 3; i++)
            {
                Assert.Equal(expected[i], random.NextRaw());
            }
            ForcedDraws.End();
            Assert.Equal(3, ForcedDraws.LastCount);
            ForcedDraws.Counting = false;
            ForcedDraws.Begin(random, []);
            random.NextRaw();
            ForcedDraws.End();
            Assert.Equal(0, ForcedDraws.LastCount);
        }
        finally
        {
            ForcedDraws.Counting = false;
            ForcedDraws.End();
        }
    }

    [Fact]
    public void ASavedGameHeldInMemoryIsReadByTheLoaderAsAFileIs()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var (sav, _) = CardOracleStates.Export(CardOracleStates.New(3, 2));
        var canonical = CardFuzz.Canonical(sav, 2);
        Assert.NotNull(canonical);
        // the canonical form is a fixed point of the C#'s read and write
        Assert.Equal(canonical, CardFuzz.Canonical(canonical, 2));
        // the play-range filter: the start state is in range; a month of 12 or a location-now past the places is not
        Assert.True(CardFuzz.InPlayRange(canonical, canonical));
        var month = (byte[])canonical.Clone();
        month[0x6A] = 12;
        Assert.False(CardFuzz.InPlayRange(month, canonical));
        var place = (byte[])canonical.Clone();
        place[0x7C] = 0xE7;
        place[0x7D] = 0x03;
        Assert.False(CardFuzz.InPlayRange(place, canonical));
        const string Id = "fztest-held";
        try
        {
            CardOracleStates.InMemorySaves[Id] = canonical;
            var state = CardOracleStates.FromKey($"S{Id}-O2");
            Assert.Equal(CardOracleStates.FromBytes(canonical, 2).Party.Slots.Count, state.Party.Slots.Count);
            Assert.Equal(canonical, CardOracleStates.Export(state).Sav);
            // the run key's state atoms apply after the load as for any S key
            Assert.Equal(7u, ((MscRandom)CardOracleStates.FromKey($"S{Id}-Vrn7-O2").Random).State);
        }
        finally
        {
            CardOracleStates.InMemorySaves.TryRemove(Id, out _);
        }
    }
}
