namespace Darklands.Core;

/// <summary>A set of rules a game plays with.</summary>
public enum RulePreset
{
    /// <summary>Every bug as in the original: the oracles, the headless host and the lockstep proofs. Not offered to players.</summary>
    Faithful,

    /// <summary>The player's reference: the original's rules with the data-safety and visual fixes (<see cref="OriginalBehaviour.DataSafe"/>).</summary>
    Original,

    /// <summary>Original and every gameplay fix (not offered to players yet).</summary>
    Fixed,

    /// <summary>Fixed and the restoration items (none yet; not offered to players).</summary>
    Restored,
}

/// <summary>The rules presets' names in the settings file, on the command line and in a saved game's record.</summary>
// Not in the original: the rules setting (rules.preset).
public static class RulePresets
{
    /// <summary>
    /// Whether the Fixed preset plays as itself yet. False until the gameplay fixes are switched on for players (rules set A3): until
    /// then a choice of Fixed plays, and is recorded, as Original (<see cref="ForNewGames"/>).
    /// </summary>
    public const bool FixedPlayable = false;

    /// <summary>The presets a player chooses between, in the launcher's order.</summary>
    public static IReadOnlyList<RulePreset> Offered { get; } = [RulePreset.Original, RulePreset.Fixed];

    /// <summary>The preset of a name ("original", "fixed", "restored", "faithful"; any case), or null.</summary>
    public static RulePreset? Parse(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "original" => RulePreset.Original,
        "fixed" => RulePreset.Fixed,
        "restored" => RulePreset.Restored,
        "faithful" => RulePreset.Faithful,
        _ => null,
    };

    /// <summary>A preset's name in files ("original", "fixed" ...).</summary>
    public static string Name(RulePreset preset) => preset.ToString().ToLowerInvariant();

    /// <summary>A preset's name for the player ("Original", "Fixed" ...).</summary>
    public static string Title(RulePreset preset) => preset.ToString();

    /// <summary>The preset a new game plays with when the player chose <paramref name="chosen"/>: Fixed and Restored play as Original until <see cref="FixedPlayable"/>.</summary>
    public static RulePreset ForNewGames(RulePreset chosen) =>
        chosen is RulePreset.Fixed or RulePreset.Restored && !FixedPlayable ? RulePreset.Original : chosen;
}

/// <summary>
/// The fix flags of the switch register (docs/faithfulness/switches.csv): each flag's register id (<c>fix.&lt;area&gt;.&lt;name&gt;</c>),
/// its <see cref="OriginalBehaviour"/> property, its class and the presets it is on in. The list itself is generated from the CSV
/// (RuleTable.Generated.cs, by tools/faithfulness/rule_table.py), not written by hand.
/// </summary>
// Not in the original: the keyed rule table of the presets.
public static partial class RuleTable
{
    /// <summary>One fix flag: its register id, property, class, and whether the Original, Fixed and Restored presets turn it on.</summary>
    public sealed record Rule(string Id, string Property, string Class, bool Original, bool Fixed, bool Restored)
    {
        /// <summary>Whether <paramref name="preset"/> turns the flag on (the Faithful set turns nothing on).</summary>
        public bool In(RulePreset preset) => preset switch
        {
            RulePreset.Original => Original,
            RulePreset.Fixed => Fixed,
            RulePreset.Restored => Restored,
            _ => false,
        };
    }

    /// <summary>The index of the flag with register id <paramref name="id"/>, or -1 when no flag has it.</summary>
    public static int IndexOf(string id) => id is not null && Ids.ById.TryGetValue(id, out var index) ? index : -1;

    // built on first use, after Flags (static initializers of two files of a partial class have no set order)
    private static class Ids
    {
        public static readonly Dictionary<string, int> ById = Flags.Select((rule, index) => (rule.Id, index)).ToDictionary(p => p.Id, p => p.index, StringComparer.Ordinal);
    }
}
