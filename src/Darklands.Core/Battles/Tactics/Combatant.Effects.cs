namespace Darklands.Core.Battles;

/// <summary>The temporary modifiers of the battle's effects on a combatant (DS:A532, 0x12 bytes per combatant).</summary>
// orig: DS:A532[13] (struct mod) matched; the timers are rebased and tested by fight-main-loop /
// close-combat-rounds R014+0x2A4A and ended by R014+0x0694
public sealed class EffectModifiers
{
    /// <summary>−536 (65000): the timer is not running.</summary>
    public const ushort Off = 65000;

    /// <summary>+0/+1: what was taken from agility (+0x5F) and perception (+0x60).</summary>
    public sbyte[] Attributes { get; } = new sbyte[2];

    /// <summary>+2..+8: what was taken from the first seven skills (+0x6B..).</summary>
    public sbyte[] Skills { get; } = new sbyte[7];

    /// <summary>+9: held (stands, cannot fight but parries).</summary>
    public bool Held { get; set; }

    /// <summary>+0x0A: the action word it had when it was held (restored when the hold ends if it fought hand to hand).</summary>
    public CombatAction SavedAction { get; set; }

    /// <summary>+0x0C: when the attribute modifier ends; +0x0E the skill modifier; +0x10 the hold.</summary>
    public ushort AttributesUntil { get; set; } = Off;

    public ushort SkillsUntil { get; set; } = Off;

    public ushort HeldUntil { get; set; } = Off;

    /// <summary>A copy (the program's DS:A532 kept from one battle to the next).</summary>
    public EffectModifiers Copy()
    {
        var m = new EffectModifiers();
        m.CopyFrom(this);
        return m;
    }

    /// <summary>The values of <paramref name="other"/>, or of a fresh program's zeroed memory (every timer 0) when null.</summary>
    public void CopyFrom(EffectModifiers? other)
    {
        for (var k = 0; k < Attributes.Length; k++)
        {
            Attributes[k] = other?.Attributes[k] ?? 0;
        }
        for (var k = 0; k < Skills.Length; k++)
        {
            Skills[k] = other?.Skills[k] ?? 0;
        }
        Held = other?.Held ?? false;
        SavedAction = other?.SavedAction ?? CombatAction.None;
        AttributesUntil = other?.AttributesUntil ?? 0;
        SkillsUntil = other?.SkillsUntil ?? 0;
        HeldUntil = other?.HeldUntil ?? 0;
    }

    /// <summary>fight-state-reset (R020 06B6): everything off.</summary>
    public void Clear()
    {
        Array.Clear(Attributes);
        Array.Clear(Skills);
        Held = false;
        SavedAction = CombatAction.None;
        AttributesUntil = SkillsUntil = HeldUntil = Off;
    }
}

public sealed partial class Combatant
{
    /// <summary>DS:A532[i]: the effects' temporary modifiers.</summary>
    public EffectModifiers Modifiers { get; } = new();

    /// <summary>
    /// R136+0x35EE[i] (three words): the hit flash of an effect. Word 0 negative counts the
    /// draws until the flash starts (then it becomes the first flash sprite and the hit is
    /// shown); words 1, 2 are the other sprites. DS:DA89[i]: until when it flashes.
    /// </summary>
    // orig: R136+0x35EE (struct fx) and DS:DA89, written by R019 0300/058a, run by R014+0x719E (the renderer)
    public int[] Flash { get; } = new int[3];

    public ushort FlashUntil { get; set; }
}
