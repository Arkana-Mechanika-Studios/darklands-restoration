namespace Darklands.Core.World;

/// <summary>
/// The read of a city record from DARKLAND.CTY into the working copy <c>city_rec</c>, as every
/// reader does it: <c>fseek(f, (long)city * 0x26e + 1, 0)</c> then <c>_dos_read(city_rec, 0x26e)</c> on
/// a file just opened. For a city of the file the record is that city's; for a negative number
/// the seek fails and the read starts at the file's beginning (the count byte and most of city
/// 0); past the last city the read returns nothing and the working copy keeps what it held.
/// </summary>
// orig: world_load R043+0x0100 (1462:0100) matched; load_saved_game's read R037+0x8ED4
// R037+0x8ED4 (1C7D:0D24) matched; the cards that load a city themselves (CardContext.LoadCity)
// C: fseek to a negative offset fails and leaves the handle at 0; a seek past the end succeeds and the read returns 0 bytes; a read
// that ends at the end of the file fills only its start.
public static class CityFile
{
    /// <summary>The working copy after reading city <paramref name="city"/> over <paramref name="before"/> (null: none yet).</summary>
    public static CityRecord? Read(IGameData data, int city, CityRecord? before)
    {
        if (city is >= 0 and < Location.CityCount)
        {
            return data.LoadCity(city);
        }
        var offset = (long)city * CityRecord.Size + 1;
        var bytes = data.CityFileBytes(offset < 0 ? 0 : offset, CityRecord.Size);
        if (bytes is null || (bytes.Length == 0 && before is null))
        {
            return before;
        }
        var record = before?.ToArray() ?? new byte[CityRecord.Size];
        bytes.CopyTo(record, 0);
        // UNCERTAIN: the record read at the file's start is taken as city 0's (the darkland.dsc read of the same load also starts after
        // the first byte); a record kept from before keeps its city.
        return new CityRecord(offset < 0 ? 0 : before?.City ?? 0, record);
    }
}
