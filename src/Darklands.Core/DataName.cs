namespace Darklands.Core;

/// <summary>
/// A name of the data files given to a sentence as an argument (an item's, a place's ...): its words as shown and its key, so a
/// translation pack's sentence can take the name's other forms (its Fluent attributes: <c>$arg0_acc</c>, <c>$arg0_gender</c> ...).
/// Printed, it is its words: without a pack the sentence is the original's.
/// </summary>
// Not in the original: the 1.1 translations (the data files' names).
public sealed record DataName(string Text, string Key)
{
    /// <summary>The attributes a pack's name may have, each given to a sentence as <c>$argN_&lt;attribute&gt;</c>.</summary>
    public static IReadOnlyList<string> Attributes { get; } = ["nom", "gen", "dat", "acc", "ins", "loc", "voc", "abl", "gender"];

    public override string ToString() => Text;
}
