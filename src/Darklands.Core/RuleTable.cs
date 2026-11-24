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
