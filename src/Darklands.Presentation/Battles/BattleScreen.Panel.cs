using Darklands.Cards;
using Darklands.Content.Formats.Battles;
using Darklands.Core;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Generation;
using Darklands.Core.Battles.Tactics;
using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Platform;
using Darklands.Presentation.Graphics;

namespace Darklands.Presentation.Battles;

// The battle screen: the party panel and the message line. The loop and the set-up are in BattleScreen.cs.
public sealed partial class BattleScreen
{
    /// <summary>The party panel: per member its row with portrait, name, bars and the order letter.</summary>
    // orig: party-panel-frame-set root 0000:1588, party-panel-row-set root 0000:1716, member-gauge-bars root 0000:1A9C, member-status-glyph root 0000:18FA
    // draft (spec E §3.2)
    private void DrawPanel(Canvas canvas)
    {
        var tb = _battle!;
        var common = _assets.Common;
        canvas.Fill(0, 0, PartyPanel.Width, Canvas.Height, 0);
        var screen = new Clip(0, 0, PartyPanel.Width, Canvas.Height);
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            var y = i * PartyPanel.RowHeight;
            var c = tb.Battle[i];
            var lit = c.Selected;
            PartyPanel.DrawFrame(canvas, common, i, lit);
            SpriteDrawing.Draw(canvas, common.RowBackground, 3, y + 1, screen);
            if (c.Status == CharacterStatus.Empty || c.Member is null)
            {
                continue;
            }
            var y1 = y + 1;
            if (c.Status == CharacterStatus.Dead)
            {
                SpriteDrawing.Draw(canvas, common.DeadShield, 6, y1 + 8, screen);
            }
            else
            {
                DrawShortPortrait(canvas, i, tb.SlotCodes[i], 6, y1 + 8);
            }
            // C: root 0000:1716 dims a row for `command-mode == 1 && chosen-member != i` (DS:A6B3 / DS:A6B2): the member chosen by the held
            //   button outside order mode dims nothing; order mode with nobody chosen (0xFF) dims every row
            var gone = c.Level == BattleEnd.LeftThroughExit || c.Status is CharacterStatus.Dead or CharacterStatus.Away || (tb.InOrderMode && tb.Selected != i);
            var color = gone ? Palette.Grey : tb.Battle.Leader == i ? (byte)0x0E : Palette.White;
            // orig: the panel's window DS:0156 (pointer DS:0168) has font slot 1 in a battle too (hooked at party-panel-row-set root 0000:1716 / member-gauge-bars root 0000:1A9C over the gate battle: font 1 at every call), so the name, the values, the place note and the order letter are in the map panel's font (PartyPanel); the window colour set for the name stays for the values
            // orig: the panel rows (root 0000:1716) draws the name at (4, y + 1) of the row's y = 1 + 0x28 * i (vis10 session tleads-battle-difficulty: the
            //   original's names stand a row lower than they were drawn here, in every battle frame)
            _panel.Draw(canvas, 4, y1 + 1, c.Member.ShortName, color);
            if (c.Status == CharacterStatus.Away)
            {
                // orig: the bars root 0000:1A9C: a member of status 5 shows "???? ???? ????" and nothing else (vis10, the synthetic save
                //   DKSAVE0-away24: the original's row of the away member)
                PartyPanel.DrawUnknownBars(canvas, _panel, i);
            }
            else
            {
                var note = _placeNotes[i];
                if (note is not null)
                {
                    DrawPlaceNote(canvas, i, note, color);
                }
                Bar(canvas, i, c.Endurance, c.MaximumEndurance, 0x15, 0x12, 9, color, note is null);
                Bar(canvas, i, c.Strength, c.MaximumStrength, 0x23, 0x20, 2, color, note is null);
                var favour = (int)Core.Characters.Attribute.DivineFavor;
                Bar(canvas, i, c.Attributes[favour], c.Maximums[favour], 0x30, 0x2D, 0x0E, color, note is null);
                // orig: member-gauge-bars root 0000:205D..0x2278: the target's bars beside the member's (PartyPanel.DrawTargetBars)
                if (note is null && ((ushort)c.Action & 0x1BE) != 0 && !c.Moving && c.Target != -1)
                {
                    var t = tb.Battle[c.Target];
                    PartyPanel.DrawTargetBars(canvas, _panel, i, [t.Endurance, t.Strength, t.Attributes[favour]],
                        [t.MaximumEndurance, t.MaximumStrength, t.Maximums[favour]], color);
                }
            }
            if (RowLetterShown(i) is { } letter)
            {
                // orig: the order letter root 0000:18FA draws the letter as text in the window's colour, the name's (the leader's
                //   letter yellow: vis10 session tleads-battle-difficulty)
                // C: its cell wiped first with the patch DS:A6C2 (PartyPanel.DrawLetterPatch)
                PartyPanel.DrawLetterPatch(canvas, common, i);
                _panel.Draw(canvas, 6, y + 0x1F, letter.ToString(), color);
            }
            if (c.Status == CharacterStatus.Dead)
            {
                // orig: the panel rows root 0000:1716: a dead member's row gets the grey hatching in a battle too (vis10 session D-09fbc1:
                //   the original's row of the fallen member)
                // C: after the won battle's loot screen the frames drawn on the screen keep the row's last line (PanelFramesRedrawn)
                PartyPanel.DrawHatching(canvas, y1, _framesOverHatching ? 0x26 : 0x27);
            }
            if (_orderLetters[i] is { } chosen)
            {
                // C: the chosen order's letter over the patch in colour 0xFF, on the screen until the row is drawn again (OrderLetterDrawn)
                PartyPanel.DrawLetterPatch(canvas, common, i);
                if (chosen != '\0')
                {
                    _panel.Draw(canvas, 6, y + 0x1F, chosen.ToString(), 0xFF);
                }
            }
        }
        if (_orderCellRows > 0)
        {
            // C: the cell of nobody's order letter (OrderLetterDrawn): the patch's pixels, the row background's (3..12, 0x1E..0x24), at (6..15, 0..6)
            SpriteDrawing.Draw(canvas, common.RowBackground, 3, 1 - 0x1F,
                new Clip(6, 0, 6 + PartyPanel.LetterPatchWidth, _orderCellRows));
        }
    }

    /// <summary>The order letter of a member (member-status-glyph); none for a member who left the field.</summary>
    // orig: member-status-glyph root 0000:18FA (0000:18FA) draft (checked against the asm): nothing outside a battle or for record +0x14 == 5;
    // DS:A3D8[i] 1 'b' (no line of fire), 2 'e' (out of ammunition); else the order: command-kind (DS:A6B0) when DS:A695 == 1, command-mode == 1,
    // chosen-member == i and it is not -1, else +0x10; 0x400 the place action: DS:A6D1 on the same conditions, else DS:A6C4[i]
    // (cs:1A22: 1, 2 'U', 3, 6 'P', 4, 7 'D', 5 'O', 8 'X'); 0 '-', 1 'F' following else 'W', 6 'B', 0x0A 'V', 0x12 'A', 0x22 'P', 0x40 'R',
    // 0x80 'M', 0x100 'T'
    // DIVERGENCE: any other value (an A3D8, action or place the tables do not list) leaves the original's letter byte uninitialised (a stack
    // byte drawn as it is); '-' here.
    private char? StatusLetter(int i)
    {
        var tb = _battle!;
        var c = tb.Battle[i];
        if (c.Status == CharacterStatus.Away)
        {
            return null;
        }
        if (tb.FireBlocked(i) != 0)
        {
            return tb.FireBlocked(i) switch
            {
                1 => 'b',
                2 => 'e',
                _ => '-',
            };
        }
        var pending = tb.Paused && tb.InOrderMode && tb.Selected == i;
        var order = pending && tb.OrderAction != -1 ? tb.OrderAction : (int)c.Action;
        if (order == 0x400)
        {
            var place = pending && tb.PlaceOrder != -1 ? tb.PlaceOrder : (int)c.Place;
            return place switch
            {
                1 or 2 => 'U',
                3 or 6 => 'P',
                4 or 7 => 'D',
                5 => 'O',
                8 => 'X',
                _ => '-',
            };
        }
        return order switch
        {
            0 => '-',
            1 => (c.Flags & TacticalFlags.Following) != 0 ? 'F' : 'W',
            6 => 'B',
            0x0A => 'V',
            0x12 => 'A',
            0x22 => 'P',
            0x40 => 'R',
            0x80 => 'M',
            0x100 => 'T',
            _ => '-',
        };
    }

    // orig: member-gauge-bars root 0000:1C36..1CCF: text-paint-wrapped(DS:0168, text, 0x24, 0x13, i * 0x28 + dy + 9, font height, ...) with DS:01BC = 1
    // (centred) in the window's colour (the name's), dy = (i * 0x28 - ext.high + 0x1F) / 2 but at least -1, ext = wrapped-text-size of the same
    // text at y = i * 0x28 + 9 (its high word the last line's bottom, y - 1 + height); the window's font is slot 1 (as the name and the values)
    private void DrawPlaceNote(Canvas canvas, int row, string note, byte color)
    {
        var layout = new CardTextLayout(_panel);
        var text = TextPainter.Encode(note);
        var height = layout.MeasureHeight(text, 0x24, 0);
        var dy = Math.Max(-1, (0x1F - 9 - height + 1) / 2);
        layout.Draw(canvas, text, 0x13, row * PartyPanel.RowHeight + dy + 9, 0x24,
            new CardTextLayout.Settings { Color = color, Leading = 0, Alignment = TextAlignment.Center });
    }

    /// <summary>A 16-pixel bar: the lost part white at the top, the rest in the stat colour; the value below (the bar not when a place note stands there).</summary>
    // orig: member-gauge-bars root 0000:1A9C (as PartyPanel.DrawBar): a "+" at (x - 1, top - 4) when the value exceeds the maximum (only in the bars branch: with a place note none, root 0000:1C36..1CCF); the value and a space at (textX, row * 0x28 + 0x1F) with the window's last column 0x38, in the name's colour
    private void Bar(Canvas canvas, int row, int current, int maximum, int x, int textX, byte color, byte textColor, bool bar = true)
    {
        var realMaximum = maximum;
        var top = row * PartyPanel.RowHeight + 0x0C;
        var bottom = row * PartyPanel.RowHeight + 0x1B;
        maximum = Math.Max(1, maximum);
        var lost = 16 - (current << 4) / maximum;
        if (lost == 16 && current != 0)
        {
            lost = 15;
        }
        lost = Math.Max(0, lost);
        for (var y = top; bar && y <= bottom; y++)
        {
            var c = y < top + lost ? Palette.White : color;
            canvas[x, y] = c;
            canvas[x + 1, y] = c;
        }
        if (bar && current > realMaximum)
        {
            _panel.Draw(canvas, x - 1, top - 4, "+", textColor, 0x38);
        }
        _panel.Draw(canvas, textX, row * PartyPanel.RowHeight + 0x1F, current.ToString(System.Globalization.CultureInfo.InvariantCulture) + " ", textColor, 0x38);
    }

    private readonly Dictionary<string, Content.Formats.Graphics.Pic?> _portraits = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The 11 × 19 short portrait of the member's picture family, in the member's colours.</summary>
    // orig: battle-portrait-set R002+0x0806 (0E76:0806) draft
    private void DrawShortPortrait(Canvas canvas, int slot, string code, int x, int y)
    {
        var name = code + "SHORT";
        if (!_portraits.TryGetValue(name, out var pic))
        {
            pic = _ui.Assets.PictureExists(name) ? _ui.Assets.Picture(name) : null;
            _portraits[name] = pic;
        }
        if (pic is null)
        {
            return;
        }
        var image = pic.Image;
        for (var py = 0; py < Math.Min(19, image.Height); py++)
        {
            for (var px = 0; px < Math.Min(11, image.Width); px++)
            {
                var p = image.Pixels[py * image.Width + px];
                if (p == 0)
                {
                    continue;
                }
                canvas[x + px, y + py] = p is >= 0xEB and <= 0xF2 ? (byte)(0x50 + 8 * slot + (p - 0xEB)) : p;
            }
        }
    }

    /// <summary>A message box centred in the view (notice-display).</summary>
    // orig: notice-display root 0000:24BC draft (spec E §3.5)
    private void DrawMessage(Canvas canvas)
    {
        if (_message is null)
        {
            return;
        }
        if (_ui.Clock.Milliseconds >= _messageUntil)
        {
            _message = null;
            return;
        }
        var text = _ui.Text;
        var tw = text.Measure(_message);
        var fh = text.LineHeight;
        var tx = (320 - tw - 60) / 2 + BattlefieldView.Left;
        var ty = 100 - fh / 2;
        canvas.Fill(tx - 5, ty - 4, tw + 10, fh + 7, 0xFF);
        Frame(canvas, tx - 5, ty - 4, tw + 10, fh + 7, 0x86);
        Frame(canvas, tx - 4, ty - 3, tw + 8, fh + 5, 0x82);
        text.Draw(canvas, tx, ty, _message, 1);
    }

    private static void Frame(Canvas canvas, int x, int y, int w, int h, byte color)
    {
        for (var k = 0; k < w; k++)
        {
            canvas[x + k, y] = color;
            canvas[x + k, y + h - 1] = color;
        }
        for (var k = 0; k < h; k++)
        {
            canvas[x, y + k] = color;
            canvas[x + w - 1, y + k] = color;
        }
    }
}
