using Darklands.Core.Time;

namespace Darklands.Core.Events;

/// <summary>Flags of a timed event.</summary>
[Flags]
public enum EventFlags
{
    None = 0,

    /// <summary>When the event ends, its <see cref="TimedEvent.ExpiryHandler"/> runs.</summary>
    CallExpiryHandler = 1,
}

/// <summary>
/// One timed event: the game's memory of something that holds for a while (a city closed to
/// the party, a quest under way, a temporary saintly boost, a fact about a city's politics).
/// The meaning of <see cref="Type"/>, <see cref="Value"/>, <see cref="Extra"/> and the three
/// data words depends on the <see cref="Kind"/> (see <see cref="EventKinds"/>). All fields are
/// 16-bit words in the original.
/// </summary>
// orig: R029+0x3034 (0E76:3034) matched
// UNCERTAIN: Data1..Data3 (+2A/+2C/+2E) and Extra (+20) are named by their uses.
public sealed class TimedEvent
{
    /// <summary>What the event is about (see <see cref="EventKinds"/>).</summary>
    public int Kind { get; set; }

    /// <summary>The sub-type within the kind ("type0"): a skill, a member, a fact number ...</summary>
    public int Type { get; set; }

    /// <summary>Who it belongs to: a member index, or −2 for the whole party.</summary>
    public int Owner { get; set; } = -2;

    /// <summary>The location it concerns (−1: none).</summary>
    public int Location { get; set; }

    /// <summary>Its value (a boost size, a chance, a bit set ...).</summary>
    public int Value { get; set; }

    /// <summary>A further number given on creation (potions brewed, a site kind ...).</summary>
    public int Extra { get; set; }

    public EventFlags Flags { get; set; }

    /// <summary>The handler that runs when the event ends (with <see cref="EventFlags.CallExpiryHandler"/>).</summary>
    public ExpiryHandler ExpiryHandler { get; set; }

    /// <summary>A group tag shared by related events (a quest number, a location-fact group).</summary>
    public int Tag { get; set; }

    /// <summary>First data word (a reputation change, a location ...; f2a).</summary>
    public int Data1 { get; set; }

    /// <summary>Second data word (a reward size, a location ...; f2c).</summary>
    public int Data2 { get; set; }

    /// <summary>Third data word (an item, a quest detail ...; f2e).</summary>
    public int Data3 { get; set; }

    /// <summary>When the event was written.</summary>
    public GameDate Created { get; set; }

    /// <summary>From when it holds (queries only see started events).</summary>
    public GameDate Start { get; set; }

    /// <summary>When it ends (the expiry pass removes it at or after this date).</summary>
    public GameDate End { get; set; }

    /// <summary>Lets the event run <paramref name="hours"/> longer (<c>date_add_hours</c> on its end).</summary>
    // orig: date_add_hours R029+0x58FA (1367:09EA) matched
    public void Extend(int hours) => End = End.AddHours(hours);

    public override string ToString() =>
        $"kind {Kind:X2} type {Type} owner {Owner} loc {Location} value {Value} tag {Tag} {Start}..{End}";
}
