using Darklands.Content.Formats.World;
using Darklands.Core.Items;
using Darklands.Core.World;

namespace Darklands.Content;

/// <summary>The static world: cities (DARKLAND.CTY), places (DARKLAND.LOC) and items (DARKLAND.LST).</summary>
public sealed class WorldData : IGameData
{
    private WorldData(DarklandCities cities, DarklandLocations locations, DarklandList items, DarklandDescriptions descriptions)
    {
        Cities = cities;
        Descriptions = descriptions;
        LocationRecords = locations;
        Items = items;
        ItemDefinitions = items.Items.Select(ToDefinition).ToList();
    }

    public DarklandCities Cities { get; }

    public DarklandLocations LocationRecords { get; }

    public DarklandList Items { get; }

    /// <summary>The one-line city descriptions (DARKLAND.DSC, 0x50 bytes each, in city order).</summary>
    public DarklandDescriptions Descriptions { get; }

    /// <summary>The description of a city ($PlaceDesc), or "" when the file has none.</summary>
    // orig: city-description, the 0x50-byte DARKLAND.DSC record read with the city record when the party arrives in a city: fseek(city * 0x50 + 1), repeated inline in R030+0x02C0 (1462:02C0) draft (bytes match; 16 of the file's 22 functions do); tests: TextAndOpeningTests
    public string CityDescription(int city) => Descriptions.Find(city)?.Text ?? "";

    /// <summary>The item list as the rules see it.</summary>
    public IReadOnlyList<ItemDefinition> ItemDefinitions { get; }

    // orig: world-read R043+0x0100 (1462:0100) matched (4/6 functions of the file match; the mismatch is a tool limit); loads darkland.lst (item list), darkland.cty (the current city's record), the location file, darkland.dsc (the current city's description) and events.tmp
    // DIVERGENCE: the original reads one city record and one description (those of location-now) and keeps them as city-record / city-description; here every city is decoded and the working copy comes from GameState.City; the events.tmp file is the saved game's event list (Persistence).
    // DIVERGENCE: a missing darkland.dsc stops the original ("Could not open city description file"); here the descriptions are empty.
    public static WorldData Load(GameFiles files) => new(
        new DarklandCitiesDecoder().Decode(files.Read("DARKLAND.CTY"), "DARKLAND.CTY"),
        new DarklandLocationsDecoder().Decode(files.Read("DARKLAND.LOC"), "DARKLAND.LOC"),
        DarklandListDecoder.DecodeBytes(files.Read("DARKLAND.LST"), "DARKLAND.LST"),
        files.Exists("DARKLAND.DSC")
            ? new DarklandDescriptionsDecoder().Decode(files.Read("DARKLAND.DSC"), "DARKLAND.DSC")
            : new DarklandDescriptions("DARKLAND.DSC", []));

    /// <summary>Fresh, mutable locations for a new game.</summary>
    // orig: the location part of world-read R043+0x0100 (1462:0100): the file is a word count and then 0x3A-byte records; locations[i] (far table R136+0xE9EE) points into them
    // DIVERGENCE: the original keeps the records in its own heap and writes them to locs.tmp when the card engine shuts down (cards-teardown R043+0x054E); a new game starts from the file here, a saved game restores the fields through Persistence.
    public List<Location> CreateLocations() => LocationRecords.Locations.Select(ToLocation).ToList();

    public string ItemName(int code) => code >= 0 && code < Items.Items.Count ? Items.Items[code].Name : $"item {code}";

    /// <summary>A fresh copy of a city's record (<c>city-read</c>); an unknown number gives zeros (readers outside the file go through CityFile).</summary>
    // orig: fseek(darkland.cty, city * 0x26e + 1) and _dos_read(city-record, 0x26e), repeated inline in world-read R043+0x0100,
    // repeated inline in the arrival branch of world-map-loop R030+0x02C0, saved-game-read R037+0x8ED4 and city-read R029+0x4E68
    // C: the readers that can be handed an unknown number (a load, the cards) read through CityFile.Read, which keeps the record or reads the file's start.
    public CityRecord LoadCity(int city) =>
        Cities.Find(city) is { } c ? new CityRecord(city, [.. c.RawBytes]) : new CityRecord(city, new byte[CityRecord.Size]);

    /// <summary>The bytes of DARKLAND.CTY at a file offset: the city count byte, then the 0x26E-byte records.</summary>
    // orig: the file as the city-record read seeks in it: fseek(location-now * 0x26e + 1), _dos_read(city-record, 0x26e),
    // repeated inline in world-read R043+0x0100 and in the loaded game's city read R037+0x8ED4
    public byte[]? CityFileBytes(long offset, int length)
    {
        var size = 1L + (long)Cities.Cities.Count * CityRecord.Size;
        if (offset < 0 || offset >= size)
        {
            return [];
        }
        var bytes = new byte[(int)Math.Min(length, size - offset)];
        for (var i = 0; i < bytes.Length; i++)
        {
            var at = offset + i;
            bytes[i] = at == 0 ? (byte)Cities.Cities.Count : Cities.Cities[(int)((at - 1) / CityRecord.Size)].RawBytes[(int)((at - 1) % CityRecord.Size)];
        }
        return bytes;
    }

    private static ItemDefinition ToDefinition(DarklandItemDefinition d) => new(
        d.Code, d.Name, d.ShortName, d.Type,
        (uint)(d.Flags[0] | d.Flags[1] << 8 | d.Flags[2] << 16 | d.Flags[3] << 24),
        d.Weight, d.Quality, d.Value, d.Rarity, d.Flags.Count > 4 ? d.Flags[4] : 0);

    // orig: struct location, 0x3A bytes, as the C reads it: icon +0x00, x +0x04, y +0x06, unk08 +0x08, unk0a +0x0A, menu +0x0C, unk0e +0x0E, local reputation +0x12 (Party02 stores 9), flags +0x14, unk17 +0x17, name +0x26 (struct location, struct loc); status draft; tests: none directly (CardSmokeTests; OriginalSaveTests round-trip the records)
    // orig: the other fields follow the reference's format notes (DARKLAND.LOC, confidence "doc"): cache handle +0x18 (inn_cache_idx), mark +0x1A, three recorded words from +0x1C
    // C: names. NoticeRange (unk08: how far the place is noticed while travelling, R030+0x1300 compares it with the distance) and Danger (unk0a: added to the encounter weight, R030+0x2D44) are read from how the travel code uses them; ArrivalCard (menu) and EncounterCard (unk0e) from the card numbers the map returns; LocalValues, Mark, Recorded and CacheHandle are the rules' names (place-query/place-assign), their meaning is a guess.
    // C: the +0x15..+0x17 bytes are signed here (the rules read them with a signed char) and the name is cut at 20 bytes: a name that fills all 20 would run on into the next record in the original (_fstrcpy up to the first zero).
    // C: the fields at +0x02, +0x10 and +0x22..+0x25 are not named (nothing reads them); the record's bytes stay with the place (Location.OriginalRecord) so a saved game writes them as the file had them.
    private static Location ToLocation(DarklandLocation r)
    {
        var location = new Location
        {
            Index = r.Index,
            Icon = r.ReadUInt16(0x00),
            X = r.ReadUInt16(0x04),
            Y = r.ReadUInt16(0x06),
            NoticeRange = r.ReadUInt16(0x08),
            Danger = r.ReadUInt16(0x0A),
            ArrivalCard = r.ReadUInt16(0x0C),
            EncounterCard = r.ReadUInt16(0x0E),
            Reputation = (short)r.ReadUInt16(0x12),
            Flags = r.ReadByte(0x14),
            Size = (sbyte)r.ReadByte(0x11),
            CacheHandle = (short)r.ReadUInt16(0x18),
            Mark = (short)r.ReadUInt16(0x1A),
            Name = ReadName(r.RawBytes, 0x26, 20),
            OriginalRecord = [.. r.RawBytes],
        };
        for (var k = 0; k < 3; k++)
        {
            location.LocalValues[k] = (sbyte)r.ReadByte(0x15 + k);
            location.Recorded[k] = (short)r.ReadUInt16(0x1C + 2 * k);
        }
        return location;
    }

    private static string ReadName(byte[] bytes, int offset, int length)
    {
        var end = offset;
        while (end < offset + length && end < bytes.Length && bytes[end] != 0)
        {
            end++;
        }
        return System.Text.Encoding.Latin1.GetString(bytes, offset, end - offset);
    }
}
