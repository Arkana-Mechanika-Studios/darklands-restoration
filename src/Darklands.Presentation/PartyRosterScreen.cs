using Darklands.Content;
using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Core.Characters.Creation;
using Darklands.Core.Rules;
using Darklands.Platform;
using Darklands.Presentation.Graphics;
using CreationData = Darklands.Content.Formats.Characters.CreationData;
using CreationDataDecoder = Darklands.Content.Formats.Characters.CreationDataDecoder;

namespace Darklands.Presentation;

/// <summary>
/// The inn's party roster ("retire / recruit"), the in-game twin of "Create a new party" (PICS\CRETSCR3.PIC): eight
/// commands in the middle (Create a Character, Add to the Party, Heraldry, Select Character Image, the three colours,
/// Return to Adventure), the characters who can be met here on the right ('+' marks a party member's row), the party
/// column on the left and a member's figure under the commands. Opened by the inn card's first line ($Party00).
/// </summary>
// orig: R038+0x0000 (1462:0000) draft (the loop; only an unused stack slot differs), R038+0x01FE (the exit), R038+0x024E (the pictures),
// R038+0x0292 (the redraw), R038+0x23EC (the list) and R038+0x25CA (the labels) draft (bytes match), R038+0x02F8 draft (the key handler,
// checked against asm where it matters: InnRoster); opened through thunk 09C0:1EE1 by R029+0x2246; tests: InnRosterTests, headless
// roster session (PNG)
// C: the screen draws on the back window (DS:2806) and the screen window (DS:27F4); restores copy the back window's rectangles,
// C: which keep what the first drawing left on them (the names and '+' marks of the first list, the first highlight); a list row's
// C: strip starts at its '+' line, so the top line of a '+' drawn above it on the screen (a member who left) stays.
// C: the screen is put back as it was before the card's own redraw (thunk 09C0:1EA5, ICardScreen.Reload).
// C: F1..F5 reach the screen's key handler as unknown keys, as in the original (the hot keys take them only in the card engine's and the
//   map's loops: GameUi.SheetKeys).
public sealed class PartyRosterScreen
{
    private const int BackLabelColor = 8;
    private const byte NameColor = 0x10;
    private const byte NameShadow = 0x11;
    private const byte Highlight = 0x12;
    // orig: the figure colours DS:E753..DS:E76A, written into the current palette block (DS:E48C) and set (root 047C:058B), are colours
    // 235..242 on the screen, not 237..244: the original's DAC after A in the creation menu (R037+0x0B9C..0x0C88) holds the 24 start bytes
    // there (vis10 session D-b0c9a6) (R038 writes the same DS:E753).
    private const int FirstFigureColor = 235;

    /// <summary>The commands from top to bottom: label text, the key a click on the row gives, the label's y.</summary>
    // orig: R038+0x0000 the click rows (y 0x2D + 15 x row) and their keys; R038+0x25CA the labels at (0x48, 0x30 + 15 x row)
    private static readonly (int Text, int Key, int Y)[] Commands =
    [
        (CreationData.CreateCharacter, 0x2E43, 0x30),
        (CreationData.AddToParty, 0x1E41, 0x3F),
        (CreationData.Heraldry, 0x2348, 0x4E),
        (CreationData.SelectImage, 0x1F53, 0x5D),
        (CreationData.FirstColour, 0x0231, 0x6C),
        (CreationData.SecondColour, 0x0332, 0x7B),
        (CreationData.ThirdColour, 0x0433, 0x8A),
        (CreationData.ReturnToAdventure, Keys.Escape, 0x99),
    ];

    private readonly GameUi _ui;
    private readonly PartyPanel _panel;
    private readonly WorldData _world;
    private readonly CreationData _data;
    private readonly Canvas _back = new(new Screen());
    private GameState _state = null!;
    private IReadOnlyList<string> _images = [];

    public PartyRosterScreen(GameUi ui, PartyPanel panel, WorldData world)
    {
        _ui = ui;
        _panel = panel;
        _world = world;
        _data = CreationDataDecoder.Decode(ui.Assets.Files.Read("DARKLAND.EXE"));
    }

    /// <summary>The flags of the game's Save Game and Load Saved Game items, greyed while the screen is up.</summary>
    public HotKeys? HotKeys { get; init; }

    private static PartyMenuState Menu => CharacterCreationScreen.Shared;

    private Canvas Screen => _ui.Canvas;

    /// <summary>Runs the screen until Return (or Esc, R).</summary>
    /// <param name="callerWord">The register word the card line had when it opened the screen (its hours): the first list's term test reads it (InnRoster.List).</param>
    // orig: R038+0x0000: the menu items' flags DS:9920 = DS:9934 = 2, the fonts kept, DS:E88E = 0, the figure list (thunk 09C0:20C1), R038+0x024E,
    // R038+0x0292, the button waited out; then per pass: key = 0; a left press on the commands (x 0x47..0xC7, y 0x2E..0xA4) waits for the
    // release and gives the row's key; one on the list (x 0xC6..0x137, y 0x15..0xB3) waits and moves the highlight to the row if
    // DS:E7DC > row (no figure); root 0000:0000 (the menu bar and the hot keys); the redraw when DS:9C46 is set (key = 0); a waiting key; 0x11B, 0x1352
    // and 0x1372 leave (R038+0x01FE(0)); another key goes to R038+0x02F8, whose 0x11B leaves (R038+0x01FE(1))
    // C: the row of a click is a signed division by 15 (commands) and by 8 (the list), of positive values here.
    // C: the hot keys run inside root 0000:0000 before the key is read: Alt-Q never reaches the handler's own 0x1000 case.
    // C: SI comes through the thunk 09C0:1EE1 unchanged: the overlay manager's entry (root 09C0:052B) pushes SI at 09C0:0533 and pops it at 09C0:059F before
    //   it passes control to the function.
    public void Run(GameState state, int callerWord)
    {
        _state = state;
        var savedRedraw = _ui.Redraw;
        SetSaveAndLoad(2);
        try
        {
            _images = CharacterCreationScreen.FigureImages(_ui.Assets.Files);
            LoadPictures();
            Redraw(callerWord);
            _ui.Redraw = () =>
            {
                // orig: DS:9C46 set (the party's icons changed): root 047C:0568, R038+0x024E, R038+0x0292, root 047C:056F, key = 0 (SI is that key, 0)
                LoadPictures();
                Redraw(0);
            };
            ScreenDrawing.WaitForRelease(_ui);
            while (true)
            {
                var key = 0;
                var action = _ui.PollFrame();
                if (action is { IsLeftClick: true } click)
                {
                    key = Click(click.X, click.Y);
                }
                else if (action is { IsKey: true } pressed)
                {
                    key = pressed.Key;
                }
                if (key is Keys.Escape or 0x1352 or 0x1372)
                {
                    return;
                }
                if (key != 0 && Handle(key) == Keys.Escape)
                {
                    return;
                }
            }
        }
        finally
        {
            // orig: R038+0x01FE: the figure and the button pictures freed, the fonts put back, DS:9920 = DS:9934 = 1, the button waited out
            //   (R038+0x0235), then the mouse driver's release count read and cleared (root 047C:1320 with 6, 0, 0, 0 at R038+0x0244)
            _ui.Redraw = savedRedraw;
            SetSaveAndLoad(1);
            ScreenDrawing.WaitForRelease(_ui);
            _ui.TakeLeftReleases();
        }
    }

    private void SetSaveAndLoad(int flag)
    {
        if (HotKeys is { } keys)
        {
            keys.SaveGameItem = flag;
            keys.LoadGameItem = flag;
        }
    }

    // ----- input -----

    // orig: R038+0x0000, the click areas (the button is waited out before the row is taken)
    private int Click(int x, int y)
    {
        if (x > 0x46 && x < 0xC8 && y > 0x2D && y < 0xA5)
        {
            ScreenDrawing.WaitForRelease(_ui);
            var row = (y - 0x2D) / 15;
            return row < Commands.Length ? Commands[row].Key : 0;
        }
        if (x > 0xC5 && x < 0x138 && y > 0x14 && y < 0xB4)
        {
            ScreenDrawing.WaitForRelease(_ui);
            var row = (y - 0x14) / 8;
            if (Menu.Count > row)
            {
                Screen.Recolor(0xC6, (Menu.Selected << 3) + 0x16, 0x71, 8, Highlight, NameColor);
                Menu.Selected = row;
                Screen.Recolor(0xC6, (row << 3) + 0x16, 0x71, 8, NameColor, Highlight);
            }
        }
        return 0;
    }

    /// <summary>One key (R038+0x02F8); returns what the handler leaves in AX, which ends the screen when it is 0x11B.</summary>
    // orig: R038+0x02F8 (1462:02F8) draft: the dispatch checked against asm (a compare/subtract chain); unknown keys fall through with AX
    // the chain's remainder: key - 0x1000 (below 0x1352), key - 0x1F73 (0x1352..0x2348), key - 0x4800 (0x2348..0x4B00), key - 0x5133 (above)
    // C: the function has no return statement: AX is what the last instruction left; ResultOfDriver stands for a graphics call's AX.
    // DORMANT: case 0x1352 / 0x1372 (R: return 0x11B): the loop leaves on R before calling the handler.
    // DORMANT: case 0x1000 (Alt-Q: R038+0x01FE(0), then root 0000:13A0, the question): the hot keys take Alt-Q first.
    private int Handle(int key)
    {
        switch (key)
        {
            case Keys.Up or Keys.Left or Keys.Right:
                return Arrow(up: true);
            case Keys.Down:
                return Arrow(up: false);
            case 0x1352 or 0x1372:
                return Keys.Escape;
            case 0x1E41 or 0x1E61:
                return AddOrRemove(key);
            case 0x2E43 or 0x2E63:
                return CreateCharacter(key);
            case 0x2348 or 0x2368:
                return Heraldry();
            case 0x1F53 or 0x1F73:
                return SelectImage();
            case 0x0231 or 0x4F31:
                return Colour(1);
            case 0x0332 or 0x5032:
                return Colour(2);
            case 0x0433 or 0x5133:
                return Colour(3);
            case Keys.AltQ:
                // C: R038+0x01FE(0) first (the flags back to 1, the pictures freed), then the question; after "no" the screen goes on.
                SetSaveAndLoad(1);
                if (new MessageBox(_ui).Ask("Quit to DOS?"))
                {
                    throw new QuitGameException();
                }
                return ResultOfDriver;
            default:
                return (ushort)(key < 0x1352 ? key - 0x1000 : key < 0x2348 ? key - 0x1F73 : key < 0x4B00 ? key - 0x4800 : key - 0x5133);
        }
    }

    /// <summary>AX after a graphics driver call (a picture drawn, the palette set, the pointer shown).</summary>
    // UNCERTAIN: the driver's AX is taken never to be 0x11B (it would end the screen).
    private const int ResultOfDriver = 0;

    /// <summary>The arrows: the highlight moves (wrapping on DS:E7DC), the figure place is cleared and the figure of the slot holding the ROW's number drawn.</summary>
    // orig: R038+0x02F8 keys 0x4800/0x4B00/0x4D00 and 0x5000 (checked against asm): the old highlight off, the move, the new one on;
    // the figure rectangle (0x46, 0xA5, 0x23 x 0x23) copied back; the ids read; for the slot found: the figure picture
    // PICS\ + the figure list's entry of the ROW + SMALL.PIC, the slot's colours into the shared ones, the palette set, the figure drawn at
    // (0x0C, 0xA4) for Up / Left / Right and at (0x46, 0xA5) for Down; no slot: AX is DS:E7DA (the search's last load)
    // C: Up, Left and Right draw the figure at (0x0C, 0xA4), over the fifth row of the party column, where the creation menu has it.
    // DIVERGENCE: a row past the figure list (DS:E7E2, one 4-byte name per figure picture, indexed by the row DS:E7DA) whose number a party slot
    // holds names "pics\small.pic", which does not exist: the original's picture load (root 0617:045C from R038+0x04CA) ends the program
    // (exit 99, "pics\small.pic: No such file or directory", root 06A1:01F0); here nothing is drawn and the screen goes on.
    private int Arrow(bool up)
    {
        Screen.Recolor(0xC6, (Menu.Selected << 3) + 0x16, 0x71, 8, Highlight, NameColor);
        if (up)
        {
            InnRoster.SelectPrevious(Menu);
        }
        else
        {
            InnRoster.SelectNext(Menu);
        }
        Screen.Recolor(0xC6, 8 * Menu.Selected + 0x16, 0x71, 8, NameColor, Highlight);
        CopyBack(0x46, 0xA5, 0x23, 0x23);
        if (InnRoster.FigureSlot(_state, Menu) < 0)
        {
            return Menu.Selected;
        }
        ApplyFigureColours();
        if (Menu.Selected >= 0 && Menu.Selected < _images.Count)
        {
            DrawFigure(_images[Menu.Selected], up ? 0x0C : 0x46, up ? 0xA4 : 0xA5);
        }
        return ResultOfDriver;
    }

    /// <summary>"Add to the Party" (A): the button pressed, the character added or taken out again, the list drawn again.</summary>
    // orig: R038+0x02F8 key 0x1E41 / 0x1E61 (checked against asm): with DS:E7DC > 0: the button (0x43, 0x3B) and its label at (0x4A, 0x3F);
    // InnRoster.AddOrRemove; leaving: the slot's panel row; joining: the start colours set, the figure place (0x46, 0xA5, 0x23 x 0x24)
    // copied back and the first figure drawn there, the slot's frame and row; no room: the button put back (AX: the pointer's);
    // otherwise the ids written, the button put back and R038+0x23EC on the screen window (its SI: this key)
    private int AddOrRemove(int key)
    {
        if (Menu.Count <= 0)
        {
            return 0;
        }
        PressButton(0x3B, 0x3F, CreationData.AddToParty);
        var (change, slot) = InnRoster.AddOrRemove(_state, Menu, _images);
        switch (change)
        {
            case InnRosterChange.NoRoom:
                CopyBack(0x43, 0x3B, 0x82, 0x0E);
                return ResultOfDriver;
            case InnRosterChange.Left:
                _panel.DrawRow(Screen, _state.Party, slot);
                break;
            case InnRosterChange.Joined:
                var member = _state.Party.Slot(slot);
                _panel.SetShieldPicture(slot, member, 'A');
                ApplyFigureColours();
                CopyBack(0x46, 0xA5, 0x23, 0x24);
                if (_images.Count > 0)
                {
                    DrawFigure(_images[0], 0x46, 0xA5);
                }
                PartyPanel.DrawFrame(Screen, _ui.Assets.CommonSprites, slot, lit: false);
                _panel.DrawRow(Screen, _state.Party, slot);
                break;
        }
        CopyBack(0x43, 0x3B, 0x82, 0x0E);
        DrawList(Screen, key);
        return ResultOfDriver;
    }

    /// <summary>"Create a Character" (C): the character generator, then the whole screen again; the new character joins the roster here.</summary>
    // orig: R038+0x02F8 key 0x2E43 / 0x2E63: the button (0x43, 0x2C) and its label at (0x4A, 0x31), the character generator R037+0x4664 (thunk
    // 09C0:20CB, which appends the character with R037+0x38AA), the button picture freed, R038+0x024E, R038+0x0292 (its SI: this key)
    // C: the generator draws with the game's random numbers and the party's seed (CharacterCreation).
    private int CreateCharacter(int key)
    {
        PressButton(0x2C, 0x31, CreationData.CreateCharacter);
        var creation = new CharacterCreation(_data.Tables, _world.ItemDefinitions, _state.Random, _state.Party.Seed, _state.ClockSeed ?? (() => 0))
        {
            RecordBufferWord = CharacterCreationScreen.RecordBuffer,
        };
        if (new CharacterGeneratorScreen(_ui, _data, _world, creation).Run() is { } made)
        {
            InnRoster.AddNew(_state, made);
        }
        LoadPictures();
        Redraw(key);
        return ResultOfDriver;
    }

    /// <summary>"Heraldry" (H): the next shield for the highlighted character if in a slot.</summary>
    // orig: R038+0x02F8 key 0x2348 / 0x2368: with DS:E7DC > 0: the button (0x43, 0x4A), label (0x4A, 0x4E); InnRoster.NextShield; the slot's
    // shield picture and panel row; the button put back
    private int Heraldry()
    {
        if (Menu.Count <= 0)
        {
            return 0;
        }
        PressButton(0x4A, 0x4E, CreationData.Heraldry);
        var slot = InnRoster.NextShield(_state, Menu);
        if (slot >= 0)
        {
            var member = _state.Party.Slot(slot);
            _panel.SetShieldPicture(slot, member, member.ShieldLetter);
            _panel.DrawRow(Screen, _state.Party, slot);
        }
        CopyBack(0x43, 0x4A, 0x82, 0x0E);
        return ResultOfDriver;
    }

    /// <summary>"Select Character Image" (S): the next figure for the highlighted character if in a slot.</summary>
    // orig: R038+0x02F8 key 0x1F53 / 0x1F73: with DS:E7DC > 0: the button (0x43, 0x59), label (0x4A, 0x5D); InnRoster.NextImage; the figure
    // place (0x46, 0xA5, 0x23 x 0x24) copied back and the new figure drawn there; the button put back
    private int SelectImage()
    {
        if (Menu.Count <= 0)
        {
            return 0;
        }
        PressButton(0x59, 0x5D, CreationData.SelectImage);
        if (InnRoster.NextImage(_state, Menu, _images) >= 0)
        {
            CopyBack(0x46, 0xA5, 0x23, 0x24);
            DrawFigure(_images[Menu.Image], 0x46, 0xA5);
        }
        CopyBack(0x43, 0x59, 0x82, 0x0E);
        return ResultOfDriver;
    }

    /// <summary>1, 2, 3: the next colours of a part of the figure of the slot holding the ROW's number; no button.</summary>
    // orig: R038+0x02F8 keys 0x231/0x4F31, 0x332/0x5032, 0x433/0x5133: with DS:E7DC > 0; InnRoster.CycleColour, the palette set; no slot: AX is
    // DS:E7DA (the search's last load)
    // C: the shared colours are the current palette's colours 235..242 (DS:E753 is the current palette's colour 235): read from the screen here.
    private int Colour(int key)
    {
        if (Menu.Count <= 0)
        {
            return 0;
        }
        ReadFigureColours();
        if (InnRoster.CycleColour(_state, Menu, key) < 0)
        {
            return Menu.Selected;
        }
        ApplyFigureColours();
        return ResultOfDriver;
    }

    // ----- drawing -----

    /// <summary>The background with its colours and the button picture (R038+0x024E).</summary>
    // orig: R038+0x024E (1462:024E) draft (bytes match): PICS\CRETSCR3.PIC with its palette into the current palette (DS:E48C), PICS\BUTTNCR2.PIC loaded,
    // the palette set (root 047C:058B), the back window's colour 8, both windows' font 1
    private void LoadPictures()
    {
        var picture = _ui.Assets.Picture("cretscr3");
        _back.DrawPicture(picture);
        Screen.UsePalette(picture);
    }

    /// <summary>The labels and the list on the back window, the whole of it on the screen, then the party column (R038+0x0292).</summary>
    // orig: R038+0x0292 (1462:0292) draft (bytes match): R038+0x25CA(back), R038+0x23EC(back), the back window copied to the screen, for each of
    // the five slots whose status byte is not 0 the panel's frame and row (root 0000:1588, root 0000:1716)
    private void Redraw(int callerWord)
    {
        // orig: R038+0x25CA (1462:25CA) draft (bytes match): the eight labels in colour 8 at (0x48, 0x30 + 15 x row)
        foreach (var (text, _, y) in Commands)
        {
            _ui.Text.DrawCoded(_back, 0x48, y, _data.Texts[text], BackLabelColor);
        }
        DrawList(_back, callerWord);
        Screen.Draw(_back.Screen.Pixels, Canvas.Width, Canvas.Height, 0, 0);
        for (var i = 0; i < Party.MaxMembers; i++)
        {
            if (_state.Party.Slot(i).IsPresent)
            {
                PartyPanel.DrawFrame(Screen, _ui.Assets.CommonSprites, i, lit: false);
                _panel.DrawRow(Screen, _state.Party, i);
            }
        }
    }

    /// <summary>The list (R038+0x23EC) on <paramref name="on"/>: each row's strip copied back to the screen, the '+' and the name, the highlight.</summary>
    // orig: R038+0x23EC (1462:23EC) draft (bytes match), checked against asm: for each record listed (InnRoster.List) the strip (0xC5, 0x18 + 8 x row,
    // 0x73 x 8) copied from the back window to the screen; '+' in colour 0x11 at (200, y) and 0x10 at (199, y - 1) for each slot whose entry
    // equals the row; the name (record +0x25) in 0x11 at (0xD0, y - 1) and 0x10 at (0xCF, y - 2); DS:E7DC = the count; the highlight on
    // the window drawn on
    private void DrawList(Canvas on, int callerWord)
    {
        foreach (var row in InnRoster.List(_state, Menu, callerWord))
        {
            var y = 0x18 + row.Row * 8;
            Screen.Restore(_back.Save(0xC5, y, 0x73, 8));
            if (row.Marked)
            {
                _ui.Text.Draw(on, 200, y, "+", NameShadow);
                _ui.Text.Draw(on, 199, y - 1, "+", NameColor);
            }
            _ui.Text.Draw(on, 0xD0, y - 1, row.Record.Name, NameShadow);
            _ui.Text.Draw(on, 0xCF, y - 2, row.Record.Name, NameColor);
        }
        on.Recolor(0xC6, (Menu.Selected << 3) + 0x16, 0x71, 8, NameColor, Highlight);
    }

    /// <summary>A command's button pressed: the button picture and its label in colour 8 on the screen.</summary>
    private void PressButton(int y, int labelY, int text)
    {
        Screen.Draw(_ui.Assets.Picture("buttnCR2").Image, 0x43, y);
        _ui.Text.DrawCoded(Screen, 0x4A, labelY, _data.Texts[text], BackLabelColor);
        _ui.Present();
    }

    private void CopyBack(int x, int y, int width, int height) => Screen.Restore(_back.Save(x, y, width, height));

    /// <summary>A figure (PICS\&lt;code&gt;SMALL.PIC).</summary>
    private void DrawFigure(string code, int x, int y)
    {
        var picture = code + "small";
        if (_ui.Assets.PictureExists(picture))
        {
            Screen.Draw(_ui.Assets.Picture(picture).Image, x, y, transparent: true);
        }
    }

    /// <summary>The shared figure colours into colours 235..242 (the palette set).</summary>
    private void ApplyFigureColours()
    {
        var p = Menu.Colours.Palette;
        for (var i = 0; i < 8; i++)
        {
            Screen.SetColor(FirstFigureColor + i, new Content.Formats.Graphics.Rgb24(Vga(p[3 * i]), Vga(p[3 * i + 1]), Vga(p[3 * i + 2])));
        }
    }

    /// <summary>Colours 235..242 of the screen into the shared figure colours (6-bit values).</summary>
    private void ReadFigureColours()
    {
        var p = Menu.Colours.Palette;
        for (var i = 0; i < 8; i++)
        {
            var rgb = Screen.Screen.Palette[FirstFigureColor + i];
            p[3 * i] = (byte)((rgb >> 16 & 0xFF) >> 2);
            p[3 * i + 1] = (byte)((rgb >> 8 & 0xFF) >> 2);
            p[3 * i + 2] = (byte)((rgb & 0xFF) >> 2);
        }
    }

    private static byte Vga(byte v) => (byte)((v & 0x3F) << 2 | (v & 0x3F) >> 4);
}
