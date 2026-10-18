namespace Darklands.Core.Characters.Creation;

/// <summary>
/// The characters of the "Create a new party" menu (the original's <c>charactr.tmp</c>): the
/// four ready-made adventurers of SAVES\DEFAULT, then every character made, and the party slots
/// that hold copies of them. The menu fills only the first four slots.
/// </summary>
// orig: R037+0x0444 (1462:0444) draft (key handler; the charactr.tmp
// layout: int count, int ids[5], char member_shield[5][4], count records of 0x22A); tests: CreationRosterTests
// C: a slot holds the record read from the file when the character was added (fread into combatants), so a change
// made in the slot (heraldry) is lost when the character leaves the party and joins again.
public sealed class CreationRoster
{
    /// <summary>Slots the menu fills (<c>for (il = 0; il &lt; 4; il++)</c>).</summary>
    public const int MenuSlots = 4;

    private readonly int[] _slots = [-1, -1, -1, -1, -1];
    private readonly Character?[] _members = new Character?[Party.MaxMembers];

    /// <param name="slotPalettes">The five slots' figure colours (member_palette), kept per slot.</param>
    public CreationRoster(IEnumerable<Character> characters, IReadOnlyList<byte[]> slotPalettes)
    {
        Characters.AddRange(characters);
        SlotPalettes = [.. slotPalettes.Select(p => (byte[])p.Clone())];
    }

    /// <summary>Each slot's figure colours: they stay with the slot, whoever joins it.</summary>
    public byte[][] SlotPalettes { get; }

    /// <summary>The records of charactr.tmp, in order.</summary>
    public List<Character> Characters { get; } = [];

    /// <summary>The character each party slot holds (−1: empty).</summary>
    public IReadOnlyList<int> Slots => _slots;

    /// <summary>The copy in a party slot, or null.</summary>
    public Character? Member(int slot) => _members[slot];

    /// <summary>The party slot of a character, searching the first <paramref name="slots"/> slots, or −1.</summary>
    // C: the add and drop commands search 4 slots, heraldry, image, colours and kill search 5; the fifth is never filled.
    public int SlotOf(int character, int slots = Party.MaxMembers)
    {
        for (var i = 0; i < slots; i++)
        {
            if (_slots[i] == character)
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>A new character joins the roster (<c>R037+0x38AA</c>: appended to charactr.tmp).</summary>
    // orig: R037+0x38AA (1462:38AA) matched
    public void Add(Character character) => Characters.Add(character);

    /// <summary>
    /// "Add to the Party" (A): a character not yet in the first four slots takes the first free one
    /// of them, with the first figure image; returns the slot, or −1 (already in, or no room).
    /// </summary>
    // orig: R037+0x0444 case 0x1E41/0x1E61 (A)
    // C: the record keeps its own shield letter, but the menu loads PICS\SHIELDA.PIC for the new slot (the screen shows the letter A until H).
    // C: the R037 menu does not reset the slot's colours (member_palette[il]); the member takes what the slot held
    // (the R038 twin, R038+0x02F8, does reset them).
    public int AddToParty(int character, string image)
    {
        if (character < 0 || character >= Characters.Count || SlotOf(character, MenuSlots) >= 0)
        {
            return -1;
        }
        var slot = SlotOf(-1, MenuSlots);
        if (slot < 0)
        {
            return -1;
        }
        var member = Characters[character].Copy();
        member.Status = CharacterStatus.Able;
        member.Image = image;
        member.ImagePalette = SlotPalettes[slot];
        // UNCERTAIN: equip_weight_vec(il) recomputes the encumbrance from the equipped items; the copy keeps the record's
        // value (0 for made characters, who have nothing equipped; the ready-made four carry the game's own values).
        _slots[slot] = character;
        _members[slot] = member;
        return slot;
    }

    /// <summary>"Delete from the Party" (D): the character's slot (first four) is emptied.</summary>
    // orig: R037+0x0444 case 0x2044/0x2064 (D)
    public bool DropFromParty(int character)
    {
        var slot = SlotOf(character, MenuSlots);
        if (slot < 0)
        {
            return false;
        }
        _slots[slot] = -1;
        _members[slot] = null;
        return true;
    }

    /// <summary>"Kill character" (K): out of the party, out of the roster; later characters move down.</summary>
    // orig: R037+0x0444 case 0x254B/0x256B (K), R037+0x4048 (1462:4048) matched
    public bool Kill(int character)
    {
        if (character < 0 || character >= Characters.Count)
        {
            return false;
        }
        var slot = SlotOf(character);
        if (slot >= 0)
        {
            _slots[slot] = -1;
            _members[slot] = null;
        }
        Characters.RemoveAt(character);
        for (var i = 0; i < _slots.Length; i++)
        {
            if (_slots[i] > character)
            {
                _slots[i]--;
            }
        }
        return true;
    }

    /// <summary>"Heraldry" (H): the next shield, A..O, of the character's party copy.</summary>
    // orig: R037+0x0444 case 0x2348/0x2368 (H): if (++party[il][1] >= 'P') party[il][1] = 'A'
    public bool NextShield(int character)
    {
        var slot = SlotOf(character);
        if (slot < 0 || _members[slot] is not { } member)
        {
            return false;
        }
        member.ShieldLetter = (char)(member.ShieldLetter + 1 >= 'P' ? 'A' : member.ShieldLetter + 1);
        return true;
    }

    /// <summary>Changes the figure image of the character's party copy.</summary>
    // orig: R037+0x0444 case 0x1F53/0x1F73 (S): strcpy(member_shield[il], DS:E7E2[DS:E7E0])
    public bool SetImage(int character, string image)
    {
        var slot = SlotOf(character);
        if (slot < 0 || _members[slot] is not { } member)
        {
            return false;
        }
        member.Image = image;
        return true;
    }

    /// <summary>
    /// "Begin the Adventure" (B): the party is the slots up to the last filled one; empty slots in
    /// between stay as empty members. Null when no slot is filled.
    /// </summary>
    // orig: R037+0x0444 case 0x3042/0x3062 (B): party_size = last slot with ids[il] != -1, plus one; party[il][0] = 0 for empty slots
    // C: the menu never writes `leader`: it stays as it was (0 after start-up, BSS), even when slot 0 is empty; the leader rules of
    // C: play (an unable leader is replaced when time passes, the loot screen's) take over from there.
    /// <param name="seed">The party's seed.</param>
    /// <param name="leader">The leader variable as the game left it before the menu (0 after start-up).</param>
    public Party? ToParty(int seed, int leader = 0)
    {
        var size = 0;
        for (var i = 0; i < Party.MaxMembers; i++)
        {
            if (_slots[i] != -1)
            {
                size = i + 1;
            }
        }
        if (size == 0)
        {
            return null;
        }
        var party = new Party { Seed = seed, Purse = Economy.Money.Zero };
        for (var i = 0; i < size; i++)
        {
            party.Members.Add(_members[i] ?? new Character { Status = CharacterStatus.Empty });
        }
        party.LeaderIndex = leader;
        return party;
    }

    /// <summary>
    /// The game's roster when the adventure begins: every character of the menu, the party's
    /// slots holding the party's members (the copies the menu put in the slots).
    /// </summary>
    // C: charactr.tmp keeps the records as they were read; the slots' copies (combatants) are what a saved game writes for the party.
    public void CopyTo(CharacterRoster roster, Party party)
    {
        roster.Reset(new Party());
        roster.Characters.AddRange(Characters);
        for (var slot = 0; slot < Party.MaxMembers; slot++)
        {
            roster.PartySlots[slot] = _slots[slot];
            if (_slots[slot] >= 0 && slot < party.Members.Count && party.Members[slot].IsPresent)
            {
                // C: the file's record stays the one the menu wrote (CharacterRoster.StoredRecord).
                roster.KeepStored(party.Members[slot], roster.Characters[_slots[slot]]);
                roster.Characters[_slots[slot]] = party.Members[slot];
            }
        }
    }
}
