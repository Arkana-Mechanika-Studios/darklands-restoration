using Darklands.Core.Economy;

namespace Darklands.Core;

/// <summary>
/// The rules' numbers a rule set (later a rules mod) may change, beside the fix flags of <see cref="OriginalBehaviour"/>: each holds the
/// original's value by default, and each site that reads one does nothing else with it, so at <see cref="Original"/> every site plays
/// exactly as the original. Immutable: a game is given one and keeps it. Nothing lets a player change it yet.
/// </summary>
// Not in the original: the Rules axis of the presets and the mods (the extensibility plan's rule parameters).
public sealed record RuleParameters
{
    /// <summary>The original's numbers everywhere.</summary>
    public static RuleParameters Original { get; } = new();

    /// <summary>The overland's minutes per step (Terrain.MinutesPerStep, OverlandTravel), scaled; a scaled step takes at least a minute.</summary>
    public RuleScale StepMinutes { get; init; } = RuleScale.One;

    /// <summary>A day's lodging at an inn or other lodging (CampStay.LodgingPrice, the inn cards' price), scaled.</summary>
    public RuleScale LodgingPrice { get; init; } = RuleScale.One;

    /// <summary>The damage a blow queues on an enemy (Attacks.QueueDamage), scaled.</summary>
    public RuleScale DamageToEnemies { get; init; } = RuleScale.One;

    /// <summary>The damage a blow queues on a member of the party (Attacks.QueueDamage), scaled.</summary>
    public RuleScale DamageToParty { get; init; } = RuleScale.One;

    /// <summary>The chance in a card's practice of a skill (Practice.PracticeSkill's percent), scaled.</summary>
    public RuleScale PracticeChance { get; init; } = RuleScale.One;

    /// <summary>How many times the end of a battle tries to raise each skill used (BattleEnd.TryImprove): 1 in the original.</summary>
    public int BattleSkillTries { get; init; } = 1;

    /// <summary>The Quickstart party's every attribute (current and maximum) set to this value; none: the original's records.</summary>
    public int? QuickstartAttributes { get; init; }

    /// <summary>The Quickstart party's every skill set to this value; none: the original's records.</summary>
    public int? QuickstartSkills { get; init; }

    /// <summary>The purse a new game starts with (the opening card's), after its draws; none: the original's 8–10 groschen and 10–12 pfennigs.</summary>
    public Money? StartingMoney { get; init; }

    /// <summary>Whether every number is the original's.</summary>
    public bool IsOriginal => Equals(Original);
}

/// <summary>A ratio a rule's number is multiplied by, rounded up; 1/1 gives the number itself.</summary>
public readonly record struct RuleScale(int Numerator, int Denominator)
{
    /// <summary>The original's number, unchanged.</summary>
    public static RuleScale One { get; } = new(1, 1);

    /// <summary><paramref name="value"/> scaled and rounded up (towards positive infinity); unchanged at 1/1.</summary>
    public int Apply(int value)
    {
        if (Numerator == Denominator)
        {
            return value;
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Denominator);
        var product = (long)value * Numerator;
        var quotient = product / Denominator;
        return (int)(product % Denominator > 0 ? quotient + 1 : quotient);
    }
}
