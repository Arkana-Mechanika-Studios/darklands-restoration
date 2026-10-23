using Darklands.Core.World;
using static Darklands.Persistence.RecordBytes;

namespace Darklands.Persistence;

/// <summary>A place's 0x3A-byte record (DARKLAND.LOC, LOCS.TMP, a saved game) ↔ <see cref="Location"/>.</summary>
// orig: struct location as WorldData.ToLocation reads DARKLAND.LOC (icon +0x00, x +0x04, y +0x06, +0x08, +0x0A, cards +0x0C/+0x0E,
// size +0x11, reputation +0x12, flags +0x14, local values +0x15..+0x17, cache +0x18, mark +0x1A, recorded words +0x1C..+0x21,
// name +0x26); a saved game holds the 414 records as R037+0x2ACE writes locations[i]; tests: OriginalSaveTests
// C: +0x02, +0x10, +0x22..+0x25 and the bytes after the name's zero are not named; they are kept as read (Location.OriginalRecord).
public static class LocationRecords
{
    public const int Size = 0x3A;

    private const int NameAt = 0x26;
    private const int NameLength = 20;

    public static Location Read(int index, ReadOnlySpan<byte> r)
    {
        var l = new Location
        {
            Index = index,
            OriginalRecord = r[..Size].ToArray(),
            Icon = UWord(r, 0x00),
            X = UWord(r, 0x04),
            Y = UWord(r, 0x06),
            NoticeRange = UWord(r, 0x08),
            Danger = UWord(r, 0x0A),
            ArrivalCard = UWord(r, 0x0C),
            EncounterCard = UWord(r, 0x0E),
            Size = (sbyte)r[0x11],
            Reputation = Word(r, 0x12),
            Flags = r[0x14],
            CacheHandle = Word(r, 0x18),
            Mark = Word(r, 0x1A),
            Name = Text(r, NameAt, NameLength),
        };
        for (var k = 0; k < 3; k++)
        {
            l.LocalValues[k] = (sbyte)r[0x15 + k];
            l.Recorded[k] = Word(r, 0x1C + 2 * k);
        }
        return l;
    }

    public static byte[] Write(Location l)
    {
        var r = l.OriginalRecord is { Length: Size } original ? (byte[])original.Clone() : new byte[Size];
        PutWord(r, 0x00, l.Icon);
        PutWord(r, 0x04, l.X);
        PutWord(r, 0x06, l.Y);
        PutWord(r, 0x08, l.NoticeRange);
        PutWord(r, 0x0A, l.Danger);
        PutWord(r, 0x0C, l.ArrivalCard);
        PutWord(r, 0x0E, l.EncounterCard);
        r[0x11] = (byte)l.Size;
        PutWord(r, 0x12, l.Reputation);
        r[0x14] = (byte)l.Flags;
        for (var k = 0; k < 3; k++)
        {
            r[0x15 + k] = (byte)l.LocalValues[k];
            PutWord(r, 0x1C + 2 * k, l.Recorded[k]);
        }
        PutWord(r, 0x18, l.CacheHandle);
        PutWord(r, 0x1A, l.Mark);
        PutText(r, NameAt, NameLength, l.Name);
        return r;
    }
}
