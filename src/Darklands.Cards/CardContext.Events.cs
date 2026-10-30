using Darklands.Core.Events;

namespace Darklands.Cards;

/// <summary>
/// Timed events for card scripts: <see cref="Events"/> has every <c>event-*</c>,
/// <c>open-quest-*</c>, <c>resolved-quest-*</c> and <c>place-flag*</c> operation; the shortcuts here cover the
/// common "at the current location" forms.
/// </summary>
public sealed partial class CardContext
{
    /// <summary>The 300 timed events (<c>events</c>).</summary>
    public EventLog Events => State.Events;

    /// <summary>The slot created or visited last (<c>latest-event</c>).</summary>
    // orig: latest-event DS:E3CE
    public int LastEvent => State.Events.Last;

    /// <summary><c>event-here-test</c>(kind, hours): the party's event here (9999 hours = forever).</summary>
    // orig: event-here-test R029+0x2930 (0E76:2930) draft
    public int EventHere(int kind, int hours) => State.Events.CreateHere(kind, hours);

    /// <summary><c>event-here-by-type</c>.</summary>
    // orig: event-here-by-type R029+0x2A32 (0E76:2A32) draft
    public int EventHere(int kind, int type, int hours) => State.Events.CreateHere(kind, type, hours);

    /// <summary><c>event-here-lengthen</c>.</summary>
    // orig: event-here-lengthen R029+0x2B36 (0E76:2B36) matched
    public void ExtendEventHere(int kind, int hours) => State.Events.ExtendHere(kind, hours);

    /// <summary><c>event-at-place</c>(kind, location-now).</summary>
    // orig: event-at-place R029+0x3878 (0E76:3878) matched
    public bool EventAtHere(int kind) => State.Events.At(kind, Here);

    /// <summary><c>event-kind-at</c>(kind, type, location-now).</summary>
    // orig: event-kind-at R029+0x392C (0E76:392C) matched
    public bool EventTypeAtHere(int kind, int type) => State.Events.TypeAt(kind, type, Here);

    /// <summary><c>event-datum-at</c>(kind, location-now).</summary>
    // orig: event-datum-at R029+0x3A20 (0E76:3A20) matched
    public int EventValueHere(int kind) => State.Events.ValueAt(kind, Here);

    /// <summary><c>event-place-of</c>: the location of the event in a slot.</summary>
    // orig: event-place-inner R039+0x1A68 (1462:1A68) matched
    // DIVERGENCE: an empty slot is read through a null far pointer in the original; here 0 (the same for the four accessors below).
    public int EventLocation(int slot) => State.Events[slot]?.Location ?? 0;

    /// <summary><c>event-kind-of</c>.</summary>
    // orig: event-kind-inner R039+0x1A98 (1462:1A98) matched
    public int EventType(int slot) => State.Events[slot]?.Type ?? 0;

    /// <summary><c>event-detail-word-of</c>: the event's third data word (an item ...).</summary>
    // orig: event-detail-word-inner R039+0x1A80 (1462:1A80) matched
    public int EventDetail(int slot) => State.Events[slot]?.Data3 ?? 0;

    /// <summary>The owner (R039 1a38) and value (R039 1a50) of the event in a slot.</summary>
    // orig: R039+0x1A38 (1462:1A38) matched
    public int EventOwner(int slot) => State.Events[slot]?.Owner ?? 0;

    // orig: R039+0x1A50 (1462:1A50) matched
    public int EventValue(int slot) => State.Events[slot]?.Value ?? 0;
}
