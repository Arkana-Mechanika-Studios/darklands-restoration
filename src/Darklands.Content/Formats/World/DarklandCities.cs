namespace Darklands.Content.Formats.World;

/// <summary>
/// Decoded `DARKLAND.CTY` city table.
/// </summary>
/// <remarks>
/// The original file starts with `num_cities`, followed by fixed 0x26E-byte
/// city records. City-navigation token arms index this table through the
/// current location selector and city record pointer.
/// </remarks>
public sealed record DarklandCities(
    string AssetName,
    IReadOnlyList<DarklandCity> Cities)
{
    public DarklandCity? Find(int index)
    {
        return index >= 0 && index < Cities.Count ? Cities[index] : null;
    }
}

public sealed record DarklandCity(
    int Index,
    IReadOnlyList<byte> RawBytes,
    string ShortName,
    string FullName,
    DarklandCityData Data,
    string LeaderName,
    string RulerName,
    string PoliticalCenterName,
    string TownHallName,
    string FortressName,
    string CathedralName,
    string ChurchName,
    string MarketName,
    string ImperialMintName,
    string SlumName,
    string CityBarracksName,
    string PawnshopName,
    string MonasteryName,
    string InnName,
    string UniversityName)
{
    /// <summary>
    /// Reads a byte from the original 0x26E-byte `DARKLAND.CTY` record.
    /// </summary>
    /// <remarks>
    /// City-navigation predicates use original record offsets directly through
    /// the current-city pointer at `[9785:9787]`, for example `+5E`, `+5F`,
    /// and `+6C`.
    /// </remarks>
    public int ReadByte(int offset)
    {
        if (offset < 0 || offset >= RawBytes.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        return RawBytes[offset];
    }

    /// <summary>
    /// Reads a little-endian word from the original 0x26E-byte city record.
    /// </summary>
    public int ReadUInt16(int offset)
    {
        if (offset < 0 || offset + 1 >= RawBytes.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        return RawBytes[offset] | (RawBytes[offset + 1] << 8);
    }
}

public sealed record DarklandCityData(
    int Size,
    int EntryX,
    int EntryY,
    int ExitX,
    int ExitY,
    IReadOnlyList<int> DockDestinations,
    int CityType,
    int Contents);

