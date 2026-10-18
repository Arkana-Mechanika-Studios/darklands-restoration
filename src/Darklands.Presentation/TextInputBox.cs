using Darklands.Platform;

namespace Darklands.Presentation;

/// <summary>
/// The game's text box (PICS\TEXTBACK.PIC at 110,80): a prompt and the text typed under it.
/// Enter accepts, Esc empties the text and ends, Backspace or Left removes the last letter,
/// printable characters are added up to the limit. The screen under it comes back after.
/// </summary>
// orig: text_input = R037+0x43E6 (1462:43E6) matched; tests: headless chargen session
// C: the box is redrawn only while the text is shorter than the limit (after a key), so at the limit a printable key
// changes nothing on screen; a deletion at the limit redraws.
// C: before saving the region the original turns colour 0 into 5 inside it and back after (a no-op for the picture, it
// only turns pixels that were colour 5 into 0 when restored).
// DIVERGENCE: that 0 -> 5 -> 0 recolouring is not repeated (it changes at most colour-5 pixels of the card behind).
public sealed class TextInputBox(GameUi ui)
{
    private const int X = 0x6E;
    private const int Y = 0x50;
    private const byte TextColor = 0;

    public string Ask(string prompt, int maxLength, string text = "")
    {
        var canvas = ui.Canvas;
        var saved = canvas.Save(X, Y, 0xA0, 0x32);
        var box = ui.Assets.Picture("textback");
        var buffer = new System.Text.StringBuilder(text);
        void Draw()
        {
            canvas.Draw(box.Image, X, Y);
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
        canvas.Restore(saved);
        ui.Present();
        return buffer.ToString();
    }
}
