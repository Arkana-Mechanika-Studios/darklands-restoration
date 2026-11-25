using System.IO.Compression;
using System.Text;

namespace Darklands.Platform.Recording;

/// <summary>What one entry of the tape is: a value the host gave the game (by the call that asked for it), or a note.</summary>
// Not in the original: R-1, the input recording of a problem report (docs/REPORTING.md).
public enum TapeKind : byte
{
    /// <summary>The game pumped the host's events (no value; notes made during the pump follow it).</summary>
    Pump = 1,
    QuitRequested = 2,
    /// <summary>TryReadKey: 0 for none, else the key + 1.</summary>
    Key = 3,
    /// <summary>The mouse: x | y &lt;&lt; 16 | buttons &lt;&lt; 32.</summary>
    Mouse = 4,
    Shift = 5,
    Ctrl = 6,
    /// <summary>TryReadKeyTransition: 0 for none, else (scan code &lt;&lt; 1 | released) + 1.</summary>
    KeyTransition = 7,
    CtrlBreak = 8,
    PointerInWindow = 9,
    ClockMilliseconds = 10,
    TimerTicks = 11,
    TimerFrames = 12,
    RequestTick = 13,
    PlayMusicNow = 14,
    RequestCurrentPiece = 15,
    /// <summary>A seed the game asked for (DarklandsGame.Seed: the clock).</summary>
    Seed = 16,
    /// <summary>The party's tick word of the day the game asked for (DarklandsGame.PartyTicks: the time of day).</summary>
    PartyTicks = 17,
    /// <summary>A note: a setting changed while the game was paused, a console command (text).</summary>
    Note = 30,
    /// <summary>The previous entry again, so many more times (never written by a caller).</summary>
    Repeat = 31,
}

/// <summary>
/// The tape of a recording: every value the host gives the game, in the order the game asks for it, with its kind (so a replay
/// that asks for something else stops at once and says where). Each value is kept as its difference from the last value of its kind,
/// an entry that repeats the one before as a count, and the whole compressed: a typical hour of play is a few megabytes.
/// </summary>
// Not in the original: R-1, the input recording.
public sealed class TapeWriter : IDisposable
{
    private readonly MemoryStream _bytes = new();
    private readonly DeflateStream _deflate;
    private readonly long[] _last = new long[32];
    private TapeKind _previousKind;
    private long _previousDelta;
    private long _repeats;
    private bool _hasPrevious;
    private readonly Lock _gate = new();

    public TapeWriter()
    {
        _deflate = new DeflateStream(_bytes, CompressionLevel.Optimal, leaveOpen: true);
    }

    /// <summary>The entries written so far (each repeat counted).</summary>
    public long Entries { get; private set; }

    /// <summary>The compressed bytes so far (about; the last block is not out yet).</summary>
    public long CompressedBytes
    {
        get
        {
            lock (_gate)
            {
                return _bytes.Length;
            }
        }
    }

    /// <summary>True once <see cref="Limit"/> compressed bytes are reached: nothing more is written (the recording says it was cut).</summary>
    public bool Full { get; private set; }

    /// <summary>The most compressed bytes the tape grows to (default 12 MB: a report's file may be 20 MB; many hours of play).</summary>
    public long Limit { get; init; } = 12L * 1024 * 1024;

    /// <summary>Writes a value of <paramref name="kind"/>.</summary>
    public void Value(TapeKind kind, long value)
    {
        lock (_gate)
        {
            if (Full)
            {
                return;
            }
            var delta = value - _last[(int)kind];
            _last[(int)kind] = value;
            Entries++;
            if (_hasPrevious && kind == _previousKind && delta == _previousDelta)
            {
                _repeats++;
                return;
            }
            FlushRepeats();
            _deflate.WriteByte((byte)kind);
            WriteVarint(_deflate, ZigZag(delta));
            (_previousKind, _previousDelta, _hasPrevious) = (kind, delta, true);
            if (_bytes.Length >= Limit)
            {
                Full = true;
            }
        }
    }

    /// <summary>Writes a note (a setting changed, a console command).</summary>
    public void Note(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        lock (_gate)
        {
            if (Full)
            {
                return;
            }
            FlushRepeats();
            _hasPrevious = false;
            Entries++;
            var bytes = Encoding.UTF8.GetBytes(text);
            _deflate.WriteByte((byte)TapeKind.Note);
            WriteVarint(_deflate, (ulong)bytes.Length);
            _deflate.Write(bytes);
        }
    }

    /// <summary>The tape so far, complete (a copy; the writer goes on).</summary>
    public byte[] Snapshot()
    {
        lock (_gate)
        {
            FlushRepeats();
            _hasPrevious = false;
            _deflate.Flush();
            // a sync flush ends the data so far on a byte boundary; the reader stops at the end of the bytes
            return _bytes.ToArray();
        }
    }

    public void Dispose()
    {
        _deflate.Dispose();
        _bytes.Dispose();
    }

    private void FlushRepeats()
    {
        if (_repeats > 0)
        {
            _deflate.WriteByte((byte)TapeKind.Repeat);
            WriteVarint(_deflate, (ulong)_repeats);
            _repeats = 0;
        }
    }

    internal static ulong ZigZag(long value) => (ulong)((value << 1) ^ (value >> 63));

    internal static long UnZigZag(ulong value) => (long)(value >> 1) ^ -(long)(value & 1);

    internal static void WriteVarint(Stream stream, ulong value)
    {
        while (value >= 0x80)
        {
            stream.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }
        stream.WriteByte((byte)value);
    }
}

/// <summary>A value the replay asked for that is not the tape's next one: the game went another way than in the recording.</summary>
public sealed class TapeMismatchException(string message) : Exception(message);

/// <summary>The end of the tape: everything recorded has been replayed.</summary>
public sealed class TapeEndedException() : Exception("the recording ends here");

/// <summary>Reads a tape back, value by value, in the order it was written (<see cref="TapeWriter"/>).</summary>
// Not in the original: R-1, the input recording.
public sealed class TapeReader
{
    private readonly Stream _stream;
    private readonly long[] _last = new long[32];
    private TapeKind _repeatKind;
    private long _repeatDelta;
    private long _repeatsLeft;
    private (TapeKind Kind, long Delta, string? Note)? _peeked;

    public TapeReader(byte[] tape)
    {
        ArgumentNullException.ThrowIfNull(tape);
        _stream = new DeflateStream(new MemoryStream(tape), CompressionMode.Decompress);
    }

    /// <summary>The entries read so far.</summary>
    public long Position { get; private set; }

    /// <summary>True when every entry has been read.</summary>
    public bool AtEnd => Peek() is null;

    /// <summary>The next value, which must be of <paramref name="kind"/>.</summary>
    public long Value(TapeKind kind)
    {
        var next = Peek() ?? throw new TapeEndedException();
        if (next.Kind != kind)
        {
            // a developer's aid: DARKLANDS_TAPE_STACK=1 adds where the game asked from
            var stack = Environment.GetEnvironmentVariable("DARKLANDS_TAPE_STACK") == "1" ? Environment.NewLine + Environment.StackTrace : "";
            throw new TapeMismatchException((next.Kind == TapeKind.Note
                ? $"entry {Position}: the game asked for {kind}, the recording has a note here (\"{next.Note}\")"
                : $"entry {Position}: the game asked for {kind}, the recording has {next.Kind}") + stack);
        }
        Take();
        _last[(int)kind] += next.Delta;
        return _last[(int)kind];
    }

    /// <summary>The next entry whatever its kind (a tape's listing): its kind, its value (a note's text for a note); null at the end.</summary>
    public (TapeKind Kind, long Value, string? Note)? Next()
    {
        if (Peek() is not { } next)
        {
            return null;
        }
        Take();
        if (next.Kind == TapeKind.Note)
        {
            return (next.Kind, 0, next.Note);
        }
        _last[(int)next.Kind] += next.Delta;
        return (next.Kind, _last[(int)next.Kind], null);
    }

    /// <summary>The next entry's note, when the next entry is one (taken); null otherwise.</summary>
    public string? TakeNote()
    {
        if (Peek() is { Kind: TapeKind.Note } next)
        {
            Take();
            return next.Note;
        }
        return null;
    }

    private void Take()
    {
        _peeked = null;
        Position++;
    }

    private (TapeKind Kind, long Delta, string? Note)? Peek()
    {
        if (_peeked is { } peeked)
        {
            return peeked;
        }
        if (_repeatsLeft > 0)
        {
            _repeatsLeft--;
            return _peeked = (_repeatKind, _repeatDelta, null);
        }
        var b = ReadByteOrEnd();
        if (b < 0)
        {
            return null;
        }
        var kind = (TapeKind)b;
        if (kind == TapeKind.Repeat)
        {
            _repeatsLeft = (long)ReadVarint();
            return Peek();
        }
        if (kind == TapeKind.Note)
        {
            var length = (int)ReadVarint();
            var bytes = new byte[length];
            _stream.ReadExactly(bytes);
            return _peeked = (kind, 0, Encoding.UTF8.GetString(bytes));
        }
        var delta = TapeWriter.UnZigZag(ReadVarint());
        (_repeatKind, _repeatDelta) = (kind, delta);
        return _peeked = (kind, delta, null);
    }

    private int ReadByteOrEnd()
    {
        try
        {
            return _stream.ReadByte();
        }
        catch (InvalidDataException)
        {
            // a tape cut inside its last block (a report made while writing): it ends there
            return -1;
        }
    }

    private ulong ReadVarint()
    {
        ulong value = 0;
        for (var shift = 0; ; shift += 7)
        {
            var b = _stream.ReadByte();
            if (b < 0)
            {
                throw new TapeEndedException();
            }
            value |= (ulong)(b & 0x7F) << shift;
            if (b < 0x80)
            {
                return value;
            }
        }
    }
}
