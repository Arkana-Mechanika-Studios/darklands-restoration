using Darklands.Platform;

namespace Darklands.Presentation;

/// <summary>
/// The game's text box (PICS\TEXTBACK.PIC at 110,80): a prompt and the text typed under it.
/// Enter accepts, Esc empties the text and ends, Backspace or Left removes the last letter,
/// printable characters are added up to the limit. The screen under it comes back after.
/// </summary>
// orig: text-prompt = R037+0x43E6 (1462:43E6) matched; tests: headless chargen session
// C: the box is redrawn only while the text is shorter than the limit (after a key), so at the limit a printable key
// changes nothing on screen; a deletion at the limit redraws.
// C: R037+0x440F..0x441F turns colour 0 into 5 in 0x9B x 0x32 pixels at (0x6E, 0x50), R037+0x442C..0x4437 saves 0xA0 x 0x32 there
//   (root 047C:04D5), R037+0x451A..0x4524 draws the saved rectangle back with colour 0 transparent (root 047C:05A5) and R037+0x452C..0x453C
//   turns 5 back into 0 in the 0x9B columns: in the five columns past them (x 0x109..0x10D) the screen's black pixels are not drawn back,
//   so the box's own pixels stay there (vis10 session D-b797f7: x 265..267, y 83..129 after the save's comment), and pixels that were
//   colour 5 in the 0x9B columns come back as 0.
// DIVERGENCE: with GameOptions.CleanDrawing (the Fixed and Classic + comfort presets) the region is put back as it was saved: no strip of
//   the box is left and colour-5 pixels stay 5.
public sealed class TextInputBox(GameUi ui)
{
    private const int X = 0x6E;
    private const int Y = 0x50;
    private const int Height = 0x32;
    private const int RecolorWidth = 0x9B;
    private const byte TextColor = 0;

    public string Ask(string prompt, int maxLength, string text = "")
    {
        var canvas = ui.Canvas;
        var clean = ui.Options.CleanDrawing;
        if (!clean)
        {
            canvas.Recolor(X, Y, RecolorWidth, Height, 0, 5);
        }
        var saved = canvas.Save(X, Y, 0xA0, Height);
        var box = ui.Assets.Picture("textback");
        var buffer = new System.Text.StringBuilder(text);
        void Draw()
        {
            // orig: R037+0x4447..0x4452 / R037+0x44D9..0x44E4 the picture draw (root 047C:05A5): the picture's colour-0 pixels are not drawn (the
            //   box's corners show the screen behind: vis10 session D-b797f7)
            canvas.Draw(box.Image, X, Y, transparent: true);
            ui.Text.Draw(canvas, 0x7D, 0x5A, prompt, TextColor);
            ui.Text.Draw(canvas, 0x87, 0x64, buffer.ToString(), TextColor);
        }
        Draw();
        while (true)
        {
            var action = ui.WaitForAction();
            if (!action.IsKey)
            {
                continue;
            }
            var key = action.Key;
            if (key == Keys.Enter)
            {
                break;
            }
            if (key == Keys.Escape)
            {
                buffer.Clear();
                break;
            }
            if (key is Keys.Left or Keys.Backspace && buffer.Length > 0)
            {
                buffer.Length--;
            }
            if (maxLength > buffer.Length)
            {
                var c = Keys.CharacterOf(key);
                if (c is >= ' ' and <= '~')
                {
                    buffer.Append(c);
                }
                Draw();
            }
        }
        if (clean)
        {
            canvas.Restore(saved);
        }
        else
        {
            canvas.RestoreTransparent(saved);
            canvas.Recolor(X, Y, RecolorWidth, Height, 5, 0);
        }
        ui.Present();
        return buffer.ToString();
    }
}
