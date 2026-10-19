using Darklands.Content;
using Darklands.Content.Formats.Graphics;
using Darklands.Core.Characters;
using Darklands.Presentation.Graphics;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Presentation;

/// <summary>
/// The party column on the left of card and map screens: for each member a 40-pixel row with
/// shield, name and three bars (endurance, strength, divine favour).
/// </summary>
// orig: party_panel_rows root 0000:1716 draft (matches the bytes) + member_bars root 0000:1A9C (draft, "cannot match": 6.00 saves SI/DI around the inline strcpy/strcat; written for documentation) + party_panel_frames root 0000:1588 (matches); tests: none (drawing)
// DIVERGENCE: the status texts that replace the bars while a member is "away" is drawn only as bars here (see Draw); the combat extras of member_bars (the carried member's bars, the status-letter column of member_status_letter root 0000:18FA) belong to battles (BattleScreen.DrawPanel).
// DIVERGENCE: the original copies each finished row from the off-screen page to the screen (copy_rect_clipped) and hides the mouse while it does; here it is drawn straight into the screen.
public sealed class PartyPanel(GameAssets assets)
{
    public const int Width = 60;
    public const int RowHeight = 40;

    /// <summary>The page window's last column while the values are drawn (DS:0168->x1 = 0x38).</summary>
    private const int ValuesRight = 0x38;

    // orig: the panel is drawn on the page window DS:0156 (pointer DS:0168), whose font is slot 1 (font 0 of FONTS.FNT); see GameUi.Text
    private readonly TextPainter _text = new(assets.Font(1));

    // the shield picture a slot holds when it is not the one of the member's letter (the inn's roster loads PICS\SHIELDA.PIC for a new member)
    private readonly (Character Member, char Letter)?[] _shieldPictures = new (Character, char)?[Party.MaxMembers];

    /// <summary>
    /// The slot's shield picture is the one of <paramref name="letter"/> while it holds <paramref name="member"/>, whatever the member's
    /// own letter (the picture loaded for the slot; a saved game loaded again takes the letter's).
    /// </summary>
    // orig: the slot's shield picture handle (DS:A2D7 + 2 x slot), loaded by R038+0x02F8 key 0x1E41 from PICS\SHIELDA.PIC and by key 0x2348 from the letter
    public void SetShieldPicture(int slot, Character member, char letter) => _shieldPictures[slot] = (member, letter);

    /// <summary>The frames, then every row (what a card or the map draws when it comes up).</summary>
    // orig: party_panel_frames root 0000:1588 draft (bytes match), then party_panel_rows root 0000:1716 draft (bytes match), both (0x100)
    // C: card_load (R043+0x0A9A) draws the panel this way when a card comes up.
    public void Draw(Canvas canvas, Party party)
    {
        for (var i = 0; i < Party.MaxMembers; i++)
        {
            DrawFrame(canvas, assets.CommonSprites, i, lit: false);
        }
        for (var i = 0; i < Party.MaxMembers; i++)
        {
            DrawRow(canvas, party, i);
        }
    }

    /// <summary>The frame around one row: an edge left and right, a line above and below (the lit pieces for the member selected in a battle).</summary>
    // orig: party_panel_frames root 0000:1588: for row i at y = 0x28 * i: the side piece at (0, y) and (0x39, y), the top at (3, y), the bottom at (3, y + 0x27); cbt_selected[i] 0 takes DS:A2E9 / DS:A3D6 / DS:A3E5, 1 takes DS:A33B / DS:A33E / DS:A392
    // C: the pieces go to both pages (DS:0168 off-screen, DS:0190 the screen); here to the one screen. A cbt_selected value other than 0 or 1 draws nothing.
    public static void DrawFrame(Canvas canvas, CommonSprites sprites, int row, bool lit)
    {
        var y = row * RowHeight;
        var side = lit ? sprites.PanelSideLit : sprites.PanelSide;
        SpriteDrawing.Draw(canvas, side, 0, y);
        SpriteDrawing.Draw(canvas, side, 0x39, y);
        SpriteDrawing.Draw(canvas, lit ? sprites.PanelTopLit : sprites.PanelTop, 3, y);
        SpriteDrawing.Draw(canvas, lit ? sprites.PanelBottomLit : sprites.PanelBottom, 3, y + 0x27);
    }

    // orig: party_panel_rows: for each of the five rows y = 1 + 0x28 * i: the background picture at (3, y); if the slot holds anyone (status != 0) the shield picture at (6, y + 8), the name in colour 7 (status 3 dead or 5 away, or someone else is acting in the order menu, or cbt_floor[i] == 0x9d), 0x0e (the leader) or 0x0f at (4, y + 1), the bars (member_bars) and the status letter; a dead member (3) gets the picture DS:A2E5 instead of the shield and diagonal grey hatching over the row
    // C: the name is the short name at +0x2A of the member record.
    /// <summary>One member's row on its background (an empty slot shows the background only).</summary>
    public void DrawRow(Canvas canvas, Party party, int i)
    {
        var y = i * RowHeight + 1;
        SpriteDrawing.Draw(canvas, assets.CommonSprites.RowBackground, 3, y);
        if (i >= party.Members.Count || !party.Members[i].IsPresent)
        {
            return;
        }
        var member = party.Members[i];
        DrawShield(canvas, member, y, i);
        var color = member.Status is CharacterStatus.Dead or CharacterStatus.Away ? Palette.Grey
            : party.LeaderIndex == i ? (byte)0x0E : Palette.White;
        _text.Draw(canvas, 4, y + 1, member.ShortName, color);
        // C: the window colour set for the name stays for everything member_bars draws after it: the "+" marks and the values are in the name's colour (yellow for the leader).
        // orig: member_bars: a member of status 5 shows the text "???? ???? ????" centred in a 28-pixel column at (0x16, row + 9) in colour 7 instead of the bars
        if (member.Status == CharacterStatus.Away)
        {
            DrawUnknownBars(canvas, i);
        }
        else
        {
            DrawBar(canvas, member, Attribute.Endurance, 0x15, 0x12, i, 9, color);
            DrawBar(canvas, member, Attribute.Strength, 0x23, 0x20, i, 2, color);
            DrawBar(canvas, member, Attribute.DivineFavor, 0x30, 0x2D, i, 0x0E, color);
        }
        if (member.Status == CharacterStatus.Dead)
        {
            DrawHatching(canvas, y);
        }
    }

    // orig: draw_text_wrapped(DS:0168, "???? ???? ????", 0x1c, 0x16, i * 0x28 + dy + 9, ...) with DS:01BC = 1 (centred), colour 7, dy = (i * 0x28 + 0x25 - height) / 2 but at least -1
    private void DrawUnknownBars(Canvas canvas, int row)
    {
        const string Text = "???? ???? ????";
        var layout = new CardTextLayout(_text);
        var settings = new CardTextLayout.Settings { Color = Palette.Grey, Leading = 0, Alignment = TextAlignment.Center };
        // C: ext = measure_text_wrapped(..., y = row * 0x28 + 9, ...) returns (y_end - 1) in its high word, which includes the start y; so dy = (0x25 - 9 - height + 1) / 2, in int arithmetic, and never less than -1.
        var height = layout.MeasureHeight(TextPainter.Encode(Text), 0x1C, 0);
        var dy = Math.Max(-1, (0x25 - 9 - height + 1) / 2);
        layout.Draw(canvas, TextPainter.Encode(Text), 0x16, row * RowHeight + dy + 9, 0x1C, settings);
    }

    // orig: the shield picture is shield_pics[i] (SHIELDn.PIC with n from the member's shield letter, loaded when the party is made); a dead member's (status 3) is DS:A2E5 (COMMONSP.IMG)
    private void DrawShield(Canvas canvas, Character member, int y, int slot)
    {
        if (member.Status == CharacterStatus.Dead)
        {
            SpriteDrawing.Draw(canvas, assets.CommonSprites.DeadShield, 6, y + 8);
            return;
        }
        var letter = _shieldPictures[slot] is { } loaded && ReferenceEquals(loaded.Member, member) ? loaded.Letter : member.ShieldLetter;
        var picture = "shield" + letter;
        if (assets.PictureExists(picture))
        {
            canvas.Draw(assets.Picture(picture).Image, 6, y + 8, transparent: true);
        }
    }

    /// <summary>A two-pixel bar 16 high: the lost part white at the top, the rest coloured; the value below.</summary>
    // orig: member_bars: top = i * 0x28 + 0xC, bottom = top + 0xF; q = (cur << 4) / max; len = 16 - q (15 when it would be 16 and cur != 0, 0 at least); a "+" at (x - 1, top - 4) when cur exceeds max; lines of colour 0x0F for the first `len` rows and then the bar colour (endurance 9, strength 2, favour 0x0E) to the bottom; the value (itoa_commas) and a space at (textX, i * 0x28 + 0x1F) with the window's right edge at 0x38
    // C: q is an int division of (cur << 4) by the maximum; a maximum of 0 would divide by zero in the original (guarded here by Math.Max(1, ...)); the count is the byte value of the attribute (a char), so values above 127 would be negative there.
    // C: the value is followed by a space (strcat(buf, " ")) and drawn with the window's last column set to 0x38: the space of the third value is cut at the panel's edge. The text colour is the window colour the name left.
    // DIVERGENCE: the value is printed without the thousands comma itoa_commas would add (the values stay below 1000).
    private void DrawBar(Canvas canvas, Character member, Attribute attribute, int x, int textX, int row, byte color, byte textColor)
    {
        var top = row * RowHeight + 0x0C;
        var bottom = row * RowHeight + 0x1B;
        var current = member[attribute];
        var maximum = Math.Max(1, member.Maximum(attribute));
        var lost = 16 - (current << 4) / maximum;
        if (lost == 16 && current != 0)
        {
            lost = 15;
        }
        lost = Math.Max(0, lost);
        for (var y = top; y <= bottom; y++)
        {
            var c = y < top + lost ? Palette.White : color;
            canvas[x, y] = c;
            canvas[x + 1, y] = c;
        }
        if (current > member.Maximum(attribute))
        {
            _text.Draw(canvas, x - 1, top - 4, "+", textColor, ValuesRight);
        }
        _text.Draw(canvas, textX, row * RowHeight + 0x1F, current.ToString(System.Globalization.CultureInfo.InvariantCulture) + " ", textColor, ValuesRight);
    }

    // orig: `for (k = 5; k < 0x69; k += 10) gfx_line(DS:0168, k, 0, 0, k, 7)` inside a clip window at (3, y) of 0x36 x 0x27 pixels: eleven parallel diagonal lines (k, 0) -> (0, k) in grey, for a dead member (an X was drawn here before)
    private static void DrawHatching(Canvas canvas, int y)
    {
        for (var k = 5; k < 0x69; k += 10)
        {
            for (var t = 0; t <= k; t++)
            {
                var (px, py) = (k - t, t);
                if (px < 0x36 && py < 0x27)
                {
                    canvas[3 + px, y + py] = Palette.Grey;
                }
            }
        }
    }
}
