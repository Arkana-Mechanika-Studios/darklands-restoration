using Darklands.Content;
using Darklands.Localization;
using Darklands.Presentation.Graphics;

namespace Darklands.Presentation;

/// <summary>
/// The boxes of texts drawn in a column whose room the screens' code fixes, for the keys the recorded sessions never drew (so the
/// shipped boxes have none): the template's comments, the pack check and the Workshop measure a translation against them as against
/// the recorded ones. A box the shipped (or a pack's own) boxes have wins. Today: the formulae's short names in the alchemy screen's
/// batch list.
/// </summary>
// Not in the original: the 1.2 translations (the untranslated-text audit, issue 5).
public static class ColumnBoxes
{
    // orig: R033 (the batch page, AlchemyScreen.DrawBatchPage): the short name at x 0xDE in font 1, the row's highlight band from x 0xC1
    //   (ListX + 1), 0x5A wide: 61 pixels to the band's end; the first row at y 0x2D
    private const int ShortNameX = 0xDE;
    private const int BandRight = 0xC0 + 1 + 0x5A;
    private const int FirstRow = 0x2D;

    /// <summary>The room of a formula's short name in the batch list (pixels).</summary>
    public const int FormulaShortRoom = BandRight - ShortNameX;

    /// <summary>The boxes of the fixed columns for the game in <paramref name="assets"/> (the English measured in its own font).</summary>
    public static IReadOnlyList<TextBox> For(GameAssets assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        var font = new TextPainter(assets.Font(1));
        var formulae = WorldData.Load(assets.Files).Items.Formulae;
        return [.. formulae.Select(f => new TextBox(DataNames.FormulaShort(f.Index), font.Measure(f.ShortName), FormulaShortRoom, true, 1, null)
        {
            Screens = ["AlchemyScreen"],
            X = ShortNameX,
            Y = FirstRow,
        })];
    }

    /// <summary><paramref name="boxes"/> with the fixed columns' boxes for the keys it has none of.</summary>
    public static IReadOnlyList<TextBox> With(IReadOnlyList<TextBox> boxes, GameAssets assets)
    {
        ArgumentNullException.ThrowIfNull(boxes);
        var known = boxes.Select(b => b.Key).ToHashSet(StringComparer.Ordinal);
        return [.. boxes, .. For(assets).Where(b => !known.Contains(b.Key))];
    }
}
