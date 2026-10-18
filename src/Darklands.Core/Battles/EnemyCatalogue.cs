namespace Darklands.Core.Battles;

/// <summary>A die roll "1d<see cref="Sides"/> + <see cref="Add"/>", with the quality of what is found.</summary>
public readonly record struct DiceEntry(sbyte Sides, sbyte Add, sbyte Quality);

/// <summary>
/// One enemy template of DARKLAND.ENM (0xCC bytes; <c>struct mtpl</c>): a variant of an enemy
/// kind with its looks, attributes, skills, equipment choices, what it may carry and the dice
/// for group bonuses and group money.
/// </summary>
// orig: mon_templates (R004 enemy buffer +0xB4C0, darkland.enm +0), read by monster_type_map R004+0x06AA
public sealed class EnemyTemplate
{
    public const int Size = 0xCC;

    /// <summary>Index in the template list.</summary>
    public int Index { get; init; }

    /// <summary>+0x00: sprite family ("E02", "M00").</summary>
    public string Image { get; init; } = "";

    /// <summary>+0x04: internal name ("Sergeant1").</summary>
    public string Name { get; init; } = "";

    /// <summary>+0x0E: number of variants on the first template of a kind; −1 on the others.</summary>
    public sbyte VariantCount { get; init; }

    /// <summary>+0x0F: how many colour schemes to choose from.</summary>
    public sbyte ColourSchemes { get; init; }

    /// <summary>+0x10: palettes per colour scheme.</summary>
    public sbyte PalettesPerScheme { get; init; }

    /// <summary>+0x11: first palette in ENEMYPAL.DAT.</summary>
    public sbyte FirstPalette { get; init; }

    /// <summary>+0x12 / +0x13: copied to the kind's +0x27 and +0x26. UNCERTAIN: meaning (+0x12 becomes 2 for missile users and casters).</summary>
    public sbyte Byte12 { get; init; }

    public sbyte Byte13 { get; init; }

    /// <summary>+0x14: attributes (current = maximum).</summary>
    public sbyte[] Attributes { get; init; } = new sbyte[7];

    /// <summary>+0x1B: skills.</summary>
    public sbyte[] Skills { get; init; } = new sbyte[19];

    /// <summary>+0x2E: base load.</summary>
    public sbyte Load { get; init; }

    /// <summary>
    /// +0x30: the effects the enemy does not feel, a bit per effect type 0..11 (copied to the kind's +0x5E,
    /// then to DS:C927 of its combatant, which R019 effect_spell and cbt_step test).
    /// </summary>
    // UNCERTAIN: read as "money" before; the values (gnomes, demons, the dragon 1; gargoyles 2; evil alchemists 0xA00) and
    // R019's (DS:C927[i] &amp; (1 &lt;&lt; type)) make it a mask of effect types.
    public short EffectImmunity { get; init; }

    /// <summary>+0x32: 22 potion dice (potion item codes 0x5F..0x74).</summary>
    public DiceEntry[] PotionDice { get; init; } = new DiceEntry[22];

    /// <summary>+0x74: 10 dice of alchemical components.</summary>
    public DiceEntry[] ComponentDice { get; init; } = new DiceEntry[10];

    /// <summary>+0x92/+0x93 armour types, +0x94/+0x95 their item codes, +0x96 armour quality.</summary>
    public sbyte[] ArmourTypes { get; init; } = new sbyte[2];

    public sbyte[] ArmourItems { get; init; } = new sbyte[2];

    public sbyte ArmourQuality { get; init; }

    /// <summary>+0x97 shield type, +0x98 its item code, +0x99 shield quality.</summary>
    public sbyte ShieldType { get; init; }

    public sbyte ShieldItem { get; init; }

    public sbyte ShieldQuality { get; init; }

    /// <summary>+0x9A missile weapon type, +0x9B its item code, +0x9C its quality.</summary>
    public sbyte MissileType { get; init; }

    public sbyte MissileItem { get; init; }

    public sbyte MissileQuality { get; init; }

    /// <summary>+0x9D ammunition type, +0x9E its item code, +0x9F how many.</summary>
    public sbyte AmmunitionType { get; init; }

    public sbyte AmmunitionItem { get; init; }

    public byte AmmunitionCount { get; init; }

    /// <summary>+0xA0: up to six hand weapon types (−1 ends the list), +0xA6 their item codes, +0xAC weapon quality.</summary>
    public sbyte[] WeaponTypes { get; init; } = new sbyte[6];

    public sbyte[] WeaponItems { get; init; } = new sbyte[6];

    public sbyte WeaponQuality { get; init; }

    /// <summary>+0xAD: per armour piece the type and quality of the item it leaves (−1 nothing, −2.. use the worn one).</summary>
    public (sbyte Type, sbyte Quality)[] ArmourDrops { get; init; } = new (sbyte, sbyte)[2];

    /// <summary>+0xB1/+0xB2: shield drop type/quality.</summary>
    public (sbyte Type, sbyte Quality) ShieldDrop { get; init; }

    /// <summary>+0xB3/+0xB4: weapon drop type/quality.</summary>
    public (sbyte Type, sbyte Quality) WeaponDrop { get; init; }

    /// <summary>+0xB5/+0xB6: missile weapon drop type/quality.</summary>
    public (sbyte Type, sbyte Quality) MissileDrop { get; init; }

    /// <summary>+0xB9: copied to the kind's +0x57. UNCERTAIN.</summary>
    public sbyte ByteB9 { get; init; }

    /// <summary>+0xBA/+0xBC/+0xBE: dice (sides, add) for the strength, endurance and weapon-skill bonus of a group led by this kind.</summary>
    public sbyte[] StrengthBonusDice { get; init; } = new sbyte[2];

    public sbyte[] EnduranceBonusDice { get; init; } = new sbyte[2];

    public sbyte[] SkillBonusDice { get; init; } = new sbyte[2];

    /// <summary>+0xC0..+0xCB: dice sides (florins, groschen, pfennigs) and additions of a group's money.</summary>
    public short[] MoneyDiceSides { get; init; } = new short[3];

    public short[] MoneyDiceAdd { get; init; } = new short[3];
}

/// <summary>One enemy type of DARKLAND.ENM (0x18 bytes; <c>struct etype</c>): the template of its first variant, its name and how it fights.</summary>
// orig: enemy_types (R004 enemy buffer +0xED54, darkland.enm +0x3894)
public sealed class EnemyType
{
    public const int Size = 0x18;

    public int Index { get; init; }

    /// <summary>+0x00: the template it uses.</summary>
    public short Template { get; init; }

    /// <summary>+0x02: its name ("Sergeant").</summary>
    public string Name { get; init; } = "";

    /// <summary>+0x16: bits 0..5 which of the template's weapons it may carry; 0x40 uses missiles; 0x80 casts spells.</summary>
    public byte Flags { get; init; }

    public bool UsesMissiles => (Flags & 0x40) != 0;

    public bool CastsSpells => (Flags & 0x80) != 0;
}

/// <summary>DARKLAND.ENM: 71 templates then 82 enemy types.</summary>
public sealed record EnemyCatalogue(IReadOnlyList<EnemyTemplate> Templates, IReadOnlyList<EnemyType> Types);
