namespace Darklands.Core.Characters;

/// <summary>
/// Every character of the game (the original's CHARACTR.TMP): the party's members and those
/// left behind at inns, in the order the game keeps them, and which roster entry each of the
/// five party slots holds. While a character is in the party, the roster entry and the party
/// member are the same object.
/// </summary>
// orig: charactr.tmp (int count, int ids[5], char member-shield-picture[5][4], count 0x22A-byte records), kept by
// R037+0x38AA (append), party-leave R037+0x3B58, party-join R037+0x3CD6, party-wipe R037+0x3F30 and
// R037+0x4048 (delete, later records move down) draft; tests: OriginalSaveTests
public sealed class CharacterRoster
{
    /// <summary>The characters, in roster order.</summary>
    public List<Character> Characters { get; } = [];

    /// <summary>The roster entry in each party slot (−1: the slot is empty).</summary>
    public int[] PartySlots { get; } = [-1, -1, -1, -1, -1];

    // the file's own record of a party member, where it is no longer the member as he or she is now (keyed by the member)
    private readonly Dictionary<Character, Character> _stored = new(ReferenceEqualityComparer.Instance);

    // the slot memory of a slot whose id is -1 (a loaded slot without a record, what a member who left kept there): no entry of the roster
    private readonly HashSet<Character> _offRoster = new(ReferenceEqualityComparer.Instance);

    // a second party slot's copy of an entry's character (party-join of a character already in the party), and the entry it names
    private readonly Dictionary<Character, int> _secondSlots = new(ReferenceEqualityComparer.Instance);

    /// <summary><paramref name="copy"/>, in a party slot, is a second slot of roster entry <paramref name="entry"/> (its own copy of the record).</summary>
    // C: party-join R037+0x3CD6 reads the entry's record into the slot whoever else holds it: two slots then name the same entry.
    public void KeepSecondSlot(Character copy, int entry) => _secondSlots[copy] = entry;

    /// <summary>
    /// <paramref name="residue"/> is what a slot without a roster entry holds: a status a card later gives it (a random member who is
    /// nobody) does not make it a member of the roster.
    /// </summary>
    // C: only join-party-member (R029+0x4650), party-join (R037+0x3CD6) and the inn's roster write ids[slot]; party[slot][0] alone is not
    // C: the roster, and the saved game (R037+0x2ACE) writes the records of charactr.tmp, so such a slot is saved as no member.
    public void MarkOffRoster(Character residue) => _offRoster.Add(residue);

    /// <summary>
    /// The record CHARACTR.TMP holds for an entry: for a party member the record as it was when the file last
    /// had it written (the game was loaded, the character joined), not the member as he or she is now; for
    /// anybody else the entry itself.
    /// </summary>
    // C: the original reads a member's record into the slot when the member joins (the load R037+0x4184, R037+0x3CD6, the inn's roster
    // C: R038+0x02F8) and writes it back only when the member leaves through R037+0x3B58; a saved game takes the party's records from memory.
    public Character StoredRecord(int entry) =>
        _stored.TryGetValue(Characters[entry], out var record) ? record : Characters[entry];

    /// <summary>The file's record of <paramref name="member"/> is now <paramref name="record"/> (a copy taken as it was written).</summary>
    public void KeepStored(Character member, Character record) => _stored[member] = record;

    /// <summary>The member's record has been written back to the file: the entry is the member again.</summary>
    public void WrittenBack(Character member) => _stored.Remove(member);

    /// <summary>
    /// A member leaves the party without his or her record being written back (the inn's roster): the entry
    /// becomes the file's record again; the member's slot keeps the member as he or she was.
    /// </summary>
    // orig: R038+0x02F8 (1462:02F8) draft, checked against asm; key 0x1E41: the slot's id becomes -1 and its status byte 0, the ids are written back, the record is not
    public void LeaveUnwritten(int slot)
    {
        var entry = PartySlots[slot];
        PartySlots[slot] = -1;
        if (entry < 0 || entry >= Characters.Count)
        {
            return;
        }
        var member = Characters[entry];
        Characters[entry] = _stored.TryGetValue(member, out var record) ? record : member.Copy();
        _stored.Remove(member);
        MarkOffRoster(member);
    }

    /// <summary>A roster of just the party: its present members in slot order (a new game's charactr.tmp).</summary>
    // orig: R037+0x9DB2 key Q: saved-game-read("default") writes the four default records and ids 0, 1, 2, 3, -1
    // C: the file's records are the members as they were written (KeepStored).
    public void Reset(Party party)
    {
        Characters.Clear();
        _stored.Clear();
        _offRoster.Clear();
        _secondSlots.Clear();
        Array.Fill(PartySlots, -1);
        for (var slot = 0; slot < party.Members.Count && slot < PartySlots.Length; slot++)
        {
            if (party.Members[slot].IsPresent)
            {
                PartySlots[slot] = Characters.Count;
                Characters.Add(party.Members[slot]);
                KeepStored(party.Members[slot], party.Members[slot].Copy());
            }
        }
    }

    /// <summary>
    /// Brings the slots in line with the party before the roster is written: a slot keeps its
    /// entry while it holds that entry's character (whatever the status: a loaded slot may hold a
    /// character of status 0), a present member not on the roster gets a new entry at the end
    /// (a hireling), any other slot holds none.
    /// </summary>
    // C: the original keeps charactr.tmp in step at every change (party-join, party-leave, party-wipe, join-party-member: `idx[slot] = cnt++`);
    // here the changes the model makes without the roster are caught up with when the game is saved.
    public void Reconcile(Party party)
    {
        for (var slot = 0; slot < PartySlots.Length; slot++)
        {
            // C: ids[] is the five slots' whatever party-headcount is: a slot past it keeps its entry while it still holds that entry's record
            //   (a loaded slot of status 0 after the last occupied one).
            var member = slot < party.Members.Count ? party.Members[slot] : party.SpareRecord(slot);
            var entry = PartySlots[slot];
            if (member is not null && entry >= 0 && entry < Characters.Count
                && (ReferenceEquals(Characters[entry], member) || (_secondSlots.TryGetValue(member, out var named) && named == entry)))
            {
                continue;
            }
            if (member is null || !member.IsPresent || _offRoster.Contains(member))
            {
                PartySlots[slot] = -1;
                continue;
            }
            entry = Characters.FindIndex(c => ReferenceEquals(c, member));
            if (entry < 0)
            {
                entry = Characters.Count;
                Characters.Add(member);
            }
            PartySlots[slot] = entry;
        }
    }

    /// <summary>A member leaves the party for the roster (party-leave: <c>ids[p] = -1</c>, the record stays).</summary>
    // orig: party-leave R037+0x3B58 (1462:3B58) matched
    public void LeaveParty(int slot)
    {
        if (slot >= 0 && slot < PartySlots.Length)
        {
            if (PartySlots[slot] >= 0 && PartySlots[slot] < Characters.Count)
            {
                // C: R037+0x3B58 writes the member's record back to the file.
                WrittenBack(Characters[PartySlots[slot]]);
            }
            PartySlots[slot] = -1;
        }
    }

    /// <summary>A character leaves the roster for good; the entries after it move down.</summary>
    // orig: R037+0x4048 (1462:4048) matched: n--, ids[i] == c -> -1, ids[i] > c -> ids[i] - 1, records c+1.. copied down
    public void Forget(Character character)
    {
        var entry = Characters.FindIndex(c => ReferenceEquals(c, character));
        if (entry < 0)
        {
            return;
        }
        Characters.RemoveAt(entry);
        _stored.Remove(character);
        for (var slot = 0; slot < PartySlots.Length; slot++)
        {
            if (PartySlots[slot] == entry)
            {
                PartySlots[slot] = -1;
            }
            else if (PartySlots[slot] > entry)
            {
                PartySlots[slot]--;
            }
        }
    }
}
