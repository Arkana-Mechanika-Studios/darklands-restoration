using System.Collections;

namespace Darklands.Core.Items;

/// <summary>
/// The working table of the item store screen (<c>R136+0x11124</c>, 100 four-byte entries, and its
/// count <c>DS:89F6</c>): <c>cache_load</c> copies a store into it without clearing the entries
/// after the store's, <c>cache_take</c> closes a gap by copying the entry after the last one
/// down, and the screen reads entries past the count when a row past the end is lit. So the
/// entries after the count are what earlier stores left in this run (zero before any).
/// </summary>
// orig: R136+0x11124 (2E38:37F4) and DS:89F6 / DS:89F0 (R035 matched): cache_load R035+0x1350, cache_add R035+0x10D2, cache_take R035+0x114A
// C: entries past the 100th (a store of more than 100 different stacks) go on past the table in the original, over the new character's
// starting equipment (2E38:3984, 0x180 bytes, refilled before every use by the character generator) and are read back from there:
// up to 196 entries this is the same as keeping them in the table.
// DIVERGENCE: past 196 entries the original writes over what follows the starting equipment (2E38:3B04 on, not traced); here the table keeps them.
public sealed class StoreTable : IReadOnlyList<StoredItem>
{
    public const int Size = 100;

    public static readonly StoredItem Zero = new(0, 0, 0);

    private readonly List<StoredItem> _entries = [.. Enumerable.Repeat(Zero, Size)];

    // C: a negative index (the store's lit row -1: Down with an empty store, R035+0x0A46 case 0x5000, lowers the row from 0) addresses
    // the bytes before the table (16-bit offsets): entry -1 is 2E38:37F0..37F3, the last of the 66 far pointers at 2E38:36EC that
    // R043+0x0100 fills with the DARKLAND.LST names (offset word = code, the segment's low byte = quality, high byte = count).
    // DIVERGENCE: those bytes are a heap address in the original, unknown here: the entries before the table start as zero; what a G
    // writes there is kept, as the original keeps it.
    private readonly Dictionary<int, StoredItem> _before = [];

    /// <summary>The number of entries in use (<c>DS:89F6</c>).</summary>
    public int Count { get; private set; }

    /// <summary>Entry <paramref name="index"/>, in use or left from an earlier store (negative: the bytes before the table).</summary>
    public StoredItem this[int index]
    {
        get => index < 0 ? _before.GetValueOrDefault(index, Zero) : index < _entries.Count ? _entries[index] : Zero;
        set
        {
            if (index < 0)
            {
                _before[index] = value;
                return;
            }
            while (index >= _entries.Count)
            {
                _entries.Add(Zero);
            }
            _entries[index] = value;
        }
    }

    /// <summary>A table holding <paramref name="items"/> (tests and tools).</summary>
    public static StoreTable Of(IEnumerable<StoredItem> items)
    {
        var t = new StoreTable();
        t.Load(items);
        return t;
    }

    /// <summary>Copies a store into the first entries (<c>cache_load</c>); the entries after it stay.</summary>
    public void Load(IEnumerable<StoredItem> items)
    {
        var i = 0;
        foreach (var e in items)
        {
            this[i++] = e;
        }
        Count = i;
    }

    /// <summary>No store (<c>cache_load(-1)</c>: count 0, the entries stay).</summary>
    public void Empty() => Count = 0;

    /// <summary>A new entry after the last one (<c>cache_add</c>).</summary>
    public void Add(StoredItem e)
    {
        this[Count] = e;
        Count++;
    }

    /// <summary>Removes entry <paramref name="index"/> as <c>cache_take</c> does: the entries after it move down, the stale one after the last too.</summary>
    // C: `for (i = idx; i < DS:89F6; i++) e[i] = e[i + 1]; DS:89F0 = --DS:89F6;` (unsigned indexes: R035+0x116A
    // `cmp bx, dx; jb`, so a negative index moves nothing and the count still drops).
    public void RemoveAt(int index)
    {
        for (var i = index; (uint)i < (uint)Count; i++)
        {
            this[i] = this[i + 1];
        }
        Count--;
    }

    /// <summary>The first entry in use matching, or −1.</summary>
    public int FindIndex(Predicate<StoredItem> match)
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

    public IEnumerator<StoredItem> GetEnumerator()
    {
        for (var i = 0; i < Count; i++)
        {
            yield return _entries[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
