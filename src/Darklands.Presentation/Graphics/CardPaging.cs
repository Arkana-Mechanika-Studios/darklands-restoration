using Darklands.Cards;

namespace Darklands.Presentation.Graphics;

/// <summary>
/// A translation's card text in pages (docs/LOCALISATION.md, card texts): when a pack's text runs past the foot of the card's window,
/// it is shown in pages, each but the last with a "more" prompt at its foot, the option lines together on the last page. A page
/// ends between lines (after a line break) or, in a paragraph taller than a page, at a space: never inside a word.
/// </summary>
/// <param name="Pages">The pages' codes, in order.</param>
/// <param name="CutAtSpace">For each page, whether it ends at a space inside a line (the space is left out of both pages).</param>
// Not in the original: the 1.1 translations (a text longer than the original's could not be drawn whole). Only the Unicode text path
//   (a pack's card text) is ever paged; the original's bytes never are.
public sealed record CardPaging(IReadOnlyList<int[]> Pages, IReadOnlyList<bool> CutAtSpace)
{
    /// <summary>The pages joined again: the text they were cut from (with its passages resolved).</summary>
    public int[] Joined()
    {
        var all = new List<int>();
        for (var k = 0; k < Pages.Count; k++)
        {
            all.AddRange(Pages[k]);
            if (CutAtSpace[k])
            {
                all.Add(' ');
            }
        }
        return [.. all];
    }

    /// <summary>
    /// The pages of a card text block's expanded text as the card screen lays it out (at the block's place, the card window's font,
    /// the passages <paramref name="conditions"/> shows, the option lines' <paramref name="options"/>, the decorated capital on the
    /// first page), each page above the foot of the card's text area (and, but the last, above the prompt's line); null when the text
    /// fits whole or cannot be paged.
    /// </summary>
    public static CardPaging? ForBlock(TextPainter painter, Content.MessageBlock block, int[] expanded, IReadOnlyList<bool> conditions,
        IReadOnlyList<OptionState> options, Func<byte, Content.Formats.Graphics.IndexedImage?>? dropCap)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(block);
        var text = Resolve(expanded, conditions);
        var bottom = WindowFoot;
        var prompt = painter.LineHeight + 1;
        bool Fits(int[] page, bool first, bool last)
        {
            // measured in colour 15 on a blank canvas (a card's text colour may be 0), as the pack check measures
            var scratch = new Canvas(new Darklands.Platform.Screen());
            new CardTextLayout(painter).Draw(scratch, page, CardScreen.WindowX + block.X + 5, block.Y + 5, block.Width - 10, new CardTextLayout.Settings
            {
                Options = last ? options : [],
                Color = 15,
                OptionColor = 15,
                DropCap = first ? dropCap : null,
            });
            return Foot(scratch) < bottom - (last ? 0 : prompt);
        }
        return Of(text, Fits);
    }

    /// <summary>The foot of the card's text area: a page ends above it.</summary>
    public static int WindowFoot => CardScreen.TextArea.Y + CardScreen.TextArea.Height;

    /// <summary>The lowest row of the card window a drawing reached (0: none).</summary>
    public static int Foot(Canvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var pixels = canvas.Screen.Pixels;
        for (var row = Canvas.Height - 1; row >= 0; row--)
        {
            for (var column = CardScreen.WindowX; column < Canvas.Width; column++)
            {
                if (pixels[row * Canvas.Width + column] != 0)
                {
                    return row;
                }
            }
        }
        return 0;
    }

    /// <summary>
    /// <paramref name="text"/> without the conditional passages <paramref name="conditions"/> hides and without the markers of those
    /// it shows (the layout shows such a text as it shows the original, the passages chosen), so no page break falls in a passage.
    /// </summary>
    public static int[] Resolve(int[] text, IReadOnlyList<bool> conditions)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(conditions);
        var result = new List<int>(text.Length);
        var number = 0;
        for (var i = 0; i < text.Length && text[i] != 0; i++)
        {
            var c = text[i];
            if (c == CardTextLayout.ConditionStart)
            {
                var shown = number < conditions.Count && conditions[number];
                number++;
                if (!shown)
                {
                    while (i + 1 < text.Length && text[i + 1] != CardTextLayout.ConditionEnd && text[i + 1] != 0)
                    {
                        i++;
                    }
                    i++;   // the passage's end marker
                }
                continue;
            }
            if (c != CardTextLayout.ConditionEnd)
            {
                result.Add(c);
            }
        }
        return [.. result];
    }

    /// <summary>
    /// The pages of <paramref name="text"/> (its passages resolved), or null when it fits whole or cannot be paged (its option lines
    /// alone do not fit, or a word is wider than a page). <paramref name="fits"/> tells whether a page fits: (its codes, whether it is
    /// the first page (the decorated capital), whether it is the last (no prompt)).
    /// </summary>
    public static CardPaging? Of(int[] text, Func<int[], bool, bool, bool> fits)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(fits);
        if (fits(text, true, true))
        {
            return null;
        }
        var first = Array.FindIndex(text, CardTextLayout.IsOptionMarker);
        var prose = first < 0 ? text : text[..first];
        int[] options = first < 0 ? [] : text[first..];
        // the units: lines up to and with their line break
        var units = new LinkedList<int[]>();
        var start = 0;
        for (var i = 0; i < prose.Length; i++)
        {
            if (prose[i] == '\n')
            {
                units.AddLast(prose[start..(i + 1)]);
                start = i + 1;
            }
        }
        if (start < prose.Length)
        {
            units.AddLast(prose[start..]);
        }
        var pages = new List<int[]>();
        var cuts = new List<bool>();
        var current = new List<int>();
        while (units.First is { } node)
        {
            var unit = node.Value;
            units.RemoveFirst();
            int[] candidate = [.. current, .. unit];
            if (fits(candidate, pages.Count == 0, false))
            {
                current.AddRange(unit);
                continue;
            }
            if (current.Count > 0)
            {
                pages.Add([.. current]);
                cuts.Add(false);
                current.Clear();
                units.AddFirst(unit);
                continue;
            }
            // a line taller than a page: cut at the last space whose part still fits
            var spaces = Enumerable.Range(1, Math.Max(0, unit.Length - 1)).Where(k => unit[k] == ' ').ToList();
            int lo = 0, hi = spaces.Count - 1, best = -1;
            while (lo <= hi)
            {
                var mid = (lo + hi) / 2;
                if (fits(unit[..spaces[mid]], pages.Count == 0, false))
                {
                    best = mid;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }
            if (best < 0)
            {
                return null;
            }
            pages.Add(unit[..spaces[best]]);
            cuts.Add(true);
            units.AddFirst(unit[(spaces[best] + 1)..]);
        }
        int[] last = [.. current, .. options];
        if (fits(last, pages.Count == 0, true))
        {
            pages.Add(last);
            cuts.Add(false);
        }
        else
        {
            if (current.Count > 0)
            {
                pages.Add([.. current]);
                cuts.Add(false);
            }
            if (!fits(options, pages.Count == 0, true))
            {
                return null;
            }
            pages.Add(options);
            cuts.Add(false);
        }
        return pages.Count > 1 ? new CardPaging(pages, cuts) : null;
    }
}
