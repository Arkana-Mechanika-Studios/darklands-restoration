using Darklands.Platform;
using Darklands.Presentation.Graphics;

namespace Darklands.Presentation;

/// <summary>
/// The game's small framed one-line boxes over the card window: a message shown for a moment,
/// a "y/n" question, the pause box. Each puts back what was under it.
/// </summary>
// orig: text_box root 0000:23E6 (frame + text, saves the covered rectangle) + draw_frame_box root 0000:2286 + show_message root 0000:24BC + ask_yes_no root 0000:2522 + the pause box root 0000:1080, all draft (per the file's header 2286, 1080, 24bc and its neighbours match the bytes; 23e6 does not: a stack-slot order); tests: none (needs a host)
// C: the boxes are drawn into card_window (DS:017C) with text colour f0c = 1 and box colour f0e = 0xff, centred horizontally on a 320-wide screen less the window's own x offset (w->w), vertically at row y or, for y = -1, mid-screen (100 - font height / 2). They save what they cover in card_window2 (DS:0154) with copy_rect_saved and put it back afterwards.
// UNCERTAIN: the horizontal centring uses (320 - text width - w->w) / 2 relative to the window; the window offset w->w of card_window is taken as 60 (the card window starts right of the party panel), so the box is centred on the 260 pixels right of the panel. The offset was not checked at run time (it could be 0 when a box comes up over a full screen such as the start screen).
public sealed class MessageBox(GameUi ui)
{
    /// <summary>The card window starts at x 60; boxes are centred in it.</summary>
    private const int WindowX = 60;
    private const int WindowWidth = Canvas.Width - WindowX;
    public const byte TextColor = 1;
    private const byte Background = 0xFF;

    /// <summary>How long a message stays (the original waits 60 timer ticks).</summary>
    // orig: root 0410:0302(0x3c) in show_message (root 0410, the timer driver; asm)
    // UNCERTAIN: the length of a 0410 timer tick. 1000 ms assumes 60 ticks per second; the PAN player's measured tick (86 ms per 6 ticks, see Cinematics) would give 860 ms.
    public const int MessageMilliseconds = 1000;

    /// <summary>A message for a moment; nothing ends the wait early (the keys and clicks made meanwhile stay for later).</summary>
    // orig: root 0000:24BC(text, ticks) root 0000:24BC: f0c = 1, f0e = 0xff, root 047C:12C3, root 0000:23E6(card_window, -1, 1, text, card_window2, &r),
    // root 0410:0302(ticks) (asm: `if (n > 0) { t = (byte)(DS:07D2 + n); while (DS:07D2 != t); }`, a spin on the timer's tick byte, no
    // input test), root 03EC:000E back, root 047C:1293; tests: ScreenInputTests
    // C: mouse_hide before the box and mouse_show after the rectangle is put back: no pointer while the box waits (the battle bar's
    //   Difficulty sub-item, whose bar shows the pointer: vis10 session tleads-battle-difficulty, local/batch/tleads)
    public void Show(string text, int milliseconds = MessageMilliseconds)
    {
        var pointer = ui.PointerVisible;
        ui.PointerVisible = false;
        try
        {
            var saved = Draw(text, Centred);
            ui.Delay(milliseconds);
            ui.Canvas.Restore(saved);
        }
        finally
        {
            ui.PointerVisible = pointer;
        }
        ui.Present();
    }

    /// <summary>A box that stays while something is done ("Loading Game", "Saving Game"); <see cref="Close"/> puts back what it covered.</summary>
    // orig: text_box(card_window, -1, 1, text, card_window2, &r) ... copy_rect_saved(card_window2, r..., card_window, ...),
    // repeated inline in R037+0x8ED4 and R037+0x2ACE
    public SavedRegion Open(string text, byte textColor = TextColor)
    {
        var saved = Draw(text, Centred, textColor);
        ui.Present();
        return saved;
    }

    public void Close(SavedRegion saved)
    {
        ui.Canvas.Restore(saved);
        ui.Present();
    }

    /// <summary>"question y/n": waits for Y or N; true for yes.</summary>
    // orig: ask_yes_no(question) root 0000:2522: the box shows question + " y/n"; `c = getch(); if (c >= 0x60) c -= 0x20;` repeated until c is 'Y' (1) or 'N' (0)
    // C: c is a char; a lower-case letter (0x60 and up) loses 0x20, so only y/Y and n/N answer; any other key, mouse clicks included, is ignored. getch() of an extended key returns 0 first and the scan code next, both ignored.
    // C: getch() reads the keyboard itself: no hot keys, no menu bar, and the mouse is not looked at (a release made meanwhile stays
    // in the mouse driver's count for later).
    public bool Ask(string question)
    {
        var saved = Draw(question + " y/n", Centred);
        bool? answer = null;
        while (answer is null)
        {
            foreach (var b in GetchBytes())
            {
                // C: `if (c >= 0x60) c -= 0x20;` with c a signed char: bytes 0x80 and up are negative and stay
                var c = (sbyte)b;
                if (c >= 0x60)
                {
                    c -= 0x20;
                }
                if (c == 'Y' || c == 'N')
                {
                    answer = c == 'Y';
                    break;
                }
            }
        }
        ui.Canvas.Restore(saved);
        ui.Present();
        return answer.Value;
    }

    /// <summary>getch(): the next key's character, or 0 and then its scan code for an extended key.</summary>
    private IEnumerable<byte> GetchBytes()
    {
        int key;
        while ((key = ui.PollKeyOnly()) == 0)
        {
        }
        var ascii = (byte)key;
        if (ascii is 0 or 0xE0)
        {
            return [0, (byte)(key >> 8)];
        }
        return [ascii];
    }

    /// <summary>"Game Paused / Press a key to resume".</summary>
    // orig: root 0000:1080: sound_play(2), two text boxes ("Game Paused" at row 0x32 and "Press a key to resume" at mid-screen), getch(), both put back, sound_play(3); the flag DS:07D3 keeps a pause inside a pause from restoring early
    // C: sound_play(2) / sound_play(3) bracket the pause (2 stops and 3 resumes the music? the numbers are sound-driver commands; sound_play(1) is "stop the music")
    // C: what sound_play(2) and (3) do; they are not played here.
    public void Pause()
    {
        var first = Draw("Game Paused", 0x32);
        var second = Draw("Press a key to resume", Centred);
        // C: getch(): any key, read without the hot keys; the mouse is not looked at
        while (ui.PollKeyOnly() == 0)
        {
        }
        ui.Canvas.Restore(second);
        ui.Canvas.Restore(first);
        ui.Present();
    }

    private const int Centred = -1;

    /// <summary>Frames <paramref name="text"/> at text row <paramref name="y"/> (−1: mid-screen) and returns what it covered.</summary>
    // orig: text_box(w, y, save, text, dst, &r): ty = y (or -(font_height / 2 - 100)); tw = text_pixel_width; bw = tw + 10; bh = font_height + 7; tx = (320 - tw - w->w) / 2; bx = tx - 5; by = ty - 4; draw_frame_box(w, bx, by, bw, bh, w->f0e) then draw_text(w, tx, ty, text)
    // C: draw_frame_box (ScreenDrawing.FrameBox): the box colour, the two outlines and the four corner pieces of COMMONSP.IMG.
    private SavedRegion Draw(string text, int y, byte textColor = TextColor)
    {
        var canvas = ui.Canvas;
        // C: the box is drawn in the card window with its font: slot 3, slot 1 inside menu_bar_run (a hot key's box: GameUi.CardWindowFont).
        var font = ui.CardWindowText;
        var textY = y != Centred ? y : 100 - font.LineHeight / 2;
        var textWidth = font.Measure(text);
        var textX = WindowX + (WindowWidth - textWidth) / 2;
        var (x, top, width, height) = (textX - 5, textY - 4, textWidth + 10, font.LineHeight + 7);
        var saved = canvas.Save(x, top, width, height);
        ScreenDrawing.FrameBox(canvas, ui.Assets.CommonSprites, x, top, width, height, Background);
        font.Draw(canvas, textX, textY, text, textColor);
        ui.Present();
        return saved;
    }
}
