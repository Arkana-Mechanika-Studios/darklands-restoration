namespace Darklands.Core.Items;

/// <summary>An item of the game's item list (DARKLAND.LST): what every stack of that code is.</summary>
/// <param name="Code">The item code (index in the list).</param>
/// <param name="Type">The item type (equipment slots compare types).</param>
/// <param name="Flags">The item's kind bits (see <see cref="ItemFlags"/>), the first 32 of the record.</param>
/// <param name="Weight">Weight of one item.</param>
/// <param name="Quality">Default quality.</param>
/// <param name="Value">Base price (a signed 16-bit word in the list).</param>
/// <param name="Rarity">How rarely shops stock it (a signed byte; 0 common .. 11 rare).</param>
/// <param name="TradeFlags">The fifth kind byte; bit 0x80: shops neither sell nor buy it.</param>
public sealed record ItemDefinition(int Code, string Name, string ShortName, int Type, uint Flags, int Weight, int Quality, int Value,
    int Rarity = 0, int TradeFlags = 0)
{
    /// <summary>Shops neither sell nor buy the item (quest items, relics ...).</summary>
    public bool CannotBeTraded => (TradeFlags & 0x80) != 0;
}

/// <summary>Item kind bits the rules test.</summary>
public static class ItemFlags
{
    /// <summary>A hand weapon (edged, impact, flail, polearm): bits 0–3.</summary>
    public const uint HandWeapon = 0x0000_000F;

    /// <summary>A missile weapon (thrown, bow, missile device).</summary>
    public const uint MissileWeapon = 0x0800_0030;

    /// <summary>A mount (horse): <c>party_get(5)</c> and the riding speed use it.</summary>
    public const uint Mount = 0x0000_2000;
}

/// <summary>Potions: item code = <see cref="FirstPotion"/> + formula / 3 (grade = formula % 3).</summary>
public static class Potions
{
    public const int FirstPotion = 0x5F;

    /// <summary>Potion item codes 0x5F..0x74, one per formula group of three.</summary>
    public const int Count = 22;
}
