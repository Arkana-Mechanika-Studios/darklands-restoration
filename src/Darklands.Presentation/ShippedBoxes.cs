using Darklands.Localization;

namespace Darklands.Presentation;

/// <summary>
/// The boxes the program ships (docs/LOCALISATION.md): the texts' widths, fonts and screens as the developer run of the recorded
/// sessions drew them, without the game's words (TextBoxes.Strip: each argument is filler of its length). The translator's
/// template, the pack check and the Workshop use them when no other boxes are given (a pack's own <c>boxes.json</c> or
/// <c>--text-boxes</c> win over them). Regenerated with <c>DarklandsRP --text-boxes &lt;run&gt;/boxes --strip-text-boxes
/// src/Darklands.Presentation/Resources/boxes.json</c>.
/// </summary>
// Not in the original: the 1.1 translations.
public static class ShippedBoxes
{
    private static IReadOnlyList<TextBox>? _default;

    /// <summary>The file's text as embedded (for its test: no word of the game in it).</summary>
    public static string Text
    {
        get
        {
            using var stream = typeof(ShippedBoxes).Assembly.GetManifestResourceStream("Darklands.Presentation.Resources.boxes.json")
                ?? throw new InvalidOperationException("the shipped boxes are not embedded");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }

    /// <summary>The shipped boxes.</summary>
    public static IReadOnlyList<TextBox> Default => _default ??= TextBoxes.Parse(Text);
}
