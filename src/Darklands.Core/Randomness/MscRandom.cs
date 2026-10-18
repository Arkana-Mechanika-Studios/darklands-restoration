namespace Darklands.Core.Randomness;

/// <summary>A source of the game's random numbers.</summary>
public interface IRandomSource
{
    /// <summary>A raw value 0..32767.</summary>
    int NextRaw();

    /// <summary>0..n−1 by remainder; n below 2 counts as 2 (<c>rand_mod</c>).</summary>
    int Below(int n);

    /// <summary>0..n−1 by scaling the 15-bit value (<c>random_n</c>).</summary>
    int Scaled(int n);

    void Seed(uint seed);
}

/// <summary>
/// The Microsoft C 6 run-time generator the game uses: a 32-bit linear congruential state,
/// <c>state = state * 214013 + 2531011</c>, returning bits 16..30.
/// </summary>
// orig: _rand (MSC 6 run-time library, root 06A1:29B6) asm; tests: CoreValuesOracleTests (Oracle:core.json rand_mod random_n)
public sealed class MscRandom(uint seed = 1) : IRandomSource
{
    private const uint Multiplier = 214013;
    private const uint Increment = 2531011;

    public uint State { get; private set; } = seed;

    private uint _previous = seed;

    /// <summary>The state before the last draw (what rand()'s long multiply left on the stack; cloud_blocks_line reads it).</summary>
    public uint PreviousState() => _previous;

    public void Seed(uint seed) => State = _previous = seed;

    public int NextRaw()
    {
        _previous = State;
        State = unchecked(State * Multiplier + Increment);
        return (int)((State >> 16) & 0x7FFF);
    }

    // orig: rand_mod root 0000:0C70 (0000:0C70) matched; tests: CoreValuesOracleTests (Oracle:core.json); CoreValueRulesTests
    // C: `if (n <= 1) n = 2; rand() % n` with a signed idiv (checked against the asm): n < 2, negative n included, counts as 2.
    public int Below(int n) => NextRaw() % (n > 1 ? n : 2);

    // orig: random_n root 0410:0008 (0410:0008) matched; tests: CoreValuesOracleTests (Oracle:core.json)
    // C: ((unsigned long)(unsigned)(rand() << 1) * (unsigned)n) >> 16: the doubled value and n are unsigned 16-bit.
    public int Scaled(int n)
    {
        var doubled = (ushort)(NextRaw() << 1);
        return (int)(((uint)doubled * (ushort)n) >> 16);
    }
}
