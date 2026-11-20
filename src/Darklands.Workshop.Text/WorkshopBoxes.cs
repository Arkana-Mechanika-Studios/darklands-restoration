using Darklands.Localization;

namespace Darklands.Workshop.Text;

/// <summary>
/// The boxes the Workshop ships (docs/LOCALISATION.md): the texts' widths, fonts and screens as the developer run of the recorded
/// sessions drew them, without the game's words (TextBoxes.Strip: each argument is filler of its length). A pack's own
/// <c>boxes.json</c> wins over them. Regenerated with <c>DarklandsRP --text-boxes &lt;run&gt;/boxes --strip-text-boxes
/// src/Darklands.Workshop.Text/Resources/boxes.json</c>.
/// </summary>
// Not in the original: the 1.1 Workshop.
public static class WorkshopBoxes
{
    private static IReadOnlyList<TextBox>? _default;

    /// <summary>The file's text as embedded (for its test: no word of the game in it).</summary>
    public static string Text
    {
        get
        {
            using var stream = typeof(WorkshopBoxes).Assembly.GetManifestResourceStream("Darklands.Workshop.Text.boxes.json")
                ?? throw new InvalidOperationException("the Workshop's boxes are not embedded");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }

    /// <summary>The shipped boxes.</summary>
    public static IReadOnlyList<TextBox> Default => _default ??= TextBoxes.Parse(Text);
}
