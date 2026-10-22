using Darklands.Core.Time;

namespace Darklands.Core.Events;

/// <summary>
/// The 300 timed-event slots and every operation the rules and card scripts use on them
/// (<c>event_*</c>, <c>ev8_*</c>, <c>ev24_*</c>, <c>loc_fact*</c>). Durations are in hours;
/// <see cref="Forever"/> hours means "until the end of 1499".
/// </summary>
/// <remarks>
/// Faithful details: queries (except <see cref="Count(int)"/>, <see cref="Exists"/>'s slot scan
/// and <see cref="Find"/>/<see cref="Remove"/>) only see events that have <em>started</em>;
/// kind <see cref="EventKinds.LocationFact"/> scans also match <see cref="EventKinds.Quest"/>
/// events; every scan leaves the last slot it visited in <see cref="Last"/>; and while
/// <see cref="QueriesSuppressed"/> is set the presence and value queries report nothing.
/// The original scans also read the empty slots through null pointers; here an empty slot
/// never matches.
/// </remarks>
public sealed class EventLog
{
    public const int Capacity = 300;

    /// <summary>Duration meaning "forever": the event ends 31.12.1499 23:00.</summary>
    public const int Forever = 9999;

    /// <summary>A year in hours (the original's 0x2238 duration).</summary>
    public const int OneYear = 8760;

    /// <summary>
    /// The end of a <see cref="Forever"/> event. The month is 12 as the original writes it
    /// (one past December), which only matters in comparisons.
    /// </summary>
    public static readonly GameDate ForeverEnd = new(23, 31, 12, 1499);

    private readonly TimedEvent?[] _slots = new TimedEvent?[Capacity];
    private readonly GameState _state;

    public EventLog(GameState state) => _state = state;

    public TimedEvent? this[int index] => index is >= 0 and < Capacity ? _slots[index] : null;

    /// <summary>The slot created, extended or visited last (<c>last_event</c>).</summary>
    public int Last { get; set; }

    /// <summary>
    /// The original's flag at DS:00F0: while set, <see cref="Exists"/>, <see cref="At"/>,
    /// <see cref="HasType"/>, <see cref="TypeAt"/>, <see cref="OwnedBy"/> and the value
    /// queries report nothing. Nothing in the game is known to set it.
    /// </summary>
    // orig: DS:00F0
    // C: tested by event_exists, event_at, event_type, event_type_at, R029 3996 and the value_at/value_type queries only.
    public bool QueriesSuppressed { get; set; }

    /// <summary>Events in use, with their slots.</summary>
    public IEnumerable<(int Slot, TimedEvent Event)> All()
    {
        for (var i = 0; i < Capacity; i++)
        {
            if (_slots[i] is { } e)
            {
                yield return (i, e);
            }
        }
    }

    /// <summary>Puts an event into a slot (loading a saved game).</summary>
    public void Put(int slot, TimedEvent? e) => _slots[slot] = e;

    public void Clear() => Array.Clear(_slots);

    // ---- the work file around a battle (EVENTS.TMP) ---------------------------------

    /// <summary>The records of EVENTS.TMP as <see cref="Shutdown"/> last wrote them (null: not written yet).</summary>
    private TimedEvent[]? _workFile;

    /// <summary>
    /// Before a battle (<c>cards_shutdown</c>): the events in use are written to the work file
    /// in slot order and every slot is emptied, so the battle runs on an empty table.
    /// </summary>
    // orig: cards_shutdown R043+0x054E (1462:054E) matched: 1462:0665-0693 writes the count of the non-empty slots, 1462:069B-06F5
    //   each non-empty event's 0x30 bytes (slot order), frees it and empties its slot; last_event is not touched; tests: EventsFileOracleTests
    public void Shutdown()
    {
        _workFile = All().Select(x => Record(x.Event)).ToArray();
        Array.Clear(_slots);
    }

    /// <summary>
    /// After a battle (<c>world_load</c> of <c>cards_startup</c>), and in a save made in a battle:
    /// the work file's n events are read back into slots 0..n−1 as new records; slots n and up
    /// keep what is in them.
    /// </summary>
    // C: the save in a battle (R037+0x2ACE) reads it back the same way (1462:2C57-2D0E).
    // orig: world_load R043+0x0100 (1462:0100) matched: 1462:048A-053F, only when EVENTS.TMP exists (_dos_findfirst): the count word, then
    //   for i < count (unsigned) events[i] = a new 0x30-byte record read from the file; the record that was in the slot is not freed;
    //   tests: EventsFileOracleTests, EventLogTests
    // BUG: the table comes back compacted, and what a battle put into the slots below the count is lost: a saint's boost
    //   (kind 0x48/0x49) or a price called in a battle is never undone, as its event, which would end it, is gone.
    public void Reload()
    {
        if (_workFile is null)
        {
            return;
        }
        for (var i = 0; i < _workFile.Length; i++)
        {
            _slots[i] = Record(_workFile[i]);
        }
    }

    /// <summary>
    /// The end of a save made in a battle: every event is freed and every slot emptied (the work
    /// file stays as it is), so the battle goes on with an empty table again.
    /// </summary>
    // orig: R037+0x2ACE (1462:2ACE) draft, 1462:37C8-3832: with DS:907A == 1, after the file is written, the 300 slots are freed and
    //   emptied (also those the battle filled at slot n and up, which were just saved with the game).
    public void ReleaseAfterSave() => Array.Clear(_slots);

    /// <summary>An event as its 0x30-byte record holds it: every field a 16-bit word.</summary>
    private static TimedEvent Record(TimedEvent e) => new()
    {
        Type = (short)e.Type, Created = e.Created, Start = e.Start, End = e.End, Owner = (short)e.Owner,
        Location = (short)e.Location, Value = (short)e.Value, Extra = (short)e.Extra, Kind = (short)e.Kind,
        Flags = (EventFlags)(short)e.Flags, ExpiryHandler = (ExpiryHandler)(short)e.ExpiryHandler, Tag = (short)e.Tag,
        Data1 = (short)e.Data1, Data2 = (short)e.Data2, Data3 = (short)e.Data3,
    };

    // ---- creating -------------------------------------------------------------------

    /// <summary>
    /// A new event from now for <paramref name="hours"/> (<c>event_new</c>; the arguments in
    /// the reference's order: owner, loc, c, kind, type, value, handler, hours, flags, tag).
    /// Returns its slot, or −1 when all 300 slots are in use ("EVENT Failure").
    /// </summary>
    // orig: event_new R029+0x2C4E (0E76:2C4E) draft; R029+0x3034 (0E76:3034) matched; tests: EventLogTests
    // BUG: "forever" events end on 31 December 1499: 9999 hours is the fixed end 23:00 31.12.1499, not an open end, so
    // on 1 January 1500 every such event expires (a quest clears its facts) and one created later expires at the next
    // expiry pass; the witches' sabbat quest and its planned sabbat end for good.
    // C: checked against the asm: 9999 hours = end 23:00 31.12.1499 (month 12), any other count is added to the end with date_add_hours.
    public int Create(int owner, int location, int extra, int kind, int type, int value, int handler, int hours,
        int flags, int tag)
    {
        var slot = FreeSlot();
        if (slot == -1)
        {
            return -1;
        }
        var e = Fill(slot, owner, location, extra, kind, type, value, handler, flags, tag);
        if (hours == Forever)
        {
            e.End = ForeverEnd;
        }
        else
        {
            e.Extend(hours);
        }
        return Last = slot;
    }

    /// <summary>The party's event of <paramref name="kind"/> at the current location, value 100 (<c>event_here</c>).</summary>
    // orig: event_here R029+0x2930 (0E76:2930) draft; tests: EventLogTests
    public int CreateHere(int kind, int hours) => CreateHere(kind, 0, hours);

    /// <summary>The same with a type (<c>event_here_type</c>).</summary>
    // orig: event_here_type R029+0x2A32 (0E76:2A32) draft
    public int CreateHere(int kind, int type, int hours) =>
        Create(-2, _state.CurrentLocation, 0, kind, type, 100, 0, hours, 0, 0);

    /// <summary>
    /// Extends the party's untyped, untagged event of <paramref name="kind"/> here by
    /// <paramref name="hours"/>, or starts one (<c>event_here_extend</c>). As in the original,
    /// <see cref="Last"/> ends as the scan position: −1 or 300 when a new event was started.
    /// </summary>
    // orig: event_here_extend R029+0x2B36 (0E76:2B36) matched; tests: EventLogTests
    public void ExtendHere(int kind, int hours)
    {
        var here = _state.CurrentLocation;
        var i = Scan(kind, e => e.Type == 0 && e.Location == here && e.Owner == -2 && e.Tag == 0);
        if (i is >= 0 and < Capacity)
        {
            _slots[i]!.Extend(hours);
        }
        else
        {
            CreateHere(kind, hours);
        }
        Last = i;
    }

    /// <summary>
    /// Extends the matching event (type, owner, tag exact; location exact or −1 = any) by
    /// <paramref name="hours"/>, or creates it running from now (<c>event_renew</c>). A new event
    /// gets <paramref name="hours"/> literally: <see cref="Forever"/> is not special here.
    /// </summary>
    // orig: event_renew R029+0x2D5C (0E76:2D5C) draft; tests: EventLogTests
    public int Renew(int owner, int location, int extra, int kind, int type, int value, int handler, int hours,
        int flags, int tag)
    {
        var slot = Scan(kind, e => e.Type == type && (e.Location == location || location == -1) && e.Owner == owner && e.Tag == tag);
        if (slot is < 0 or >= Capacity)
        {
            slot = FreeSlot();
            if (slot == -1)
            {
                return -1;
            }
            Fill(slot, owner, location, extra, kind, type, value, handler, flags, tag);
        }
        _slots[slot]!.Extend(hours);
        return Last = slot;
    }

    /// <summary>
    /// Changes the value of the matching event (−1 = any type, location, owner or tag), creating
    /// it for 999 hours if there is none (<c>event_update</c>). Original quirk: a new event
    /// starts with the value <paramref name="value"/> and then the operation is applied, so
    /// <see cref="EventUpdate.Add"/> on a new event leaves 2 × value.
    /// </summary>
    // orig: event_update R029+0x2E74 (0E76:2E74) draft; tests: EventLogTests
    // C: checked against the asm: op 0 set, 1 add, 2 and, 3 or (dec/je chain), any other op leaves the value; a new event lasts 999 hours.
    // BUG: the new event already holds the value before the op (FixNewEventAddDoubles).
    public int Update(int owner, int location, int kind, int type, int value, int tag, EventUpdate op)
    {
        var slot = Scan(kind, e => (type == -1 || e.Type == type) && (location == -1 || e.Location == location)
            && (owner == -1 || e.Owner == owner) && (tag == -1 || e.Tag == tag));
        if (slot is < 0 or >= Capacity)
        {
            slot = FreeSlot();
            if (slot == -1)
            {
                return -1;
            }
            var start = _state.Behaviour.FixNewEventAddDoubles && op == EventUpdate.Add ? 0 : value;
            Fill(slot, owner, location, 0, kind, type, start, 0, 0, tag).Extend(999);
        }
        var target = _slots[slot]!;
        target.Value = (short)(op switch
        {
            EventUpdate.Set => value,
            EventUpdate.Add => target.Value + value,
            EventUpdate.And => target.Value & value,
            EventUpdate.Or => target.Value | value,
            _ => target.Value,
        });
        return Last = slot;
    }

    /// <summary>Removes the event in a slot (<c>event_free</c>).</summary>
    // orig: event_free R029+0x30FE (0E76:30FE) draft
    public void Free(int slot)
    {
        if (slot is >= 0 and < Capacity)
        {
            _slots[slot] = null;
        }
    }

    // ---- state of one event ---------------------------------------------------------

    /// <summary>The event has started (its start is not after now).</summary>
    // orig: event_has_started R029+0x3180 (0E76:3180) matched
    public bool HasStarted(int slot) => this[slot] is { } e && !IsAfter(e.Start, _state.Date);

    /// <summary>The event is over (its end is not after now).</summary>
    // orig: event_has_ended R029+0x31E6 (0E76:31E6) matched
    public bool HasEnded(int slot) => this[slot] is { } e && !IsAfter(e.End, _state.Date);

    /// <summary>The first slot at or after <paramref name="from"/> holding <paramref name="kind"/>, or −1 (<c>event_next</c>).</summary>
    // orig: event_next R029+0x324C (0E76:324C) matched
    // C: the original reads events[i]->kind of empty slots through a null far pointer (0000:0022, the INT 8 vector's segment, a code
    // segment of 0x70 and up), which never equals an event kind (0x4A and below): an empty slot never matches, as here.
    public int Next(int kind, int from)
    {
        for (var i = Math.Max(from, 0); i < Capacity; i++)
        {
            if (_slots[i] is { } e && (e.Kind == kind || (kind == EventKinds.LocationFact && e.Kind == EventKinds.Quest)))
            {
                return Last = i;
            }
        }
        return -1;
    }

    // ---- queries by kind ------------------------------------------------------------

    /// <summary>An event of <paramref name="kind"/> exists, started or not (<c>event_exists</c>).</summary>
    // orig: event_exists R029+0x32AA (0E76:32AA) matched
    public bool Exists(int kind) => !QueriesSuppressed && Next(kind, 0) != -1;

    /// <summary>The first event of <paramref name="kind"/> has started (<c>event_started</c>).</summary>
    // orig: event_started R029+0x32CE (0E76:32CE) matched
    public bool Started(int kind)
    {
        var i = Next(kind, 0);
        return i != -1 && HasStarted(i);
    }

    /// <summary>Events of <paramref name="kind"/>, started or not (<c>event_count</c>).</summary>
    // orig: event_count R029+0x37B8 (0E76:37B8) matched
    public int Count(int kind) => Slots(kind).Count();

    /// <summary>Started events of <paramref name="kind"/> at a location.</summary>
    // orig: R029+0x37E2 (0E76:37E2) matched
    public int CountAt(int kind, int location) => Slots(kind).Count(i => HasStarted(i) && _slots[i]!.Location == location);

    /// <summary>Quest events with <paramref name="tag"/> (−1: all), started or not.</summary>
    // orig: R029+0x382E (0E76:382E) matched
    public int CountQuests(int tag) => Slots(EventKinds.Quest).Count(i => _slots[i]!.Tag == tag || tag == -1);

    /// <summary>A started event of <paramref name="kind"/> at a location (<c>event_at</c>).</summary>
    // orig: event_at R029+0x3878 (0E76:3878) matched
    public bool At(int kind, int location) => Any(kind, e => e.Location == location);

    /// <summary>A started event of <paramref name="kind"/> and <paramref name="type"/> (<c>event_type</c>).</summary>
    // orig: event_type R029+0x38D2 (0E76:38D2) matched
    public bool HasType(int kind, int type) => Any(kind, e => e.Type == type);

    /// <summary>A started event of kind and type at a location (<c>event_type_at</c>).</summary>
    // orig: event_type_at R029+0x392C (0E76:392C) matched
    public bool TypeAt(int kind, int type, int location) => Any(kind, e => e.Type == type && e.Location == location);

    /// <summary>A started event of <paramref name="kind"/> owned by <paramref name="owner"/>.</summary>
    // orig: R029+0x3996 (0E76:3996) matched
    public bool OwnedBy(int kind, int owner) => Any(kind, e => e.Owner == owner);

    /// <summary>The value of the first event of <paramref name="kind"/>, started or not; 0 if none (<c>event_value</c>).</summary>
    // orig: event_value R029+0x39F0 (0E76:39F0) matched
    // C: no DS:00F0 test and no started test in the original.
    public int Value(int kind)
    {
        var i = Next(kind, 0);
        return i == -1 ? 0 : _slots[i]!.Value;
    }

    /// <summary>The value of the first started event of kind at a location, 0 if none (<c>event_value_at</c>).</summary>
    // orig: event_value_at R029+0x3A20 (0E76:3A20) matched
    public int ValueAt(int kind, int location) => ValueOf(kind, e => e.Location == location);

    /// <summary>The value of the first started event of kind and type (<c>event_value_type</c>).</summary>
    // orig: event_value_type R029+0x3A86 (0E76:3A86) matched
    public int ValueOfType(int kind, int type) => ValueOf(kind, e => e.Type == type);

    /// <summary>The value of the first started event of kind and type at a location (<c>event_value_type_at</c>).</summary>
    // orig: event_value_type_at R029+0x3AEC (0E76:3AEC) matched
    public int ValueOfTypeAt(int kind, int type, int location) => ValueOf(kind, e => e.Type == type && e.Location == location);

    /// <summary>
    /// The first event of <paramref name="kind"/> matching type, location, owner, tag and
    /// <see cref="TimedEvent.Data1"/> (−1 = any), started or not; its slot or −1 (<c>event_find</c>).
    /// </summary>
    // orig: event_find R029+0x3B62 (0E76:3B62) matched; tests: EventLogTests
    public int Find(int kind, int type, int location, int owner, int tag, int data1)
    {
        var i = Scan(kind, e => (type == -1 || e.Type == type) && (location == -1 || e.Location == location)
            && (owner == -1 || e.Owner == owner) && (data1 == -1 || e.Data1 == data1) && (tag == -1 || e.Tag == tag));
        return i is >= 0 and < Capacity ? i : -1;
    }

    /// <summary>The location of the first matching event (−1 = any), or −1 (<c>event_find_loc</c>).</summary>
    // orig: event_find_loc R029+0x3C28 (0E76:3C28) matched
    public int FindLocation(int kind, int type, int owner, int tag, int data1)
    {
        var i = Scan(kind, e => (type == -1 || e.Type == type) && (owner == -1 || e.Owner == owner)
            && (data1 == -1 || e.Data1 == data1) && (tag == -1 || e.Tag == tag));
        return i is >= 0 and < Capacity ? _slots[i]!.Location : -1;
    }

    /// <summary>
    /// Removes the matching events (−1 = any type, location, owner, tag), started or not
    /// (<c>event_remove</c>); returns how many, except when slot 299 holds an event of
    /// <paramref name="kind"/>: then the loop falls off its end and returns what AX holds.
    /// </summary>
    // orig: event_remove R029+0x3CDE (0E76:3CDE) matched; tests: EventLogTests
    // C: checked against the asm: the function has no return on the slot-299 path; AX is 299 (event_next's result) unless a test
    // C: loaded its argument (location, owner, tag in turn, each only when not −1 and the tests before it passed), and 0 after event_free.
    public int Remove(int kind, int type, int location, int owner, int tag)
    {
        var n = 0;
        var i = 0;
        do
        {
            i = Next(kind, i);
            if (i == -1)
            {
                return n;
            }
            var e = _slots[i]!;
            var ax = i;
            var match = type == -1 || e.Type == type;
            if (match && location != -1)
            {
                ax = location;
                match = e.Location == location;
            }
            if (match && owner != -1)
            {
                ax = owner;
                match = e.Owner == owner;
            }
            if (match && tag != -1)
            {
                ax = tag;
                match = e.Tag == tag;
            }
            if (match)
            {
                Free(i);
                n++;
                ax = 0;
            }
            if (i == Capacity - 1)
            {
                return ax;
            }
        }
        while (++i < Capacity);
        return n;
    }

    // ---- quests (kind 8) and resolved quests (kind 0x24) ----------------------------

    public IEnumerable<TimedEvent> QuestsAll() => _state.Events.All().Select(x => x.Event);

    /// <summary>A started quest with <paramref name="tag"/> (<c>ev8_exists</c>).</summary>
    // orig: ev8_exists R029+0x32FE (0E76:32FE) matched
    public bool QuestExists(int tag) => Any(EventKinds.Quest, e => e.Tag == tag, ignoreSuppression: true);

    /// <summary>A started quest with <paramref name="tag"/> at a location (<c>ev8_at</c>).</summary>
    // orig: ev8_at R029+0x3470 (0E76:3470) matched
    public bool QuestAt(int tag, int location) =>
        Any(EventKinds.Quest, e => e.Tag == tag && e.Location == location, ignoreSuppression: true);

    /// <summary>A started quest with tag, owner and location (<c>ev8_at_owner</c>).</summary>
    // orig: ev8_at_owner R029+0x34D2 (0E76:34D2) matched
    public bool QuestAt(int tag, int owner, int location) =>
        Any(EventKinds.Quest, e => e.Tag == tag && e.Location == location && e.Owner == owner, ignoreSuppression: true);

    /// <summary>A started quest with tag, owner and value (<c>ev8_owner_value</c>).</summary>
    // orig: ev8_owner_value R029+0x353E (0E76:353E) matched
    public bool QuestWithValue(int tag, int owner, int value) =>
        Any(EventKinds.Quest, e => e.Tag == tag && e.Value == value && e.Owner == owner, ignoreSuppression: true);

    /// <summary>The value of the first started quest with <paramref name="tag"/>, 0 if none (<c>ev8_value</c>).</summary>
    // orig: ev8_value R029+0x3676 (0E76:3676) matched
    public int QuestValue(int tag) => ValueOf(EventKinds.Quest, e => e.Tag == tag, ignoreSuppression: true);

    /// <summary>A started resolved quest with <paramref name="tag"/> (<c>ev24_exists</c>).</summary>
    // orig: ev24_exists R029+0x3350 (0E76:3350) matched
    public bool ResolvedQuestExists(int tag) => Any(EventKinds.ResolvedQuest, e => e.Tag == tag, ignoreSuppression: true);

    /// <summary>A started resolved quest with tag at a location (<c>ev24_at</c>).</summary>
    // orig: ev24_at R029+0x33A2 (0E76:33A2) matched
    public bool ResolvedQuestAt(int tag, int location) =>
        Any(EventKinds.ResolvedQuest, e => e.Tag == tag && e.Location == location, ignoreSuppression: true);

    /// <summary>A started resolved quest with tag, owner and location (<c>ev24_at_owner</c>).</summary>
    // orig: ev24_at_owner R029+0x3404 (0E76:3404) matched
    public bool ResolvedQuestAt(int tag, int owner, int location) =>
        Any(EventKinds.ResolvedQuest, e => e.Tag == tag && e.Location == location && e.Owner == owner, ignoreSuppression: true);

    // ---- location facts (kind 0x1C, quests included) --------------------------------

    /// <summary>A started fact of group <paramref name="tag"/> and number <paramref name="type"/> anywhere (<c>loc_fact_any</c>).</summary>
    // orig: loc_fact_any R029+0x35AA (0E76:35AA) matched
    public bool FactAnywhere(int tag, int type) =>
        Any(EventKinds.LocationFact, e => e.Tag == tag && e.Type == type, ignoreSuppression: true);

    /// <summary>A started fact of group and number at a location (<c>loc_fact</c>).</summary>
    // orig: loc_fact R029+0x360C (0E76:360C) matched
    public bool Fact(int tag, int type, int location) =>
        Any(EventKinds.LocationFact, e => e.Tag == tag && e.Type == type && e.Location == location, ignoreSuppression: true);

    /// <summary>The value of a started fact anywhere, 0 if none (<c>loc_fact_value_any</c>).</summary>
    // orig: loc_fact_value_any R029+0x36D4 (0E76:36D4) matched
    public int FactValueAnywhere(int tag, int type) =>
        ValueOf(EventKinds.LocationFact, e => e.Tag == tag && e.Type == type, ignoreSuppression: true);

    /// <summary>The value of a started fact at a location, 0 if none (<c>loc_fact_value</c>).</summary>
    // orig: loc_fact_value R029+0x3742 (0E76:3742) matched
    public int FactValue(int tag, int type, int location) =>
        ValueOf(EventKinds.LocationFact, e => e.Tag == tag && e.Type == type && e.Location == location, ignoreSuppression: true);

    /// <summary>
    /// Removes the facts of group <paramref name="tag"/> at <paramref name="location"/> (−1 = any),
    /// started or not, and the quests with that tag and location too (<c>loc_facts_clear</c>);
    /// returns how many, or what AX holds when slot 299 holds a fact or quest (as <see cref="Remove"/>).
    /// </summary>
    // orig: loc_facts_clear R029+0x3D92 (0E76:3D92) matched; tests: EventLogTests
    // C: checked against the asm: on the slot-299 path AX is 299, or the location when the tag test passed and location is not −1, or 0 after event_free.
    public int ClearFacts(int tag, int location)
    {
        var n = 0;
        var i = 0;
        do
        {
            i = Next(EventKinds.LocationFact, i);
            if (i == -1)
            {
                return n;
            }
            var e = _slots[i]!;
            var ax = i;
            var match = tag == -1 || e.Tag == tag;
            if (match && location != -1)
            {
                ax = location;
                match = e.Location == location;
            }
            if (match)
            {
                Free(i);
                n++;
                ax = 0;
            }
            if (i == Capacity - 1)
            {
                return ax;
            }
        }
        while (++i < Capacity);
        return n;
    }

    // ---- temporary boosts -----------------------------------------------------------

    /// <summary>
    /// The largest temporary boost of <paramref name="attribute"/> for a member, started or not
    /// (<c>event_48_max</c>): attributes may exceed their maximum by this much.
    /// </summary>
    // orig: event_48_max R029+0x4E0C (0E76:4E0C) matched; tests: MemberRulesTests
    // C: checked against the asm: with a kind-0x48 event in slot 299 the loop falls off its end and returns AX: the attribute
    // C: when that event's type differs, else the member (whatever its owner and value).
    public int AttributeBoost(int member, int attribute)
    {
        var n = 0;
        var i = 0;
        do
        {
            i = Next(EventKinds.AttributeBoost, i);
            if (i == -1)
            {
                return n;
            }
            var e = _slots[i]!;
            if (e.Type == attribute && e.Owner == member && e.Value > n)
            {
                n = e.Value;
            }
            if (i == Capacity - 1)
            {
                return e.Type != attribute ? attribute : member;
            }
        }
        while (++i < Capacity);
        return n;
    }

    /// <summary>The sum of the temporary boosts of a skill for a member, started or not.</summary>
    // orig: R029+0x4DB6 (0E76:4DB6) draft
    public int SkillBoostTotal(int member, int skill) =>
        Slots(EventKinds.SkillBoost).Select(i => _slots[i]!).Where(e => e.Type == skill && e.Owner == member).Sum(e => e.Value);

    // ---- helpers --------------------------------------------------------------------

    /// <summary>The first free slot; "EVENT Failure" and −1 when all are used.</summary>
    // orig: event_slot_free R029+0x3144 (0E76:3144) matched; tests: EventLogTests
    private int FreeSlot()
    {
        var i = Array.IndexOf(_slots, null);
        if (i == -1)
        {
            _state.Messages.Show("EVENT Failure");
        }
        return i;
    }

    // orig: R029+0x3034 (0E76:3034) matched
    private TimedEvent Fill(int slot, int owner, int location, int extra, int kind, int type, int value, int handler,
        int flags, int tag)
    {
        var now = _state.Date;
        var e = new TimedEvent
        {
            Owner = owner, Location = location, Extra = extra, Kind = kind, Type = type, Value = value,
            ExpiryHandler = (ExpiryHandler)handler, Flags = (EventFlags)flags, Tag = tag,
            Created = now, Start = now, End = now,
        };
        _slots[slot] = e;
        return e;
    }

    /// <summary>Slots holding <paramref name="kind"/>, visited the way the original scans them.</summary>
    private IEnumerable<int> Slots(int kind)
    {
        for (var i = Next(kind, 0); i != -1; i = Next(kind, i + 1))
        {
            yield return i;
        }
    }

    /// <summary>
    /// The original "find" loop: the first slot of <paramref name="kind"/> matching, or −1 when
    /// the scan ran out of events, or <see cref="Capacity"/> when it ran past the last slot.
    /// </summary>
    private int Scan(int kind, Func<TimedEvent, bool> match)
    {
        var i = 0;
        do
        {
            i = Next(kind, i);
            if (i == -1)
            {
                return -1;
            }
            if (match(_slots[i]!))
            {
                return i;
            }
        }
        while (++i < Capacity);
        return Capacity;
    }

    private bool Any(int kind, Func<TimedEvent, bool> match, bool ignoreSuppression = false) =>
        (ignoreSuppression || !QueriesSuppressed) && Slots(kind).Any(i => HasStarted(i) && match(_slots[i]!));

    private int ValueOf(int kind, Func<TimedEvent, bool> match, bool ignoreSuppression = false)
    {
        if (!ignoreSuppression && QueriesSuppressed)
        {
            return 0;
        }
        foreach (var i in Slots(kind))
        {
            if (HasStarted(i) && match(_slots[i]!))
            {
                return _slots[i]!.Value;
            }
        }
        return 0;
    }

    /// <summary>Field-by-field comparison as the original does it: year, month, day, hour.</summary>
    private static bool IsAfter(GameDate date, GameDate now) =>
        date.Year != now.Year ? date.Year > now.Year
        : date.Month != now.Month ? date.Month > now.Month
        : date.Day != now.Day ? date.Day > now.Day
        : date.Hour > now.Hour;
}
