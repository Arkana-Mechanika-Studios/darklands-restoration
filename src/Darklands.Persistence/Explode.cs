namespace Darklands.Persistence;

/// <summary>
/// The decompressor of the PKWARE Data Compression Library 1.02 ("explode"), as the original links it into
/// its battle-state overlay: a header byte for the literal coding (0 plain bytes, 1 Huffman-coded) and one
/// for the dictionary size (4, 5, 6: 1, 2, 4 KB), then, bit by bit from the low end, literals and
/// (length, distance) copies up to the end code (length 519).
/// </summary>
// orig: R003+0x2707 (1016:0D07) asm (the far pascal entry: marks the stack switch through R003+0x1A86, then R003+0x26AE); its table
// copies and the window's move down use the library's copy R003+0x25D6 (1016:0BD6); tests: ImplodeOracleTests, OriginalSaveTests
public sealed class Explode
{
    /// <summary>Reads up to <paramref name="size"/> bytes into <paramref name="buffer"/> at <paramref name="at"/>; returns how many.</summary>
    public delegate int ReadBlock(byte[] buffer, int at, int size);

    /// <summary>Takes <paramref name="size"/> bytes of output at <paramref name="at"/>.</summary>
    public delegate void WriteBlock(byte[] buffer, int at, int size);

    private int _ctype;
    private int _outputPos;
    private int _dsizeBits;
    private int _dsizeMask;
    private int _bitBuff;
    private int _extraBits;
    private int _inPos;
    private int _inBytes;
    private ReadBlock _read = (_, _, _) => 0;
    private WriteBlock _write = (_, _, _) => { };

    /// <summary>+0x1A: the output window (a 0x1000-byte dictionary, then the bytes made since the last write).</summary>
    private readonly byte[] _out = new byte[0x2204];

    /// <summary>+0x221E: the input buffer.</summary>
    private readonly byte[] _in = new byte[0x800];

    private readonly byte[] _distPosCodes = new byte[0x100];
    private readonly byte[] _lengthCodes = new byte[0x100];
    private readonly byte[] _asciiTable2C34 = new byte[0x100];
    private readonly byte[] _asciiTable2D34 = new byte[0x100];
    private readonly byte[] _asciiTable2E34 = new byte[0x80];
    private readonly byte[] _asciiTable2EB4 = new byte[0x100];
    private readonly byte[] _chBitsAsc = new byte[0x100];

    /// <summary>
    /// Decompresses one block of a battle file. Throws, as the original stops the game, when the block is shorter
    /// than its header or does not decode.
    /// </summary>
    // orig: R003+0x2707 (1016:0D07) asm (the far pascal entry: the stack switch, then Run R003+0x26AE)
    public static byte[] Decompress(ReadOnlySpan<byte> input, string what)
    {
        var source = input.ToArray();
        var read = 0;
        var output = new List<byte>();
        var result = new Explode().Run(
            (buffer, at, size) =>
            {
                // orig: R003+0x0000 (0E76:0000) draft (the memory reader: at most what is left of the block)
                var n = Math.Min(size, source.Length - read);
                Array.Copy(source, read, buffer, at, n);
                read += n;
                return n;
            },
            // orig: R003+0x006A (0E76:006A) draft (the memory writer)
            (buffer, at, size) => output.AddRange(buffer.AsSpan(at, size)));
        if (result != 0)
        {
            // orig: R003+0x19E8 (0E76:19E8) draft ("Corrupt file-%s %d", then the game ends)
            // DIVERGENCE: the original shuts the game down and returns to DOS; here the load fails with the same text.
            throw new InvalidDataException($"Corrupt file-{what} {result}");
        }
        return [.. output];
    }

    /// <summary>Returns 0, 1 for a dictionary size other than 4..6, 2 for a literal coding other than 0..1, 3 for less than 5 bytes of input, 4 when a distance does not decode.</summary>
    // orig: R003+0x26AE (1016:0CAE) asm (explode: the header, the decode tables, then the expansion)
    public int Run(ReadBlock read, WriteBlock write)
    {
        _write = write;
        _read = read;
        _inBytes = read(_in, 0, 0x800);
        if (_inBytes <= 4)
        {
            return 3;
        }
        _ctype = _in[0];
        _bitBuff = _in[2];
        _dsizeBits = _in[1];
        _extraBits = 0;
        _inPos = 3;
        if (_dsizeBits is < 4 or > 6)
        {
            return 1;
        }
        _dsizeMask = 0xFFFF >> (16 - _dsizeBits);
        switch (_ctype)
        {
            case 0:
                break;
            case 1:
                DclTables.ChBitsAsc.CopyTo(_chBitsAsc, 0);
                MakeAsciiTables();
                break;
            default:
                return 2;
        }
        MakeDecodeTable(DclTables.LenBits, DclTables.LenCode, _lengthCodes);
        MakeDecodeTable(DclTables.DistBits, DclTables.DistCode, _distPosCodes);
        return Expand() == 0x306 ? 4 : 0;
    }

    /// <summary>
    /// The expansion loop: literals and copies into the window; each 0x1000 bytes made are written and the window
    /// moves down. Returns 0x306 when a distance did not decode.
    /// </summary>
    // orig: R003+0x2801 (1016:0E01) asm
    // C: the value returned is the size word of the last write call ([bp-2]): 0x306 only after a failed distance (0x1000 after a
    // full write, otherwise the stack word the function starts with; the original's caller only tests 0x306).
    private int Expand()
    {
        var result = 0;
        _outputPos = 0x1000;
        while (true)
        {
            var literal = DecodeLiteral();
            if (literal >= 0x305)
            {
                break;
            }
            if (literal < 0x100)
            {
                _out[_outputPos++] = (byte)literal;
            }
            else
            {
                var length = literal - 0xFE;
                var distance = DecodeDistance(length);
                if (distance == 0)
                {
                    result = 0x306;
                    break;
                }
                // C: a forward byte copy: a copy nearer than its length repeats the bytes it has just made.
                for (var i = 0; i < length; i++, _outputPos++)
                {
                    _out[_outputPos] = _out[_outputPos - distance];
                }
            }
            if (_outputPos >= 0x2000)
            {
                result = 0x1000;
                _write(_out, 0x1000, 0x1000);
                Array.Copy(_out, 0x1000, _out, 0, _outputPos - 0x1000);
                _outputPos -= 0x1000;
            }
        }
        _write(_out, 0x1000, _outputPos - 0x1000);
        return result;
    }

    /// <summary>The next literal (0..0xFF), length code (0x100 + length − 2; 0x305 the end) or 0x306 when the input ran out.</summary>
    // orig: R003+0x28CC (1016:0ECC) asm
    private int DecodeLiteral()
    {
        if ((_bitBuff & 1) != 0)
        {
            if (WasteBits(1) != 0)
            {
                return 0x306;
            }
            int code = _lengthCodes[_bitBuff & 0xFF];
            if (WasteBits(DclTables.LenBits[code]) != 0)
            {
                return 0x306;
            }
            int extra = DclTables.ExLenBits[code];
            if (extra != 0)
            {
                var value = (((1 << extra) - 1) & _bitBuff) + DclTables.LenBase[code];
                if (WasteBits(extra) != 0)
                {
                    return 0x306;
                }
                code = value;
            }
            return code + 0x100;
        }
        if (WasteBits(1) != 0)
        {
            return 0x306;
        }
        int literal;
        if (_ctype == 0)
        {
            literal = _bitBuff & 0xFF;
            return WasteBits(8) != 0 ? 0x306 : literal;
        }
        if ((_bitBuff & 0xFF) != 0)
        {
            literal = _asciiTable2C34[_bitBuff & 0xFF];
            if (literal == 0xFF)
            {
                if ((_bitBuff & 0x3F) != 0)
                {
                    if (WasteBits(4) != 0)
                    {
                        return 0x306;
                    }
                    literal = _asciiTable2D34[_bitBuff & 0xFF];
                }
                else
                {
                    if (WasteBits(6) != 0)
                    {
                        return 0x306;
                    }
                    literal = _asciiTable2E34[_bitBuff & 0x7F];
                }
            }
        }
        else
        {
            if (WasteBits(8) != 0)
            {
                return 0x306;
            }
            literal = _asciiTable2EB4[_bitBuff & 0xFF];
        }
        return WasteBits(_chBitsAsc[literal & 0xFF]) != 0 ? 0x306 : literal & 0xFF;
    }

    /// <summary>The distance of a copy of <paramref name="length"/> bytes (2 uses two low bits), 0 when the input ran out.</summary>
    // orig: R003+0x2A0A (1016:100A) asm
    private int DecodeDistance(int length)
    {
        int code = _distPosCodes[_bitBuff & 0xFF];
        if (WasteBits(DclTables.DistBits[code]) != 0)
        {
            return 0;
        }
        int distance, bits;
        if (length == 2)
        {
            distance = code << 2 | (_bitBuff & 3);
            bits = 2;
        }
        else
        {
            distance = code << _dsizeBits | (_dsizeMask & _bitBuff);
            bits = _dsizeBits;
        }
        return WasteBits(bits) != 0 ? 0 : distance + 1;
    }

    /// <summary>Drops <paramref name="count"/> bits of the bit buffer, reading a byte (and the next 0x800 when needed); 1 when the input ran out.</summary>
    // orig: R003+0x2A72 (1016:1072) asm
    private int WasteBits(int count)
    {
        if (count <= _extraBits)
        {
            _bitBuff >>= count;
            _extraBits -= count;
            return 0;
        }
        _bitBuff >>= _extraBits;
        if (_inPos >= _inBytes)
        {
            _inBytes = _read(_in, 0, 0x800);
            if (_inBytes == 0)
            {
                return 1;
            }
            _inPos = 0;
        }
        _bitBuff |= _in[_inPos++] << 8;
        var rest = count - _extraBits;
        _bitBuff = (_bitBuff >> rest) & 0xFFFF;
        _extraBits = 8 - rest;
        return 0;
    }

    /// <summary>A decode table: for every byte value whose low bits are code i (bits[i] long), i.</summary>
    // orig: R003+0x2AF5 (1016:10F5) asm
    private static void MakeDecodeTable(byte[] bits, byte[] codes, byte[] table)
    {
        for (var i = bits.Length - 1; i >= 0; i--)
        {
            var step = 1 << bits[i];
            for (var at = codes[i] & 0xFF; at <= 0xFF; at += step)
            {
                table[at] = (byte)i;
            }
        }
    }

    /// <summary>The four lookup tables of the Huffman-coded literals (codes of up to 8 bits, and the longer ones by their low 4, 6 or 8 bits).</summary>
    // orig: R003+0x2B2B (1016:112B) asm
    private void MakeAsciiTables()
    {
        for (var literal = 0xFF; literal >= 0; literal--)
        {
            int code = DclTables.ChCodeAsc[literal];
            var mask = 0xFF;
            var shift = 0;
            var table = _asciiTable2C34;
            int bits = _chBitsAsc[literal];
            if (bits > 8)
            {
                if ((code & 0xFF) != 0)
                {
                    _asciiTable2C34[code & 0xFF] = 0xFF;
                    shift = 4;
                    table = _asciiTable2D34;
                    if ((code & 0x3F) == 0)
                    {
                        shift = 6;
                        table = _asciiTable2E34;
                        mask = 0x7F;
                    }
                }
                else
                {
                    shift = 8;
                    table = _asciiTable2EB4;
                }
            }
            code >>= shift;
            bits = (byte)(bits - shift);
            _chBitsAsc[literal] = (byte)bits;
            var step = 1 << bits;
            for (var at = code; at <= mask; at += step)
            {
                table[at] = (byte)literal;
            }
        }
    }
}
