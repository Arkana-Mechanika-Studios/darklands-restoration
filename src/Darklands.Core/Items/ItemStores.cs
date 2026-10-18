using Darklands.Core.World;

namespace Darklands.Core.Items;

/// <summary>One entry of an item store: an item code, its quality and how many (a byte, as in the original).</summary>
public sealed record StoredItem(int Code, int Quality, int Count);

/// <summary>
/// The items the party has left at places (the game's CACHE.TMP): a table of stores, each
/// a list of entries, numbered from 1. A place refers to its store by
/// <see cref="Location.CacheHandle"/> (−1: nothing stored there). Like the file, the table
/// only grows: a store emptied and given up by its place keeps its old entries in the table.
/// </summary>
// orig: the store table of cache.tmp, read and written by cache_load R035+0x1350 and cache_save R035+0x1468 (R035) matched
public sealed class ItemStores
{
    private readonly List<List<StoredItem>> _stores = [];

    /// <summary>The stores by handle − 1.</summary>
    public IReadOnlyList<IReadOnlyList<StoredItem>> All => _stores;

    /// <summary>Copies store <paramref name="handle"/> into the working table (none for −1 or an unknown handle); the table's entries after it stay.</summary>
    // orig: cache_load R035+0x1350 (1462:1350) matched; tests: TradingAndLootTests
    // C: cache.tmp: byte 0 = size n of the offset area, byte 1 = number of stores, then from byte 2 a 16-bit offset per store (handle h at 2h); a store is a count byte then {id word, qual, cnt} entries.
    // DORMANT: "No cache file" (cache.tmp missing) prints and exits to DOS; the table is always present here.
    // DIVERGENCE: handle 0 or a handle past the table reads a garbage offset in the original; an empty store here.
    public StoreTable Open(int handle, StoreTable table)
    {
        if (handle >= 1 && handle <= _stores.Count)
        {
            table.Load(_stores[handle - 1]);
        }
        else
        {
            table.Empty();
        }
        return table;
    }

    /// <summary>
    /// Writes a place's store back when the screen closes: an empty store makes the place
    /// forget it (handle −1); a place without a store gets a new one at the end of the table.
    /// </summary>
    // orig: cache_close R035+0x04BC (1462:04BC) and cache_save R035+0x1468 (1462:1468) matched; tests: TradingAndLootTests
    // C: cache_save(-1) appends at the end of the file and writes the new offset at (ncache+1)*2; a known handle rewrites the whole file, copying the other stores in order. The rewrite skips to the first store with a signed-char offset (p += (char)*q), which assumes the header is under 128 bytes.
    // DIVERGENCE: the file layout (offset area size n, the 128-byte assumption, the write-back order) is the persistence layer's concern; only the table semantics are modelled.
    public void Close(Location place, IReadOnlyList<StoredItem> items)
    {
        if (items.Count == 0)
        {
            place.CacheHandle = -1;
        }
        else if (place.CacheHandle >= 1 && place.CacheHandle <= _stores.Count)
        {
            _stores[place.CacheHandle - 1] = [.. items];
        }
        else
        {
            _stores.Add([.. items]);
            place.CacheHandle = _stores.Count;
        }
    }

    /// <summary>Adds a store as read from a saved game (persistence); returns its handle.</summary>
    public int Add(IEnumerable<StoredItem> items)
    {
        _stores.Add([.. items]);
        return _stores.Count;
    }
}
