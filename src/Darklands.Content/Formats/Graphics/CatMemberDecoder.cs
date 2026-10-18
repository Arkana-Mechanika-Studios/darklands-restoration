using System.Buffers.Binary;

namespace Darklands.Content.Formats.Graphics;

/// <summary>Decodes a terminal CAT compression chunk into resource bytes.</summary>
/// <remarks>
/// Resource transformation from original 05D9:0070..0199. Control bits are read
/// least significant first, with a word refill before consuming the last bit.
/// Short and long matches copy forwards and can overlap. Extended length zero
/// terminates; the multi-chunk continuation token remains unsupported.
/// This decoder owns no file cursor, DOS buffer, registers or gameplay state.
/// </remarks>
public static class CatMemberDecoder
{
    public static byte[] Decode(ReadOnlySpan<byte> stored, int maximumDecodedBytes)
    {
        if (maximumDecodedBytes < 0) throw new ArgumentOutOfRangeException(nameof(maximumDecodedBytes));
        return new Reader(stored, maximumDecodedBytes).Decode();
    }

    private ref struct Reader
    {
        private readonly ReadOnlySpan<byte> input;
        private readonly int limit;
        private readonly List<byte> output;
        private int cursor;
        private ushort control;
        private int bits;

        internal Reader(ReadOnlySpan<byte> input, int limit)
        {
            this.input = input;
            this.limit = limit;
            output = new List<byte>(Math.Min(limit, 65536));
            cursor = 0;
            control = 0;
            bits = 0;
        }

        internal byte[] Decode()
        {
            control = Word();
            bits = 16;
            while (true)
            {
                if (Bit()) { Append(Byte()); continue; }
                int distance, length;
                if (Bit())
                {
                    ushort word = Word();
                    distance = -unchecked((short)((((word >> 11) | 0xE0) << 8) | (word & 0xFF)));
                    length = (word >> 8) & 7;
                    if (length == 0)
                    {
                        byte extended = Byte();
                        if (extended == 0) return output.ToArray();
                        if (extended == 1)
                            throw new InvalidDataException("CAT multi-chunk continuation is not supported.");
                        length = extended + 1;
                    }
                    else length += 2;
                }
                else
                {
                    length = (Bit() ? 2 : 0) + (Bit() ? 1 : 0) + 2;
                    distance = 256 - Byte();
                }
                if (distance > output.Count)
                    throw new InvalidDataException("CAT match precedes the decoded resource.");
                for (int index = 0; index < length; index++) Append(output[output.Count - distance]);
            }
        }

        private bool Bit()
        {
            bool value = (control & 1) != 0;
            if (--bits == 0)
            {
                control = Word();
                bits = 16;
            }
            else control >>= 1;
            return value;
        }

        private byte Byte()
        {
            if (cursor >= input.Length) throw new InvalidDataException("CAT compressed resource is truncated.");
            return input[cursor++];
        }

        private ushort Word()
        {
            if (input.Length - cursor < 2) throw new InvalidDataException("CAT control word or match is truncated.");
            ushort value = BinaryPrimitives.ReadUInt16LittleEndian(input.Slice(cursor, 2));
            cursor += 2;
            return value;
        }

        private void Append(byte value)
        {
            if (output.Count == limit) throw new InvalidDataException("CAT resource exceeds its decoded capacity.");
            output.Add(value);
        }
    }
}
