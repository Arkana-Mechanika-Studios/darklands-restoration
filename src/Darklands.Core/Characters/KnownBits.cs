using System.Collections;

namespace Darklands.Core.Characters;

/// <summary>
/// A set of numbers kept as bits of a member's raw bytes, as the original keeps them: the
/// formulas known (22 bytes, formula f = potion × 3 + grade is bit 1 &lt;&lt; grade of byte f / 3;
/// the upper five bits are not formulas but belong to the byte) or the saints known (20 bytes,
/// saint s is bit 0x80 &gt;&gt; (s &amp; 7) of byte s &gt;&gt; 3). The bytes themselves are
/// <see cref="Bytes"/>: the original also writes and tests whole bytes there.
/// </summary>
// orig: formulas R136+0xE08F + 0x5F = 2E38:07BE (0x16 bytes per member), saints_known 2E38:082C (0x14 bytes per member)
public sealed class KnownBits : ICollection<int>, IReadOnlyCollection<int>
{
    private readonly bool _saints;

    private KnownBits(int bytes, bool saints)
    {
        Bytes = new byte[bytes];
        _saints = saints;
    }

    /// <summary>The 22 formula bytes of a member.</summary>
    public static KnownBits FormulaTable() => new(22, saints: false);

    /// <summary>The 20 saint bytes of a member.</summary>
    public static KnownBits SaintTable() => new(20, saints: true);

    /// <summary>The raw bytes.</summary>
    public byte[] Bytes { get; }

    /// <summary>How many numbers the bytes can hold (66 formulas, 160 saints).</summary>
    public int Capacity => _saints ? Bytes.Length * 8 : Bytes.Length * 3;

    public int Count
    {
        get
        {
            var k = 0;
            for (var n = 0; n < Capacity; n++)
            {
                k += Contains(n) ? 1 : 0;
            }
            return k;
        }
    }

    public bool IsReadOnly => false;

    private (int Byte, int Mask) Bit(int n) => _saints ? (n >> 3, 0x80 >> (n & 7)) : (n / 3, 1 << (n % 3));

    public bool Contains(int n)
    {
        if (n < 0 || n >= Capacity)
        {
            return false;
        }
        var (b, m) = Bit(n);
        return (Bytes[b] & m) != 0;
    }

    public void Add(int n)
    {
        if (n >= 0 && n < Capacity)
        {
            var (b, m) = Bit(n);
            Bytes[b] |= (byte)m;
        }
    }

    public bool Remove(int n)
    {
        if (!Contains(n))
        {
            return false;
        }
        var (b, m) = Bit(n);
        Bytes[b] &= (byte)~m;
        return true;
    }

    public void UnionWith(IEnumerable<int> numbers)
    {
        foreach (var n in numbers)
        {
            Add(n);
        }
    }

    /// <summary>Clears every number (the formula bytes' upper bits stay).</summary>
    public void Clear()
    {
        for (var n = 0; n < Capacity; n++)
        {
            Remove(n);
        }
    }

    public void CopyTo(int[] array, int arrayIndex)
    {
        foreach (var n in this)
        {
            array[arrayIndex++] = n;
        }
    }

    public IEnumerator<int> GetEnumerator()
    {
        for (var n = 0; n < Capacity; n++)
        {
            if (Contains(n))
            {
                yield return n;
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
