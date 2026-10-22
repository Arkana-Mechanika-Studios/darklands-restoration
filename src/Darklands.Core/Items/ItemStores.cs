using Darklands.Core.World;

namespace Darklands.Core.Items;

/// <summary>One entry of an item store: an item code, its quality and how many (a byte, as in the original).</summary>
public sealed record StoredItem(int Code, int Quality, int Count);

/// <summary>
/// The items the party has left at places: the game's CACHE.TMP, kept as its bytes. Byte 0 is
/// the size n of the offset area, byte 1 the number of stores, from byte 2 a word per store
/// (store h at 2h: its offset in the file); a store is a count byte then {item word, quality,
/// count} entries. A place refers to its store by <see cref="Location.CacheHandle"/> (−1: nothing
/// stored there). The file only grows: a store emptied and given up by its place stays in it.
/// </summary>
// orig: cache.tmp, read and written by cache_load R035+0x1350 and cache_save R035+0x1468 (R035) matched; the empty file of a loaded or
// new game is written by load_saved_game R037+0x4184 (1462:4184) matched (0x63, 0, then 0x62 zero words), a saved game's tail copied over
// it by the loader (R037 1462:1C7D, fopen "wb")
public sealed class ItemStores
{
    private const int OffsetAreaSize = 0x63;
    private const int EmptyFileSize = 2 + 0x62 * 2;

    private List<byte> _file = [.. EmptyFile()];

    /// <summary>CACHE.TMP as load_saved_game writes it before a saved game's tail replaces it: 0x63, no store, 0x62 zero offsets.</summary>
    public static byte[] EmptyFile()
    {
        var bytes = new byte[EmptyFileSize];
        bytes[0] = OffsetAreaSize;
        return bytes;
    }

    /// <summary>The file's bytes (what a saved game holds after its places).</summary>
    public byte[] File => [.. _file];

    /// <summary>Replaces the file by a saved game's tail (the loader copies it byte for byte).</summary>
    public void Load(ReadOnlySpan<byte> file) => _file = [.. file];

    /// <summary>The stores by handle − 1, each read as <see cref="Open"/> reads it.</summary>
    public IReadOnlyList<IReadOnlyList<StoredItem>> All =>
        [.. Enumerable.Range(1, _file.Count > 1 ? _file[1] : 0).Select(h => (IReadOnlyList<StoredItem>)(Read(h) ?? []))];

    /// <summary>Copies store <paramref name="handle"/> into the working table (none for −1); the table's entries after it stay.</summary>
    // orig: cache_load R035+0x1350 (1462:1350) matched; tests: TradingAndLootTests
    // C: `q++; q += handle * 2 - 1; p += *q; p += *++q << 8; DS:89F6 = *p++;` then {id word, qual, cnt} per entry (p a far pointer: 16-bit offsets).
    // DORMANT: "No cache file" (cache.tmp missing) prints and exits to DOS; the file is always present here.
    // DIVERGENCE: a store read past the file's end reads the heap after its buffer in the original (handle 0's offset is bytes 0..1, a handle past
    // the table a zero offset word or the stores' bytes); an empty store here.
    public StoreTable Open(int handle, StoreTable table)
    {
        if (handle != -1 && Read(handle) is { } items)
        {
            table.Load(items);
        }
        else
        {
            table.Empty();
        }
        return table;
    }

    /// <summary>The most stores the compact layout holds: the last offset word (bytes 124..125) ends its 126-byte table.</summary>
    public const int MaxStores = 62;

    /// <summary>The most entries a store may hold in the compact layout: the original's rewrite reads a store's count as a signed byte.</summary>
    public const int MaxEntries = 127;

    // The compact layout's table: byte 0 = 124 (the original's rewrite writes byte 0 zero bytes after bytes 0..1), so 126 bytes.
    private const int CompactTableSize = 126;

    private int StoreCount => _file.Count > 1 ? _file[1] : 0;

    /// <summary>Whether the place's handle names a store of the file (1..the count).</summary>
    public bool HasStore(Location place) => place.CacheHandle >= 1 && place.CacheHandle <= StoreCount;

    /// <summary>
    /// Whether one more item (code, quality) may be put into the working table at <paramref name="place"/>. Always so unless
    /// <see cref="OriginalBehaviour.CompactCache"/>: then no new entry past <see cref="MaxEntries"/> and no new store (a place without
    /// one, its table still empty) past <see cref="MaxStores"/> live ones. <paramref name="places"/>: the places that refer to stores
    /// (none given: every store counts as live).
    /// </summary>
    // DIVERGENCE: with OriginalBehaviour.FixCacheStoreLimit (or FixInnCacheRewrite) the original's Put never refuses; here a Put that would need a 63rd live
    // store or a 128th entry of a store is refused when it is made (cache_key, R035 keys 0x1950/0x1970), the item staying in the member's pack, so that
    // closing the screen cannot lose items.
    public bool CanPut(Location place, IReadOnlyList<Location>? places, StoreTable table, int code, int quality, OriginalBehaviour? behaviour)
    {
        if (!(behaviour?.CompactCache ?? false) || table.FindIndex(e => e.Code == code && e.Quality == (byte)quality) >= 0)
        {
            return true;
        }
        if (table.Count >= MaxEntries)
        {
            return false;
        }
        if (!(table.Count != 0 || HasStore(place) || Live(place, places).Count < MaxStores))
        {
            return false;
        }
        // DIVERGENCE: data safety (FixCacheStoreLimit): a new entry or store that no layout of the live stores can hold within 16-bit offsets and
        // a file of at most 0xFFFF bytes is refused too (the file is then only ever written in place, see CloseCompact), the item staying in the pack.
        var stores = LiveStores(place, places, [.. table, new StoredItem(code, quality, 1)]);
        return LayoutTable(stores) > 0;
    }

    private List<List<StoredItem>> LiveStores(Location place, IReadOnlyList<Location>? places, List<StoredItem> items)
    {
        var stores = new List<List<StoredItem>>();
        foreach (var h in Live(place, places))
        {
            stores.Add(HasStore(place) && h == place.CacheHandle ? items : Read(h) ?? []);
        }
        if (!HasStore(place))
        {
            stores.Add(items);
        }
        return stores;
    }

    /// <summary>The offset table size a layout of these stores uses (the 126-byte one if they fit it, else the enlarged one), 0 if none keeps every offset and the file size within 0xFFFF.</summary>
    private static int LayoutTable(List<List<StoredItem>> stores)
    {
        var data = stores.Sum(x => 1 + 4 * x.Count);
        if (stores.Any(x => x.Count > 255) || stores.Count > 254)
        {
            return 0;
        }
        if (stores.Count <= MaxStores && stores.All(x => x.Count <= MaxEntries) && CompactTableSize + data <= 0xFFFF)
        {
            return CompactTableSize;
        }
        var table = Math.Max(EmptyFileSize, 2 * stores.Count + 2);
        return table + data <= 0xFFFF ? table : 0;
    }

    private SortedSet<int> Live(Location place, IReadOnlyList<Location>? places) =>
        places is null
            ? [.. Enumerable.Range(1, StoreCount)]
            : [.. places.Append(place).Where(HasStore).Select(l => l.CacheHandle)];

    /// <summary>
    /// Writes a place's store back when the screen closes: an empty store makes the place
    /// forget it (handle −1); a place without a store gets a new one at the end of the file,
    /// a known store rewrites the file.
    /// </summary>
    // orig: cache_close R035+0x04BC (1462:04BC) and cache_save R035+0x1468 (1462:1468) matched; tests: TradingAndLootTests
    // C: `if (DS:89F6 != 0) cache_save(locations[cur_loc]->cache); else locations[cur_loc]->cache = -1;` (DS:89F0, the count written, is DS:89F6).
    // BUG: OriginalBehaviour.FixInnCacheRewrite - a known store's rewrite (cache_save R035+0x1468) lays the file out with a 101-byte header over offsets of a 198-byte one.
    // BUG: OriginalBehaviour.FixCacheStoreLimit - the append (cache_save(-1) R035+0x1468, the offset written at R035+0x1532) has no limit: the 99th store's offset lands at 198..199, over store 1's count byte and its first item.
    public void Close(Location place, IReadOnlyList<StoredItem> items, OriginalBehaviour? behaviour = null, IReadOnlyList<Location>? places = null)
    {
        if (items.Count == 0)
        {
            place.CacheHandle = -1;
        }
        else if (behaviour?.CompactCache ?? false)
        {
            CloseCompact(place, items, places);
        }
        else if (place.CacheHandle == -1)
        {
            place.CacheHandle = Append(items);
        }
        else
        {
            Rewrite(place.CacheHandle, items);
        }
    }

    // DIVERGENCE: with OriginalBehaviour.FixInnCacheRewrite or FixCacheStoreLimit (data safety, switchable; on in the player's game) the append and the
    // rewrite of a known store (cache_save R035+0x1468) are not made: the stores the places refer to are read by their offsets and the file is laid out
    // compactly, the way the original's own cache_save reads and rewrites it without loss: byte 0 = 124, byte 1 the store count, the offsets from 2 (store h at 2h),
    // store 1 at 126, the stores packed in handle order and nothing after the last. At most 62 stores, 127 entries a store; stores no place refers to are dropped
    // and the places' handles (Location +0x18) renumbered. The original lays out a 198-byte table, whose rewrite loses stores (cbw R035+0x1615), and
    // has no limit (the append at R035+0x1532). Put is refused beyond the limits (CanPut). A file the original itself wrote can already hold more than
    // 62 live stores or a store of more than 127 entries: closing in it never fails and loses nothing; the stores are laid out in
    // a table large enough for them (the original's 198-byte one, or byte 0 = half the table with 2 bytes a store), a file beyond what the original's own
    // rewrite handles, as its own files are. A file whose stores cannot be laid out within 16-bit offsets and 0xFFFF bytes at all is never laid out again, see WriteInPlace. A place's handle that names no store (not -1, past the file's count) has nothing to drop: only the handle
    // is reset to -1, which reads an empty store just as the invalid one does.
    private void CloseCompact(Location place, IReadOnlyList<StoredItem> items, IReadOnlyList<Location>? places)
    {
        var live = Live(place, places);
        var isNew = !HasStore(place);
        var map = new Dictionary<int, int>();
        var stores = new List<List<StoredItem>>();
        foreach (var h in live)
        {
            map[h] = stores.Count + 1;
            stores.Add(Read(h) ?? []);
        }
        var newHandles = new List<(Location Place, int Handle)>();
        foreach (var l in places ?? [])
        {
            newHandles.Add((l, HasStore(l) ? map[l.CacheHandle] : -1));
        }
        if (isNew)
        {
            stores.Add([.. items]);
            newHandles.Add((place, stores.Count));
        }
        else
        {
            stores[map[place.CacheHandle] - 1] = [.. items];
            newHandles.Add((place, map[place.CacheHandle]));
        }
        if (LayoutTable(stores) == 0)
        {
            WriteInPlace(place, items);
            return;
        }
        Lay(stores);
        foreach (var (l, handle) in newHandles)
        {
            l.CacheHandle = handle;
        }
    }

    // DIVERGENCE: data safety (FixCacheStoreLimit): when no layout keeps every store offset and the file size within 16 bits (a file of at most 0xFFFF
    // bytes: the original computes the next store's position, the file size, in 16 bits at cache_save R035+0x1468, so a size of 0x10000 or more wraps to
    // 0 or low), the file is not laid out again. The store the screen is left from is written over its old bytes when it is no longer than they are
    // (a Get only shrinks it, a Put onto an existing entry changes a count; the stale bytes after a shrunk store stay, as in the original's own rewrite,
    // and no offset moves), else nothing is written. Places' handles stay. No offset past 0xFFFF is ever written; Put of a new entry or store is
    // refused (CanPut, Storing.NoRoomMessage). Opened and left without a change, the file is byte-identical.
    private void WriteInPlace(Location place, IReadOnlyList<StoredItem> items)
    {
        if (!HasStore(place))
        {
            return;
        }
        var at = _file[place.CacheHandle * 2] | _file[place.CacheHandle * 2 + 1] << 8;
        if (at < _file.Count && 4 * items.Count <= 4 * _file[at] && at + 1 + 4 * _file[at] <= _file.Count)
        {
            WriteStore(ref at, items);
        }
    }

    private void Lay(List<List<StoredItem>> stores)
    {
        var table = LayoutTable(stores);
        var fits = table == CompactTableSize;
        _file = [.. new byte[table]];
        _file[0] = (byte)(fits ? CompactTableSize - 2 : table / 2);
        _file[1] = (byte)stores.Count;
        var at = table;
        for (var i = 0; i < stores.Count; i++)
        {
            Put((i + 1) * 2, (byte)at);
            Put((i + 1) * 2 + 1, (byte)(at >> 8));
            WriteStore(ref at, stores[i]);
        }
    }

    /// <summary>Adds a store at the end of the file (as a place without one does when its screen closes); returns its handle.</summary>
    public int Add(IEnumerable<StoredItem> items) => Append([.. items]);

    // C: cache_save(-1): `fgetc(fp); ncache = fgetc(fp); fseek(fp, 0L, 2); pos = ftell(fp); fputc(DS:89F0, fp); _dos_write(entries);
    // C: fseek(fp, (long)((ncache + 1) * 2), 0); fputc(pos); fputc(pos >> 8); fseek(fp, 1L, 0); fputc(++ncache, fp); locations[cur_loc]->cache = ncache;`
    // C: no limit: the 99th store's offset lands at 198..199, on the first store's count byte and the low byte of its first item.
    private int Append(IReadOnlyList<StoredItem> items)
    {
        // C: ncache is unsigned: fgetc's EOF (an empty file) is 0xFFFF; the offset's file position is computed in 16 bits.
        var ncache = (ushort)(_file.Count > 1 ? _file[1] : -1);
        var pos = _file.Count;
        var at = pos;
        WriteStore(ref at, items);
        var slot = (ushort)((ncache + 1) * 2);
        Put(slot, (byte)pos);
        Put(slot + 1, (byte)(pos >> 8));
        ncache++;
        Put(1, (byte)ncache);
        return (short)ncache;
    }

    // C: cache_save(handle) on a known store: the file read into a buffer, reopened without truncation (fdopen "wb" on a handle opened for
    // C: writing: bytes past the new end stay), `n = *q++; ncache = *q++; p += (char)*q;`, n and ncache written, n zero bytes, then each store
    // C: i < ncache in turn at the file position reached, its offset written at (i + 1) * 2: store `handle` from the working table
    // C: (`k = *p++; p += k << 2;` skips its old bytes), the others copied from p (`k = *p; _dos_write(p, (k << 2) + 1); p += (k << 2) + 1;`).
    // BUG: the rewrite finds store 1 by the signed low byte of its offset (p = buf + (signed char)buf[2], cbw R035+0x1615 after the load at R035+0x15A2): with store 1 at 0x80 or later (a table of 128 bytes or more, the 198-byte one of a game's file too) the other stores are copied from the wrong bytes; and it reopens the file without truncating it (R035+0x15DD, mode 1), so bytes left after a shrunk store stay and a store appended after them is later copied into an earlier one. The compact layout (126-byte table, store 1 at 126) avoids the first; the second is reproduced.
    // C: The header becomes n + 2 = 101 bytes; p starts at the low byte of store 1's offset taken as a signed char, which for the 198-byte
    // C: header of a game's file (0xC6) is offset 0xFFC6 of the buffer's segment, the heap 58 bytes before it.
    // DIVERGENCE: a store copied from bytes outside the file (that read before the buffer, or past its end) copies heap memory in the original;
    // here such a store is written as its offset reads it. With only the store being saved (one store, the usual first revisit) nothing is copied and
    // the file is the original's; once the header is 101 bytes p starts at store 1 and the copies read the file.
    private void Rewrite(int handle, IReadOnlyList<StoredItem> items)
    {
        var buf = _file.ToArray();
        int n = buf.Length > 0 ? buf[0] : 0;
        int ncache = buf.Length > 1 ? buf[1] : 0;
        int? p = buf.Length > 2 ? (ushort)(sbyte)buf[2] : null;
        Put(0, (byte)n);
        Put(1, (byte)ncache);
        var at = 2;
        for (var i = 0; i < n; i++)
        {
            Put(at++, 0);
        }
        for (var i = 0; i < ncache; i++)
        {
            var pos = at;
            Put((i + 1) * 2, (byte)pos);
            Put((i + 1) * 2 + 1, (byte)(pos >> 8));
            at = pos;
            if (handle - i == 1)
            {
                WriteStore(ref at, items);
                p = p is { } q && q < buf.Length ? (ushort)(q + 1 + (buf[q] << 2)) : null;
            }
            else if (p is { } q && q < buf.Length && q + 1 + (buf[q] << 2) <= buf.Length)
            {
                var size = 1 + (buf[q] << 2);
                for (var b = 0; b < size; b++)
                {
                    Put(at++, buf[q + b]);
                }
                p = (ushort)(q + size);
            }
            else
            {
                WriteStore(ref at, ReadFrom(buf, i + 1) ?? []);
                p = null;
            }
        }
    }

    private void WriteStore(ref int at, IReadOnlyList<StoredItem> items)
    {
        Put(at++, (byte)items.Count);
        foreach (var item in items)
        {
            Put(at++, (byte)item.Code);
            Put(at++, (byte)(item.Code >> 8));
            Put(at++, (byte)item.Quality);
            Put(at++, (byte)item.Count);
        }
    }

    // DIVERGENCE: a write past the file's end (an offset word of a file shorter than its offset area) leaves DOS's undefined bytes before it; zeros here.
    private void Put(int at, byte value)
    {
        while (at >= _file.Count)
        {
            _file.Add(0);
        }
        _file[at] = value;
    }

    private List<StoredItem>? Read(int handle) => ReadFrom([.. _file], handle);

    private static List<StoredItem>? ReadFrom(byte[] file, int handle)
    {
        var slot = (ushort)(handle * 2);
        if (slot + 1 >= file.Length)
        {
            return null;
        }
        var at = file[slot] | file[slot + 1] << 8;
        if (at >= file.Length || at + 1 + 4 * file[at] > file.Length)
        {
            return null;
        }
        var items = new List<StoredItem>();
        for (var i = 0; i < file[at]; i++)
        {
            var e = at + 1 + 4 * i;
            items.Add(new StoredItem(file[e] | file[e + 1] << 8, file[e + 2], file[e + 3]));
        }
        return items;
    }
}
