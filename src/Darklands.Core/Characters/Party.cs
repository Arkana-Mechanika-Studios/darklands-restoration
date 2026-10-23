using Darklands.Core.Economy;

namespace Darklands.Core.Characters;

/// <summary>The adventuring party: up to five members, a leader, a purse and their fame.</summary>
public sealed class Party
{
    public const int MaxMembers = 5;

    public List<Character> Members { get; } = [];

    /// <summary>Index of the leader in <see cref="Members"/>.</summary>
    public int LeaderIndex { get; set; }

    public Money Purse { get; set; }

    /// <summary>Party fame ("reputation" in the save header); quest rewards add to it.</summary>
    public int Fame { get; set; }

    /// <summary>Random seed fixed when the party is founded; it keeps cities and shops stable.</summary>
    public int Seed { get; set; }

    public Character Leader => Members[LeaderIndex];

    public IEnumerable<Character> Able => Members.Where(m => m.IsAble);

    public IEnumerable<Character> Present => Members.Where(m => m.IsPresent);

    /// <summary>Makes the first able member the leader if the current leader cannot lead.</summary>
    // orig: hours_pass R029+0x255A (0E76:255A) matched; tests: EventLogTests
    // C: `if (party[leader][0] != 1)` takes the first status-1 slot of five; with none the leader stays.
    public void EnsureAbleLeader()
    {
        if (Members.Count == 0 || Leader.IsAble)
        {
            return;
        }
        var able = Members.FindIndex(m => m.IsAble);
        if (able >= 0)
        {
            LeaderIndex = able;
        }
    }

    public int BestSkill(Skill skill) => Able.Select(m => m[skill]).DefaultIfEmpty(0).Max();

    /// <summary>The party owns the philosopher's stone (<c>party_get(6)</c>).</summary>
    public int PhilosophersStone { get; set; }

    /// <summary>
    /// The money held with the bankers, "bank notes" (DS:9072/9074/9076: a florins, groschen, pfennigs word triple like the
    /// purse; save header +0x8C/+0x8E/+0x90). The party screen shows only its florins under "Notes".
    /// </summary>
    // orig: DS:9072 (save: R037 seg 1C7D / R037 seg 1462 read and write the three words after prev_screen); tests: BankNotesTests
    // C: the cards use DS:9072 as an int (only the florins word: deposit, redeem, display, R133 reset) or as a struct money
    // (DS:9072 as char[] in R086, R105, R110, R132); both views are the same three words, so one Money serves both.
    public Money BankNotes { get; set; }

    /// <summary>Results of the last party query, which card scripts read afterwards.</summary>
    public PartyQueryResult Last { get; } = new();

    /// <summary>The member a <c>who</c> argument means: −1 is the leader, other values as given.</summary>
    // orig: leader DS:907B
    // C: who == -1 becomes the signed char leader (cbw); each accessor resolves it itself, some do not (noted at the caller).
    public int Resolve(int who) => who == Who.Leader ? LeaderIndex : who;

    /// <summary>The member in slot <paramref name="index"/> when that slot is occupied (status not empty).</summary>
    public Character? PresentMember(int index) =>
        index >= 0 && index < Members.Count && Members[index].IsPresent ? Members[index] : null;

    /// <summary>
    /// The member in slot <paramref name="index"/> (−1: the leader). A slot past the list is an
    /// empty member whose bytes (pack, formulas, saints) persist like the original's slot memory;
    /// an index outside 0..4 is a fresh empty member.
    /// </summary>
    public Character Slot(int index)
    {
        index = Resolve(index);
        Character m;
        if (index >= 0 && index < Members.Count)
        {
            m = Members[index];
        }
        else if (index >= 0 && index < MaxMembers)
        {
            m = _spare[index] ??= new Character { Status = CharacterStatus.Empty };
        }
        else
        {
            return new Character { Status = CharacterStatus.Empty };
        }
        m.Pack.Home = (this, index);
        return m;
    }

    private readonly Character?[] _spare = new Character?[MaxMembers];

    /// <summary>
    /// The record a slot past <see cref="Members"/> still holds (a departed member's, or a loaded slot's), null if none was left there:
    /// party_remove and party_delete clear only the status byte, so the name, sex, figure and record bytes stay until a member takes the slot.
    /// </summary>
    // C: party DS:9C69 (the 0x80-byte records from DS:9C55, the status byte at +0x14); party_delete R037+0x3F30 writes the status byte and nothing else of the record
    public Character? SpareRecord(int index) => index >= Members.Count && index < MaxMembers && _departed[index] ? _spare[index] : null;

    // the slots a member left (TrimToSize), as opposed to a slot only read past the party (Slot)
    private readonly bool[] _departed = new bool[MaxMembers];

    /// <summary>
    /// The party size becomes one past the last occupied slot (<c>party_size</c>); the slots
    /// dropped keep their member's bytes (name, pack, formulas, saints), as the original's slot
    /// memory does when only the status byte is cleared.
    /// </summary>
    // C: party_remove / party_delete clear party[p][0] and recount party_size; the 0x80-byte record and the slot's tables stay.
    public void TrimToSize()
    {
        while (Members.Count > 0 && !Members[^1].IsPresent)
        {
            _spare[Members.Count - 1] = Members[^1];
            _departed[Members.Count - 1] = true;
            Members.RemoveAt(Members.Count - 1);
        }
    }

    /// <summary>The six bytes before member 0's pack (2E38:0038..003D, entry −1 of member 0): zero in the image, written only through that entry.</summary>
    // orig: data_file[0].pad00 + 0x38 (2E38:0038); the 2E38 segment is zero in the EXE image (R136)
    public ItemStack BeforeFirstPack { get; set; } = Pack.Zero;

    /// <summary>Pack entry −1 or 64 of slot <paramref name="slot"/>: the neighbouring member's entry or the bytes around the packs.</summary>
    // C: the packs lie at 2E38:003E + slot * 0x180 one after the other; member 4's entry 64 is member 0's first six formula bytes (2E38:07BE).
    internal ItemStack EntryOutside(int slot, int index)
    {
        var offset = PacksAt + slot * PackBytes + index * EntryBytes;
        return new ItemStack(MemoryByte(offset) | MemoryByte(offset + 1) << 8, MemoryByte(offset + 2), MemoryByte(offset + 3),
            MemoryByte(offset + 4), MemoryByte(offset + 5));
    }

    internal void SetEntryOutside(int slot, int index, ItemStack value)
    {
        var offset = PacksAt + slot * PackBytes + index * EntryBytes;
        if (offset == PacksAt - EntryBytes)
        {
            BeforeFirstPack = value;
            return;
        }
        SetMemoryEntry(offset, value);
    }

    private const int PacksAt = 0x3E;
    private const int PackBytes = Pack.Size * EntryBytes;
    private const int EntryBytes = 6;
    private const int FormulasAt = PacksAt + MaxMembers * PackBytes;     // 0x7BE
    private const int FormulaBytes = 22;
    private const int SaintsAt = FormulasAt + MaxMembers * FormulaBytes; // 0x82C
    private const int SaintBytes = 20;
    private const int SaintsEnd = SaintsAt + MaxMembers * SaintBytes;    // 0x890

    /// <summary>
    /// A byte of the original's member data in segment 2E38 (0x38 .. 0x88F): the bytes before the
    /// packs, the five packs, the five formula tables and the five saint tables. Other offsets
    /// (the item-definition pointers from 0x890 on, the header before 0x38) are not modelled: 0.
    /// </summary>
    // orig: data_file / inventories 2E38:003E, R136+0xE08F + 0x5F = formulas 2E38:07BE, saints_known 2E38:082C, item_defs 2E38:0890 (far pointers: heap addresses)
    public int MemoryByte(int offset)
    {
        if (offset is >= PacksAt - EntryBytes and < PacksAt)
        {
            return EntryByte(BeforeFirstPack, offset - (PacksAt - EntryBytes));
        }
        if (offset is >= PacksAt and < FormulasAt)
        {
            var o = offset - PacksAt;
            return EntryByte(Slot(o / PackBytes).Pack.Entries[o % PackBytes / EntryBytes], o % EntryBytes);
        }
        if (offset is >= FormulasAt and < SaintsAt)
        {
            return Slot((offset - FormulasAt) / FormulaBytes).Formulas.Bytes[(offset - FormulasAt) % FormulaBytes];
        }
        if (offset is >= SaintsAt and < SaintsEnd)
        {
            return Slot((offset - SaintsAt) / SaintBytes).Saints.Bytes[(offset - SaintsAt) % SaintBytes];
        }
        return 0;
    }

    /// <summary>Writes a byte of the member data in segment 2E38 (see <see cref="MemoryByte"/>); outside it nothing is written.</summary>
    public void SetMemoryByte(int offset, int value)
    {
        var b = (byte)value;
        if (offset is >= PacksAt - EntryBytes and < PacksAt)
        {
            BeforeFirstPack = WithByte(BeforeFirstPack, offset - (PacksAt - EntryBytes), b);
        }
        else if (offset is >= PacksAt and < FormulasAt)
        {
            var o = offset - PacksAt;
            var pack = Slot(o / PackBytes).Pack;
            var i = o % PackBytes / EntryBytes;
            pack[i] = WithByte(pack[i], o % EntryBytes, b);
        }
        else if (offset is >= FormulasAt and < SaintsAt)
        {
            Slot((offset - FormulasAt) / FormulaBytes).Formulas.Bytes[(offset - FormulasAt) % FormulaBytes] = b;
        }
        else if (offset is >= SaintsAt and < SaintsEnd)
        {
            Slot((offset - SaintsAt) / SaintBytes).Saints.Bytes[(offset - SaintsAt) % SaintBytes] = b;
        }
    }

    /// <summary>The 2E38 offset of saint byte <paramref name="index"/> of slot <paramref name="who"/> (<c>saints_known[who][index]</c>, unresolved: −1 is the 20 bytes before slot 0's).</summary>
    public static int SaintByteOffset(int who, int index) => SaintsAt + who * SaintBytes + index;

    /// <summary>The 2E38 offset of formula byte <paramref name="index"/> of slot <paramref name="who"/>.</summary>
    public static int FormulaByteOffset(int who, int index) => FormulasAt + who * FormulaBytes + index;

    private void SetMemoryEntry(int offset, ItemStack value)
    {
        SetMemoryByte(offset, value.Code);
        SetMemoryByte(offset + 1, value.Code >> 8);
        SetMemoryByte(offset + 2, value.Type);
        SetMemoryByte(offset + 3, value.Quality);
        SetMemoryByte(offset + 4, value.Count);
        SetMemoryByte(offset + 5, value.Weight);
    }

    private static int EntryByte(ItemStack e, int k) => k switch
    {
        0 => e.Code & 0xFF,
        1 => (e.Code >> 8) & 0xFF,
        2 => e.Type & 0xFF,
        3 => e.Quality & 0xFF,
        4 => e.Count & 0xFF,
        _ => e.Weight & 0xFF,
    };

    private static ItemStack WithByte(ItemStack e, int k, byte b) => k switch
    {
        0 => e with { Code = (e.Code & 0xFF00) | b },
        1 => e with { Code = (e.Code & 0x00FF) | b << 8 },
        2 => e with { Type = b },
        3 => e with { Quality = b },
        4 => e with { Count = b },
        _ => e with { Weight = b },
    };
}

/// <summary>The special <c>who</c> values of the rules.</summary>
public static class Who
{
    /// <summary>The leader (the active member).</summary>
    public const int Leader = -1;

    /// <summary>Every present member.</summary>
    public const int Everybody = -2;
}

/// <summary>
/// What the last party query found (<c>best_member</c>, <c>best_value</c>, <c>best_skill</c>,
/// and the attribute queries' own value). The values are bytes, as in the original.
/// </summary>
// orig: best_member DS:991D
// C: best_value and best_skill are unsigned chars, best_member a char, DS:9C1D an unsigned char: the stored values are bytes.
public sealed class PartyQueryResult
{
    /// <summary>The member found (<c>best_member</c>).</summary>
    public int Member { get; set; }

    /// <summary>The skill value found (<c>best_value</c>).</summary>
    public int Value { get; set; }

    /// <summary>The skill found by the best-skill queries (<c>best_skill</c>); also the pack slot of the last equipment lookup.</summary>
    public int Skill { get; set; }

    /// <summary>The attribute value found by the attribute queries (DS:9C1D).</summary>
    public int AttributeValue { get; set; }

    /// <summary>
    /// The member found by the min/max attribute queries (DS:9A44, set beside <c>best_member</c>
    /// by <c>party_min_attr</c>, R029 1696, <c>party_max_attr</c> and R029 174a only).
    /// </summary>
    public int AttributeMember { get; set; }
}
