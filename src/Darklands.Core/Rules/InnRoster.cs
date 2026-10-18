using Darklands.Core.Characters;
using Darklands.Core.Characters.Creation;
using Darklands.Core.Events;

namespace Darklands.Core.Rules;

/// <summary>A row of the inn's character list: the row, the roster entry it shows, the record drawn, and the '+' mark.</summary>
public sealed record InnRosterRow(int Row, int Entry, Character Record, bool Marked);

/// <summary>What "Add to the Party" did at the inn, and in which slot (−1: none).</summary>
public readonly record struct InnRosterResult(InnRosterChange Change, int Slot);

/// <summary>What "Add to the Party" did at the inn.</summary>
public enum InnRosterChange
{
    /// <summary>The character is in the party but did not join here this week: nothing changes.</summary>
    None,

    /// <summary>The character, who joined here, leaves the party again.</summary>
    Left,

    /// <summary>The character joins the party.</summary>
    Joined,

    /// <summary>The first four slots are taken: nobody joins.</summary>
    NoRoom,
}

/// <summary>
/// The rules of the inn's party roster ("retire / recruit"): the list of the characters who can be seen here,
/// and its commands - add a character to the party or take one who joined here out again, the next heraldic
/// shield, the next figure image, the three figure colours. The roster is CHARACTR.TMP
/// (<see cref="CharacterRoster"/>); the menu's own state is the creation menu's (<see cref="PartyMenuState"/>).
/// </summary>
// orig: R038+0x23EC (1462:23EC) draft (the list; checked against asm), R038+0x02F8 (1462:02F8) draft (the key handler: 8428 of its 8436
// bytes match per the draft's header; the dispatch, the arrows, A and the colour tables checked against asm); tests: InnRosterTests
// C: each command reads the slots' ids from the file (and the figure codes into memory) first; the model's roster is brought in step instead.
public static class InnRoster
{
    /// <summary>Slots A searches and fills (the first four); H, S and the colour keys search five.</summary>
    public const int AddSlots = 4;

    /// <summary>Hours the "joined here" event lasts (a week).</summary>
    public const int JoinedHours = 0xA8;

    /// <summary>
    /// The list (R038+0x23EC): every record of CHARACTR.TMP left here or not of status 0, but not those away (status 5), nor a
    /// hireling whose term event names the location <paramref name="staleLocation"/>. Writes the row table and the length.
    /// </summary>
    /// <param name="staleLocation">The location word the term test reads (see the C note).</param>
    // C: the term test is a 2-argument call of R029+0x392C (kind 0x43, the slot of the record from R037+0x3AE2): the
    // C: third argument, the location, is the word above them on the stack, the SI the list was entered with: the caller's SI (the
    // C: key being handled by R038+0x02F8, the hours passed by the card line that opened the screen, see PartyRosterScreen).
    // C: the record tested is the file's (CharacterRoster.StoredRecord): a member who joined at another inn has the status 0 there and is
    // C: not listed here. The '+' marks a ROW whose number is one of the slots' roster entries, not the row of a party member: they differ
    // C: as soon as a record before a member is not listed.
    // C: the length (DS:E7DC) is the number of records, not of rows listed: the highlight can go below the last row.
    // DIVERGENCE: rows past the 20th write past the row table into the record buffer, which the next read replaces (from the 39th the
    // name drawn is changed); here the rows are listed and the table keeps its 20 entries.
    public static IReadOnlyList<InnRosterRow> List(GameState s, PartyMenuState menu, int staleLocation)
    {
        s.Roster.Reconcile(s.Party);
        var ids = (int[])s.Roster.PartySlots.Clone();
        var count = s.Roster.Characters.Count;
        var rows = new List<InnRosterRow>();
        for (var i = 0; i < count; i++)
        {
            var record = s.Roster.StoredRecord(i);
            var status = (int)record.Status;
            if ((record.RosterLocation == s.CurrentLocation || status != 0) && status != (int)CharacterStatus.Away
                && (status == 0 || !s.Events.TypeAt(EventKinds.MemberTerm, PartyRules.SlotOfCharacter(s, i), staleLocation)))
            {
                var row = rows.Count;
                if (row < PartyMenuState.RowTableLength)
                {
                    menu.Rows[row] = i;
                }
                rows.Add(new InnRosterRow(row, i, record, Array.IndexOf(ids, row) >= 0));
            }
        }
        menu.Count = count;
        return rows;
    }

    /// <summary>The roster entry of a row as the row table holds it (DS:8D06[row]): left from an earlier listing for a row not listed now.</summary>
    // DIVERGENCE: a row outside the table (the highlight below a 20th row, or −1) reads the words around it (the record buffer DS:8D2E
    // after it, DS:8D04 before it); here it shows no entry (−1).
    public static int RowEntry(PartyMenuState menu, int row) =>
        row >= 0 && row < PartyMenuState.RowTableLength ? menu.Rows[row] : -1;

    /// <summary>
    /// Up, Left, Right: the highlight moves up, wrapping to the list's length − 1 (DS:E7DC is every record, listed or not).
    /// </summary>
    // orig: R038+0x02F8 keys 0x4800 / 0x4B00 / 0x4D00 (asm: `dec DS:E7DA; jns`)
    public static void SelectPrevious(PartyMenuState menu)
    {
        if (--menu.Selected < 0)
        {
            menu.Selected = menu.Count - 1;
        }
    }

    /// <summary>Down: the highlight moves down, wrapping to the top past the list's length.</summary>
    // orig: R038+0x02F8 key 0x5000 (asm: `inc DS:E7DA; cmp DS:E7DC; jl`)
    public static void SelectNext(PartyMenuState menu)
    {
        if (++menu.Selected >= menu.Count)
        {
            menu.Selected = 0;
        }
    }

    /// <summary>
    /// The slot whose figure the arrows show: the first of the four whose roster entry equals the highlighted ROW; its colours become
    /// the shared ones. −1: none (the figure is only erased).
    /// </summary>
    // orig: R038+0x02F8 keys 0x4800 / 0x4B00 / 0x4D00 / 0x5000: the first of four slots whose id equals DS:E7DA itself, then the
    // colours DS:E753.. = the slot's 24 (the figure picture is the figure list's entry of the ROW: PartyRosterScreen)
    // C: the row is compared with roster entries (a stale index): the figure and colours shown are a member's only while the rows
    // C: before it list every record.
    public static int FigureSlot(GameState s, PartyMenuState menu)
    {
        s.Roster.Reconcile(s.Party);
        var slot = Array.IndexOf(s.Roster.PartySlots, menu.Selected, 0, AddSlots);
        if (slot >= 0)
        {
            menu.Colours.Load(s.Party.Slot(slot).ImagePalette);
        }
        return slot;
    }

    /// <summary>
    /// "Add to the Party" (A) on the highlighted row's character: a character in one of the first four slots who joined here this
    /// week leaves again (the record is not written back); one in a slot who did not, stays; anybody else takes the first free slot of
    /// the four, read from the file with nothing equipped and no pack, the first figure image and the inn's start colours.
    /// </summary>
    /// <param name="images">The figure codes (the first is the new member's).</param>
    // orig: R038+0x02F8 (1462:02F8) key 0x1E41 / 0x1E61, draft checked against asm: the slot search (four slots), R029+0x392C
    // (0x4C, entry, DS:907E) then R029+0x3CDE (0x4C, entry, DS:907E, -1, -1), the shield picture freed, id -1, status 0,
    // the panel row (root 0000:1716); or the free slot (no free one: the button is put back, the file left as it was); R029+0x2A32
    // (0x4C, entry, 0xA8); id; the slot's figure code = the first; PICS\SHIELDA.PIC for the slot's shield picture; the start colours
    // (PortraitColours.InnStartPalette, all 24 bytes, shared and the slot's); the 0x80-byte record, the saints, formulas and pack read
    // from the file; status 0 -> 1; unequipped (types -1, qualities 0, record bytes +0x4D/+0x4E 0) and the item count 0; the party size (DS:A67E)
    // raised to the slot + 1 if below; R031+0x5C5A (the encumbrance); the panel's frame and row; the ids and figure codes written back
    // C: the pack's 64 entries are read but the count at +0x7E is set to 0: the member joins with an empty pack (the entries stay as
    // C: stale bytes). The record kept in the file is the one read (CharacterRoster.StoredRecord): leaving again restores it.
    // C: the leader is not changed when the leader leaves; the party size is not lowered.
    // DIVERGENCE: an entry past the roster's end (a stale row after a character was deleted) reads past the file's records: nothing is
    // read and the slot's old bytes get status 1; here nobody joins (the original's slot would hold the previous occupant's record).
    public static InnRosterResult AddOrRemove(GameState s, PartyMenuState menu, IReadOnlyList<string> images)
    {
        s.Roster.Reconcile(s.Party);
        var ids = s.Roster.PartySlots;
        var entry = RowEntry(menu, menu.Selected);
        for (var il = 0; il < AddSlots; il++)
        {
            if (ids[il] != entry)
            {
                continue;
            }
            if (!s.Events.TypeAt(EventKinds.JoinedAtInn, entry, s.CurrentLocation))
            {
                return new InnRosterResult(InnRosterChange.None, il);
            }
            s.Events.Remove(EventKinds.JoinedAtInn, entry, s.CurrentLocation, -1, -1);
            var leaving = s.Party.Slot(il);
            s.Roster.LeaveUnwritten(il);
            leaving.Status = CharacterStatus.Empty;
            return new InnRosterResult(InnRosterChange.Left, il);
        }
        var slot = Array.IndexOf(ids, -1, 0, AddSlots);
        if (slot < 0)
        {
            return new InnRosterResult(InnRosterChange.NoRoom, -1);
        }
        s.Events.CreateHere(EventKinds.JoinedAtInn, entry, JoinedHours);
        if (entry < 0 || entry >= s.Roster.Characters.Count)
        {
            return new InnRosterResult(InnRosterChange.None, -1);
        }
        ids[slot] = entry;
        var member = s.Roster.Characters[entry];
        s.Roster.KeepStored(member, member.Copy());
        var party = s.Party;
        while (party.Members.Count <= slot)
        {
            party.Members.Add(party.Slot(party.Members.Count));
        }
        party.Members[slot] = member;
        member.Image = images.Count > 0 ? images[0] : member.Image;
        member.ImagePalette = [.. PortraitColours.InnStartPalette];
        for (var k = 0; k < menu.Colours.Palette.Length; k++)
        {
            menu.Colours.Palette[k] = PortraitColours.InnStartPalette[k];
        }
        if (member.Status == CharacterStatus.Empty)
        {
            member.Status = CharacterStatus.Able;
        }
        member.Weapon = new EquippedItem(-1, 0);
        member.MissileWeapon = new EquippedItem(-1, 0);
        member.VitalArmor = new EquippedItem(-1, 0);
        member.LimbArmor = new EquippedItem(-1, 0);
        member.Shield = new EquippedItem(-1, 0);
        // C: the member bytes +0x39 and +0x3A, record bytes +0x4D/+0x4E (the model does not name them), are cleared too.
        if (member.OriginalRecord is { Length: > 0x4E } original)
        {
            var record = (byte[])original.Clone();
            record[0x4D] = 0;
            record[0x4E] = 0;
            member.OriginalRecord = record;
        }
        member.Pack.Stacks = 0;
        Inventory.EquipmentWeight(s, slot);
        return new InnRosterResult(InnRosterChange.Joined, slot);
    }

    /// <summary>"Heraldry" (H): the next shield letter, A..O, of the highlighted row's character if in one of the five slots; the slot or −1.</summary>
    // orig: R038+0x02F8 key 0x2348 / 0x2368: the member byte +1 (the letter) + 1, 'P' and above back to 'A', PICS\SHIELD + letter + .PIC loaded for the slot,
    // its panel row drawn; the record is not written to the file
    public static int NextShield(GameState s, PartyMenuState menu)
    {
        var slot = SlotOfRow(s, menu);
        if (slot >= 0)
        {
            var member = s.Party.Slot(slot);
            member.ShieldLetter = (char)(member.ShieldLetter + 1 >= 'P' ? 'A' : member.ShieldLetter + 1);
        }
        return slot;
    }

    /// <summary>"Select Character Image" (S): the next figure of the list for the highlighted row's character if in a slot; the slot or −1.</summary>
    // orig: R038+0x02F8 key 0x1F53 / 0x1F73: `if (++DS:E7E0 >= DS:E7DE) DS:E7E0 = 0`, the slot's figure code, the figure drawn at (0x46, 0xA5),
    // the figure codes written back to the file (20 bytes back from the position after the header's ids)
    public static int NextImage(GameState s, PartyMenuState menu, IReadOnlyList<string> images)
    {
        var slot = SlotOfRow(s, menu);
        if (slot >= 0 && images.Count > 0)
        {
            if (++menu.Image >= images.Count)
            {
                menu.Image = 0;
            }
            s.Party.Slot(slot).Image = images[menu.Image];
        }
        return slot;
    }

    /// <summary>
    /// "1st / 2nd / 3rd color" (key 1, 2, 3): the next colours of one part of the figure of the slot whose roster entry equals the
    /// highlighted ROW (the arrows' stale index), with the inn's tables; the slot or −1.
    /// </summary>
    // orig: R038+0x02F8 keys 0x231/0x4F31, 0x332/0x5032, 0x433/0x5133: the first of five slots whose id equals DS:E7DA itself, then
    // the chain on the slot's figure code (PortraitColours.CycleAtInn) and the palette set (root 047C:058B)
    // C: unlike H and S, the row number itself is compared with the slots' entries.
    public static int CycleColour(GameState s, PartyMenuState menu, int key)
    {
        s.Roster.Reconcile(s.Party);
        var slot = Array.IndexOf(s.Roster.PartySlots, menu.Selected);
        if (slot >= 0)
        {
            var member = s.Party.Slot(slot);
            menu.Colours.CycleAtInn(key, member.Image, member.ImagePalette);
        }
        return slot;
    }

    /// <summary>A character made with "Create a Character" joins the roster (appended to CHARACTR.TMP), left at the current place.</summary>
    // orig: R037+0x38AA (1462:38AA) draft (bytes match): count + 1, the record written at the end with its first word = DS:907E
    public static void AddNew(GameState s, Character made)
    {
        made.RosterLocation = s.CurrentLocation;
        s.Roster.Characters.Add(made);
    }

    // C: H and S search the five slots for the highlighted row's ROSTER ENTRY (DS:8D06[DS:E7DA]).
    private static int SlotOfRow(GameState s, PartyMenuState menu)
    {
        s.Roster.Reconcile(s.Party);
        return Array.IndexOf(s.Roster.PartySlots, RowEntry(menu, menu.Selected));
    }
}
