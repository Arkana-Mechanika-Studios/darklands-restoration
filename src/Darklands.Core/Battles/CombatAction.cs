namespace Darklands.Core.Battles;

/// <summary>
/// What a combatant is doing (the combatant word +0x10). The attack modes of the Attack menu
/// combine <see cref="Melee"/> with a stance bit: standard 0x12, berserk 0x06, vulnerable 0x0A,
/// parry 0x22.
/// </summary>
// orig: R016+0x15BE (1EF8:15BE) draft (the order letters W/F A B V P R M T U..H name these codes)
[Flags]
public enum CombatAction : ushort
{
    None = 0,

    /// <summary>Walking (letter W, or F when following).</summary>
    Walk = 0x01,

    /// <summary>Fighting hand to hand.</summary>
    Melee = 0x02,

    /// <summary>Berserk stance (with <see cref="Melee"/>): faster blows.</summary>
    Berserk = 0x04,

    /// <summary>Aiming at weak spots (with <see cref="Melee"/>): slower, more penetration.</summary>
    Vulnerable = 0x08,

    /// <summary>Standard attack stance (with <see cref="Melee"/>).</summary>
    Standard = 0x10,

    /// <summary>Parrying stance (with <see cref="Melee"/>): slower, harder to hit.</summary>
    Parry = 0x20,

    /// <summary>Letter R. UNCERTAIN: what it orders.</summary>
    R = 0x40,

    /// <summary>Using the missile weapon.</summary>
    Missile = 0x80,

    /// <summary>Throwing a potion or using an item (letter T): always hits, does no weapon damage.</summary>
    UseItem = 0x100,

    /// <summary>A place action (door, lock, chest, trap: letters U P D O X H).</summary>
    PlaceAction = 0x400,

    StandardAttack = Melee | Standard,
    BerserkAttack = Melee | Berserk,
    VulnerableAttack = Melee | Vulnerable,
    ParryAttack = Melee | Parry,
}

/// <summary>Whether all bits of a stance are set (the original tests <c>(action &amp; mask) == mask</c>).</summary>
public static class CombatActionExtensions
{
    public static bool Has(this CombatAction action, CombatAction mask) => (action & mask) == mask;
}
