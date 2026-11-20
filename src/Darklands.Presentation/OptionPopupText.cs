using Darklands.Cards;
using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Core.Rules;
using Darklands.Presentation.Graphics;

namespace Darklands.Presentation;

/// <summary>
/// The text of an item or saint pop-up box: for each member who has (or may use) one of the card's
/// four items or saints, the member's name and one option line per item or saint. Option k*4 + j is
/// member k's line for the card's slot j; its state says whether it can be chosen.
/// </summary>
/// <param name="Text">The box text with its control codes (option markers 0x15, colour codes 0x80..0x8F, paragraph 0x14).</param>
/// <param name="States">The 20 option states: hidden, enabled, or shown greyed (not choosable).</param>
/// <param name="Width">The box width: the widest line plus 6.</param>
/// <param name="Tail">What text-bank-buffer holds after the text's zero: the last byte the builder cut off, then its zero (empty for an
/// empty text). wrapped-text-size can read it (<see cref="CardTextLayout.MeasureHeight"/>).</param>
// orig: R043+0x2CE2 (150B:2252) draft (bytes match; compiled without global optimisation); tests: OptionPopupTests
public sealed record OptionPopupText(byte[] Text, OptionState[] States, int Width, byte[] Tail)
{
    /// <summary>The text with its zero and <see cref="Tail"/>: text-bank-buffer as the box's size and drawing read it.</summary>
    public byte[] Buffer => [.. Text, 0, .. Tail];

    /// <summary>
    /// The same text as codes (the Unicode text path, <see cref="TextCodes"/>): with a translation pack, the names in its words and
    /// letters; null without one (the bytes, as the original).
    /// </summary>
    // Not in the original: the 1.1 translations (the data files' names in the pop-ups).
    public int[]? Codes { get; init; }

    /// <summary><see cref="Codes"/> with its zero and the tail, as <see cref="Buffer"/>.</summary>
    public int[]? CodeBuffer => Codes is null ? null : [.. Codes, 0, .. Tail.Select(TextCodes.OfByte)];

    /// <summary>Five members times four slots.</summary>
    public const int Lines = 20;

    private const byte Option = 0x15;
    private const byte Paragraph = 0x14;
    private const byte NameColor = 0x81;
    private const byte DefaultColor = 0xFF;
    private const byte LeaderOnlyColor = 0x87;
    private const byte KnownFormulaColor = 0x84;
    private const byte NotUsableColor = 0x83;

    /// <summary>Builds the box of an item line (<paramref name="saints"/> false) or a saint line.</summary>
    /// <param name="darkPicture">The card picture's name starts with 'x' (white text): carried items are listed in colour 15, not 0.</param>
    // orig: R043+0x2CE2(win, unused, kind): kind 1 lists card-item-lines through carries-item (1 carried, 2 formula known), kind 2 card-saint-lines through member-flag-bit; for each member i < party-headcount: status 1 builds "\x81" + name + "\xff\n" (and counts the name's width); every slot j: status 0 or 5 -> "\x15\n"; a listed item/saint the member has, or any at Basic for the leader, or any while DS:5204 -> "\x15 " + colour + name ("\x87" grey and state 2 for the Basic leader who lacks it, "\x84" state 2 for a known formula unless at Expert, "\x80" / "\x8f" state 1 when carried or DS:5204, else "\x83" state 0) + " (count)" when carried + "\n"; otherwise "\x15\n"; a member with any line adds name + lines + "\x14", else only the lines; finally the last two bytes are cut off; returns the widest line + 6
    // C: the name is kept from the last member of status 1: a member of another status (2 unconscious, 3 dead) whose slots qualify gets the previous member's name above his lines; before any status-1 member it is uninitialised stack (DIVERGENCE: empty here).
    // C: the widths are text-width-px of the raw strings, control and colour codes included, in the font of the window passed: the card
    //   window DS:016A, font slot 3 (FONTS.FNT font 2, codes 31..127: a colour code, 0x15, 0x14 and the newline are 0 wide; glyph-width
    //   drv+0x11A9); hooked in the original (vis10 session uncert-polit-popup-hint): window 016A with font 3 at R043+0x2CE2, the box 0x43
    //   wide for "Stone-tar (1)". The name's width is counted for every status-1 member, listed or not.
    // C: the count is number-with-separators(item-tally(i, item)); rn is the carries-item answer until then.
    public static OptionPopupText Build(CardContext c, bool saints, TextPainter painter, bool darkPicture) =>
        Build(c.PopupLines(saints), saints ? k => SaintName(c, k) : c.World.ItemName, painter, darkPicture);

    /// <summary>Lays out a box the rules decided (<see cref="CardContext.PopupLines"/>); <paramref name="entryName"/> names an item or saint.</summary>
    public static OptionPopupText Build(PopupModel model, Func<int, string> entryName, TextPainter painter, bool darkPicture) =>
        Build(model, entryName, painter, darkPicture, codes: false);

    /// <summary>
    /// <see cref="Build(PopupModel, Func{int, string}, TextPainter, bool)"/>, and with <paramref name="codes"/> the text as codes too
    /// (<see cref="Codes"/>: the names as <paramref name="entryName"/> gives them, measured by codes).
    /// </summary>
    // Not in the original: the 1.1 translations; without codes the bytes and widths are the original's.
    public static OptionPopupText Build(PopupModel model, Func<int, string> entryName, TextPainter painter, bool darkPicture, bool codes)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(entryName);
        ArgumentNullException.ThrowIfNull(painter);
        var text = new List<byte>();
        var codeText = new List<int>();
        byte[] name = [];
        int[] codeName = [];
        var widest = -1;
        foreach (var member in model.Members)
        {
            if (member.Status == CharacterStatus.Able)
            {
                name = [NameColor, .. TextPainter.Encode(member.ShortName), DefaultColor, (byte)'\n'];
                codeName = TextCodes.OfBytes(name);
                widest = Math.Max(widest, painter.Measure(member.ShortName));
            }
            var lines = new List<byte>();
            var codeLines = new List<int>();
            foreach (var entry in member.Lines)
            {
                if (entry.Kind == PopupLineKind.Blank)
                {
                    lines.AddRange([Option, (byte)'\n']);
                    codeLines.AddRange(TextCodes.OfBytes([Option, (byte)'\n']));
                    continue;
                }
                var line = new List<byte> { Option, (byte)' ', entry.Kind switch
                {
                    PopupLineKind.LeaderOnly => LeaderOnlyColor,
                    PopupLineKind.KnownFormula => KnownFormulaColor,
                    PopupLineKind.Usable => darkPicture ? (byte)0x8F : (byte)0x80,
                    _ => NotUsableColor,
                } };
                var codeLine = TextCodes.OfBytes(line.ToArray()).ToList();
                var words = entryName(entry.Entry);
                line.AddRange(TextPainter.Encode(words));
                codeLine.AddRange(TextCodes.OfText(words));
                if (entry.Count >= 0)
                {
                    var count = $" ({entry.Count.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)})";
                    line.AddRange(TextPainter.Encode(count));
                    codeLine.AddRange(TextCodes.OfText(count));
                }
                line.Add((byte)'\n');
                codeLine.Add('\n');
                widest = Math.Max(widest, codes ? painter.MeasureCodes(codeLine.ToArray()) : painter.Measure(line.ToArray()));
                lines.AddRange(line);
                codeLines.AddRange(codeLine);
            }
            if (member.Listed)
            {
                text.AddRange(name);
                text.AddRange(lines);
                text.Add(Paragraph);
                codeText.AddRange(codeName);
                codeText.AddRange(codeLines);
                codeText.Add(Paragraph);
            }
            else
            {
                text.AddRange(lines);
                codeText.AddRange(codeLines);
            }
        }
        byte[] tail = [];
        if (text.Count > 0)
        {
            // C: text-bank-buffer[strlen(text-bank-buffer) - 2] = 0: the last byte (0x14 after a member's lines, else the newline of the last
            //   empty slot) and the string's own zero stay behind the new end
            tail = text.Count >= 2 ? [text[^1], 0] : [];
            text.RemoveRange(Math.Max(0, text.Count - 2), Math.Min(2, text.Count));
            codeText.RemoveRange(Math.Max(0, codeText.Count - 2), Math.Min(2, codeText.Count));
        }
        return new OptionPopupText([.. text], model.States, widest + 6, tail) { Codes = codes ? [.. codeText] : null };
    }

    // orig: R136+0x114F4, the second name list of the saints in darkland.lst (filled by world-read R043+0x0100): the short names
    internal static string SaintName(CardContext c, int saint) =>
        saint >= 0 && saint < c.World.Items.Saints.Count ? c.World.Items.Saints[saint].ShortName : "";
}
