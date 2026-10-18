namespace Darklands.Core.World;

/// <summary>
/// The working copy of the current city's DARKLAND.CTY record (<c>city_rec</c>): loaded when
/// a city is set up, read and changed by <c>loc_query</c>/<c>loc_set</c>. The original keeps
/// the last loaded city while the party is elsewhere, and so does this.
/// </summary>
// orig: load_city R029+0x4E68 (0E76:4E68) matched; city_rec DS:9785
public sealed class CityRecord
{
    public const int Size = 0x26E;

    private readonly byte[] _bytes;

    public CityRecord(int city, byte[] bytes)
    {
        if (bytes.Length != Size)
        {
            throw new ArgumentException("a city record has 622 bytes", nameof(bytes));
        }
        City = city;
        _bytes = (byte[])bytes.Clone();
    }

    /// <summary>The city (location index 0..91) the record belongs to.</summary>
    public int City { get; }

    /// <summary>City size 3..8 (<c>loc_query</c> 2).</summary>
    public int CitySize { get => Word(0x40); set => SetWord(0x40, value); }

    /// <summary>Map x the party leaves to (<c>loc_query</c> 34).</summary>
    public int ExitX => Word(0x46);

    /// <summary>The city's ordinal 0..91; plus the party seed it seeds the city's randomness (<c>loc_query</c> 33).</summary>
    public int Ordinal => Word(0x56);

    /// <summary>0 free city, 1 ruled, 2 capital (<c>loc_query</c> 40/41, swapped after an uprising).</summary>
    public int CityType => Word(0x58);

    /// <summary>The other city-type word (0..3) <c>loc_query</c> 40/41 swaps with <see cref="CityType"/>.</summary>
    public int OtherCityType => Word(0x5A);

    /// <summary>The building bits (<see cref="CityContents"/>).</summary>
    public int Contents => Word(0x5E);

    /// <summary>A shop's quality (0 = no shop).</summary>
    // orig: loc_query R029+0x1A8E (0E76:1A8E) draft
    // C: city_rec[0x62..0x6A] are signed chars.
    public int Shop(CityShop shop) => (sbyte)_bytes[(int)shop];

    public void SetShop(CityShop shop, int quality) => _bytes[(int)shop] = (byte)quality;

    /// <summary>The city's festival day of the year (<c>loc_query</c> 35 tests "within 14 days").</summary>
    public int FestivalDay => Word(0x6C);

    /// <summary>A raw byte of the record, for scripts that read the record directly.</summary>
    public int Byte(int offset) => _bytes[offset];

    /// <summary>A raw little-endian word of the record.</summary>
    public int Word(int offset) => (short)(_bytes[offset] | _bytes[offset + 1] << 8);

    public void SetWord(int offset, int value)
    {
        _bytes[offset] = (byte)value;
        _bytes[offset + 1] = (byte)(value >> 8);
    }

    public byte[] ToArray() => (byte[])_bytes.Clone();
}

/// <summary>The shop quality bytes of a city record, by their place in it.</summary>
public enum CityShop
{
    Blacksmith = 0x62,
    Merchant = 0x63,
    Swordsmith = 0x64,
    Armorer = 0x65,

    /// <summary>A shop of unknown trade (<c>loc_query</c> 11; <c>loc_set</c> 6 writes it).</summary>
    UnknownShop = 0x66,
    Bowyer = 0x67,
    Tinker = 0x68,

    /// <summary>A second shop of unknown trade (<c>loc_query</c> 10).</summary>
    UnknownShop2 = 0x69,
    Clothier = 0x6A,
}

/// <summary>Building bits of <see cref="CityRecord.Contents"/>.</summary>
[Flags]
public enum CityContents
{
    Unknown0 = 0x0001,
    TownHall = 0x0002,
    Fortress = 0x0004,
    Unknown3 = 0x0008,
    Cathedral = 0x0010,
    Barracks = 0x0020,
    Slums = 0x0040,
    Monastery = 0x0080,
    University = 0x0100,
    Pawnshop = 0x0200,
    Mint = 0x0400,
    Docks = 0x0800,
}
