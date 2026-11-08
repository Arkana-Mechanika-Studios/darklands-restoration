using System.Text.Json;
using Darklands.Core.Randomness;

namespace Darklands.Oracle.Tests;

/// <summary>
/// Forced draws of a battlefield-generator scenario (its "force" map, tools/oracle/gen_force.py and mutate_battle.py): the k-th draw of the
/// generator's run, from 0, returns the raw value v. As gen_force.py does at the entry of the original's rand(), the generator's state is set
/// before that draw to the state whose successor is <c>v &lt;&lt; 16</c>, so both programs draw v and go on from the same state. Kept per
/// thread and ended when the scenario's scope ends; a scenario without the map changes nothing.
/// </summary>
internal sealed class ScenarioDraws : IDisposable
{
    private const uint Multiplier = 214013;
    private const uint Increment = 2531011;

    [ThreadStatic]
    private static ScenarioDraws? _current;

    private readonly MscRandom _random;
    private readonly Dictionary<int, int> _draws;
    private int _count;

    private ScenarioDraws(MscRandom random, Dictionary<int, int> draws)
    {
        _random = random;
        _draws = draws;
    }

    /// <summary>The inverse of the multiplier modulo 2^32.</summary>
    public static uint MultiplierInverse { get; } = Inverse(Multiplier);

    /// <summary>The generator state from which the next draw returns <paramref name="raw"/> (0..32767).</summary>
    public static uint StateFor(int raw) => unchecked(((((uint)raw & 0x7FFF) << 16) - Increment) * MultiplierInverse);

    /// <summary>The scenario's forced draws: "force": {"k": raw}; empty when absent.</summary>
    public static Dictionary<int, int> Of(JsonElement scenario) =>
        scenario.TryGetProperty("force", out var force) && force.ValueKind == JsonValueKind.Object
            ? force.EnumerateObject().ToDictionary(p => int.Parse(p.Name, System.Globalization.CultureInfo.InvariantCulture), p => p.Value.GetInt32() & 0x7FFF)
            : [];

    /// <summary>Starts forcing the draws of a scenario on this thread (none: nothing changes); dispose at the scenario's end.</summary>
    public static ScenarioDraws? Begin(MscRandom random, JsonElement scenario)
    {
        var draws = Of(scenario);
        if (draws.Count == 0 && !Counting)
        {
            _current = null;
            return null;
        }
        _current = new ScenarioDraws(random, draws);
        MscRandom.BeforeDraw ??= Before;
        return _current;
    }

    /// <summary>Opt-in (the generator fuzzer, GenFuzz): every scenario gets a scope, so its draws are counted (forcing unchanged).</summary>
    internal static bool Counting { get; set; }

    /// <summary>The draws of the last scenario whose scope ended on this thread (with <see cref="Counting"/>).</summary>
    [field: ThreadStatic]
    internal static int LastCount { get; private set; }

    public void Dispose()
    {
        LastCount = _count;
        if (ReferenceEquals(_current, this))
        {
            _current = null;
        }
    }

    private static void Before()
    {
        if (_current is not { } run)
        {
            return;
        }
        if (run._draws.TryGetValue(run._count, out var raw))
        {
            run._random.Seed(StateFor(raw));
        }
        run._count++;
    }

    private static uint Inverse(uint a)
    {
        var x = a;
        for (var i = 0; i < 5; i++)
        {
            x = unchecked(x * (2 - a * x));
        }
        return x;
    }
}

/// <summary>The forced draws of the generator scenarios (gen_force.py draw_state, a scenario's "force" map).</summary>
public sealed class ScenarioDrawsTests
{
    [Fact]
    public void TheStateIsTheOneThePythonSideComputes()
    {
        // gen_force.py draw_state(37) = ((37 << 16) - 2531011) * pow(214013, -1, 2**32) mod 2**32
        Assert.Equal(0xC2B9F641u, ScenarioDraws.StateFor(37));
        var random = new MscRandom(ScenarioDraws.StateFor(32767));
        Assert.Equal(32767, random.NextRaw());
    }

    [Fact]
    public void TheKthDrawOfAScenarioIsForcedOnlyInsideItsScope()
    {
        var plain = new MscRandom(8);
        var expected = new[] { plain.NextRaw(), plain.NextRaw(), plain.NextRaw() };
        using (var doc = JsonDocument.Parse("""{"seed": 8, "force": {"1": 4321}}"""))
        {
            var random = new MscRandom(8);
            using (ScenarioDraws.Begin(random, doc.RootElement))
            {
                Assert.Equal(expected[0], random.NextRaw());
                Assert.Equal(4321, random.NextRaw());
                Assert.Equal(new MscRandom(4321u << 16).NextRaw(), random.NextRaw());
            }
        }
        using var none = JsonDocument.Parse("""{"seed": 8}""");
        Assert.Empty(ScenarioDraws.Of(none.RootElement));
        var again = new MscRandom(8);
        Assert.Null(ScenarioDraws.Begin(again, none.RootElement));
        Assert.Equal(expected, new[] { again.NextRaw(), again.NextRaw(), again.NextRaw() });
    }
}
