using Darklands.Core.Randomness;

namespace Darklands.Cards.Tests.Oracle;

/// <summary>
/// Forced draws of a card run (the run key's parts <c>R&lt;k&gt;_&lt;v&gt;</c>, tools/oracle/mutate_branch.py): the k-th draw of the generator
/// counted from the card's start returns the raw value v. As tools/oracle/card_oracle.py does at the entry of the original's rand(), the
/// generator's state is set before that draw to the state whose successor is <c>v &lt;&lt; 16</c>, so both programs draw v and go on from
/// the same state. The count is kept per thread (the replay runs cards in parallel); outside a forced run nothing changes.
/// </summary>
public static class ForcedDraws
{
    private const uint Multiplier = 214013;
    private const uint Increment = 2531011;

    /// <summary>The inverse of the multiplier modulo 2^32.</summary>
    public static readonly uint MultiplierInverse = Inverse(Multiplier);

    [ThreadStatic]
    private static Run? _current;

    private sealed class Run(MscRandom random, Dictionary<int, int> draws)
    {
        public MscRandom Random { get; } = random;

        public Dictionary<int, int> Draws { get; } = draws;

        public int Count { get; set; }
    }

    /// <summary>The generator state from which the next draw returns <paramref name="raw"/> (0..32767).</summary>
    public static uint StateFor(int raw) => unchecked(((((uint)raw & 0x7FFF) << 16) - Increment) * MultiplierInverse);

    /// <summary>Starts forcing the draws of a run (none: nothing to do).</summary>
    public static void Begin(IRandomSource random, Dictionary<int, int> draws)
    {
        if (draws.Count == 0 || random is not MscRandom msc)
        {
            _current = null;
            return;
        }
        _current = new Run(msc, draws);
        MscRandom.BeforeDraw ??= Before;
    }

    public static void End() => _current = null;

    private static void Before()
    {
        if (_current is not { } run)
        {
            return;
        }
        if (run.Draws.TryGetValue(run.Count, out var raw))
        {
            run.Random.Seed(StateFor(raw));
        }
        run.Count++;
    }

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
