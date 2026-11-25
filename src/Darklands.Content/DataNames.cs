using System.Globalization;
using Darklands.Content.Formats.Text.Strings;

namespace Darklands.Content;

/// <summary>
/// The names of the data files as a translation pack gives them (docs/LOCALISATION.md, tables.ftl): items, saints, formulae, cities
/// and their buildings and people, places and enemies, keyed by record and index as the original-English table keys them
/// (<see cref="OriginalTextExtractor"/>); and since 1.1.3 the descriptions of the saints, the formulae and the cities. A name or
/// description is translated where it is shown; the rules and the saves keep the original's.
/// </summary>
// Not in the original: the 1.1 translations (names and descriptions of the data files).
public static class DataNames
{
    private static string N(int n) => n.ToString(CultureInfo.InvariantCulture);

    /// <summary>An item's name (DARKLAND.LST, 20 bytes) and its short name (10 bytes).</summary>
    public static string Item(int code) => $"lst:item/{N(code)}.name";

    /// <inheritdoc cref="Item"/>
    public static string ItemShort(int code) => $"lst:item/{N(code)}.short";

    /// <summary>A saint's full name and short name (DARKLAND.LST).</summary>
    public static string Saint(int index) => $"lst:saint/{N(index)}.name";

    /// <inheritdoc cref="Saint"/>
    public static string SaintShort(int index) => $"lst:saint/{N(index)}.short";

    /// <summary>A formula's full name and short name (DARKLAND.LST).</summary>
    public static string Formula(int index) => $"lst:formula/{N(index)}.name";

    /// <inheritdoc cref="Formula"/>
    public static string FormulaShort(int index) => $"lst:formula/{N(index)}.short";

    /// <summary>A city's field (DARKLAND.CTY, 32 bytes each): short, full, leader, ruler, church, market ...</summary>
    public static string City(int index, string field) => $"cty:{N(index)}.{field}";

    private static readonly string[] Buildings =
        ["political-center", "town-hall", "fortress", "cathedral", "church", "market", "imperial-mint", "slum", "barracks", "pawnshop",
         "monastery", "inn", "university"];

    /// <summary>
    /// The key of a city record's name slot <paramref name="n"/> (the 32-byte strings from +0x6E: 0 the leader, 1 the ruler, 2 a slot
    /// with no key, 3.. the buildings), or of its short name at the start of the record for -1; null for a slot with no key.
    /// </summary>
    public static string? CityName(int index, int n) => n switch
    {
        -1 => City(index, "short"),
        0 => City(index, "leader"),
        1 => City(index, "ruler"),
        >= 3 when n - 3 < Buildings.Length => City(index, Buildings[n - 3]),
        _ => null,
    };

    /// <summary>A place's name (DARKLAND.LOC, 20 bytes; stored in the saves too).</summary>
    public static string Location(int index) => $"loc:{N(index)}.name";

    /// <summary>An enemy kind's name (DARKLAND.ENM: a template's 10 bytes, a type's 20).</summary>
    public static string EnemyTemplate(int index) => $"enm:template/{N(index)}.name";

    /// <inheritdoc cref="EnemyTemplate"/>
    public static string EnemyType(int index) => $"enm:type/{N(index)}.name";

    /// <summary>A saint's description (DARKLAND.SNT, 360 bytes), on the prayer screen.</summary>
    public static string SaintDescription(int index) => $"snt:saint/{N(index)}.desc";

    /// <summary>A formula's description (DARKLAND.ALC), on the alchemy screens.</summary>
    public static string FormulaDescription(int index) => $"alc:{N(index)}.desc";

    /// <summary>A city's description (DARKLAND.DSC, 80 bytes), in card texts ($PlaceDesc).</summary>
    public static string CityDescription(int index) => $"dsc:{N(index)}.desc";

    /// <summary>What a key of the names or descriptions names (the template's comment), or null for a key that is not a translatable one.</summary>
    public static string? What(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.StartsWith("lst:item/", StringComparison.Ordinal))
        {
            return key.EndsWith(".short", StringComparison.Ordinal) ? "an item's short name (10 bytes in DARKLAND.LST)" : "an item's name (20 bytes in DARKLAND.LST)";
        }
        if (key.StartsWith("lst:saint/", StringComparison.Ordinal))
        {
            return key.EndsWith(".short", StringComparison.Ordinal) ? "a saint's short name" : "a saint's name";
        }
        if (key.StartsWith("lst:formula/", StringComparison.Ordinal))
        {
            return key.EndsWith(".short", StringComparison.Ordinal) ? "a formula's short name" : "a formula's name";
        }
        if (key.StartsWith("cty:", StringComparison.Ordinal))
        {
            var field = key[(key.IndexOf('.', StringComparison.Ordinal) + 1)..];
            return field switch
            {
                "short" => "a city's short name (32 bytes in DARKLAND.CTY)",
                "full" => "a city's full name (32 bytes in DARKLAND.CTY)",
                "leader" or "ruler" => $"the city's {field}, a person's name (32 bytes in DARKLAND.CTY)",
                _ => $"the city's {field.Replace('-', ' ')}, a building's name in card texts (32 bytes in DARKLAND.CTY)",
            };
        }
        if (key.StartsWith("loc:", StringComparison.Ordinal))
        {
            return "a place's name (20 bytes in DARKLAND.LOC; a save keeps the original's)";
        }
        if (key.StartsWith("enm:template/", StringComparison.Ordinal) && key.EndsWith(".name", StringComparison.Ordinal))
        {
            return "an enemy's name in battle (10 bytes in DARKLAND.ENM)";
        }
        if (key.StartsWith("enm:type/", StringComparison.Ordinal))
        {
            return "an enemy kind's name (20 bytes in DARKLAND.ENM)";
        }
        if (key.StartsWith("snt:saint/", StringComparison.Ordinal) && key.EndsWith(".desc", StringComparison.Ordinal))
        {
            return "a saint's description (DARKLAND.SNT)";
        }
        if (key.StartsWith("alc:", StringComparison.Ordinal) && key.EndsWith(".desc", StringComparison.Ordinal))
        {
            return "a formula's description (DARKLAND.ALC)";
        }
        if (key.StartsWith("dsc:", StringComparison.Ordinal) && key.EndsWith(".desc", StringComparison.Ordinal))
        {
            return "a city's description, in the card texts that show $PlaceDesc (80 bytes in DARKLAND.DSC)";
        }
        return null;
    }
}
