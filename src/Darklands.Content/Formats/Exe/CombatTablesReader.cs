using Darklands.Core.Battles;

namespace Darklands.Content.Formats.Exe;

/// <summary>Reads the battle's weapon, armour and damage tables from the player's DARKLAND.EXE.</summary>
public static class CombatTablesReader
{
    /// <summary>The far data segment holding the weapon tables.</summary>
    public const int WeaponSegment = 0x20A5;

    // orig: R136+0x75AC..R136+0x7822 (weapon and armour tables read by weapon-armed R014+0xAED0), DS:1B88/DS:1B90 (low damage tables), DS:22CE/DS:22D2 (skill gains), DS:13D0 (castle and mine neighbourhood patterns)
    public static CombatTables Read(DarklandsExecutable exe) => new(
        exe.Far(WeaponSegment, CombatTables.FirstOffset, CombatTables.Length).ToArray(),
        exe.DataSegment(0x1B88, 16).ToArray(),
        exe.DataSegment(0x22CE, 7).ToArray(),
        exe.DataSegment(0x13D0, 32).ToArray());
}
