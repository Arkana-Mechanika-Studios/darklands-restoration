using Darklands.Core.Characters;

namespace Darklands.Core.Battles.Tactics;

/// <summary>
/// The battle's loot file (DS:1162): the item records (6 bytes: item word, type, quality, count, weight) of the piles the
/// fallen left and of the chests, appended at the end and taken out from the middle, so the offsets the piles and chests
/// keep move down when a block before them goes. Kept with the battlefield (a revisit finds its piles and chests).
/// </summary>
// orig: R014+0xE774 (19CD:3204) matched (append), R014+0xE7C8
// (19CD:3258) unmatched (take out), R014+0xE930 (19CD:33C0) unmatched (merge into the work list DS:C654 / DS:C67F)
public sealed class LootStore
{
    /// <summary>The bytes of a record.</summary>
    public const int RecordSize = 6;

    /// <summary>The file's records, in file order.</summary>
    public List<ItemStack> Records { get; } = [];

    /// <summary>loot_piles (far 20A5:59C8, 12-byte records) / loot_pile_count (cleared for a new battlefield).</summary>
    public List<LootPile> Piles { get; } = [];

    /// <summary>DS:C7A4[20]: each chest's offset in the file (by the chest's loot index).</summary>
    public int[] ChestOffsets { get; } = new int[20];

    /// <summary>DS:C83E: the first pile not gathered after a fight yet.</summary>
    public int Gathered { get; set; }

    /// <summary>The file exists (an append or a take has written it since the battlefield was made; it may be empty).</summary>
    // C: the battle start of a new battlefield removes it; R014+0xE774 (19CD:3204) opens it "ab" (creating it), the take writes it back.
    public bool FileExists { get; set; }

    /// <summary>The file's bytes (6 per record: item word, type, quality, count, weight).</summary>
    public byte[] ToFile()
    {
        var data = new byte[Records.Count * RecordSize];
        for (var k = 0; k < Records.Count; k++)
        {
            var r = Records[k];
            var at = k * RecordSize;
            data[at] = (byte)r.Code;
            data[at + 1] = (byte)(r.Code >> 8);
            data[at + 2] = (byte)r.Type;
            data[at + 3] = (byte)r.Quality;
            data[at + 4] = (byte)r.Count;
            data[at + 5] = (byte)r.Weight;
        }
        return data;
    }

    /// <summary>The records of a loot file (a partial record at the end is dropped).</summary>
    public void FromFile(ReadOnlySpan<byte> data)
    {
        Records.Clear();
        for (var at = 0; at + RecordSize <= data.Length; at += RecordSize)
        {
            Records.Add(new ItemStack((short)(data[at] | data[at + 1] << 8), data[at + 2], data[at + 3], data[at + 4], data[at + 5]));
        }
        FileExists = true;
    }

    /// <summary>Appends records; returns their byte offset.</summary>
    // orig: R014+0xE774: fopen "ab", ftell, fwrite(buf, 6, n)
    // C: the offset is ftell cast to an int: past 32767 bytes it would read as negative (never that large in a battle).
    public int Append(IReadOnlyList<ItemStack> records, int n)
    {
        var pos = Records.Count * RecordSize;
        FileExists = true;
        for (var k = 0; k < n && k < records.Count; k++)
        {
            Records.Add(records[k]);
        }
        return (short)pos;
    }

    /// <summary>
    /// Takes the blocks at <paramref name="offsets"/> (counts[k] records each, −1 skipped) out of the file into the work list
    /// (which keeps its first <paramref name="keep"/> entries), merging equal items; the later offsets of the blocks still to
    /// take, of the chests and of the piles move down, and a pile or chest that pointed at a taken block points at −1.
    /// </summary>
    // orig: R014+0xE7C8 (19CD:3258) unmatched
    public void Take(List<ItemStack> work, int keep, int[] counts, int[] offsets, int n)
    {
        // C: the file is read whole and written back (file_save) whatever was taken.
        FileExists = true;
        if (work.Count > keep)
        {
            work.RemoveRange(keep, work.Count - keep);
        }
        for (var k = 0; k < n; k++)
        {
            if (offsets[k] == -1)
            {
                continue;
            }
            var o = offsets[k];
            var c = counts[k] & 0xFF;
            var first = o / RecordSize;
            // C: the block is read at its offset whatever it holds (a stale offset reads the records now there).
            var block = new List<ItemStack>();
            for (var j = 0; j < c && first + j < Records.Count; j++)
            {
                block.Add(Records[first + j]);
            }
            Merge(work, block, block.Count);
            // C: memmove of the rest of the file over the block (size - c * 6 - ofs bytes).
            if (first >= 0 && first < Records.Count)
            {
                Records.RemoveRange(first, Math.Min(c, Records.Count - first));
            }
            var shift = c * RecordSize;
            for (var j = k; j < n; j++)
            {
                if (o < offsets[j])
                {
                    offsets[j] -= shift;
                }
            }
            for (var q = 0; q < ChestOffsets.Length; q++)
            {
                if (ChestOffsets[q] == o)
                {
                    ChestOffsets[q] = -1;
                }
                if (o < ChestOffsets[q])
                {
                    ChestOffsets[q] -= shift;
                }
            }
            foreach (var p in Piles)
            {
                if (p.Offset == o)
                {
                    p.Offset = -1;
                }
                if (o < p.Offset)
                {
                    p.Offset -= shift;
                }
            }
        }
    }

    /// <summary>Merges c records into the work list: the same item, type and quality adds to that entry's count (a byte), else a new entry.</summary>
    // orig: R014+0xE930 (19CD:33C0) unmatched
    public static void Merge(List<ItemStack> work, IReadOnlyList<ItemStack> records, int c)
    {
        for (var r = 0; r < c && r < records.Count; r++)
        {
            var p = records[r];
            var k = work.FindIndex(q => q.Code == p.Code && q.Type == p.Type && q.Quality == p.Quality);
            if (k >= 0)
            {
                // BUG: loot counts wrap at 255: the counts are added in a byte with no limit test, so equal items
                // totalling more than 255 wrap and the excess is lost.
                work[k] = work[k] with { Count = (work[k].Count + p.Count) & 0xFF };
            }
            else
            {
                work.Add(p);
            }
        }
    }
}

/// <summary>A loot pile on the field (far 20A5:59C8, 12-byte records): where it lies and its block in the loot file.</summary>
public sealed class LootPile
{
    public required int Level { get; init; }

    public MapPoint Position { get; set; }

    /// <summary>+0: the block's offset in the loot file; −1 taken (empty), −2 nothing (a body that drops no items).</summary>
    public int Offset { get; set; } = -2;

    /// <summary>+2: the records in the block.</summary>
    public int Count { get; set; }

    /// <summary>+8: drawn (the corpse mode of the animation set).</summary>
    public bool Visible { get; set; }
}
