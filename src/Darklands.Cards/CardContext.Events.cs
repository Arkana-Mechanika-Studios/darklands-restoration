using Darklands.Core.Events;

namespace Darklands.Cards;

/// <summary>
/// Timed events for card scripts: <see cref="Events"/> has every <c>event_*</c>,
/// <c>ev8_*</c>, <c>ev24_*</c> and <c>loc_fact*</c> operation; the shortcuts here cover the
/// common "at the current location" forms.
/// </summary>
public sealed partial class CardContext
{
    /// <summary>The 300 timed events (<c>events</c>).</summary>
    public EventLog Events => State.Events;

    /// <summary>The slot created or visited last (<c>last_event</c>).</summary>
    // orig: last_event DS:E3CE
    public int LastEvent => State.Events.Last;

    /// <summary><c>event_here</c>(kind, hours): the party's event here (9999 hours = forever).</summary>
    // orig: event_here R029+0x2930 (0E76:2930) draft
    public int EventHere(int kind, int hours) => State.Events.CreateHere(kind, hours);

    /// <summary><c>event_here_type</c>.</summary>
    // orig: event_here_type R029+0x2A32 (0E76:2A32) draft
    public int EventHere(int kind, int type, int hours) => State.Events.CreateHere(kind, type, hours);

    /// <summary><c>event_here_extend</c>.</summary>
    // orig: event_here_extend R029+0x2B36 (0E76:2B36) matched
    public void ExtendEventHere(int kind, int hours) => State.Events.ExtendHere(kind, hours);

    /// <summary><c>event_at</c>(kind, cur_loc).</summary>
    // orig: event_at R029+0x3878 (0E76:3878) matched
    public bool EventAtHere(int kind) => State.Events.At(kind, Here);

    /// <summary><c>event_type_at</c>(kind, type, cur_loc).</summary>
    // orig: event_type_at R029+0x392C (0E76:392C) matched
    public bool EventTypeAtHere(int kind, int type) => State.Events.TypeAt(kind, type, Here);

    /// <summary><c>event_value_at</c>(kind, cur_loc).</summary>
    // orig: event_value_at R029+0x3A20 (0E76:3A20) matched
    public int EventValueHere(int kind) => State.Events.ValueAt(kind, Here);

    /// <summary><c>event_loc_of</c>: the location of the event in a slot.</summary>
    // orig: event_loc_of_impl R039+0x1A68 (1462:1A68) matched
    // DIVERGENCE: an empty slot is read through a null far pointer in the original; here 0 (the same for the four accessors below).
    public int EventLocation(int slot) => State.Events[slot]?.Location ?? 0;

    /// <summary><c>event_type_of</c>.</summary>
    // orig: event_type_of_impl R039+0x1A98 (1462:1A98) matched
    public int EventType(int slot) => State.Events[slot]?.Type ?? 0;

    /// <summary><c>event_f2e_of</c>: the event's third data word (an item ...).</summary>
    // orig: event_f2e_of_impl R039+0x1A80 (1462:1A80) matched
    public int EventDetail(int slot) => State.Events[slot]?.Data3 ?? 0;

    /// <summary>The owner (R039 1a38) and value (R039 1a50) of the event in a slot.</summary>
    // orig: R039+0x1A38 (1462:1A38) matched
    public int EventOwner(int slot) => State.Events[slot]?.Owner ?? 0;

    // orig: R039+0x1A50 (1462:1A50) matched
    public int EventValue(int slot) => State.Events[slot]?.Value ?? 0;
}
