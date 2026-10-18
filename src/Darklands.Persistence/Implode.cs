namespace Darklands.Persistence;

/// <summary>
/// The compressor of the PKWARE Data Compression Library 1.02 ("implode"), as the original links it into
/// its battle-state overlay: the battle file's blocks are written with it, so its exact choices (the
/// pair-hash sort of the dictionary, the repetition search with its partial-match table, the one-step
/// lazy match) decide the file's bytes.
/// </summary>
/// <remarks>
/// The work area is one block of memory laid out as the library lays it out, kept from one call to the
/// next as the original keeps its buffer (the load buffer at DS:C103, + 0x4E20): the search may compare a few bytes
/// past the end of the data, and those bytes are what the earlier blocks left there.
/// </remarks>
// orig: R003+0x1EC6 (1016:04C6) asm (the far pascal entry: R003+0x1A86 (1016:0086) records whether the processor moves SP before
// pushing it, for the stack switch of the search; then R003+0x1E5E); tests: ImplodeOracleTests, OriginalSaveTests
// orig: R003+0x25D6 (1016:0BD6) asm (the library's copy: words, a leading byte when the target is odd) and R003+0x25FE (1016:0BFE)
// asm (its fill) are Array.Copy and Array.Clear here (the copies never overlap backwards).
public sealed class Implode
{
    /// <summary>The size of the library's work area for implode.</summary>
    public const int WorkSize = 0x89B8;

    /// <summary>Reads up to <paramref name="size"/> bytes into the work area at <paramref name="at"/>; returns how many.</summary>
    public delegate int ReadBlock(byte[] buffer, int at, int size);

    /// <summary>Takes <paramref name="size"/> compressed bytes at <paramref name="at"/>.</summary>
    public delegate void WriteBlock(byte[] buffer, int at, int size);

    // The work area (struct offsets of the library's state; every pointer in it is an offset in this block).
    private const int Distance = 0x00;
    private const int OutBytes = 0x02;
    private const int OutBits = 0x04;
    private const int DsizeBits = 0x06;
    private const int DsizeMask = 0x08;
    private const int Ctype = 0x0A;
    private const int DsizeBytes = 0x0C;
    private const int DistBitsAt = 0x0E;
    private const int DistCodesAt = 0x4E;
    private const int ChBitsAt = 0x8E;
    private const int ChCodesAt = 0x394;
    private const int PairHashIndexAt = 0x9A8;
    private const int OutBuffAt = 0x1BAA;
    private const int WorkBuffAt = 0x23AC;
    private const int PairHashOffsetsAt = 0x45B0;

    /// <summary>The end code: literal 0x305.</summary>
    private const int EndCode = 0x305;

    private readonly byte[] _w = new byte[WorkSize];

    /// <summary>DS:6F12: the partial-match table of the repetition search (0x204 words, in DGROUP, not in the work area).</summary>
    private readonly ushort[] _next = new ushort[0x204];

    private ReadBlock _read = (_, _, _) => 0;
    private WriteBlock _write = (_, _, _) => { };

    /// <summary>A compressor whose work area starts as zeros.</summary>
    public Implode()
    {
    }

    /// <summary>A compressor whose work area starts with <paramref name="workArea"/> (what the memory held before; the rest zeros).</summary>
    public Implode(ReadOnlySpan<byte> workArea)
    {
        workArea[..Math.Min(workArea.Length, WorkSize)].CopyTo(_w);
    }

    /// <summary>
    /// Compresses everything <paramref name="read"/> gives into what <paramref name="write"/> takes: type 0 binary
    /// (literals as 8 bits) or 1 ASCII (Huffman-coded literals), dictionary 0x400, 0x800 or 0x1000 bytes.
    /// Returns 0, or 1 for another dictionary size, 2 for another type.
    /// </summary>
    // orig: R003+0x1E5E (1016:045E) asm (implode: the work area's fields, the literal and length code tables, then the compression)
    public int Compress(ReadBlock read, WriteBlock write, int type, int dictionarySize)
    {
        _read = read;
        _write = write;
        SetWord(DsizeBytes, dictionarySize);
        SetWord(Ctype, type);
        SetWord(DsizeBits, 4);
        SetWord(DsizeMask, 0x0F);
        switch (dictionarySize & 0xFFFF)
        {
            case 0x400:
                break;
            case 0x800:
                SetWord(DsizeBits, 5);
                SetWord(DsizeMask, 0x1F);
                break;
            case 0x1000:
                SetWord(DsizeBits, 6);
                SetWord(DsizeMask, 0x3F);
                break;
            default:
                return 1;
        }
        switch (type & 0xFFFF)
        {
            case 0:
                for (var i = 0; i < 0x100; i++)
                {
                    _w[ChBitsAt + i] = 9;
                    SetWord(ChCodesAt + 2 * i, 2 * i);
                }
                break;
            case 1:
                for (var i = 0xFF; i >= 0; i--)
                {
                    _w[ChBitsAt + i] = (byte)(DclTables.ChBitsAsc[i] + 1);
                    SetWord(ChCodesAt + 2 * i, DclTables.ChCodeAsc[i] << 1);
                }
                break;
            default:
                return 2;
        }
        // The length codes 0x100..0x305: a set flag bit, the length code, then its extra bits.
        var n = 0x100;
        for (var k = 0; k < 0x10; k++)
        {
            var count = 1 << DclTables.ExLenBits[k];
            for (var extra = 0; extra < count; extra++, n++)
            {
                _w[ChBitsAt + n] = (byte)(DclTables.LenBits[k] + 1 + DclTables.ExLenBits[k]);
                SetWord(ChCodesAt + 2 * n, (((extra << DclTables.LenBits[k]) | DclTables.LenCode[k]) << 1) + 1);
            }
        }
        DclTables.DistCode.CopyTo(_w, DistCodesAt);
        DclTables.DistBits.CopyTo(_w, DistBitsAt);
        WriteCompressed();
        return 0;
    }

    /// <summary>The compression loop: 0x1000 bytes at a time behind a dictionary of the last bytes, a repetition search at each byte.</summary>
    // orig: R003+0x1FA6 (1016:05A6) asm
    private void WriteCompressed()
    {
        var input = WorkBuffAt + Word(DsizeBytes) + 0x204;
        _w[OutBuffAt] = _w[Ctype];
        _w[OutBuffAt + 1] = _w[DsizeBits];
        SetWord(OutBytes, 2);
        Array.Clear(_w, OutBuffAt + 2, 0x800);
        var phase = 0;
        var ended = false;
        SetWord(OutBits, 0);
        while (true)
        {
            int toLoad = 0x1000, total = 0;
            while (true)
            {
                var loaded = _read(_w, WorkBuffAt + Word(DsizeBytes) + 0x204 + total, toLoad);
                if (loaded == 0)
                {
                    if (total == 0 && phase == 0)
                    {
                        WriteEnd();
                        return;
                    }
                    ended = true;
                    break;
                }
                total += loaded;
                toLoad -= loaded;
                if (toLoad == 0)
                {
                    break;
                }
            }
            var end = WorkBuffAt + Word(DsizeBytes) + total + (ended ? 0x204 : 0);
            switch (phase)
            {
                case 0:
                    if (Word(DsizeBytes) != 0x1000)
                    {
                        phase++;
                    }
                    SortPairs(input, end + 1);
                    phase++;
                    break;
                case 1:
                    SortPairs(input + 0x204 - Word(DsizeBytes), end + 1);
                    phase++;
                    break;
                default:
                    SortPairs(input - Word(DsizeBytes), end + 1);
                    break;
            }
            var at = input;
            while (end > at)
            {
                var length = FindRepetition(at);
            Decide:
                if (length == 0 || (length == 2 && Word(Distance) >= 0x100))
                {
                    goto Literal;
                }
                if (ended && at + length > end)
                {
                    // C: at the end of the data a repetition that runs past it is cut to what is left.
                    length = end - at;
                    if (length < 2)
                    {
                        goto Literal;
                    }
                    if (length != 2 || Word(Distance) < 0x100)
                    {
                        goto Repetition;
                    }
                    goto Literal;
                }
                if (length < 8 && at + 1 < end)
                {
                    // A short repetition: one byte later may start a better one.
                    int savedLength = length, savedDistance = Word(Distance);
                    var next = FindRepetition(at + 1);
                    if (next > savedLength && (savedLength + 1 < next || savedDistance > 0x80))
                    {
                        OutputBits(_w[ChBitsAt + _w[at]], Word(ChCodesAt + 2 * _w[at]));
                        at++;
                        length = next;
                        goto Decide;
                    }
                    length = savedLength;
                    SetWord(Distance, savedDistance);
                }
            Repetition:
                OutputBits(_w[ChBitsAt + length + 0xFE], Word(ChCodesAt + 2 * (length + 0xFE)));
                if (length == 2)
                {
                    var high = Word(Distance) >> 2;
                    OutputBits(_w[DistBitsAt + high], _w[DistCodesAt + high]);
                    OutputBits(2, _w[Distance] & 3);
                }
                else
                {
                    var high = Word(Distance) >> (_w[DsizeBits] & 0x1F);
                    OutputBits(_w[DistBitsAt + high], _w[DistCodesAt + high]);
                    OutputBits(Word(DsizeBits), Word(DsizeMask) & Word(Distance));
                }
                at += length;
                continue;
            Literal:
                OutputBits(_w[ChBitsAt + _w[at]], Word(ChCodesAt + 2 * _w[at]));
                at++;
            }
            if (!ended)
            {
                // C: the dictionary and the look-ahead move down by 0x1000 (a forward copy within the work area).
                Array.Copy(_w, WorkBuffAt + 0x1000, _w, WorkBuffAt, Word(DsizeBytes) + 0x204);
                at -= 0x1000;
            }
            input = at;
            if (ended)
            {
                WriteEnd();
                return;
            }
        }
    }

    private void WriteEnd()
    {
        OutputBits(_w[ChBitsAt + EndCode], Word(ChCodesAt + 2 * EndCode));
        if (Word(OutBits) != 0)
        {
            SetWord(OutBytes, Word(OutBytes) + 1);
        }
        _write(_w, OutBuffAt, Word(OutBytes));
    }

    /// <summary>
    /// The pair-hash index: for each pair of bytes (hash first × 8 + second) the positions where it starts in
    /// [begin, end), in ascending order; each bucket's start is in the index table.
    /// </summary>
    // orig: R003+0x2549 (1016:0B49) asm
    // C: the hash b0 * 8 + b1 is not one-to-one, but with equal first bytes equal hashes mean equal second bytes (the search
    // checks the first byte only). The counting pass reads the byte at end (one past the range).
    private void SortPairs(int begin, int end)
    {
        Array.Clear(_w, PairHashIndexAt, 0x900 * 2);
        var count = (end - begin) & 0xFFFF;
        for (int k = 0, p = begin; k < count || (count == 0 && k < 0x10000); k++, p++)
        {
            var slot = PairHashIndexAt + 2 * (_w[p] * 8 + _w[p + 1]);
            SetWord(slot, Word(slot) + 2);
        }
        var sum = PairHashOffsetsAt;
        for (var h = 0; h < 0x900; h++)
        {
            sum += Word(PairHashIndexAt + 2 * h);
            SetWord(PairHashIndexAt + 2 * h, sum);
        }
        for (int k = 0, p = end; k < count || (count == 0 && k < 0x10000); k++)
        {
            int second = _w[p--], first = _w[p];
            var slot = PairHashIndexAt + 2 * (first * 8 + second);
            SetWord(slot, Word(slot) - 2);
            SetWord(Word(slot), p);
        }
    }

    /// <summary>
    /// The longest earlier repetition of the bytes at <paramref name="input"/> within the dictionary (at most 0x204 bytes;
    /// the nearest of equal ones), its distance − 1 left in the work area; 0 when shorter than 2.
    /// </summary>
    // orig: R003+0x2264 (1016:0864) asm (it runs on the library's own stack, here on the caller's)
    // C: candidates come from the pair's bucket from the first one inside the dictionary (the bucket start is moved up for good)
    // to the last one before input − 1. Up to a length of 14 each is compared in turn; from 14 on the search builds a partial-match
    // table of the repetition (DS:6F12) and skips candidates that cannot do better. Comparisons may run past the data's end.
    private int FindRepetition(int input)
    {
        var best = 1;
        var slot = PairHashIndexAt + 2 * (_w[input] * 8 + _w[input + 1]);
        var p = Word(slot);
        var lowest = (input - Word(DsizeBytes) + 1) & 0xFFFF;
        if (Word(p) < lowest)
        {
            do
            {
                p += 2;
            }
            while (Word(p) < lowest);
            SetWord(slot, p);
        }
        var limit = (input - 1) & 0xFFFF;
        int si, di, cx, bx;
        byte al;
        bool zf;
        int matchedNext, matchedAt, candidate = 0;
        di = Word(p);
        if (di >= limit)
        {
            return 0;
        }
        si = input;
        cx = si;
        bx = best - 1;
        al = _w[bx + si];
        goto Compare;

    Restart:
        bx = best - 1;
        si = input;
        al = _w[bx + si];
        cx = si;
    Resume:
        si = cx;
    NextCandidate:
        p += 2;
        di = Word(p);
        if (di >= limit)
        {
            return best >= 2 ? best : 0;
        }
    Compare:
        if (_w[bx + di] != al)
        {
            goto NextCandidate;
        }
        zf = _w[si] == _w[di];
        si++;
        di++;
        if (!zf)
        {
            goto Resume;
        }
        cx = 0x202;
        si++;
        di++;
        cx = RepeatCompare(ref si, ref di, cx, out zf);
        if (!zf)
        {
            cx++;
        }
        cx = ~(cx - 0x205) & 0xFFFF;
        if (best > cx)
        {
            goto Restart;
        }
        SetWord(Distance, si - 1 - di);
        best = cx;
        if (cx < 0x0E)
        {
            goto Restart;
        }
        if (cx == 0x204)
        {
            return cx;
        }
        if (Word(p + 2) >= limit)
        {
            return best;
        }
        if (Word(p + 4) >= limit)
        {
            goto Restart;
        }
        // The partial-match table of the repetition found: _next[k] = the length of its longest proper border of k bytes.
        si = 0;
        _next[1] = 0;
        _next[0] = 0xFFFF;
        di = 1;
        cx = best;
        do
        {
            al = _w[input + di];
            if (_w[input + si] != al)
            {
                si = _next[si];
                if ((short)si >= 0)
                {
                    continue;
                }
                si &= 0xFFFF;
            }
            si = (si + 1) & 0xFFFF;
            di++;
            _next[di] = (ushort)si;
        }
        while (cx > di);
        matchedNext = si;
        matchedAt = di;
        di = Word(p) + cx + 1;
    Skip:
        di--;
        si = _next[cx];
        if (si == 0xFFFF)
        {
            si = 0;
        }
    SkipCandidate:
        p += 2;
        candidate = Word(p);
        if (candidate >= limit)
        {
            return best;
        }
        if (((candidate + si) & 0xFFFF) < di)
        {
            goto SkipCandidate;
        }
        if (Pair(candidate + best - 2) == Pair(input + best - 2))
        {
            cx = si;
            if (((candidate + cx) & 0xFFFF) != di)
            {
                cx = 0;
                di = candidate;
            }
        }
        else
        {
            bx = best - 2;
            si = input;
            do
            {
                p += 2;
                di = Word(p);
                if (di >= limit)
                {
                    return best;
                }
            }
            while (Pair(bx + di) != Pair(bx + si) || _w[si] != _w[di]);
            cx = 2;
            candidate = di;
            di += 2;
        }
        si = input + cx;
        cx = ~(cx - 0x205) & 0xFFFF;
        cx = RepeatCompare(ref si, ref di, cx, out zf);
        if (!zf)
        {
            cx++;
        }
        cx = ~(cx - 0x205) & 0xFFFF;
        // C: a length above 0x204 would stop at a breakpoint (int 3); it cannot happen.
        if (best > cx)
        {
            goto Skip;
        }
        SetWord(Distance, si - 1 - di);
        if (best >= cx)
        {
            goto Skip;
        }
        if (cx == 0x204)
        {
            return cx;
        }
        best = cx;
        do
        {
            al = _w[input + matchedAt];
            bx = matchedNext;
            if (_w[input + bx] != al)
            {
                bx = _next[bx];
                if ((short)bx >= 0)
                {
                    matchedNext = bx;
                    continue;
                }
                bx &= 0xFFFF;
            }
            bx = (bx + 1) & 0xFFFF;
            matchedAt++;
            _next[matchedAt] = (ushort)bx;
            matchedNext = bx;
        }
        while (cx > matchedAt);
        goto Skip;
    }

    /// <summary><c>repe cmpsb</c>: compares while equal, at most <paramref name="count"/> bytes; the count left.</summary>
    // C: a zero count compares nothing and leaves the flags of the subtraction before it (never zero).
    private int RepeatCompare(ref int si, ref int di, int count, out bool equal)
    {
        equal = false;
        while (count != 0)
        {
            equal = _w[si] == _w[di];
            si++;
            di++;
            count--;
            if (!equal)
            {
                break;
            }
        }
        return count;
    }

    private int Pair(int at) => _w[at] | _w[at + 1] << 8;

    /// <summary>Puts the low <paramref name="count"/> bits of <paramref name="bits"/> into the output, low bit first.</summary>
    // orig: R003+0x24D2 (1016:0AD2) asm
    private void OutputBits(int count, int bits)
    {
        var value = bits & 0xFFFF;
        if (count > 8)
        {
            OutputBits(8, value);
            count -= 8;
            value >>= 8;
        }
        var used = Word(OutBits);
        var at = Word(OutBytes);
        _w[OutBuffAt + at] |= (byte)(value << used);
        var filled = used + count;
        SetWord(OutBits, filled);
        if (filled > 8)
        {
            at++;
            value >>= 8 - used;
            _w[OutBuffAt + at] = (byte)value;
            SetWord(OutBits, filled & 7);
        }
        else
        {
            SetWord(OutBits, filled & 7);
            if ((filled & 7) == 0)
            {
                at++;
            }
        }
        SetWord(OutBytes, at);
        if (at >= 0x800)
        {
            FlushOutput();
        }
    }

    /// <summary>Writes the first 0x800 output bytes and moves the rest (the byte being filled) to the start.</summary>
    // orig: R003+0x2621 (1016:0C21) asm
    private void FlushOutput()
    {
        _write(_w, OutBuffAt, 0x800);
        var filling = _w[OutBuffAt + Word(OutBytes)];
        var after = _w[OutBuffAt + 0x800];
        SetWord(OutBytes, Word(OutBytes) - 0x800);
        Array.Clear(_w, OutBuffAt, 0x802);
        if (Word(OutBytes) != 0)
        {
            _w[OutBuffAt] = after;
        }
        if (Word(OutBits) != 0)
        {
            _w[OutBuffAt + Word(OutBytes)] = filling;
        }
    }

    private int Word(int at) => _w[at] | _w[at + 1] << 8;

    private void SetWord(int at, int value)
    {
        _w[at] = (byte)value;
        _w[at + 1] = (byte)(value >> 8);
    }
}
