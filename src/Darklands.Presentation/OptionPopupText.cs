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
// orig: R043+0x2CE2 (150B:2252) draft (bytes match; compiled without global optimisation); tests: OptionPopupTests
public sealed record OptionPopupText(byte[] Text, OptionState[] States, int Width)
{
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
    // C: the widths are text-width-px of the raw strings, control and colour codes included (glyph-width: a colour code is 0 wide; the box's font 0 starts at code 0, so 0x15 and 0x14 count with their glyph widths, see TextPainter.Advance); the name's width is counted for every status-1 member, listed or not.
    // C: the count is number-with-separators(item-tally(i, item)); rn is the carries-item answer until then.
    public static OptionPopupText Build(CardContext c, bool saints, TextPainter painter, bool darkPicture)
    {
        var state = c.State;
        var list = saints ? c.CardSaints : c.CardItems;
        var basic = state.Difficulty == Difficulty.Basic;
        var all = c.PopupListsAll;
        var states = new OptionState[Lines];
        var text = new List<byte>();
        byte[] name = [];
        var widest = -1;
        var members = state.Party.Members;
        for (var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            if (member.Status == CharacterStatus.Able)
            {
                name = [NameColor, .. TextPainter.Encode(member.ShortName), DefaultColor, (byte)'\n'];
                widest = Math.Max(widest, painter.Measure(member.ShortName));
            }
            var lines = new List<byte>();
            var any = false;
            for (var j = 0; j < 4; j++)
            {
                var entry = list[j];
                if (member.Status is CharacterStatus.Empty or CharacterStatus.Away || entry == -1)
                {
                    lines.AddRange([Option, (byte)'\n']);
                    continue;
                }
                var has = saints ? (Saints.Knows(state, i, entry) ? 1 : 0) : Inventory.Has(state, i, entry);
                var leaderAtBasic = basic && state.Party.LeaderIndex == i;
                if (has == 0 && !leaderAtBasic && !all)
                {
                    lines.AddRange([Option, (byte)'\n']);
                    continue;
                }
                any = true;
                var line = new List<byte> { Option, (byte)' ' };
                var slot = i * 4 + j;
                if (leaderAtBasic && has == 0 && !all)
                {
                    (line, states[slot]) = (Append(line, LeaderOnlyColor), OptionState.Shown);
                }
                else if (!saints && state.Difficulty != Difficulty.Expert && has == 2 && !all)
                {
                    (line, states[slot]) = (Append(line, KnownFormulaColor), OptionState.Shown);
                }
                else if (saints || has == 1 || all)
                {
                    (line, states[slot]) = (Append(line, darkPicture ? (byte)0x8F : (byte)0x80), OptionState.Enabled);
                }
                else
                {
                    (line, states[slot]) = (Append(line, NotUsableColor), OptionState.Hidden);
                }
                line.AddRange(TextPainter.Encode(saints ? SaintName(c, entry) : c.World.ItemName(entry)));
                if (!saints && has == 1)
                {
                    line.AddRange(TextPainter.Encode($" ({c.CountItems(i, entry).ToString("N0", System.Globalization.CultureInfo.InvariantCulture)})"));
                }
                line.Add((byte)'\n');
                widest = Math.Max(widest, painter.Measure(line.ToArray()));
                lines.AddRange(line);
            }
            if (any)
            {
                text.AddRange(name);
                text.AddRange(lines);
                text.Add(Paragraph);
            }
            else
            {
                text.AddRange(lines);
            }
        }
        if (text.Count > 0)
        {
            // C: text-bank-buffer[strlen(text-bank-buffer) - 2] = 0
            text.RemoveRange(Math.Max(0, text.Count - 2), Math.Min(2, text.Count));
        }
        return new OptionPopupText([.. text], states, widest + 6);
    }

    private static List<byte> Append(List<byte> line, byte color)
    {
        line.Add(color);
        return line;
    }

    // orig: R136+0x114F4, the second name list of the saints in darkland.lst (filled by world-read R043+0x0100): the short names
    private static string SaintName(CardContext c, int saint) =>
        saint >= 0 && saint < c.World.Items.Saints.Count ? c.World.Items.Saints[saint].ShortName : "";
}
