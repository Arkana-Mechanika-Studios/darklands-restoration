namespace Darklands.Core.World;

/// <summary>
/// A place on the world map: one of the 92 cities (indices 0..91) or a village, castle,
/// monastery, shrine, mine or other site.
/// </summary>
// orig: locations R136+0xE9EE
// C: the names of +08 (NoticeRange), +0C/+0E (cards), +18 (CacheHandle) and +1A (Mark) come from their uses.
public sealed class Location
{
    public const int CityCount = 92;

    /// <summary>Flag bit set after an uprising (a city's two type words swap, <c>loc_query</c> 40/41).</summary>
    public const int UprisingFlag = 0x80;

    /// <summary>Flag bit of a destroyed place (never the nearest of its type).</summary>
    public const int DestroyedFlag = 0x04;

    public int Index { get; init; }

    public string Name { get; init; } = "";

    /// <summary>Map icon / kind of place (0 city, 2 raubritter castle, 0x15 witches' sabbat ...).</summary>
    public int Icon { get; set; }

    public int X { get; init; }

    public int Y { get; init; }

    /// <summary>The card shown on arrival (0x62 = cannot be entered).</summary>
    public int ArrivalCard { get; set; }

    /// <summary>The place's own encounter card.</summary>
    public int EncounterCard { get; set; }

    /// <summary>How far away the place is noticed when travelling (map tiles).</summary>
    public int NoticeRange { get; set; }

    /// <summary>Encounter weight added near this place.</summary>
    public int Danger { get; init; }

    /// <summary>The party's standing here, −99..99.</summary>
    public int Reputation { get; set; }

    public int Flags { get; set; }

    /// <summary>How big the place is (record byte 0x11): up to 4 small, 5–6 moderate, larger is large.</summary>
    public int Size { get; init; }

    /// <summary>Three local values (<c>loc_query</c>/<c>loc_set</c> 12..14; 25 each at the start), meaning unknown.</summary>
    public int[] LocalValues { get; } = [25, 25, 25];

    /// <summary>Handle of the items stored here (−1 when nothing is stored).</summary>
    public int CacheHandle { get; set; } = -1;

    /// <summary>A mark kept for the place (<c>loc_query</c> 38), meaning unknown.</summary>
    public int Mark { get; set; }

    /// <summary>A three-slot list of recorded values (<c>loc_set</c>/<c>loc_query</c> 37), meaning unknown.</summary>
    public int[] Recorded { get; } = new int[3];

    /// <summary>
    /// The 0x3A-byte record this place was read from (DARKLAND.LOC, LOCS.TMP or a saved game),
    /// kept by the persistence layer so that the bytes this model does not name are written
    /// back as they were. The rules never read it.
    /// </summary>
    public byte[]? OriginalRecord { get; set; }

    public bool IsCity => Index < CityCount;

    public MapPosition Position => new(X, Y);
}
