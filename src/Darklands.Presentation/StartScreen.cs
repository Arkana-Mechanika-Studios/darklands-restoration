using Darklands.Platform;

namespace Darklands.Presentation;

/// <summary>What the player chose on the start screen.</summary>
// orig: the key codes of R037+0x9DB2 (1E26:0172) matched: Q (0x1051 / 0x1071) the default party, C (0x2E43 / 0x2E63) create characters, T (0x1454 / 0x1474) load a saved game, Alt-Q (0x1000) quit
public enum StartChoice
{
    Quickstart,
    CreateCharacters,
    LoadGame,
    Quit,
}

/// <summary>
/// The start screen: three buttons in a column — "Quickstart" (the ready-made party),
/// "Create characters" and "Load a saved game" — and Alt-Q to quit.
/// </summary>
// orig: R037+0x9C48 (1E26:0008) = thunk 09C0:18DD, run once by the main function before the card loop; its helpers 00dc (draw the picture, load button1..4), 014a (copy to the screen), 00a6 (free the button pictures) and the key handler 0172; matched; tests: none (needs a host)
// DORMANT: picsutton4.pic is loaded (DS:8D04) but nothing in this file ever draws it: the header says "4 buttons: quit / default party / load / create" but only buttons 1..3 are drawn (as the pressed states).
// DIVERGENCE: the original runs the start screen once and ends the program when the game ends (the main loop returns, free_buffer, exit); DarklandsGame.Run comes back to this screen after a game instead.
// DIVERGENCE: the original starts the chosen action as the pressed button is drawn, with no pause (loading takes the time); the 150 ms here only lets the press be seen.
public sealed class StartScreen(GameUi ui)
{
    private const int ButtonX = 0x51;
    private const int FirstButtonY = 0x32;
    private const int ButtonPitch = 0x20;
    private static readonly int[] PressedY = [0x37, 0x57, 0x78];

    // orig: R037+0x9C48: waits for the left button to be released; then loops: a left press with 0x32 < y < 0x96 picks (y - 0x32) / 0x20 -> 0 Q, 1 C, 2 T (anything else, including the fourth band, picks nothing), a waiting key overrides it; the loop ends when the handler returns 0x11b
    // C: a click is judged by y only; x does not matter. If a key is waiting it replaces the click's code in the same pass.
    public StartChoice Run()
    {
        Draw();
        while (true)
        {
            var action = ui.WaitForAction();
            var choice = action.IsLeftClick ? ChoiceAt(action.Y) : ChoiceFor(action.Key);
            if (choice is { } chosen)
            {
                if (chosen == StartChoice.Quit)
                {
                    // C: key 0x1000: R037+0x9CE6 (frees the buttons) then quit_to_dos = "Quit to DOS? y/n"; on "n" the handler returns something other than 0x11b and the loop goes on (with the button pictures already freed, so a later button press draws stale memory).
                    // DIVERGENCE: after "no" the screen simply goes on.
                    if (new MessageBox(ui).Ask("Quit to DOS?"))
                    {
                        return chosen;
                    }
                    continue;
                }
                Press(chosen);
                return chosen;
            }
        }
    }

    // orig: R037+0x9D1C (pic_draw_pal pics\startscr.pic, back_win_colour = 8, fonts 1) + R037+0x9D8A (copy the 320x200 page to the screen)
    public void Draw() => ui.Canvas.DrawPicture(ui.Assets.Picture("startscr"));

    // orig: gfx_draw_pic(screen_win, 0x51, 0x37 / 0x57 / 0x78, button1 / button2 / button3) at the top of each case of R037+0x9DB2
    private void Press(StartChoice choice)
    {
        var button = ui.Assets.Picture("button" + ((int)choice + 1));
        ui.Canvas.Draw(button.Image, ButtonX, PressedY[(int)choice]);
        ui.Wait(150, skippable: false);
    }

    // orig: `mouse_buttons & 1 && mouse_y > 0x32 && mouse_y < 0x96`, `switch (v_si = (mouse_y - 0x32) / 0x20) { default: v_si = 0; case 0: 0x1051; case 1: 0x2e43; case 2: 0x1454 }`
    private static StartChoice? ChoiceAt(int y)
    {
        if (y <= FirstButtonY || y >= 0x96)
        {
            return null;
        }
        return ((y - FirstButtonY) / ButtonPitch) switch
        {
            0 => StartChoice.Quickstart,
            1 => StartChoice.CreateCharacters,
            2 => StartChoice.LoadGame,
            _ => null,
        };
    }

    // orig: the case labels of R037+0x9DB2 (0x1000; 0x1454 / 0x1474; 0x1051 / 0x1071; 0x2e43 / 0x2e63)
    // C: the original takes the exact codes; here any key whose character is q, c or t (either case) counts - the same keys on the PC keyboard.
    private static StartChoice? ChoiceFor(int key)
    {
        if (key == Keys.AltQ)
        {
            return StartChoice.Quit;
        }
        return char.ToLowerInvariant(Keys.CharacterOf(key)) switch
        {
            'q' => StartChoice.Quickstart,
            'c' => StartChoice.CreateCharacters,
            't' => StartChoice.LoadGame,
            _ => null,
        };
    }
}
