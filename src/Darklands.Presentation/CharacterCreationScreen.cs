using Darklands.Content;
using CreationData = Darklands.Content.Formats.Characters.CreationData;
using CreationDataDecoder = Darklands.Content.Formats.Characters.CreationDataDecoder;
using Pic = Darklands.Content.Formats.Graphics.Pic;
using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Core.Characters.Creation;
using Darklands.Core.Randomness;
using Darklands.Persistence;
using Darklands.Platform;
using Darklands.Presentation.Graphics;
using Darklands.Simulation;

namespace Darklands.Presentation;

/// <summary>
/// "Create characters" (PICS\CRETSCRN.PIC): the menu of eleven commands in the middle, the
/// characters on the right ('+' marks party members), the party column on the left and the
/// selected member's figure below it. The roster starts with the four ready-made adventurers.
/// <see cref="Run"/> returns the new game after "Begin the Adventure", or null for "Return".
/// </summary>
// orig: R037+0x0000 (1462:0000) draft (only an unused stack slot differs); key handler
// R037+0x0444 draft; pictures R037+0x0390 matched; redraw R037+0x03D4 matched;
// list R037+0x2760 matched; labels R037+0x2922 matched; figures R037+0x4562
// matched; exit R037+0x0348 matched; tests: headless chargen session (PNG)
// DORMANT: the save-game half of this overlay (game-store-entry, R037+0x2ACE) is not part of this menu.
public sealed class CharacterCreationScreen
{
    private const byte LabelColor = 8;
    private const byte NameColor = 0x10;
    private const byte NameShadow = 0x11;
    private const byte SelectedName = 0x12;
    // orig: the figure colours DS:E753..DS:E76A, written into the current palette block (DS:E48C) and set (root 047C:058B), are colours
    // 235..242 on the screen, not 237..244: the original's DAC after A in the creation menu (R037+0x0B9C..0x0C88) holds the 24 start bytes
    // there (vis10 session D-b0c9a6).
    private const int FirstFigureColor = 235;

    /// <summary>The commands from top to bottom: label text, key, label y.</summary>
    private static readonly (int Text, char Key, int Y)[] Commands =
    [
        (CreationData.CreateCharacter, 'c', 0x1C),
        (CreationData.AddToParty, 'a', 0x2B),
        (CreationData.Heraldry, 'h', 0x3A),
        (CreationData.SelectImage, 's', 0x49),
        (CreationData.FirstColour, '1', 0x58),
        (CreationData.SecondColour, '2', 0x67),
        (CreationData.ThirdColour, '3', 0x76),
        (CreationData.DeleteFromParty, 'd', 0x85),
        (CreationData.KillCharacter, 'k', 0x94),
        (CreationData.BeginAdventure, 'b', 0xA3),
        (CreationData.ReturnToMainMenu, 'r', 0xB2),
    ];

    /// <summary>
    /// The highlighted row, the figure image chosen last and the figure colours: data-segment globals that outlive the menu and that
    /// the inn's roster (PartyRosterScreen) shares.
    /// </summary>
    // orig: DS:E7DA, DS:E7E0, DS:E753..DS:E76A, DS:E890 / DS:E892 / DS:E894 (PartyMenuState)
    internal static PartyMenuState Shared { get; } = new();

    /// <summary>The last word of the record buffer DS:8B52 (DS:8BD0), which the character generator's panel reads as DS:8BD2[-1]; 0 at start-up.</summary>
    private static int s_recordBuffer;

    /// <summary>The last word of the record buffer as this menu's list left it (the character generator's panel reads it).</summary>
    internal static int RecordBuffer => s_recordBuffer;

    private readonly GameUi _ui;
    private readonly WorldData _world;
    private readonly Func<uint> _seed;
    private readonly Func<ushort>? _partyTicks;
    private readonly CreationData _data;
    private readonly PartyPanel _panel;
    private readonly Canvas _back = new(new Screen());
    private readonly IReadOnlyList<string> _images;
    private CreationRoster _roster = null!;
    private CharacterSheetScreen? _sheet;
    private GameState? _sheetState;

    /// <summary>The game's leader variable: the menu never writes it, the sheet's Leader box does.</summary>
    private int _leader;

    /// <param name="seed">The clock the original reads: the city at the click, names and reseeds after.</param>
    /// <param name="partyTicks">The BIOS tick word read at the click: the party seed (none: the clock's low word).</param>
    public CharacterCreationScreen(GameUi ui, WorldData world, Func<uint> seed, Func<ushort>? partyTicks = null)
    {
        _ui = ui;
        _world = world;
        _seed = seed;
        _partyTicks = partyTicks;
        _data = CreationDataDecoder.Decode(ui.Assets.Files.Read("DARKLAND.EXE"));
        _panel = new PartyPanel(ui.Assets);
        _images = FigureImages(ui.Assets.Files);
    }

    private Canvas Screen => _ui.Canvas;

    /// <summary>The figure codes from PICS\F??STAT.PIC, A??STAT.PIC, C??STAT.PIC (<c>R037+0x4562</c>).</summary>
    // DIVERGENCE: DOS lists the files in directory order (R037+0x4562 appends find-first / find-next results unsorted), which is the order the
    //   install wrote them on that disk, a fact of the host's file system and not of the EXE; here each group is in name order (F01, F60,
    //   then A00, then C00), the order of a folder the files were copied into by name.
    internal static IReadOnlyList<string> FigureImages(GameFiles files)
    {
        var codes = new List<string>();
        foreach (var group in new[] { "F??STAT.PIC", "A??STAT.PIC", "C??STAT.PIC" })
        {
            codes.AddRange(files.List("PICS", group).Select(n => n[..3].ToUpperInvariant()));
        }
        return codes.Count > 0 ? codes : ["F01"];
    }

    /// <summary>The four ready-made characters: the records of SAVES\DEFAULT as the menu's load reads them.</summary>
    // orig: R037+0x0000: saved-game-read("default") (R037+0x4184) fills charactr.tmp with the file's four records, byte for byte
    // DIVERGENCE: without the file the Quickstart party stands in (its item types differ from the file's in a few bytes); the figure
    // images and colours come from it either way (the same as the file's, see DefaultParty).
    private (List<Character> Characters, string[] SlotImages) ReadyMade(int partySeed)
    {
        var stand = DefaultParty.Create(partySeed).Members;
        if (!_ui.Assets.Files.Exists("SAVES/DEFAULT"))
        {
            return ([.. stand], [.. stand.Select(m => m.Image), ""]);
        }
        var file = SaveFile.Parse(_ui.Assets.Files.Read("SAVES/DEFAULT"));
        // orig: the load also fills member-shield-picture (DS:A668, 4 bytes a slot, all five) from the file; member-colours it does not touch
        var images = Enumerable.Range(0, SaveFile.PartySlots)
            .Select(k => new string([.. file.Images.AsSpan(k * SaveFile.ImageSize, SaveFile.ImageSize).ToArray().Select(b => (char)b)])).ToArray();
        var made = new List<Character>();
        foreach (var record in file.Characters.Take(stand.Count))
        {
            var character = CharacterRecords.Read(record);
            character.Image = stand[made.Count].Image;
            character.ImagePalette = stand[made.Count].ImagePalette;
            made.Add(character);
        }
        return (made, images);
    }

    /// <summary>The exit R037+0x0348 (both ways out): the button waited out, the mouse driver's left-release count cleared.</summary>
    // C: the end of R037+0x0348: R037+0x0377 `while (mouse-button-state & 1);`, then INT 33h function 6 through the root's mouse call (the left release count read and cleared)
    private T? Leave<T>(T? result) where T : class
    {
        ScreenDrawing.WaitForRelease(_ui);
        _ui.TakeLeftReleases();
        return result;
    }

    public GameState? Run()
    {
        // orig: R037+0x9DB2 case 'C': party-random-seed = BIOS ticks; the city was drawn at start-up (root 03A1) from the same clock
        // orig: party-random-seed (DS:9C4A) is the BIOS tick word at the click (R037+0x9FD9), not time(0)'s: names are drawn from it (made-up-name) and the saved game holds it
        var seed = _seed();
        var partySeed = _partyTicks?.Invoke() ?? NewGame.PartySeed(seed);
        var random = new MscRandom(seed);
        var startUpCity = random.Below(NewGame.StartingCities);
        // orig: R037+0x0000: thunk 09C0:18F1(0x14) (music), R037+0x4562, saved-game-read("default"), party[k][0] = 0
        // DIVERGENCE (host): the original reads SAVES\DEFAULT here too (R037+0x4184, the open not checked; see D-66e575 for
        // Quickstart); without the file it reads through an unset handle, and the 483.07 notes say characters can then not be
        // created. Here the ready-made characters come with the game (DefaultParty), so the file is not needed.
        _ui.PlayMusic(0x14);
        if (Shared.Image >= _images.Count)
        {
            Shared.Image = 0;
        }
        var (readyMade, slotImages) = ReadyMade(partySeed);
        _sheetState = SheetState(random, startUpCity);
        _roster = new CreationRoster(readyMade, _data.SlotPalettes, slotImages, _sheetState.Item);
        var creation = new CharacterCreation(_data.Tables, _world.ItemDefinitions, random, partySeed, _seed);
        DrawAll();
        while (true)
        {
            var action = _ui.WaitForAction();
            var key = action.IsLeftClick ? Click(action.X, action.Y) : action.Key;
            if (key == Keys.None)
            {
                continue;
            }
            // orig: 0x011B and R/r leave before the handler
            if (key == Keys.Escape || Keys.IsLetter(key, 'r'))
            {
                return Leave<GameState>(null);
            }
            if (key == Keys.AltQ)
            {
                throw new QuitGameException();
            }
            var c = char.ToLowerInvariant(Keys.CharacterOf(key));
            if (key is Keys.Up or Keys.Left or Keys.Right or Keys.Down)
            {
                Select(key == Keys.Down ? Shared.Selected + 1 : Shared.Selected - 1, wrap: true);
                continue;
            }
            switch (c)
            {
                case 'c':
                    Press('c', () =>
                    {
                        creation.RecordBufferWord = s_recordBuffer;
                        if (new CharacterGeneratorScreen(_ui, _data, _world, creation).Run() is { } made)
                        {
                            // orig: R037+0x38AA stores DS:907E (location-now, the start-up city here: the load of "default" leaves it) in the record's first word before writing it
                            made.RosterLocation = startUpCity;
                            _roster.Add(made);
                        }
                    });
                    DrawAll();
                    break;
                case 'a' when _roster.Characters.Count > 0:
                {
                    var slot = -1;
                    Press('a', () =>
                    {
                        // orig: R037+0x0B06: the new slot's shield picture DS:A2D7[slot] is PICS\SHIELDA.PIC, whatever the record's letter, until H
                        slot = _roster.AddToParty(Shared.Selected, _images[0]);
                        if (slot >= 0 && _roster.Member(slot) is { } joined)
                        {
                            _panel.SetShieldPicture(slot, joined, 'A');
                        }
                    });
                    DrawFigure();
                    DrawPartyFrame(slot);
                    DrawPartyRow(slot);
                    DrawList();
                    break;
                }
                case 'd' when _roster.Characters.Count > 0:
                {
                    var slot = _roster.SlotOf(Shared.Selected, CreationRoster.MenuSlots);
                    Press('d', () => _roster.DropFromParty(Shared.Selected));
                    DrawPartyRow(slot);
                    DrawList();
                    break;
                }
                case 'h' when _roster.Characters.Count > 0:
                {
                    var slot = _roster.SlotOf(Shared.Selected);
                    Press('h', () =>
                    {
                        // orig: R037+0x1046: the next letter, then DS:A2D7[slot] freed and loaded again from PICS\SHIELD<letter>.PIC
                        if (_roster.NextShield(Shared.Selected) && slot >= 0 && _roster.Member(slot) is { } member)
                        {
                            _panel.SetShieldPicture(slot, member, member.ShieldLetter);
                        }
                    });
                    DrawPartyRow(slot);
                    break;
                }
                case 's' when _roster.Characters.Count > 0:
                    Press('s', NextImage);
                    break;
                case '1' or '2' or '3' when _roster.Characters.Count > 0:
                    Colour(c - '0');
                    break;
                case 'k' when _roster.Characters.Count > 0:
                {
                    var slot = _roster.SlotOf(Shared.Selected);
                    Press('k', () => _roster.Kill(Shared.Selected));
                    DrawPartyRow(slot);
                    DrawList();
                    break;
                }
                case 'b' when _roster.Characters.Count > 0:
                    if (_roster.ToParty(partySeed, _leader) is { } party)
                    {
                        PressDown('b');
                        var game = NewParty.Create(_world, seed, party, random, _seed);
                        _roster.CopyTo(game.Roster, game.Party);
                        return Leave(game);
                    }
                    Press('b', () => { });
                    break;
            }
        }
    }

    // ----- input -----

    // orig: R037+0x0000, the click areas: menu rows of 15 pixels from y 0x19, the list rows of 8 from y 0x14
    private int Click(int x, int y)
    {
        if (x > 0x46 && x < 0xC8 && y > 0x19 && y < 0xCD)
        {
            var row = (y - 0x19) / 15;
            return row < Commands.Length ? (row == Commands.Length - 1 ? Keys.Escape : Keys.ForCharacter(Commands[row].Key)) : Keys.None;
        }
        if (x > 0xC5 && x < 0x138 && y > 0x14 && y < 0xB4)
        {
            var row = (y - 0x14) / 8;
            if (_roster.Characters.Count > row)
            {
                Select(row, wrap: false);
            }
            return Keys.None;
        }
        if (x < 0x3C)
        {
            MemberSheet(y / 0x28);
        }
        return Keys.None;
    }

    /// <summary>A click on a party member's figure in the party column: that member's character sheet, then the menu again.</summary>
    // orig: R037+0x01C0: with the button down, `pointer-column < 0x3C` and the slot's party flag (DS:9C69 + 0x80 x slot, set by A, cleared by D and K) set:
    // orig: member-sheet-screen (thunk 09C0:1FDB = R031+0x0124) for slot y / 0x28; then root 047C:0568, PICS\CRETSCRN.PIC with its palette, DS:E753..DS:E76A
    // orig: = the slot's member-colours (24 bytes), palette-load, R037+0x03D4, root 047C:056F and the button waited out (R037+0x02F0)
    // C: the figure (PICS\XXXSMALL.PIC at 0x0C, 0xA4) is not drawn again: R037+0x03D4 copies the back window over it.
    // C: the sheet's Ctrl-F1..F5 case tests `slot < party-headcount` (DS:A67E), and party-headcount is not written before Begin: 0 after start-up,
    // so those leader keys do nothing here (CharacterSheetScreen.PartySize)
    // C: the sheet opens on the menu's slots as the party (the panel and the sheet read the slots, not party-headcount).
    // DIVERGENCE: the sheet's menu bar and hot keys (root 0000:0000) are not passed.
    // C: a prayer from the sheet's Saints list runs on the program's own state (SheetState), not on SAVES\DEFAULT's header.
    private void MemberSheet(int slot)
    {
        if (slot >= Party.MaxMembers || _roster.Member(slot) is null)
        {
            return;
        }
        if (_sheet is null)
        {
            var formulas = FormulaBook.Load(_ui.Assets.Files, _world);
            _sheet = new CharacterSheetScreen(_ui, _panel, formulas, _world.Items.Saints.Select(s => s.FullName).ToList(), new AlchemyScreen(_ui, _panel, formulas));
            _sheet.PartySize = () => 0;
            _sheet.ShowSaint = (member, saint) => new SaintPrayerScreen(_ui, _panel) { InBattle = () => false }
                .Pray(_sheetState!, member, saint, Core.Rules.PrayerMode.ForMember, Core.Rules.SaintEffects.Instance);
        }
        var party = PanelParty();
        party.LeaderIndex = _leader;
        _sheetState!.Party = party;
        _sheet.Show(_sheetState, slot);
        _leader = party.LeaderIndex;
        DrawAll();
        Shared.Colours.Load(_roster.SlotPalettes[slot]);
        ApplyFigureColours();
        ScreenDrawing.WaitForRelease(_ui);
    }

    /// <summary>
    /// The game the member sheet (and a prayer from it) works on: the program as it started, the start-up city as the current place
    /// and the date the data segment starts with; the menu's load of SAVES\DEFAULT changes neither.
    /// </summary>
    // orig: saved-game-read R037+0x4184 reads DEFAULT's 0xEF header bytes and each 0x22A record into text-bank-buffer DS:9085 (only to copy the records
    //   into charactr.tmp), party-headcount and member-shield-picture; the date words DS:00E0..00E7 and location-now DS:907E are not written. Captured in the
    //   original at the menu (creation-menu.steps, local/batch/evid1o): DS:00E0 = 9, 26, 8, 1400; DS:907E = the start-up city (root 03A1)
    private GameState SheetState(IRandomSource random, int startUpCity)
    {
        var state = new GameState(random) { Data = _world, CurrentLocation = startUpCity, Date = NewGame.StartUpDate };
        state.City = _world.LoadCity(startUpCity);
        return state;
    }

    /// <summary>The selection moves; the figure of the selected member (if in the first four slots) shows with its colours.</summary>
    // orig: R037+0x0444 cases 0x4800/0x4B00/0x4D00 (previous) and 0x5000 (next); a click on the list only moves the highlight
    private void Select(int row, bool wrap)
    {
        var count = _roster.Characters.Count;
        Screen.Recolor(0xC6, Shared.Selected * 8 + 0x16, 0x71, 8, SelectedName, NameColor);
        Shared.Selected = !wrap ? row : row < 0 ? count - 1 : row >= count ? 0 : row;
        Screen.Recolor(0xC6, Shared.Selected * 8 + 0x16, 0x71, 8, NameColor, SelectedName);
        if (wrap)
        {
            DrawFigure();
        }
    }

    /// <summary>The command's button shows pressed while it runs, then the menu comes back.</summary>
    // orig: gfx-picture-draw(buttnCR2 at 0x43, y - 4) + the label at 0x4A, then gfx-rect-copy(back, 0x43, y - 4, 0x82, 0x0E)
    private void Press(char key, Action action)
    {
        var y = PressDown(key);
        action();
        CopyBack(0x43, y, 0x82, 0x0E);
    }

    private int PressDown(char key)
    {
        var command = Array.Find(Commands, c => c.Key == key);
        var y = command.Y - 4;
        Screen.Draw(_ui.Assets.Picture("buttnCR2").Image, 0x43, y);
        _ui.Text.DrawCoded(Screen, 0x4A, command.Y, _data.Texts[command.Text], LabelColor);
        _ui.Present();
        return y;
    }

    /// <summary>S: the next figure image for the selected member.</summary>
    // orig: R037+0x0444 case 0x1F53/0x1F73: if (++DS:E7E0 >= DS:E7DE) DS:E7E0 = 0 (only when the character is in a slot)
    private void NextImage()
    {
        if (_roster.SlotOf(Shared.Selected) < 0)
        {
            return;
        }
        if (++Shared.Image >= _images.Count)
        {
            Shared.Image = 0;
        }
        _roster.SetImage(Shared.Selected, _images[Shared.Image]);
        DrawFigure();
    }

    /// <summary>1, 2, 3: the next colours of one part of the selected member's figure.</summary>
    // orig: R037+0x0444 cases 0x231/0x4F31, 0x332/0x5032, 0x433/0x5133 (PortraitColours), then palette-load
    private void Colour(int key)
    {
        var slot = _roster.SlotOf(Shared.Selected);
        if (slot < 0 || _roster.Member(slot) is not { } member)
        {
            return;
        }
        Shared.Colours.Cycle(key, member.Image, member.ImagePalette);
        ApplyFigureColours();
    }

    // ----- drawing -----

    private void CopyBack(int x, int y, int width, int height) => Screen.Restore(_back.Save(x, y, width, height));

    /// <summary>The whole menu (<c>R037+0x0390</c> + <c>R037+0x03D4</c>).</summary>
    private void DrawAll()
    {
        var picture = _ui.Assets.Picture("cretscrn");
        _back.DrawPicture(picture);
        Screen.UsePalette(picture);
        foreach (var (text, _, y) in Commands)
        {
            _ui.Text.DrawCoded(_back, 0x48, y, _data.Texts[text], LabelColor);
        }
        Screen.Draw(_back.Screen.Pixels, Canvas.Width, Canvas.Height, 0, 0);
        DrawList();
        for (var i = 0; i < Party.MaxMembers; i++)
        {
            if (_roster.Member(i) is not null)
            {
                DrawPartyFrame(i);
                _panel.DrawRow(Screen, PanelParty(), i);
            }
        }
    }

    /// <summary>The characters (<c>R037+0x2760</c>): name with a shadow, '+' for party members, the selection highlighted.</summary>
    private void DrawList()
    {
        var count = _roster.Characters.Count;
        // C: each record is read into DS:8B52 (0x80 bytes) to draw its name: the buffer ends with the last record's +0x7E/+0x7F.
        if (count > 0)
        {
            var last = _roster.Characters[count - 1];
            var high = last.OriginalRecord is { Length: > 0x7F } r ? r[0x7F] : 0;
            s_recordBuffer = (short)((byte)last.Pack.Stacks | high << 8);
        }
        CopyBack(0xC5, 0x14, 0x73, 0x14);
        for (var i = 0; i < count; i++)
        {
            CopyBack(0xC5, i * 8 + 0x18, 0x73, 0x10);
            if (_roster.Slots.Contains(i))
            {
                _ui.Text.Draw(Screen, 0xC7, i * 8 + 0x18, "+", NameShadow);
                _ui.Text.Draw(Screen, 0xC6, i * 8 + 0x17, "+", NameColor);
            }
            _ui.Text.Draw(Screen, 0xCD, i * 8 + 0x17, _roster.Characters[i].Name, NameShadow);
            _ui.Text.Draw(Screen, 0xCC, i * 8 + 0x16, _roster.Characters[i].Name, NameColor);
        }
        if (count <= Shared.Selected)
        {
            Shared.Selected = count - 1;
        }
        if (count == 0)
        {
            Shared.Selected = -1;
        }
        else if (Shared.Selected == -1)
        {
            Shared.Selected = 0;
        }
        Screen.Recolor(0xC6, Shared.Selected * 8 + 0x16, 0x71, 8, NameColor, SelectedName);
    }

    /// <summary>The party column row of the one slot a command changed (none when <paramref name="slot"/> is -1).</summary>
    // orig: only the slot found is drawn: A (R037+0x0D6F / 0x0D7A) root 0000:1588 + root 0000:1716 for the slot filled; D (R037+0x0EF1, the
    // first four slots), H (R037+0x1102) and K (R037+0x126B, the five slots) root 0000:1716 for the character's slot, which draws the row's
    // background there even when the slot was just emptied; the other rows keep what is on the screen, so an empty slot no command touched
    // shows the menu's picture (CRETSCRN.PIC), as after R037+0x03D4 (rows only for slots whose status DS:9C69 + 0x80 x slot is not 0).
    // C: a new member's row shows PICS\SHIELDA.PIC until H is pressed (the slot's shield picture, PartyPanel.SetShieldPicture), whatever the
    //   letter of its record; checked against the original's screen (vis10 session D-b0c9a6 with a member whose letter is not A, local/batch/c5uncertain)
    private void DrawPartyRow(int slot)
    {
        if (slot >= 0)
        {
            _panel.DrawRow(Screen, PanelParty(), slot);
        }
    }

    /// <summary>The frame around the party column row of a filled slot (A, and the whole menu's drawing).</summary>
    // orig: root 0000:1588 with the slot (its bit of the table DS:01E4) from A (R037+0x0D6F) and R037+0x0420; DS:9064 + slot is 0 in this menu
    // (the unlit pieces): the frame shows in the original's picture after A (vis10 session D-b0c9a6), not on the menu's picture
    private void DrawPartyFrame(int slot)
    {
        if (slot >= 0)
        {
            PartyPanel.DrawFrame(Screen, _ui.Assets.CommonSprites, slot, lit: false);
        }
    }

    // orig: root 0000:1716 writes the name and values of the slot equal to the leader byte DS:907B in colour 0x0E; the menu starts with
    // DS:907B = 0 (the original's picture after A shows the first slot's name and values in 0x0E, vis10 session D-b0c9a6), and the
    // member sheet's leader change (MemberSheet) keeps it.
    private Party PanelParty()
    {
        var party = new Party { LeaderIndex = _leader };
        for (var i = 0; i < Party.MaxMembers; i++)
        {
            party.Members.Add(_roster.Member(i) ?? new Character { Status = CharacterStatus.Empty });
        }
        return party;
    }

    /// <summary>The selected member's figure (PICS\XXXSMALL.PIC at 12,164), when it is in one of the first four slots.</summary>
    // orig: R037+0x0444 arrows: copy back (0x0A, 0xA5, 0x23, 0x23); if the character is in slot il &lt; 4: DS:E753.. = member-colours[il], palette-load, draw
    private void DrawFigure()
    {
        CopyBack(0x0A, 0xA5, 0x24, 0x24);
        var slot = _roster.SlotOf(Shared.Selected, CreationRoster.MenuSlots);
        if (slot < 0 || _roster.Member(slot) is not { } member)
        {
            return;
        }
        Shared.Colours.Load(member.ImagePalette);
        ApplyFigureColours();
        var picture = member.Image + "small";
        if (_ui.Assets.PictureExists(picture))
        {
            Screen.Draw(_ui.Assets.Picture(picture).Image, 0x0C, 0xA4, transparent: true);
        }
    }

    /// <summary>The shared figure palette into colours 235..242 (6-bit VGA values).</summary>
    private void ApplyFigureColours()
    {
        for (var i = 0; i < 8; i++)
        {
            Screen.SetColor(FirstFigureColor + i, new Content.Formats.Graphics.Rgb24(
                Vga(Shared.Colours.Palette[3 * i]), Vga(Shared.Colours.Palette[3 * i + 1]), Vga(Shared.Colours.Palette[3 * i + 2])));
        }
    }

    private static byte Vga(byte v) => (byte)((v & 0x3F) << 2 | (v & 0x3F) >> 4);
}
