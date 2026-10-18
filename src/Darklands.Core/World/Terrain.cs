namespace Darklands.Core.World;

/// <summary>Terrain codes of the world map (DARKLAND.MAP), and what crossing them costs.</summary>
public static class Terrain
{
    public const int Plains = 0;
    public const int Ocean = 1;
    public const int MajorRiver = 2;
    public const int MinorRiver = 3;
    public const int TidalMarsh = 4;
    public const int Marsh = 5;
    public const int Geest = 6;
    public const int LightGeest = 7;
    public const int Road = 24;
    public const int Ford = 25;
    public const int Bridge = 27;
    public const int Castle = 28;
    public const int City = 29;

    /// <summary>Map codes of 50 and more are place icons laid over the terrain.</summary>
    public const int FirstPlaceIcon = 50;

    /// <summary>Game minutes one step (one pixel of map movement) takes on a terrain.</summary>
    // orig: R030+0x2AC8 (1462:2AC8) matched
    // DORMANT: `if (v >= 0x5A0)` (a day of minutes: advance_hours(1) per 1440 and the rest) cannot hold: the largest cost is 197.
    public static int MinutesPerStep(int terrain) => terrain switch
    {
        1 or 2 or 16 => 40,
        3 or 19 => 30,
        4 or 5 => 50,
        6 => 17,
        7 => 13,
        9 => 11,
        10 => 10,
        11 => 12,
        12 => 14,
        13 => 16,
        14 => 18,
        15 => 27,
        17 => 60,
        18 => 20,
        20 => 45,
        21 => 67,
        22 => 98,
        23 => 197,
        Road => 6,
        _ => 9,
    };

    /// <summary>Ocean and major rivers cannot be walked across.</summary>
    // orig: R030+0x1300 (1462:1300) draft
    // C: `t == 1 || t == 2` (refused without a blessing, event 0x4B, unless the party already stands on water).
    public static bool IsDeepWater(int terrain) => terrain is Ocean or MajorRiver;

    /// <summary>Terrain under the party where a place may be entered.</summary>
    // orig: R030+0x1300 (1462:1300) draft
    // C: `t >= 25 && t != 27 && t != 25` (codes from 50 on count as 28).
    public static bool CanEnterPlace(int terrain) => terrain >= 26 && terrain != Bridge;

    /// <summary>The map code drawn for a place, by its icon (cities are part of the map itself).</summary>
    // orig: R030+0x1C76 (1462:1C76) draft
    // DORMANT: icon 15 (dragon lair) maps to 0x38 but the map loader skips icon 15 and arrival card 0x62 places before the switch.
    public static int PlaceIconCode(int icon) => icon switch
    {
        3 => 0x33,
        4 or 13 => 0x39,
        5 or 15 or 19 or 24 => 0x38,
        6 => 0x37,
        8 or 10 => 0x1D,
        9 => 0x3E,
        16 => 0x36,
        17 => 0x3B,
        18 => 0x35,
        20 => 0x3C,
        21 => 0x3D,
        22 => 0x3F,
        23 => 0x40,
        26 => 0x3A,
        _ => 0x1C,
    };
}
