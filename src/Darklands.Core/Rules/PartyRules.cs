using Darklands.Core.Characters;
using Darklands.Core.Events;

namespace Darklands.Core.Rules;

/// <summary>Members joining and leaving: hirelings, members who went away, and the aftermath of a battle.</summary>
public static class PartyRules
{
    /// <summary>
    /// A hireling joins in the first free slot (<c>add_member</c>): the standard attributes
    /// 37/34/31/29/32/34/99 and skills, age 25, a random shield picture 'A'..'O', the figure
    /// <paramref name="image"/>, three standard items, and a <see cref="EventKinds.MemberTerm"/> event of
    /// <paramref name="hours"/> whose end makes the member leave. Returns the slot, or −1 when the party is
    /// full. (The original also writes the member into CHARACTR.TMP and loads the shield picture.)
    /// </summary>
    // orig: add_member R029+0x4650 (0E76:4650) matched; tests: PracticeAndItemsTests
    // C: the shield letter rand_mod(15) is the only draw, before the three items.
    // DIVERGENCE: CHARACTR.TMP, the shield picture, party_size and the redraw are the host's.
    public static int AddMember(GameState s, string name, string shortName, string image, int hours, int term)
    {
        var party = s.Party;
        var slot = Enumerable.Range(0, Party.MaxMembers).FirstOrDefault(i => !party.Slot(i).IsPresent, -1);
        if (slot == -1)
        {
            return -1;
        }
        // C: the new member is written over the slot's record (a departed member's, a loaded slot's zeros): what add_member does not store
        // C: stays. A slot past the party keeps the record of the member who left it (party_remove / party_delete clear the status byte).
        var before = slot < party.Members.Count ? party.Members[slot] : party.SpareRecord(slot);
        var m = new Character { Name = name, ShortName = shortName, Status = CharacterStatus.Able, Age = 25, Image = image };
        // C: R029+0x48B1 copies the argument at [bp+0xC] into member_shield[slot] (DS:A668 + slot * 4, written back to charactr.tmp);
        // C: member_palette[slot] (R136+0x33FC) is not written: the member is drawn with, and the saved game (R037+0x2ACE) writes, the
        // C: palette the slot held.
        if (before is not null)
        {
            m.ImagePalette = [.. before.ImagePalette];
        }
        int[] maxima = [37, 34, 31, 29, 32, 34, 99];
        maxima.CopyTo(m.AttributeMaximums, 0);
        maxima.CopyTo(m.Attributes, 0);
        int[] skills = [59, 21, 0, 57, 23, 0, 46, 11, 15, 36, 51, 0, 0, 16, 59, 52, 48, 40, 31];
        skills.CopyTo(m.Skills, 0);
        m.ShieldLetter = (char)('A' + s.Random.Below(15));
        m.Encumbrance = 10;
        m.VitalArmor = new EquippedItem(-1, 25);
        m.LimbArmor = new EquippedItem(-1, 25);
        m.Shield = new EquippedItem(-1, 25);
        // C: the weapon and missile qualities (+0x44/+0x46, record +0x58/+0x5A) are not stored: the slot's earlier values.
        m.Weapon = new EquippedItem(-1, before?.Weapon.Quality ?? 0);
        m.MissileWeapon = new EquippedItem(-1, before?.MissileWeapon.Quality ?? 0);
        // C: it also stores bytes the model does not name: +4 = 0xFF, +5..+7 = 0, +8 = the slot, +0x36 = 0xFF, +0x39 = 5, +0x3A = 4
        // C: (record +0x18..+0x1C, +0x4A, +0x4D/+0x4E), over the slot's earlier bytes: the record kept from its earlier member, or the
        // C: zero member record of a slot no member held. What the model names is written over the record when it is used.
        var record = before?.OriginalRecord is { Length: > 0x4E } kept ? (byte[])kept.Clone() : new byte[Character.MemberRecordSize];
        record[0x18] = 0xFF;
        record[0x19] = record[0x1A] = record[0x1B] = 0;
        record[0x1C] = (byte)slot;
        record[0x4A] = 0xFF;
        record[0x4D] = 5;
        record[0x4E] = 4;
        m.OriginalRecord = record;
        while (party.Members.Count <= slot)
        {
            // C: a slot between keeps its memory (a slot past the party that a card gave a status is not free)
            party.Members.Add(party.Slot(party.Members.Count));
        }
        party.Members[slot] = m;
        Inventory.AddWithQuality(s, slot, 0x2C, 10);
        Inventory.AddWithQuality(s, slot, 0x33, 15);
        Inventory.AddWithQuality(s, slot, 3, 15);
        s.Events.Create(-2, s.CurrentLocation, 0, EventKinds.MemberTerm, slot, term, (int)ExpiryHandler.MemberTermEnds, hours,
            (int)EventFlags.CallExpiryHandler, 0);
        // C: DS:A67C = event_count(0x43), the cached hireling count (GameState.HirelingCount).
        s.HirelingCount = s.Events.Count(EventKinds.MemberTerm);
        return slot;
    }

    /// <summary>
    /// Ends the term of every hireling whose term value is <paramref name="term"/>: a
    /// <see cref="EventKinds.MemberLeft"/> note each, and the term events are removed.
    /// </summary>
    // orig: R029+0x4AA4 (0E76:4AA4) matched; tests: PracticeAndItemsTests
    public static void EndTerms(GameState s, int term)
    {
        var log = s.Events;
        for (var i = log.Next(EventKinds.MemberTerm, 0); i != -1; i = log.Next(EventKinds.MemberTerm, i + 1))
        {
            var e = log[i]!;
            if (e.Value == term)
            {
                log.Create(-2, s.CurrentLocation, 0, EventKinds.MemberLeft, e.Type, e.Type, 0, EventLog.Forever, 0, 0);
                log.Free(i);
            }
        }
    }

    private static IEnumerable<int> Away(GameState s) =>
        Enumerable.Range(0, s.Party.Members.Count).Where(i => s.Party.Members[i].Status == CharacterStatus.Away).ToList();

    /// <summary>
    /// Members who went away are told of and leave (<c>party_report_away</c>): a
    /// <see cref="EventKinds.MemberAway"/> note here for a year each, naming the character's
    /// roster entry, then the member leaves the party for the roster. Returns how many.
    /// </summary>
    // orig: party_report_away R029+0x23E2 (0E76:23E2) matched:
    //   for each slot of status 5: event_here_type(0x4D, char_in_slot_vec(i), 0x2238), party_remove_vec(i); then party_panel_rows(0x100)
    public static int ReportAway(GameState s)
    {
        var n = 0;
        for (var i = 0; i < Party.MaxMembers; i++)
        {
            if (s.Party.Slot(i).Status != CharacterStatus.Away)
            {
                continue;
            }
            s.Events.CreateHere(EventKinds.MemberAway, CharacterInSlot(s, i), EventLog.OneYear);
            RemoveFromParty(s, i);
            n++;
        }
        return n;
    }

    /// <summary>The roster entry in a party slot (<c>char_in_slot</c>: ids[p] of CHARACTR.TMP), −1 for none.</summary>
    // orig: char_in_slot R037+0x3A8A (1462:3A8A) draft (fn matched): reads ids[5] from charactr.tmp
    // C: CHARACTR.TMP is in step with the party at every change in the original; the roster is brought in step first (Reconcile).
    public static int CharacterInSlot(GameState s, int slot)
    {
        s.Roster.Reconcile(s.Party);
        return slot >= 0 && slot < s.Roster.PartySlots.Length ? s.Roster.PartySlots[slot] : -1;
    }

    /// <summary>The party slot of roster entry <paramref name="character"/> (<c>slot_of_char</c>), −1 if not in the party.</summary>
    // orig: slot_of_char R037+0x3AE2 (1462:3AE2) draft (fn matched): the first i < 5 with ids[i] == c
    public static int SlotOfCharacter(GameState s, int character)
    {
        s.Roster.Reconcile(s.Party);
        return Array.IndexOf(s.Roster.PartySlots, character);
    }

    /// <summary>
    /// A member leaves the party but stays on the roster (<c>party_remove</c>): the leader passes
    /// to the first other able member, the character is left here, and the slot is emptied.
    /// </summary>
    // orig: party_remove R037+0x3B58 (1462:3B58) draft (fn matched); tests: MemberRulesTests
    // C: if (leader == p) the first i != p of status 1 leads; ids[p] = -1; if (party[p][0] != 5) party[p][0] = 0; the record's first word
    //   = cur_loc; the record goes back to charactr.tmp (status 0, or 5 for a member who went away); then party[p][0] = 0 and party_size =
    //   one past the last occupied slot. The roster keeps the record (Character.RosterLocation, its status); the slot keeps a copy of
    //   its bytes with status 0 (Party.TrimToSize keeps a dropped slot's).
    // DIVERGENCE: the shield picture is the party panel's (loaded on demand).
    public static void RemoveFromParty(GameState s, int slot)
    {
        var party = s.Party;
        slot = party.Resolve(slot);
        if (party.LeaderIndex == slot)
        {
            for (var i = 0; i < party.Members.Count; i++)
            {
                if (party.Members[i].Status == CharacterStatus.Able && i != slot)
                {
                    party.LeaderIndex = i;
                    break;
                }
            }
        }
        s.Roster.Reconcile(party);
        s.Roster.LeaveParty(slot);
        if (slot >= 0 && slot < party.Members.Count)
        {
            var record = party.Members[slot];
            if (record.Status != CharacterStatus.Away)
            {
                record.Status = CharacterStatus.Empty;
            }
            record.RosterLocation = s.CurrentLocation;
            // C: the slot's memory keeps the record's bytes (name, pack, formulas, saints) with status 0: a copy, apart from the roster's record.
            var residue = record.Copy();
            residue.Status = CharacterStatus.Empty;
            party.Members[slot] = residue;
            s.Roster.MarkOffRoster(residue);
        }
        party.TrimToSize();
    }

    /// <summary>
    /// A character of the roster joins the party (<c>party_add</c>) in the first free slot, able;
    /// returns the slot, or −1 when all five are taken.
    /// </summary>
    // orig: party_add R037+0x3CD6 (1462:3CD6) draft (fn matched); tests: MemberRulesTests
    // C: the first slot with ids[slot] == -1 (of five); ids[slot] = c; the record (member, saints, formulas, pack) is read into the slot,
    //   party[slot][0] = 1, its shield picture loaded and the panel drawn; ids written back; party_size = one past the last occupied slot.
    // DIVERGENCE: an entry past the roster's end makes the original read past the file's records (stale bytes); here nobody joins.
    public static int AddFromRoster(GameState s, int character)
    {
        var party = s.Party;
        s.Roster.Reconcile(party);
        var slot = Array.IndexOf(s.Roster.PartySlots, -1);
        if (slot < 0 || character < 0 || character >= s.Roster.Characters.Count)
        {
            return -1;
        }
        s.Roster.PartySlots[slot] = character;
        var member = s.Roster.Characters[character];
        // C: the file keeps the record as it was read (CharacterRoster.StoredRecord).
        s.Roster.KeepStored(member, member.Copy());
        member.Status = CharacterStatus.Able;
        // C: R037+0x3D91..0x3E16 read the record, saints, formulas and pack into the slot, R037+0x3E19 the status; member_shield[slot]
        // C: (DS:A668, read back from the file's header) and member_palette[slot] (R136+0x33FC) are not written: the member takes the
        // C: figure the slot held (a slot no member held since start-up: the member's own here).
        if ((slot < party.Members.Count ? party.Members[slot] : party.SpareRecord(slot)) is { } figure)
        {
            member.Image = figure.Image;
            member.ImagePalette = [.. figure.ImagePalette];
        }
        while (party.Members.Count <= slot)
        {
            // C: a slot between keeps its memory (none in practice: every slot before the first free id holds a member)
            party.Members.Add(party.Slot(party.Members.Count));
        }
        party.Members[slot] = member;
        party.TrimToSize();
        return slot;
    }

    /// <summary>Members who went away come back, having lost things (<c>party_rejoin_away</c>); returns how many.</summary>
    // orig: party_rejoin_away R029+0x251A (0E76:251A) matched
    public static int RejoinAway(GameState s)
    {
        var n = 0;
        foreach (var i in Away(s))
        {
            s.Party.Members[i].Status = CharacterStatus.Able;
            Inventory.Lose(s, i);
            n++;
        }
        return n;
    }

    /// <summary>
    /// The encounter seed of a battle (<c>run_battle</c>): −2 = this location (party seed +
    /// location), −1 = random 0..999; then the party seed is added again (an original bug for
    /// the location seed, which so gets it twice).
    /// </summary>
    // orig: run_battle R029+0x2278 (0E76:2278) matched
    // C: run_battle draws the side (rand_mod(2)) before the seed (rand_mod(1000)): CardContext.RunBattle keeps that order.
    // BUG: seed -2 gets party_seed twice (FixBattleSeed).
    public static int BattleSeed(GameState s, int seed)
    {
        if (seed == -2)
        {
            seed = s.Behaviour.FixBattleSeed ? s.CurrentLocation : s.Party.Seed + s.CurrentLocation;
        }
        else if (seed == -1)
        {
            seed = s.Random.Below(1000);
        }
        return (short)(seed + s.Party.Seed);
    }

    /// <summary>After a battle: the dead leave the party for good, everybody else (but those away) is able again.</summary>
    /// <param name="delete">party_delete (the slot emptied, the character out of the roster, the party trimmed).</param>
    // orig: run_battle R029+0x2278 (0E76:2278) matched
    // C: for each of the 5 slots, status 3 -> 0 and party_delete_vec(i) (R037+0x3F30) first, then every status but 0 and 5 -> 1.
    public static void AfterBattle(GameState s, Action<int> delete)
    {
        for (var i = 0; i < Party.MaxMembers; i++)
        {
            if (i < s.Party.Members.Count && s.Party.Members[i].Status == CharacterStatus.Dead)
            {
                s.Party.Members[i].Status = CharacterStatus.Empty;
                delete(i);
            }
        }
        foreach (var m in s.Party.Members)
        {
            if (m.Status is not CharacterStatus.Empty and not CharacterStatus.Away)
            {
                m.Status = CharacterStatus.Able;
            }
        }
    }
}
