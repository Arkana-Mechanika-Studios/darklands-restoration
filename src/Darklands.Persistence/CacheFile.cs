using Darklands.Core.Items;
using static Darklands.Persistence.RecordBytes;

namespace Darklands.Persistence;

/// <summary>
/// CACHE.TMP (the end of a saved game) ↔ <see cref="ItemStores"/>: byte 0 the size of the offset
/// area (0x63), byte 1 the number of stores, from byte 2 a word per store (store h at 2h: its
/// offset in the file), then the stores, each a count byte and {item word, quality, count} entries.
/// </summary>
// orig: cache_load R035+0x1350 (1462:1350) and cache_save R035+0x1468 (1462:1468) matched;
// the empty file of a loaded or new game is written by load_saved_game R037+0x4184 (1462:4184) matched
// (0x63, 0, then 0x62 zero words); tests: OriginalSaveTests
// C: cache_save(-1) appends a store at the end of the file and writes its offset at (n + 1) * 2, which is the layout written here: the
// 198-byte header of the empty file, then the stores in handle order. Its rewrite of a known store writes a 101-byte header and finds
// the first store with a signed-char offset (p += (char)*q), wrong for the 198-byte header (the offsets are 198 and up); that layout is
// read here as well (by the offsets), never written.
public static class CacheFile
{
    private const int OffsetAreaSize = 0x63;
    private const int EmptyFileSize = 2 + 0x62 * 2;

    public static ItemStores Read(ReadOnlySpan<byte> file)
    {
        var stores = new ItemStores();
        if (file.Length < 2)
        {
            return stores;
        }
        int count = file[1];
        for (var h = 1; h <= count && 2 * h + 1 < file.Length; h++)
        {
            var at = UWord(file, 2 * h);
            var items = new List<StoredItem>();
            if (at < file.Length)
            {
                int n = file[at];
                for (var i = 0; i < n && at + 1 + 4 * i + 3 < file.Length; i++)
                {
                    var e = file.Slice(at + 1 + 4 * i, 4);
                    items.Add(new StoredItem(UWord(e, 0), e[2], e[3]));
                }
            }
            stores.Add(items);
        }
        return stores;
    }

    public static byte[] Write(ItemStores stores)
    {
        using var out_ = new MemoryStream();
        out_.Write(new byte[EmptyFileSize]);
        var offsets = new List<int>();
        foreach (var store in stores.All)
        {
            offsets.Add((int)out_.Position);
            out_.WriteByte((byte)store.Count);
            foreach (var item in store)
            {
                out_.WriteByte((byte)item.Code);
                out_.WriteByte((byte)(item.Code >> 8));
                out_.WriteByte((byte)item.Quality);
                out_.WriteByte((byte)item.Count);
            }
        }
        var bytes = out_.ToArray();
        bytes[0] = OffsetAreaSize;
        // DIVERGENCE: the file has room for 0x62 offsets and a count byte; the original has no limit check, a 99th store would overwrite its first entry. Here the table stops at 0x62.
        var written = Math.Min(offsets.Count, 0x62);
        bytes[1] = (byte)written;
        for (var h = 1; h <= written; h++)
        {
            PutWord(bytes, 2 * h, offsets[h - 1]);
        }
        return bytes;
    }
}
