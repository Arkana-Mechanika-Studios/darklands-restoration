namespace Darklands.Core.World;

/// <summary>
/// The read of a city record from DARKLAND.CTY into the working copy <c>city_rec</c>, as every
/// reader does it: <c>fseek(f, (long)city * 0x26e + 1, 0)</c> then <c>_dos_read(city_rec, 0x26e)</c> on
/// a file just opened. For a city of the file the record is that city's; for a negative number
/// the seek fails and the read starts at the file's beginning (the count byte and most of city
/// 0); past the last city the read returns nothing and the working copy keeps what it held.
/// </summary>
// orig: the city-record read (fseek city * 0x26E + 1, read 0x26E bytes), repeated inline in world_load R043+0x0100 (1462:0100),
// repeated inline in load_saved_game R037+0x8ED4 (1C7D:0D24), load_city R029+0x4E68 (0E76:4E68, CardContext.LoadCity),
// repeated inline in overland_loop R030+0x02C0 (1462:02C0) and the cards that load a city themselves
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

    /// <summary>
    /// After every battle (a card's and the main loop's) cards_startup reloads the world: the working copy is read again for cur_loc, so
    /// what loc_set wrote into it (city size, the shops' qualities: LocationRules.Set codes 2..8 and 11) is lost.
    /// </summary>
    // orig: world_load R043+0x0100 (1462:0100) matched, called by cards_startup R043+0x078E (1462:078E) matched after cards_shutdown
    //   R043+0x054E freed the old copy; tests: CityReloadTests (the original's shutdown and startup run directly on a city state after
    //   loc_set(cur_loc, 3, 0x55): city_rec is DARKLAND.CTY's record again, local/batch/nmB)
    // DIVERGENCE: at a place past the last city the read returns nothing into the block just allocated, uninitialised memory in the
    //   original; the working copy is kept here (in the wilderness, cur_loc -1, the read is defined: the file's first bytes, as Read).
    public static void ReloadAfterBattle(GameState s) => s.City = Read(s.Data, s.CurrentLocation, s.City) ?? s.City;
}
