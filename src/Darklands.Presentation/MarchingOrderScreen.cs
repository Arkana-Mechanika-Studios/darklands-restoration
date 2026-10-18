using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Core.Rules;
using Darklands.Platform;

namespace Darklands.Presentation;

/// <summary>
/// "Change Marching Order" outside a battle (Party menu, Alt-O): over the card or map, the able members' frames are lit in the
/// party column; a box asks for the first, second ... member, chosen with F1..F5 or a click on the member's row; the choice is
/// the game's marching order, which the next battle starts from.
/// </summary>
// orig: R022+0x051C (1EF8:051C) draft, checked against asm, with DS:907A != 1 (no battle: no hot spots); R022+0x07AA draft (bytes match); opened
// by thunk 09C0:183D from the hot keys (root 0000:0CAA key 0x1800) and the Party menu; tests: MarchingOrderTests, headless session (PNG)
// C: the box is root 0000:23E6 over the card window (DS:017C) with the window's colours set to 1 and 0xFF, its rectangle put
// back after each pick (the paged rectangle copy of the graphics layer); the window's colours are restored at the end.
// C: the loop reads keys and the mouse itself: no menu bar, no hot keys while the box is up.
// C: the frames: all unlit (the selection bytes cleared, root 0000:1588 with 0x100), then lit for each able member; each pick draws its
// frame unlit again; at the end the selection bytes (DS:9064) are put back and every frame drawn as they say.
// UNCERTAIN: the selection bytes are taken to be 0 outside a battle (the panel is drawn unlit everywhere else, PartyPanel.Draw).
// C: a click is the mouse driver's count of left-button releases (root 047C:1320 function 6, GameUi.TakeLeftReleases) since its last
// query: a release made before the screen opened (the Party menu's own) is taken by the first prompt, as in the original.
// DIVERGENCE: with the battle's move mode at 2 (single file: DS:C102 as the last battle left it) the original then sets the battlefield's
// members walking after each other (thunk 09C0:1DE7, R016+0x0E7E) on the last battle's records; outside a battle that is not done here.
public sealed class MarchingOrderScreen(GameUi ui, PartyPanel panel, Func<int, string> selectOrdinal)
{
    public void Run(GameState state)
    {
        var canvas = ui.Canvas;
        var sprites = ui.Assets.CommonSprites;
        for (var i = 0; i < Party.MaxMembers; i++)
        {
            PartyPanel.DrawFrame(canvas, sprites, i, lit: false);
        }
        var choice = new MarchingOrderChoice(state.MarchingOrder, i => state.Party.Slot(i).Status == CharacterStatus.Able);
        for (var i = 0; i < Party.MaxMembers; i++)
        {
            if (choice.Selected[i])
            {
                PartyPanel.DrawFrame(canvas, sprites, i, lit: true);
            }
        }
        var box = new MessageBox(ui);
        int releases = 0, releaseX = 0, releaseY = 0;
        bool TakeReleases()
        {
            (releases, releaseX, releaseY) = ui.TakeLeftReleases();
            return releases > 0;
        }
        for (var k = 0; k < choice.Prompts; k++)
        {
            var shown = box.Open(selectOrdinal(k));
            while (true)
            {
                ui.Present();
                var taken = -1;
                if (ui.TryReadKey(out var key))
                {
                    // orig: R022+0x051C: keys 0x3B00..0x3F00: R022+0x07AA(n) with no test; any other key is dropped
                    if (key is Keys.F1 or Keys.F2 or Keys.F3 or Keys.F4 or Keys.F5)
                    {
                        taken = (key - Keys.F1) >> 8;
                        choice.Take(taken);
                    }
                }
                else if (releases > 0 || TakeReleases())
                {
                    // orig: DS:C62A / DS:C4FE, the release count and place of the driver (as the battle's picker): x < 0x3C: row = y / 40
                    // (signed); the row's status byte 1 and selection byte 1; a release elsewhere is used up (no hot spots outside a battle)
                    releases = 0;
                    if (releaseX < PartyPanel.Width)
                    {
                        var row = releaseY / PartyPanel.RowHeight;
                        if (choice.CanClick(row, state.Party.Slot(row).Status == CharacterStatus.Able))
                        {
                            taken = row;
                            choice.Take(row);
                        }
                    }
                }
                if (taken >= 0)
                {
                    if (taken < Party.MaxMembers)
                    {
                        PartyPanel.DrawFrame(canvas, sprites, taken, lit: false);
                    }
                    break;
                }
                ui.Idle();
            }
            box.Close(shown);
        }
        foreach (var slot in choice.Finish())
        {
            PartyPanel.DrawFrame(canvas, sprites, slot, lit: false);
        }
        for (var i = 0; i < Party.MaxMembers; i++)
        {
            PartyPanel.DrawFrame(canvas, sprites, i, lit: false);
        }
        ui.Present();
        _ = panel;
    }
}
