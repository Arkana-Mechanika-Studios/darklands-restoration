using Darklands.Core.Characters;

namespace Darklands.Core.Battles;

/// <summary>
/// One of the 13 fighters of a battle (0..4 the party, 5..12 the enemies): the original's
/// 0x80-byte combatant record (<c>combatants</c>, DS:9C55; a party member's record is its
/// character record) and the battle's parallel per-combatant tables. Byte fields keep the
/// original's width and sign: they are <see cref="sbyte"/> or <see cref="byte"/> as the code
/// reads them.
/// </summary>
// orig: combatants DS:9C55 (struct cbt, 13 × 0x80); per-combatant tables cbt_attack_rate DS:D493, cbt_queued_damage DS:D70C, cbt_damage_shown DS:D4C7, cbt_floor DS:A6B4
public sealed partial class Combatant
{
    public const int PartySlots = 5;
    public const int Count = 13;
    public const int EnemyItemSlots = 16;

    public Combatant(int index)
    {
        Index = index;
    }

    /// <summary>Slot 0..12 (+0x1C for enemies).</summary>
    public int Index { get; }

    public bool IsPartyMember => Index < PartySlots;

    /// <summary>The party member this combatant stands for (party slots only).</summary>
    public Character? Member { get; set; }

    /// <summary>+0x00 / +0x02 (scrx, scry): the sprite's top left in the view where the last frame drew it (cbt_screen_pos R014+0x99F6).</summary>
    public (int X, int Y) ScreenPlace { get; set; }

    /// <summary>Map position in pixels (+0x04, +0x06).</summary>
    public MapPoint Position { get; set; }

    /// <summary>Where it walks to (+0x08, +0x0A).</summary>
    public MapPoint Destination { get; set; }

    /// <summary>+0x10.</summary>
    public CombatAction Action { get; set; }

    /// <summary>+0x14: 0 none, 1 able, 2 unconscious, 3 dead, 5 left the field.</summary>
    public CharacterStatus Status { get; set; }

    /// <summary>+0x16: the animation set slot (0..4 party, 5/6 the two enemy kinds).</summary>
    public int AnimationSlot { get; set; }

    /// <summary>+0x19 facing of the path, +0x1A facing of the sprite (0..7).</summary>
    public int Facing { get; set; }

    public int SpriteFacing { get; set; }

    /// <summary>+0x1D: ticks still to aim a missile.</summary>
    public sbyte AimDelay { get; set; }

    /// <summary>
    /// +0x20: fighting value with the readied weapon (skill adjusted for strength and the
    /// weapon's minimum skill), used by the close-combat chance on both sides.
    /// UNCERTAIN: the name (the draft calls it "def").
    /// </summary>
    public byte FightingValue { get; set; }

    /// <summary>+0x21: the lesser-penalised skill value, used by the missile chance. UNCERTAIN: the name (the draft's "atk").</summary>
    public byte AimingValue { get; set; }

    /// <summary>+0x22: the readied weapon's type (−1 none); for party members the missile weapon slot.</summary>
    public sbyte ReadiedWeapon { get; set; } = -1;

    /// <summary>+0x23: minimum skill of the readied weapon.</summary>
    public sbyte ReadiedMinimumSkill { get; set; }

    /// <summary>+0x24: halved and added to the armour quality against armour-piercing blows. UNCERTAIN: meaning.</summary>
    public sbyte PiercingResistance { get; set; }

    /// <summary>+0x0C: the readied weapon's last table value (missile range?). UNCERTAIN.</summary>
    public sbyte WeaponRange { get; set; }

    /// <summary>+0x0D: the quality the damage roll compares with armour quality (weapon or missile quality).</summary>
    public sbyte StrikeQuality { get; set; }

    /// <summary>+0x48: high nibble extra damage, low nibble extra penetration (potions).</summary>
    public byte WeaponBonus { get; set; }

    /// <summary>+0x49: weight carried.</summary>
    public sbyte Load { get; set; }

    /// <summary>+0x4A: who it fights (−1 nobody).</summary>
    public sbyte Target { get; set; } = -1;

    /// <summary>+0x4B/+0x4C: armour types worn (vital, limbs; −1 none).</summary>
    public sbyte[] ArmourType { get; } = [-1, -1];

    /// <summary>+0x4D/+0x4E: armour value per hit location (from the armour table).</summary>
    public sbyte[] ArmourValue { get; } = new sbyte[2];

    /// <summary>+0x4F/+0x50: armour quality per hit location.</summary>
    public sbyte[] ArmourQuality { get; } = [25, 25];

    /// <summary>+0x51: the hand weapon's type (the default weapon).</summary>
    public sbyte DefaultWeapon { get; set; } = -1;

    /// <summary>+0x52: speed of the readied weapon.</summary>
    public byte WeaponSpeed { get; set; }

    /// <summary>+0x53: high nibble the weapon's handling class (1: a shield can be used), low nibble its penetration.</summary>
    public byte WeaponClass { get; set; }

    /// <summary>+0x54: damage of the readied weapon.</summary>
    public sbyte WeaponDamage { get; set; }

    /// <summary>+0x55: bit 7 blows pierce armour; low 7 bits natural armour.</summary>
    public byte Protection { get; set; }

    /// <summary>+0x56: strength below which the weapon is too heavy.</summary>
    public sbyte MinimumStrength { get; set; }

    /// <summary>+0x57: low 7 bits strength above which blows hit harder; bit 7 set while the missile weapon is readied.</summary>
    public byte FullStrength { get; set; }

    /// <summary>+0x58: quality of the hand weapon.</summary>
    public sbyte WeaponQuality { get; set; } = 25;

    /// <summary>+0x59: skill with the readied weapon.</summary>
    public sbyte ReadiedSkill { get; set; }

    /// <summary>+0x5A: quality of the missile weapon.</summary>
    public sbyte MissileQuality { get; set; } = 25;

    /// <summary>+0x5B: shield quality (the shield's value in the chances).</summary>
    public sbyte ShieldQuality { get; set; } = 25;

    /// <summary>+0x5C: shield type (0x5F small, 0x60 medium, 0x61 large).</summary>
    public sbyte ShieldType { get; set; } = -1;

    /// <summary>+0x5D: current attributes.</summary>
    public sbyte[] Attributes { get; } = new sbyte[Character.AttributeCount];

    /// <summary>+0x64: attribute maxima.</summary>
    public sbyte[] Maximums { get; } = new sbyte[Character.AttributeCount];

    /// <summary>+0x6B: skills.</summary>
    public sbyte[] Skills { get; } = new sbyte[Character.SkillCount];

    /// <summary>
    /// The items carried (party: the member's 64 pack entries with their count, stale entries past the last stack
    /// included; enemies: up to 16).
    /// </summary>
    // orig: party members' items are the pack itself (2E38:003E + i × 0x180), the count the record's +0x7E; enemies' 2E38:0DBE + (i − 5) × 0x60
    public Pack Items { get; } = new();

    public sbyte Endurance
    {
        get => Attributes[(int)Characters.Attribute.Endurance];
        set => Attributes[(int)Characters.Attribute.Endurance] = value;
    }

    public sbyte Strength
    {
        get => Attributes[(int)Characters.Attribute.Strength];
        set => Attributes[(int)Characters.Attribute.Strength] = value;
    }

    public sbyte Agility => Attributes[(int)Characters.Attribute.Agility];

    public sbyte MaximumEndurance => Maximums[(int)Characters.Attribute.Endurance];

    public sbyte MaximumStrength => Maximums[(int)Characters.Attribute.Strength];

    /// <summary>cbt_attack_rate: chance in 100 of a blow each tick; 255 = to be worked out again.</summary>
    public byte AttackRate { get; set; } = 0xFF;

    /// <summary>cbt_queued_damage: endurance and strength losses applied at the next damage step.</summary>
    public sbyte QueuedEndurance { get; set; }

    public sbyte QueuedStrength { get; set; }

    /// <summary>cbt_damage_shown: endurance lost since the last display.</summary>
    public sbyte DamageShown { get; set; }

    /// <summary>cbt_floor: the level it is on (−99 left through an exit).</summary>
    public int Floor { get; set; }

    /// <summary>enemy_class: 0 hand to hand, 1 missiles, 2 casts spells.</summary>
    public int EnemyClass { get; set; }

    public bool IsAble => Status == CharacterStatus.Able;

    /// <summary>cbt_half_w / cbt_half_h (DS:C80F, DS:C831): half the collision box, from the animation set's header.</summary>
    public int HalfWidth { get; set; } = 5;

    public int HalfHeight { get; set; } = 2;

    /// <summary>cbt_moving (DS:A6D3): following a path.</summary>
    public bool Moving { get; set; }

    /// <summary>DS:D68D (party): which skills were used in this battle, for the skill improvement at its end.</summary>
    public SkillUse SkillsUsed { get; set; }

    /// <summary>DS:C927: the effect types it does not feel (bit per type 0..11; 0 for party members, R002 1830).</summary>
    public short EffectImmunity { get; set; }
}

/// <summary>The skills a party member used in a battle (DS:D68D bits).</summary>
// orig: R014+0x0694 (0E76:0694) draft (reads them); R014 0E76 199C/1BA0 set them
[Flags]
public enum SkillUse : byte
{
    None = 0,

    /// <summary>Struck with the hand weapon.</summary>
    HandWeapon = 0x01,

    /// <summary>Shot the missile weapon.</summary>
    Missile = 0x02,

    /// <summary>Threw something (skill 4).</summary>
    Thrown = 0x04,

    /// <summary>Hurt an enemy with the hand weapon (the better chance).</summary>
    HandWeaponHit = 0x08,

    /// <summary>Hurt an enemy with the missile weapon.</summary>
    MissileHit = 0x10,

    /// <summary>Picked a lock or disarmed a trap (Artifice, the better chance).</summary>
    ArtificeSucceeded = 0x20,

    /// <summary>Tried Artifice.</summary>
    ArtificeTried = 0x40,
}
