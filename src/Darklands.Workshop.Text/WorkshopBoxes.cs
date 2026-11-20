using Darklands.Localization;
using Darklands.Presentation;

namespace Darklands.Workshop.Text;

/// <summary>
/// The boxes the Workshop uses when a pack has none of its own: the program's shipped boxes (<see cref="ShippedBoxes"/>). A pack's
/// own <c>boxes.json</c> wins over them.
/// </summary>
// Not in the original: the 1.1 Workshop.
public static class WorkshopBoxes
{
    /// <summary>The file's text as embedded (for its test: no word of the game in it).</summary>
    public static string Text => ShippedBoxes.Text;

    /// <summary>The shipped boxes.</summary>
    public static IReadOnlyList<TextBox> Default => ShippedBoxes.Default;
}
