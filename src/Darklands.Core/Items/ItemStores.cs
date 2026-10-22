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

    /// <summary>
    /// Writes a place's store back when the screen closes: an empty store makes the place
    /// forget it (handle −1); a place without a store gets a new one at the end of the file,
    /// a known store rewrites the file.
    /// </summary>
    // orig: cache_close R035+0x04BC (1462:04BC) and cache_save R035+0x1468 (1462:1468) matched; tests: TradingAndLootTests
    // C: `if (DS:89F6 != 0) cache_save(locations[cur_loc]->cache); else locations[cur_loc]->cache = -1;` (DS:89F0, the count written, is DS:89F6).
    // BUG: OriginalBehaviour.FixInnCacheRewrite - a known store's rewrite (cache_save R035+0x1468) lays the file out with a 101-byte header over offsets of a 198-byte one.
    public void Close(Location place, IReadOnlyList<StoredItem> items, OriginalBehaviour? behaviour = null)
    {
        var fix = behaviour?.FixInnCacheRewrite ?? false;
        if (items.Count == 0)
        {
            place.CacheHandle = -1;
        }
        else if (place.CacheHandle == -1)
        {
            if (fix && Normalise() is { } stores && stores.Count < 0x62)
            {
                stores.Add([.. items]);
                Lay(stores);
                place.CacheHandle = stores.Count;
            }
            else
            {
                place.CacheHandle = Append(items);
            }
        }
        else if (fix && Normalise() is { } known && place.CacheHandle >= 1 && place.CacheHandle <= known.Count)
        {
            known[place.CacheHandle - 1] = [.. items];
            Lay(known);
        }
        else
        {
            Rewrite(place.CacheHandle, items);
        }
    }

    // DIVERGENCE: with OriginalBehaviour.FixInnCacheRewrite (data safety, switchable; on in the player's game) the rewrite of a known store
    // and the append read every store by its offset and write the file again as load_saved_game R037+0x4184 and cache_save(-1) R035+0x1468 lay
    // it out. The stores read by their offsets, then the file laid out as the original lays out a game's file: the 198-byte header
    // (0x63, the count, 0x62 offset words), the stores after it in handle order, every offset where its store is (what the
    // original's own cache_load reads). Null when the file cannot be read that way (more stores than offset words).
    private List<List<StoredItem>>? Normalise()
    {
        var count = _file.Count > 1 ? _file[1] : 0;
        if (count > 0x62)
        {
            return null;
        }
        return [.. Enumerable.Range(1, count).Select(h => Read(h) ?? [])];
    }

    private void Lay(List<List<StoredItem>> stores)
    {
        _file = [.. EmptyFile()];
        var at = EmptyFileSize;
        for (var i = 0; i < stores.Count; i++)
        {
            Put((i + 1) * 2, (byte)at);
            Put((i + 1) * 2 + 1, (byte)(at >> 8));
            WriteStore(ref at, stores[i]);
        }
        Put(1, (byte)stores.Count);
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
