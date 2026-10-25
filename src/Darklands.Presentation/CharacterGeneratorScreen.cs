using Darklands.Content;
using CreationData = Darklands.Content.Formats.Characters.CreationData;
using CreationDataDecoder = Darklands.Content.Formats.Characters.CreationDataDecoder;
using Pic = Darklands.Content.Formats.Graphics.Pic;
using Darklands.Core.Characters;
using Darklands.Core.Characters.Creation;
using Darklands.Platform;
using Darklands.Presentation.Graphics;

namespace Darklands.Presentation;

/// <summary>
/// The character generator screen (PICS\CHARGEN.PIC): the left-hand list of the step (menu,
/// family backgrounds, occupations) under the name line, the right-hand panel with age, the
/// attributes, the 19 skills with their "change:limit" columns, the experience points and the
/// bubble that empties as they are spent. Returns the finished character or null.
/// </summary>
// orig: create_character = R037+0x4664 (18C8:0004) matched; drawing R037+0x573A
// R037+0x573A matched, R037+0x5D98 matched, R037+0x4B9E matched, pictures R037+0x4B12
// R037+0x4B12 matched, lists R037+0x7F06 / R037+0x802A matched, exit R037+0x4ADC
// R037+0x4ADC matched; tests: headless chargen session (PNG)
// DIVERGENCE: the original advances the bubble every 0x5DD passes of its polling loop (machine speed); here every 6 frames (about 100 ms).
public sealed class CharacterGeneratorScreen
{
    private const byte Shadow = 0xBF;
    private const byte Ink = 0;
    private const byte Highlight = 9;
    private const byte Selected = 10;
    private const int ListX = 0x40;
    private const int ListTop = 0x2D;
    private const int RowHeight = 0x0B;
    private const int BubbleFrames = 6;

    private readonly GameUi _ui;
    private readonly CreationData _data;
    private readonly WorldData _world;
    private readonly CharacterCreation _creation;
    private readonly Canvas _back = new(new Screen());
    private readonly Pic _button;
    private readonly Pic[] _bubbles;
    private readonly Pic _bubbleBack;
    private int _bubbleFrame;
    private long _frames;

    /// <summary>The click that closed a list box: the button is still down when the generator's loop looks again.</summary>
    private PlayerAction? _pending;

    /// <summary>The list row under the mouse (DS:43EE, −1 at first; it stays between runs in the original).</summary>
    private static int s_hovered = -1;

    public CharacterGeneratorScreen(GameUi ui, CreationData data, WorldData world, CharacterCreation creation)
    {
        _ui = ui;
        _data = data;
        _world = world;
        _creation = creation;
        _button = ui.Assets.Picture("buttona");
        _bubbles = [.. Enumerable.Range(1, 5).Select(i => ui.Assets.Picture($"bubble{i:00}"))];
        _bubbleBack = ui.Assets.Picture("bublback");
    }

    private Canvas Screen => _ui.Canvas;

    private TextPainter Text => _ui.Text;

    /// <summary>The exit R037+0x4ADC: the button waited out, the mouse driver's left-release count cleared.</summary>
    // C: the end of R037+0x4ADC: R037+0x4AF7 `while (mouse_buttons & 1);`, then INT 33h function 6 through the root's mouse call
    private Character? Leave(Character? made)
    {
        ScreenDrawing.WaitForRelease(_ui);
        _ui.TakeLeftReleases();
        return made;
    }

    public Character? Run()
    {
        // orig: R037+0x4664 sets state 0 and resets (R037+0x675A) before drawing
        _creation.Reset();
        var chargen = _ui.Assets.Picture("chargen");
        _back.DrawPicture(chargen);
        Screen.UsePalette(chargen);
        DrawAll();
        while (true)
        {
            AnimateBubble();
            Hover();
            // orig: R037+0x4664's loop reads the left button (DS:6EF0 & 1) and the keys (root 047C:073D / 0744) itself: it does not call
            //   root 0000:0000, so neither the menu bar (the right button) nor the hot keys run in the generator
            var action = _pending ?? _ui.PollWithoutBar();
            _pending = null;
            if (action is not { } a)
            {
                _ui.Idle();
                continue;
            }
            var key = a.IsLeftClick ? Click(a.X, a.Y) : a.Key;
            if (key == Keys.Escape)
            {
                // orig: R037+0x4664: Esc goes back a step or leaves
                if (_creation.Back() == CreationOutcome.Left)
                {
                    return Leave(null);
                }
                DrawAll();
                continue;
            }
            if (key == Keys.None)
            {
                continue;
            }
            switch (Handle(key))
            {
                case CreationOutcome.Left:
                    return Leave(null);
                case CreationOutcome.Finished:
                    return Leave(_creation.Finished);
            }
        }
    }

    // ----- input -----

    /// <summary>A click: list rows give the commands 1..15, the panels give '+', '-' and the two lists (key codes as the original).</summary>
    // orig: R037+0x4664, the click areas
    private int Click(int x, int y)
    {
        var stage = _creation.Stage;
        if (x > 0x40 && x < 0xAE && y > 0x2D && y < 0xC7)
        {
            var command = (y - 0x2D) / RowHeight + 1;
            // orig: state 4, command 1 ("Go to next occupation") past 65 is ignored for the mouse (the G key still works)
            return stage == CreationStage.OccupationSkills && command == 1 && _creation.Age > 0x41 ? Keys.None : command;
        }
        if (stage == CreationStage.ChildhoodAttributes && x > 0xC4 && x < 0xEC && y > 0x12 && y < 0x4D)
        {
            var row = (y - 0x12) / 8;
            return row < 6 ? SelectRow(row, 0xC5, 0x26) : Keys.None;
        }
        if (stage == CreationStage.OccupationSkills && x > 0xF5 && x < 0x13B && y > 0x12 && y < 0xB9)
        {
            var row = (y - 0x12) / 8;
            return row is 14 or 7 ? Keys.None : SelectRow(row, 0xF6, 0x44);
        }
        if (stage is CreationStage.OccupationSkills or CreationStage.ChildhoodAttributes && x > 0xC3 && x < 0xEB && y > 0x5F && y < 0xA5)
        {
            return 0x0C2D;
        }
        if (stage >= CreationStage.Occupation && x > 0xAE && x < 0xEA && y > 0xA8 && y < 0xB4)
        {
            return 0x5032;
        }
        if (stage >= CreationStage.Occupation && x > 0xAE && x < 0xEA && y > 0xB8 && y < 0xC4)
        {
            return 0x5133;
        }
        return Keys.None;
    }

    /// <summary>Clicking an attribute or skill row selects it; clicking the selected row is '+'.</summary>
    private int SelectRow(int row, int x, int width)
    {
        var plus = _creation.Cursor == row ? 0x0D2B : Keys.None;
        Screen.Recolor(x, _creation.Cursor * 8 + 0x14, width, 8, Selected, Shadow);
        _creation.SelectRow(row);
        Screen.Recolor(x, row * 8 + 0x14, width, 8, Shadow, Selected);
        return plus;
    }

    /// <summary>The mouse over the list highlights the row (and describes it in the lists).</summary>
    private void Hover()
    {
        var mouse = _ui.Mouse;
        if (mouse.Left || mouse.X <= 0x40 || mouse.X >= 0xAE || mouse.Y <= 0x2D || mouse.Y >= 0xC7)
        {
            return;
        }
        var row = (mouse.Y - 0x2D) / RowHeight;
        if (row == s_hovered)
        {
            return;
        }
        if (s_hovered != -1)
        {
            Screen.Recolor(0x41, s_hovered * RowHeight + 0x2F, 0x6C, RowHeight, Highlight, Ink);
        }
        s_hovered = row;
        Screen.Recolor(0x41, row * RowHeight + 0x2F, 0x6C, RowHeight, Ink, Highlight);
        if (_creation.Stage is CreationStage.FamilyBackground or CreationStage.Occupation && row < _creation.Choices.Count)
        {
            DrawPanel();
        }
    }

    /// <summary>The keys (<c>R037+0x4BEE</c>): letters are numbered commands, then '+', '-', arrows, the lists.</summary>
    // orig: R037+0x4BEE (18C8:058E) draft (same length)
    // DORMANT: the keys 1, 4, 5 (and keypad 1, 4, 5) have an empty case.
    private CreationOutcome Handle(int key)
    {
        var c = char.ToLowerInvariant(Keys.CharacterOf(key));
        var stage = _creation.Stage;
        if (stage == CreationStage.OccupationSkills && key > 0xFF)
        {
            // orig: the first switch: in state 4, N is command 1, B command 2, K command 3
            key = c switch { 'n' => 1, 'b' => 2, 'k' => 3, _ => key };
        }
        var command = key is >= 1 and <= 15 ? key : c switch
        {
            'g' or 'b' => 1,
            'm' => 2,
            's' => 3,
            'e' => 4,
            'c' => 5,
            'r' => 6,
            _ => 0,
        };
        if (key == Keys.AltQ)
        {
            // orig: case 0x1000: R037+0x4ADC(), quit_to_dos()
            throw new QuitGameException();
        }
        if (key is Keys.Up or Keys.Left or Keys.Right or Keys.Down)
        {
            MoveCursor(key == Keys.Down ? 1 : -1);
            return CreationOutcome.None;
        }
        if (key is 0x0D2B or 0x0D3D or 0x4E2B || c == '+')
        {
            if (_creation.Raise())
            {
                DrawPanel();
            }
            return CreationOutcome.None;
        }
        if (key is 0x0C2D or 0x4A2D || c == '-')
        {
            if (_creation.Lower())
            {
                DrawPanel();
            }
            return CreationOutcome.None;
        }
        if (c == '2')
        {
            ShowFormulas();
            return CreationOutcome.None;
        }
        if (c == '3')
        {
            ShowSaints();
            return CreationOutcome.None;
        }
        if (command == 0)
        {
            return CreationOutcome.None;
        }
        var outcome = _creation.Choose(command);
        switch (outcome)
        {
            case CreationOutcome.EnterName:
                _creation.SetName(new TextInputBox(_ui).Ask("Character Name:", CharacterCreation.MaxNameLength));
                DrawNameLine();
                break;
            case CreationOutcome.EnterNickname:
                _creation.SetNickname(new TextInputBox(_ui).Ask("Character Nickname:", CharacterCreation.MaxNicknameLength));
                DrawNameLine();
                break;
            case CreationOutcome.Changed:
                DrawNameLine();
                DrawList();
                DrawPanel();
                break;
            case CreationOutcome.NewStage:
                DrawAll();
                break;
        }
        return outcome;
    }

    /// <summary>The arrows move the attribute or skill highlight (going up also redraws the skills).</summary>
    // orig: R037+0x4BEE cases 0x4800/0x4B00/0x4D00 and 0x5000
    private void MoveCursor(int step)
    {
        var (x, width) = _creation.Stage == CreationStage.ChildhoodAttributes ? (0xC6, 0x25) : (0xF7, 0x43);
        if (_creation.Stage is not (CreationStage.ChildhoodAttributes or CreationStage.OccupationSkills))
        {
            return;
        }
        Screen.Recolor(x, _creation.Cursor * 8 + 0x14, width, 8, Selected, Shadow);
        _creation.MoveCursor(step);
        if (step < 0 && _creation.Stage == CreationStage.OccupationSkills)
        {
            DrawPanel();
        }
        Screen.Recolor(x, _creation.Cursor * 8 + 0x14, width, 8, Shadow, Selected);
    }

    // ----- drawing -----

    /// <summary>The whole screen (<c>R037+0x4B9E</c>).</summary>
    // orig: R037+0x4B9E (18C8:053E) draft (bytes match)
    private void DrawAll()
    {
        Screen.Draw(_back.Screen.Pixels, Canvas.Width, Canvas.Height, 0, 0);
        DrawList();
        DrawPanel();
    }

    private void CopyBack(int x, int y, int width, int height) => Screen.Restore(_back.Save(x, y, width, height));

    /// <summary>Text with the shadow first, one pixel lower.</summary>
    private void Shadowed(int x, int y, string text)
    {
        Text.Draw(Screen, x, y + 1, text, Shadow);
        Text.Draw(Screen, x, y, text, Ink);
    }

    private void DrawNameLine()
    {
        CopyBack(0x40, 1, 0xB4, 0x0C);
        Shadowed(0x47, 4, _creation.Nickname);
        Shadowed(0x83, 4, _creation.Name);
    }

    private void Button(int y, string text)
    {
        Screen.Draw(_button.Image, ListX, y);
        Text.DrawCoded(Screen, 0x44, y + 2, text, Ink);
    }

    /// <summary>The left-hand side (<c>R037+0x573A</c>): name line, help lines and the step's list.</summary>
    // orig: R037+0x573A (18C8:10DA) draft (bytes match)
    private void DrawList()
    {
        var t = _data.Texts;
        Shadowed(0x47, 4, _creation.Nickname);
        Shadowed(0x83, 4, _creation.Name);
        switch (_creation.Stage)
        {
            case CreationStage.NameAndSex:
                Help("Select character name", "and gender, then start", "childhood.");
                Button(0x38, t[_creation.IsFemale ? CreationData.MakeHerAMan : CreationData.MakeHimAWoman]);
                Button(0x2D, t[CreationData.BeginChildhood]);
                Button(0x43, t[CreationData.SelectNewName]);
                Button(0x4E, t[CreationData.EnterNewName]);
                Button(0x59, t[CreationData.CreateNickname]);
                Button(0x64, t[CreationData.ReturnToGameOptions]);
                break;
            case CreationStage.FamilyBackground:
                Help(t[CreationData.SelectFamilyBackground]);
                for (var i = 0; i < 6; i++)
                {
                    Button(ListTop + i * RowHeight, _creation.Tables.Backgrounds[i].Name);
                }
                Screen.Recolor(0x44, _creation.Cursor * RowHeight + 0x2F, 0x6C, RowHeight, Ink, Highlight);
                break;
            case CreationStage.ChildhoodAttributes:
                Help("Use EPs to improve attr", "Select attribute and", "use + or - to change");
                Button(0x2D, t[CreationData.DoneChangingAttributes]);
                break;
            case CreationStage.Occupation:
                Help(t[CreationData.SelectOccupation]);
                // C: the original builds the list again here (R037+0x691E), which also puts the cursor on row 0.
                _creation.RefreshChoices();
                // C: at most 14 rows are drawn, though the list may hold 15 (the 15th can still be clicked).
                for (var i = 0; i < _creation.Choices.Count && i < 14; i++)
                {
                    Button(ListTop + i * RowHeight, _creation.Tables.Occupations[_creation.Choices[i]].Name);
                }
                Screen.Recolor(0x44, _creation.Cursor * RowHeight + 0x2F, 0x6C, RowHeight, Ink, Highlight);
                break;
            case CreationStage.OccupationSkills:
                Help("Use EPs to improve skills", "Select skill and use + or", "- to change");
                Button(0x2D, t[CreationData.NextOccupation]);
                Button(0x38, t[CreationData.BeginAdventuring]);
                Button(0x43, t[CreationData.KillCharacter]);
                break;
        }
    }

    /// <summary>Help lines at y 0x12, 0x1A, 0x22 (coded texts for the list titles).</summary>
    private void Help(params string[] lines)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            var y = 0x12 + 8 * i;
            Text.DrawCoded(Screen, 0x47, y + 1, lines[i], Shadow);
            Text.DrawCoded(Screen, 0x47, y, lines[i], Ink);
        }
    }

    private static readonly string[] AttributeLabels = ["End", "Str", "Agl", "Per", "Int", "Chr", "DF "];

    private static readonly string[] SkillLabels =
    [
        "wEdg", "wImp", "wFll", "wPol", "wThr", "wBow", "wMsD",
        "Alch", "Relg", "Virt", "SpkC", "SpkL", "R&W",
        "Heal", "Artf", "Stlh", "StrW", "Ride", "WdWs",
    ];

    /// <summary>The right-hand panel (<c>R037+0x5D98</c>).</summary>
    // orig: R037+0x5D98 (18C8:1738) draft (bytes match)
    private void DrawPanel()
    {
        var c = _creation;
        var entry = c.ShownEntry(s_hovered);
        CopyBack(0xC5, 0x57, 0x14, 9);
        Text.Draw(Screen, 0xD8, 0x57, "EP", Shadow);
        if (c.ShownExperience(entry) is { } ep)
        {
            Text.Draw(Screen, 0xC6, 0x57, Two(ep), Shadow);
        }
        CopyBack(0x10A, 4, 0x23, 9);
        Text.Draw(Screen, 0x11E, 5, "Age", Shadow);
        Text.Draw(Screen, 0x10C, 5, Two(c.Age), Shadow);
        Text.Draw(Screen, 0x11E, 4, "Age", Ink);
        Text.Draw(Screen, 0x10C, 4, Two(c.Age), Ink);

        CopyBack(0xC5, 0x0F, 0x24, 0x3C);
        for (var i = 0; i < 7; i++)
        {
            Text.Draw(Screen, 0xD8, 0x14 + 8 * i, AttributeLabels[i], Shadow);
        }
        for (var i = 0; i < 6; i++)
        {
            Text.Draw(Screen, 0xC9, 0x14 + 8 * i, Two(c.ShownAttribute(entry, i)), Shadow);
        }
        Text.Draw(Screen, 0xC9, 0x44, Two(c.Attributes[(int)Core.Characters.Attribute.DivineFavor]), Shadow);
        if (c.Stage == CreationStage.ChildhoodAttributes)
        {
            Screen.Recolor(0xC5, c.Cursor * 8 + 0x14, 0x26, 8, Shadow, Selected);
        }

        SkillBlock(entry, 0, 7, 0x0F, 0x3C, 0x3F, 0x14, 0x28);
        SkillBlock(entry, 7, 6, 0x4E, 0x34, 0x34, 0x54, 0x28);
        SkillBlock(entry, 13, 6, 0x86, 0x36, 0x34, 0x8E, 0x34);
        if (c.Stage == CreationStage.OccupationSkills)
        {
            Screen.Recolor(0xF6, c.Cursor * 8 + 0x14, 0x44, 8, Shadow, Selected);
        }
    }

    private void SkillBlock(int entry, int first, int count, int top, int labelHeight, int valueHeight, int textTop, int labelWidth)
    {
        CopyBack(0x113, top, labelWidth, labelHeight);
        for (var i = 0; i < count; i++)
        {
            Text.Draw(Screen, 0x122, textTop + 8 * i, SkillLabels[first + i], Shadow);
        }
        CopyBack(0xF5, top, 0x18, valueHeight);
        for (var i = 0; i < count; i++)
        {
            Text.Draw(Screen, 0x116, textTop + 8 * i, Two(_creation.Skills[first + i]), Shadow);
            Text.Draw(Screen, 0xF6, textTop + 8 * i, _creation.ShownSkillChange(entry, first + i), Shadow);
        }
    }

    // C: "%02d" of a negative value is "-1", not "-01".
    private static string Two(int v) => v < 0 ? v.ToString(System.Globalization.CultureInfo.InvariantCulture) : v.ToString("00", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The bubble: five frames over the background, its top covered as the points are spent.</summary>
    // orig: R037+0x4664: gfx_draw_pic(back_win, 0xD0, 0x69, bubble[f]); gfx_draw_sprite({0xD0, 0x68, 0x0C, DS:43F8}, bublback); copy 0x0C x 0x32
    private void AnimateBubble()
    {
        if (_frames++ % BubbleFrames != 0)
        {
            return;
        }
        _back.Draw(_bubbles[_bubbleFrame].Image, 0xD0, 0x69);
        var covered = Math.Clamp(_creation.SpentGauge, 0, _bubbleBack.Image.Height);
        if (covered > 0)
        {
            _back.Draw(_bubbleBack.Image.Pixels[..(covered * _bubbleBack.Image.Width)], _bubbleBack.Image.Width, covered, 0xD0, 0x68);
        }
        CopyBack(0xD0, 0x69, 0x0C, 0x32);
        _bubbleFrame = (_bubbleFrame + 1) % _bubbles.Length;
    }

    /// <summary>Key 2: the formulas known (three grade bits per potion), then the screen again.</summary>
    // orig: R037+0x7F06 (18C8:38A6) matched
    private void ShowFormulas()
    {
        Screen.Draw(_ui.Assets.Picture("armbrs13").Image, 0x29, 0x31);
        var n = 0;
        for (var i = 0; i < _creation.FormulaBytes.Length; i++)
        {
            for (var grade = 0; grade < 3; grade++)
            {
                if ((_creation.FormulaBytes[i] & (1 << grade)) != 0)
                {
                    Text.DrawCoded(Screen, 0x32, (n++ + 8) << 3, FormulaName(i * 3 + grade), 8);
                }
            }
        }
        WaitThenRedraw();
    }

    /// <summary>Key 3: the saints known, then the screen again.</summary>
    // orig: R037+0x802A (18C8:39CA) matched
    // C: the list reads the bits with DS:00E8 = {0x80, 0x40, ...}, the game's order, so it shows the saints the game knows.
    private void ShowSaints()
    {
        Screen.Draw(_ui.Assets.Picture("armbrs13").Image, 0x29, 0x41);
        var n = 0;
        foreach (var saint in _creation.Saints)
        {
            Text.DrawCoded(Screen, 0x32, (n++ << 3) + 0x4E, SaintName(saint), 8);
        }
        WaitThenRedraw();
    }

    private string FormulaName(int f) => f < _world.Items.Formulae.Count ? _world.Items.Formulae[f].FullName : "";

    private string SaintName(int s) => s < _world.Items.Saints.Count ? _world.Items.Saints[s].FullName : "";

    // orig: R037+0x7F06 / R037+0x802A: `while (!key_pressed() && !(mouse_buttons & 1));` (no root 0000:0000: no menu bar, no hot keys) then a key is taken (get_key), a press is not:
    // orig: after R037+0x4B9E the loop of R037+0x4664 finds the button still down and takes the press as a click where the pointer is
    // UNCERTAIN: whether the button is still down after the redraw depends on how long the press lasts against the redraw's time: the C#
    // redraws at once, so a press seen down here acts (the oracle's 300 ms scripted press outlasts the emulated redraw of the first steps,
    // not always that of the occupation list)
    private void WaitThenRedraw()
    {
        var closing = _ui.WaitForActionWithoutBar();
        DrawAll();
        if (closing.IsLeftClick && _ui.Mouse.Left)
        {
            _pending = closing;
        }
    }
}
