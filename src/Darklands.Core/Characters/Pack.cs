using System.Collections;

namespace Darklands.Core.Characters;

/// <summary>
/// A member's pack as the original keeps it: 64 six-byte entries (<c>data-file-handle[who].items</c>,
/// 2E38:003E + who × 0x180) and a signed-char stack count (<c>nitems</c>, record +0x7E). Only the
/// first <see cref="Count"/> entries are stacks; the entries after them keep whatever was last
/// there (deleting a stack shifts the later entries down and leaves the old last entry where it
/// was), and the original's list screens and <c>item-keep-percent</c> read and write them.
/// </summary>
// orig: inventories / data-file-handle[who].items 2E38:003E (0x180 bytes per member); nitems = party[who] +0x6A (record +0x7E), a char
// C: the five packs follow each other, so entry -1 of member k is entry 63 of member k - 1 (2E38:0038..003D, zero, for member 0)
// C: and entry 64 of member 4 is the first six formula bytes of member 0 (2E38:07BE); see Party.EntryOutside.
public sealed class Pack : IReadOnlyList<ItemStack>
{
    public const int Size = 64;

    /// <summary>An entry never written: code 0, type 0, quality 0, count 0, weight 0.</summary>
    public static readonly ItemStack Zero = new(0, 0, 0, 0, 0);

    private readonly ItemStack[] _entries = Enumerable.Repeat(Zero, Size).ToArray();

    /// <summary>The party slot this pack was last reached through (for entries −1 and 64), or null.</summary>
    internal (Party Party, int Slot)? Home { get; set; }

    /// <summary>The stack count as the original stores it: a signed char (−1 after <c>item-keep-percent</c> on an empty pack).</summary>
    public int Stacks { get; set; }

    /// <summary>The stacks to look at: <see cref="Stacks"/>, as the loops <c>i &lt; nitems</c> see it (none when negative).</summary>
    public int Count => Math.Clamp(Stacks, 0, Size);

    /// <summary>Entry <paramref name="index"/> (0..63), whether it is a stack or a stale entry past the last one.</summary>
    public ItemStack this[int index]
    {
        get => index is >= 0 and < Size ? _entries[index] : Outside(index);
        set
        {
            if (index is >= 0 and < Size)
            {
                _entries[index] = value;
            }
            else
            {
                SetOutside(index, value);
            }
        }
    }

    /// <summary>All 64 entries, stacks and stale ones (for the records).</summary>
    public IReadOnlyList<ItemStack> Entries => _entries;

    /// <summary>
    /// Appends a stack the way <c>give-item-record-form</c> does: written at entry <see cref="Stacks"/> while
    /// that is below 64 (a count of −1 writes entry −1, outside this pack), then the count goes up.
    /// </summary>
    // orig: give-item-record-inner R031+0x4A04 (18E7:01B4) draft
    // C: `if (nitems < 0x40) { items[nitems] = it; nitems++; }` with nitems a signed char.
    public void Add(ItemStack stack)
    {
        if (Stacks < Size)
        {
            this[Stacks] = stack;
            Stacks = (sbyte)(Stacks + 1);
        }
    }

    /// <summary>Appends each stack (<see cref="Add"/>).</summary>
    public void AddRange(IEnumerable<ItemStack> stacks)
    {
        foreach (var st in stacks)
        {
            Add(st);
        }
    }

    /// <summary>
    /// Deletes entry <paramref name="index"/> as <c>item-erase</c> does: entries index+1 .. Stacks
    /// move down one (the last move reads the stale entry after the stacks, entry 64 of a full
    /// pack being outside it), then the count goes down, whatever the index was.
    /// </summary>
    // orig: item-erase R031+0x4C56 (18E7:0406) matched
    // C: `for (i = idx; i < nitems; i++) items[i] = items[i + 1]; nitems--;` (an index at or past the count copies nothing).
    public void RemoveAt(int index)
    {
        for (var i = index; i < Stacks; i++)
        {
            this[i] = this[i + 1];
        }
        Stacks = (sbyte)(Stacks - 1);
    }

    /// <summary>
    /// Removes stack <paramref name="index"/> the way the battle's pack helpers do: only the stacks after it
    /// (index+1 .. Stacks − 1) move down one, so the old last stack stays where it was as a stale entry, and the
    /// count goes down (unlike <see cref="RemoveAt"/>, nothing is read past the last stack).
    /// </summary>
    // orig: R014+0xB434 (18BF:0FA4) and R014+0xB244 (18BF:0DB4) draft, checked against the asm: `if (k < nitems - 1)
    // for (n = nitems - k - 1, p = &items[k]; n != 0; n--, p++) p[0] = p[1]; nitems--;`; tests: CombatantPackTests
    // C: nitems is a signed char (cbw), k an int: the shift runs only when nitems − 1 > k (signed).
    public void RemoveInBattle(int index)
    {
        if (index < Stacks - 1)
        {
            for (int n = Stacks - index - 1, p = index; n != 0; n--, p++)
            {
                this[p] = this[p + 1];
            }
        }
        Stacks = (sbyte)(Stacks - 1);
    }

    /// <summary>The first stack (0 .. Count − 1) matching, or −1.</summary>
    public int FindIndex(Predicate<ItemStack> match)
    {
        for (var i = 0; i < Count; i++)
        {
            if (match(_entries[i]))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>Copies another pack's 64 entries and count (a record read into a party slot).</summary>
    public void CopyFrom(Pack other)
    {
        Array.Copy(other._entries, _entries, Size);
        Stacks = other.Stacks;
    }

    /// <summary>Empties the pack: count 0, every entry zero (a pack made from nothing).</summary>
    public void Clear()
    {
        Array.Fill(_entries, Zero);
        Stacks = 0;
    }

    public IEnumerator<ItemStack> GetEnumerator()
    {
        for (var i = 0; i < Count; i++)
        {
            yield return _entries[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private ItemStack Outside(int index) => Home is { } h ? h.Party.EntryOutside(h.Slot, index) : Zero;

    private void SetOutside(int index, ItemStack value)
    {
        if (Home is { } h)
        {
            h.Party.SetEntryOutside(h.Slot, index, value);
        }
    }
}
