using Darklands.Core.Battles;
using Darklands.Core.Battles.Tactics;

namespace Darklands.Battles.Tests.BattleCalls;

/// <summary>
/// The loot paired with the original's memory: the loot file (the machine's cfile.dat: 6-byte item records), the piles (20A5:59C8, 12 bytes each, their
/// count DS:C80E and the first not gathered DS:C83E), the chests' offsets in the file (DS:C7A4, twenty words), each member's pile (DS:C81C), the
/// fight-left-loot flag (DS:1B98) and the block of the gathered loot kept apart (DS:D846).
/// </summary>
internal static class LootOverlay
{
    public const int PilesAt = 0x59C8, PileSize = 12, MaxPiles = 180, PileCount = 0xC80E, Gathered = 0xC83E, ChestOffsets = 0xC7A4, MemberPiles = 0xC81C, LootLeft = 0x1B98, GatheredOffset = 0xD846;

    private static int Word(byte[] m, int at) => m[at] | m[at + 1] << 8;

    private static void PutWord(byte[] m, int at, int v)
    {
        m[at] = (byte)v;
        m[at + 1] = (byte)(v >> 8);
    }

    /// <summary>
    /// A pile not yet gathered whose block (offset, count × 6) lies past the loot file's end: the piles' blocks are appended to the file
    /// (R014+0xE774) and cut out of it together (R014+0xE7C8), so a block always lies in it; past the end the original's cut makes the length
    /// negative and writes a 64 KB file.
    /// </summary>
    public static void SkipBlocksPastTheFile(MemoryPair image)
    {
        if (!image.LootKnown)
        {
            return;
        }
        var length = image.Loot?.Length ?? 0;
        var blocks = new List<(int Offset, int Count)>();
        for (var k = image.Ds[Gathered]; k < Math.Min((int)image.Ds[PileCount], MaxPiles); k++)
        {
            var at = PilesAt + PileSize * k;
            var offset = (short)Word(image.Far, at);
            if (offset < 0)
            {
                continue;
            }
            if (offset + 6 * image.Far[at + 2] > length)
            {
                throw new BattleCallsTests.SkipJob($"pile {k}'s block ({offset}, {image.Far[at + 2]} items) past the loot file's {length} bytes: the piles' blocks lie in the file");
            }
            // piles share a block whole (the same offset and count, R014+0x25C6..0x25D1) or have blocks of their own
            if (blocks.Any(b => b.Offset == offset ? b.Count != image.Far[at + 2] : b.Offset < offset + 6 * image.Far[at + 2] && offset < b.Offset + 6 * b.Count))
            {
                throw new BattleCallsTests.SkipJob($"pile {k}'s block ({offset}, {image.Far[at + 2]} items) overlaps another pile's: the piles share a block whole or not at all");
            }
            blocks.Add((offset, image.Far[at + 2]));
        }
    }

    public static void Apply(TacticalBattle t, MemoryPair image)
    {
        var ds = image.Ds;
        var far = image.Far;
        var loot = t.Field.Store.Loot;
        if (image.LootKnown)
        {
            if (image.Loot is null)
            {
                loot.Records.Clear();
                loot.FileExists = false;
            }
            else
            {
                loot.FromFile(image.Loot);
            }
        }
        loot.Piles.Clear();
        for (var k = 0; k < Math.Min((int)ds[PileCount], MaxPiles); k++)
        {
            var at = PilesAt + PileSize * k;
            loot.Piles.Add(new LootPile { Level = far[at + 3], Position = new MapPoint((short)Word(far, at + 4), (short)Word(far, at + 6)), Offset = (short)Word(far, at), Count = far[at + 2], Visible = Word(far, at + 8) != 0 });
        }
        loot.Gathered = ds[Gathered];
        for (var k = 0; k < loot.ChestOffsets.Length; k++)
        {
            loot.ChestOffsets[k] = (short)Word(ds, ChestOffsets + 2 * k);
        }
        for (var k = 0; k < Combatant.PartySlots; k++)
        {
            t.Battle[k].LootPile = (sbyte)ds[MemberPiles + k];
        }
        TacticsOverlay.Set(t, "_lootLeft", ds[LootLeft] != 0);
        TacticsOverlay.Set(t, "_gatheredOffset", (int)(short)Word(ds, GatheredOffset));
    }

    public static void Extract(TacticalBattle t, MemoryPair image)
    {
        var ds = image.Ds;
        var far = image.Far;
        var loot = t.Field.Store.Loot;
        if (image.LootKnown)
        {
            image.Loot = loot.FileExists ? loot.ToFile() : null;
        }
        ds[PileCount] = (byte)loot.Piles.Count;
        for (var k = 0; k < Math.Min(loot.Piles.Count, MaxPiles); k++)
        {
            var at = PilesAt + PileSize * k;
            var p = loot.Piles[k];
            PutWord(far, at, p.Offset);
            far[at + 2] = (byte)p.Count;
            far[at + 3] = (byte)p.Level;
            PutWord(far, at + 4, p.Position.X);
            PutWord(far, at + 6, p.Position.Y);
            PutWord(far, at + 8, p.Visible ? 1 : 0);
        }
        ds[Gathered] = (byte)loot.Gathered;
        for (var k = 0; k < loot.ChestOffsets.Length; k++)
        {
            PutWord(ds, ChestOffsets + 2 * k, loot.ChestOffsets[k]);
        }
        for (var k = 0; k < Combatant.PartySlots; k++)
        {
            ds[MemberPiles + k] = (byte)t.Battle[k].LootPile;
        }
        ds[LootLeft] = (byte)(TacticsOverlay.Get<bool>(t, "_lootLeft") ? 1 : 0);
        PutWord(ds, GatheredOffset, TacticsOverlay.Get<int>(t, "_gatheredOffset"));
    }

    public static string? Describe(int ds)
    {
        if (ds == PileCount)
        {
            return "pile count";
        }
        if (ds == Gathered)
        {
            return "first pile not gathered";
        }
        if (ds >= ChestOffsets && ds < ChestOffsets + 40)
        {
            return $"chest offset[{(ds - ChestOffsets) / 2}]";
        }
        if (ds >= MemberPiles && ds < MemberPiles + Combatant.PartySlots)
        {
            return $"member pile[{ds - MemberPiles}]";
        }
        if (ds == LootLeft)
        {
            return "loot left";
        }
        return ds is GatheredOffset or GatheredOffset + 1 ? "gathered loot offset" : null;
    }

    public static string? DescribeFar(int at) =>
        at >= PilesAt && at < PilesAt + PileSize * MaxPiles && (at - PilesAt) % PileSize < 10 ? $"pile[{(at - PilesAt) / PileSize}] +{(at - PilesAt) % PileSize}" : null;

    /// <summary>The loot file's difference, or null.</summary>
    public static string? Differences(MemoryPair expected, MemoryPair actual)
    {
        if (!expected.LootKnown)
        {
            return null;
        }
        if (expected.Loot is null || actual.Loot is null)
        {
            return expected.Loot is null && actual.Loot is null ? null : $"loot file: original {(expected.Loot is null ? "absent" : expected.Loot.Length + " bytes")}, rewrite {(actual.Loot is null ? "absent" : actual.Loot.Length + " bytes")}";
        }
        if (expected.Loot.AsSpan().SequenceEqual(actual.Loot))
        {
            return null;
        }
        var n = Math.Min(expected.Loot.Length, actual.Loot.Length);
        var first = Enumerable.Range(0, n).FirstOrDefault(i => expected.Loot[i] != actual.Loot[i], n);
        return $"loot file: original {expected.Loot.Length} bytes, rewrite {actual.Loot.Length}; first difference at {first}";
    }
}
