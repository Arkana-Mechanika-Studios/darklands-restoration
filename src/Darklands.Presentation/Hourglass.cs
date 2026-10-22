using Darklands.Cards;
using Darklands.Core.Time;
using Darklands.Presentation.Graphics;

namespace Darklands.Presentation;

/// <summary>
/// The hourglass the game shows while more than two hours pass (advance_hours): the dial in the
/// card window with the bell, day and month, one step per three hours. Cards and the camp's day use it.
/// </summary>
internal static class Hourglass
{
    // orig: pass_hours_hourglass(hours) R043+0x3494 (150B:2A04) draft (bytes match): hours /= 3, nothing for 0; waits for the left button to be up; card_window->color = 14; gfx_recolor_rect(card_window, 0x3c, 0x32, 0x78, 0x66, 0, 5), the box area saved (gfx_save_rect), the dial DS:A62A (pics	imer01.pic) at window (0x3c, 0x32); one step per three hours from cur_hour / 3, cur_day, cur_month: past the eighth band the day goes on (days_per_month, the month wraps after 11), then `if (!key_pressed() && !(mouse_buttons & 1))` the frame (hourglass_frame(band % 8)), the bell, day and month, and thunk 09C0:1FEF(400, 0) = delay_ticks(400); after the steps the last frame and texts are drawn again and, unless a key or the button is down, delay_ticks(1000); a waiting key is eaten; the box is put back and gfx_recolor_rect(…, 5, 0)
    // C: key_pressed() only looks: once a key waits, every later step draws and waits nothing (the bands still count on, so the last frame shows the end of the time), and the key is eaten at the end. The delays themselves look at nothing.
    // C: the recolouring around the save (0 -> 5 before, 5 -> 0 after) turns the box area's colour-5 pixels into colour 0 when the box goes away.
    // C: the saved rectangle is a driver picture and goes back with colour 0 transparent (Canvas.RestoreTransparent); hence the 0 -> 5 recolouring before the save.
    public static void Show(GameUi ui, GameDate date, int hours)
    {
        var steps = hours / 3;
        if (steps == 0)
        {
            return;
        }
        ScreenDrawing.WaitForRelease(ui);
        var (band, day, month) = (date.Hour / 3, (int)date.Day, (int)date.Month);
        ui.Canvas.Recolor(DialX, DialY, DialWidth, DialHeight, 0, 5);
        var saved = ui.Canvas.Save(DialX, DialY, DialWidth, DialHeight);
        if (ui.Assets.PictureExists("timer01"))
        {
            ui.Canvas.Draw(ui.Assets.Picture("timer01").Image, DialX, DialY, transparent: true);
        }
        var keyWaiting = false;
        for (var i = 0; i < steps; i++)
        {
            if (band >= 8)
            {
                if (++day > Core.Time.GameDate.DaysInMonth(month))
                {
                    day -= Core.Time.GameDate.DaysInMonth(month);
                    if (++month > 11)
                    {
                        month = 0;
                    }
                }
                band -= 8;
            }
            if (!keyWaiting && !ui.Mouse.Left)
            {
                DrawHourglass(ui, band % 8, day, month);
                keyWaiting = Delay(ui, 400);
            }
            band++;
        }
        DrawHourglass(ui, band % 8, day, month);
        if (!keyWaiting && !ui.Mouse.Left)
        {
            Delay(ui, 1000);
        }
        // C: the key that stopped it is eaten (here it was read by Delay already).
        ui.Canvas.RestoreTransparent(saved);
        ui.Canvas.Recolor(DialX, DialY, DialWidth, DialHeight, 5, 0);
        ui.Present();
    }

    private const int DialX = CardScreen.WindowX + 0x3C;
    private const int DialY = 0x32;
    private const int DialWidth = 0x78;
    private const int DialHeight = 0x66;

    /// <summary>Waits as delay_ticks does (nothing ends it early); true when a key was pressed meanwhile.</summary>
    // orig: thunk 09C0:1FEF = delay_ticks R030+0x3096 (1767:0046) matched: spins until clock() has moved on by the count (milliseconds, MSC 6)
    private static bool Delay(GameUi ui, int milliseconds)
    {
        var end = ui.Clock.Milliseconds + milliseconds;
        var key = false;
        while (ui.Clock.Milliseconds < end)
        {
            key |= ui.Poll() is { IsKey: true };
            ui.Idle();
        }
        return key;
    }

    /// <summary>One frame of the hourglass with the bell, the day and the month under it.</summary>
    // orig: hourglass_frame(k) R043+0x37CA (150B:2D3A) draft (code identical, only the jump table's place differs):
    // root 047C:055F(card_window, 0x6c, 0x53, w, h, pic) with (w, h, pic) = (0x18, -0x14, DS:A62E) for 0,
    // (-0x18, -0x14, DS:A630), (-0x18, 0x14, DS:A632), (-0x18, 0x14, DS:A630), (0x18, 0x14, DS:A62E),
    // (0x18, 0x14, DS:A630), (0x18, 0x14, DS:A632), (0x18, -0x14, DS:A630) for 7 (DS:A62E/DS:A630/DS:A632 = pics	imer03/04/05.pic);
    // then pass_hours_hourglass R043+0x3494 (150B:2A04) after each frame (0x35A5..0x3655, 0x3697..0x3746):
    // gfx_fill_rect colour 0 at (0x48, 0x82, 0x28, 8), (0x84, 0x82, 0xc, 8), (0xa0, 0x82, 0xc, 8) and draw_text_far
    // of bell_names[hour_band(k * 3)], itoa(day), itoa(month + 1) at x 0x48, 0x84, 0xa0, y 0x82, colour 14
    // C: root 047C:055F is the driver's scaled picture draw drv+0x0C12 / drv+0x0E3F (asm): the picture is stretched to |w| x |h| (here its own
    //   24 x 20), a negative width draws the columns right to left and a negative height the rows bottom to top, colour 0 transparent. checked against asm
    private static void DrawHourglass(GameUi ui, int frame, int day, int month)
    {
        var (picture, flipX, flipY) = frame switch
        {
            0 => ("timer03", false, true),
            1 => ("timer04", true, true),
            2 => ("timer05", true, false),
            3 => ("timer04", true, false),
            4 => ("timer03", false, false),
            5 => ("timer04", false, false),
            6 => ("timer05", false, false),
            _ => ("timer04", false, true),
        };
        if (ui.Assets.PictureExists(picture))
        {
            ui.Canvas.DrawMirrored(ui.Assets.Picture(picture).Image, CardScreen.WindowX + 0x6C, 0x53, flipX, flipY);
        }
        const byte DialText = 14;
        ui.Canvas.Fill(CardScreen.WindowX + 0x48, 0x82, 0x28, 8, Palette.Black);
        ui.Canvas.Fill(CardScreen.WindowX + 0x84, 0x82, 0x0C, 8, Palette.Black);
        ui.Canvas.Fill(CardScreen.WindowX + 0xA0, 0x82, 0x0C, 8, Palette.Black);
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        ui.CardText.Draw(ui.Canvas, CardScreen.WindowX + 0x48, 0x82, TextVariables.BellNames[Core.Time.TimeOfDay.Band(frame * 3)], DialText);
        ui.CardText.Draw(ui.Canvas, CardScreen.WindowX + 0x84, 0x82, day.ToString(invariant), DialText);
        ui.CardText.Draw(ui.Canvas, CardScreen.WindowX + 0xA0, 0x82, (month + 1).ToString(invariant), DialText);
    }
}
