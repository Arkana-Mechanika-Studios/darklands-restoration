using Darklands.Core.Events;
using Darklands.Core.Time;
using static Darklands.Persistence.RecordBytes;

namespace Darklands.Persistence;

/// <summary>An event's 0x30-byte record (EVENTS.TMP, a saved game) ↔ <see cref="TimedEvent"/>: all 24 words are named.</summary>
// orig: struct event: type0 +00, created hour/day/month/year +02..+08, start +0A..+10, end +12..+18,
// owner f1a, location f1c, value f1e, extra f20, kind +22, flags f24, handler f26, tag f28, data f2a/f2c/f2e, as
// R029+0x3034 (0E76:3034) matched fills them; tests: OriginalSaveTests
public static class EventRecords
{
    public const int Size = 0x30;

    public static TimedEvent Read(ReadOnlySpan<byte> r) => new()
    {
        Type = Word(r, 0x00),
        Created = Date(r, 0x02),
        Start = Date(r, 0x0A),
        End = Date(r, 0x12),
        Owner = Word(r, 0x1A),
        Location = Word(r, 0x1C),
        Value = Word(r, 0x1E),
        Extra = Word(r, 0x20),
        Kind = Word(r, 0x22),
        Flags = (EventFlags)Word(r, 0x24),
        ExpiryHandler = (ExpiryHandler)Word(r, 0x26),
        Tag = Word(r, 0x28),
        Data1 = Word(r, 0x2A),
        Data2 = Word(r, 0x2C),
        Data3 = Word(r, 0x2E),
    };

    public static byte[] Write(TimedEvent e)
    {
        var r = new byte[Size];
        PutWord(r, 0x00, e.Type);
        PutDate(r, 0x02, e.Created);
        PutDate(r, 0x0A, e.Start);
        PutDate(r, 0x12, e.End);
        PutWord(r, 0x1A, e.Owner);
        PutWord(r, 0x1C, e.Location);
        PutWord(r, 0x1E, e.Value);
        PutWord(r, 0x20, e.Extra);
        PutWord(r, 0x22, e.Kind);
        PutWord(r, 0x24, (int)e.Flags);
        PutWord(r, 0x26, (int)e.ExpiryHandler);
        PutWord(r, 0x28, e.Tag);
        PutWord(r, 0x2A, e.Data1);
        PutWord(r, 0x2C, e.Data2);
        PutWord(r, 0x2E, e.Data3);
        return r;
    }

    // C: an event's dates are hour, day, month, year words (the save header's own date is year first).
    private static GameDate Date(ReadOnlySpan<byte> r, int at) =>
        new((short)Word(r, at), (short)Word(r, at + 2), (short)Word(r, at + 4), (short)Word(r, at + 6));

    private static void PutDate(Span<byte> r, int at, GameDate d)
    {
        PutWord(r, at, d.Hour);
        PutWord(r, at + 2, d.Day);
        PutWord(r, at + 4, d.Month);
        PutWord(r, at + 6, d.Year);
    }
}
