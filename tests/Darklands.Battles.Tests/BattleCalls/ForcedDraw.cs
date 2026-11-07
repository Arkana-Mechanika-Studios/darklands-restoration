using Darklands.Core.Randomness;

namespace Darklands.Battles.Tests.BattleCalls;

/// <summary>
/// Forced draws of the directed-mutation jobs (tools/oracle/mutate_battle.py; the job's "force" map): the k-th draw of a call returns the
/// raw value v. As tools/oracle/battle_calls.py does at the entry of the original's rand(), the generator's state is set before that draw
/// to the state whose successor is <c>v &lt;&lt; 16</c>, so both programs draw v and go on from the same state.
/// </summary>
internal static class ForcedDraw
{
    private const uint Multiplier = 214013;
    private const uint Increment = 2531011;

    /// <summary>The inverse of the multiplier modulo 2^32.</summary>
    public static readonly uint MultiplierInverse = Inverse(Multiplier);

    /// <summary>The generator state from which the next draw returns <paramref name="raw"/> (0..32767).</summary>
    public static uint StateFor(int raw) => unchecked(((((uint)raw & 0x7FFF) << 16) - Increment) * MultiplierInverse);

    private static uint Inverse(uint a)
    {
        // Newton's iteration for the inverse of an odd number modulo 2^32
        var x = a;
        for (var i = 0; i < 5; i++)
        {
            x = unchecked(x * (2 - a * x));
        }
        return x;
    }
}

/// <summary>The forced draws of the battle call jobs (battle_calls.py draw_state, the "force" field of a job).</summary>
public sealed class ForcedDrawTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(16384)]
    [InlineData(32767)]
    public void TheStateForAValueDrawsThatValue(int raw)
    {
        var random = new MscRandom(ForcedDraw.StateFor(raw));
        Assert.Equal(raw, random.NextRaw());
        Assert.Equal((uint)raw << 16, random.State);
    }

    [Fact]
    public void TheStateIsTheOneThePythonSideComputes()
    {
        // battle_calls.py draw_state(37) = ((37 << 16) - 2531011) * pow(214013, -1, 2**32) mod 2**32
        Assert.Equal(1u, unchecked(214013u * ForcedDraw.MultiplierInverse));
        Assert.Equal(0xC2B9F641u, ForcedDraw.StateFor(37));
    }

    [Fact]
    public void AJobWithoutForceForcesNothingAndTheKeysAreDrawIndices()
    {
        var plain = System.Text.Json.JsonSerializer.Deserialize<CallJob>("""{"id": "R014+0x08FE|street-p100#8", "function": "R014+0x08FE", "state": "street-p100"}""")!;
        Assert.Null(plain.Force);
        Assert.Empty(plain.ForcedDraws);
        var forced = System.Text.Json.JsonSerializer.Deserialize<CallJob>("""{"id": "x", "force": {"0": 5, "3": 40000}}""")!;
        Assert.Equal(new Dictionary<int, int> { [0] = 5, [3] = 40000 & 0x7FFF }, forced.ForcedDraws);
    }
}
