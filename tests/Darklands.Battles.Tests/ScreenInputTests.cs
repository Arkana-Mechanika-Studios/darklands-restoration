using Darklands.Content;
using Darklands.Core;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Simulation;

namespace Darklands.Battles.Tests;

/// <summary>
/// The screens read the left button as the original does: the party information closes on the button held and waits it
/// out; the camp acts on the driver's release count at the place of the release.
/// </summary>
public sealed class ScreenInputTests
{
    /// <summary>A host that plays one frame of input per Pump (a key, the mouse at a place with buttons held), counting the frames.</summary>
    private sealed class FrameHost(params (int Key, MouseState Mouse)[] frames) : IHost, IVideo, IInput, IClock
    {
        private readonly Queue<(int Key, MouseState Mouse)> _frames = new(frames);
        private int _key;
        private int _idle;
        private long _now;

        public IVideo Video => this;
        public IInput Input => this;
        public IAudio Audio { get; } = new SilentAudio();
        public IClock Clock => this;
        public bool QuitRequested { get; private set; }
        public MouseState Mouse { get; private set; } = new(5, 5, MouseButtons.None);
        public bool ShiftHeld => false;
        public long Milliseconds => _now;
        public int Pumped { get; private set; }
        public int Left => _frames.Count;

        public Action<Screen>? Presented { get; set; }

        public void Present(Screen screen) => Presented?.Invoke(screen);

        public void Sleep(int milliseconds) => _now += Math.Max(1, milliseconds);

        public bool TryReadKey(out int key)
        {
            key = _key;
            _key = 0;
            return key != 0;
        }

        public void Pump()
        {
            Pumped++;
            if (!_frames.TryDequeue(out var frame))
            {
                QuitRequested = ++_idle > 1000;
                (_key, Mouse) = (0, Mouse with { Buttons = MouseButtons.None });
                return;
            }
            (_key, Mouse) = frame;
        }
    }

    private static (int, MouseState) Held(int x, int y) => (0, new MouseState(x, y, MouseButtons.Left));

    private static (int, MouseState) Up(int x, int y) => (0, new MouseState(x, y, MouseButtons.None));

    private static (GameUi Ui, GameState State, PartyPanel Panel, WorldData World)? Start(FrameHost host)
    {
        if (GameFolder.Files is not { } files)
        {
            return null;
        }
        var world = WorldData.Load(files);
        var ui = new GameUi(host, new GameAssets(files));
        var state = NewGame.Quickstart(world, 1, () => 1);
        state.Data = world;
        return (ui, state, new PartyPanel(ui.Assets), world);
    }

    [Fact]
    public void ThePartyInformationClosesOnTheButtonHeldAndWaitsItOut()
    {
        (int, MouseState)[] frames = [Up(100, 100), Up(100, 100), Held(100, 100), Held(100, 100), Held(100, 100), Held(100, 100), Up(100, 100)];
        var host = new FrameHost(frames);
        if (Start(host) is not var (ui, state, panel, _))
        {
            return;
        }
        new PartyInfoScreen(ui, panel).Show(state);
        Assert.False(host.QuitRequested);
        Assert.Equal(0, host.Left);
        Assert.False(host.Mouse.Left);
        Assert.Equal(0, ui.TakeLeftReleases().Count);
    }

    [Fact]
    public void TabWithTheButtonHeldClosesThePartyInformation()
    {
        (int, MouseState)[] frames = [Up(100, 100), (Keys.Tab, new MouseState(100, 100, MouseButtons.Left)), Up(100, 100)];
        var host = new FrameHost(frames);
        if (Start(host) is not var (ui, state, panel, _))
        {
            return;
        }
        new PartyInfoScreen(ui, panel).Show(state);
        Assert.False(host.QuitRequested);
        Assert.Equal(0, host.Left);
    }

    // orig: R038+0x01FE: after the button is waited out (R038+0x0235) the mouse driver's release count is read and cleared (root 047C:1320 with 6, 0, 0, 0)
    [Fact]
    public void TheInnRosterLeavesNoReleaseCountBehind()
    {
        // a press and release on the list (a click the count keeps), then Esc
        (int, MouseState)[] frames = [Up(5, 5), Up(5, 5), Held(0x100, 0x20), Held(0x100, 0x20), Up(0x100, 0x20), Up(0x100, 0x20), Key(Keys.Escape), Up(5, 5), Up(5, 5)];
        var host = new FrameHost(frames);
        if (Start(host) is not var (ui, state, panel, world))
        {
            return;
        }
        new PartyRosterScreen(ui, panel, world).Run(state, 0);
        Assert.False(host.QuitRequested);
        Assert.Equal(0, ui.TakeLeftReleases().Count);
    }

    /// <summary>
    /// The inn's roster: Down onto row 4 (past the four figure pictures) with the party member of roster entry 4 there loads
    /// "pics\small.pic" (R038+0x045F..0x04CA) and the program ends (exit 99); fixed (DataSafe), the roster goes on.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnArrowOntoAPartyMemberPastTheFigureListEndsTheProgramUnlessFixed(bool fixedGame)
    {
        (int, MouseState)[] frames = [Up(5, 5), Up(5, 5), Key(Keys.Down), Up(5, 5), Key(Keys.Down), Up(5, 5), Key(Keys.Down), Up(5, 5),
            Key(Keys.Down), Up(5, 5), Key(Keys.Escape), Up(5, 5), Up(5, 5)];
        var host = new FrameHost(frames);
        if (Start(host) is not var (ui, state, panel, world))
        {
            return;
        }
        // DKSAVE0: six roster records, party slot 0 holds record 4, so row 4 (the fifth, past the four figure pictures) is a member
        var saves = DarklandsGame.DefaultSaveDirectory(ui.Assets.Files.Root);
        if (Directory.GetFiles(saves).FirstOrDefault(f => Path.GetFileName(f).Equals("DKSAVE0.SAV", StringComparison.OrdinalIgnoreCase)) is not { } sav)
        {
            return;
        }
        state = Darklands.Persistence.OriginalSaves.Read(File.ReadAllBytes(sav), [], "DKSAVE0.SAV", world).State;
        state.Data = world;
        Assert.Equal(4, state.Roster.PartySlots[0]);
        state.Behaviour = fixedGame ? OriginalBehaviour.DataSafe : OriginalBehaviour.Faithful;
        // the menus' shared row from the top (a new run of the program)
        CharacterCreationScreen.ProgramStart();
        var roster = new PartyRosterScreen(ui, panel, world);
        if (fixedGame)
        {
            roster.Run(state, 0);
            Assert.False(host.QuitRequested);
        }
        else
        {
            var exit = Assert.Throws<ProgramExitException>(() => roster.Run(state, 0));
            Assert.Equal("pics\\small.pic: No such file or directory\n", exit.Printed);
            Assert.Equal(99, exit.Code);
        }
    }

    /// <summary>The creation menu's exit R037+0x0348 clears the mouse driver's left-release count (root 047C:1320(6, 0, 0, 0)).</summary>
    [Fact]
    public void TheCreationMenuLeavesNoReleaseCountBehind()
    {
        // a press and release on the party column (a click the count keeps), then Esc
        (int, MouseState)[] frames = [Up(5, 5), Up(5, 5), Held(20, 50), Held(20, 50), Up(20, 50), Up(20, 50), Key(Keys.Escape), Up(5, 5), Up(5, 5)];
        var host = new FrameHost(frames);
        if (Start(host) is not var (ui, _, _, world))
        {
            return;
        }
        Assert.Null(new CharacterCreationScreen(ui, world, () => 1).Run());
        Assert.False(host.QuitRequested);
        Assert.Equal(0, ui.TakeLeftReleases().Count);
    }

    /// <summary>
    /// A in the creation menu: the new member's figure colours are colours 235..242 (DS:E753, set by R037+0x0B9C..0x0C88, as the original's
    /// DAC shows them), and only the filled slot's row is drawn: the three empty rows keep the menu's picture (R037+0x0D6F / 0x0D7A).
    /// </summary>
    [Fact]
    public void AddingAMemberColoursItsFigureAndDrawsOnlyItsRow()
    {
        (int, MouseState)[] frames = [Up(5, 5), Up(5, 5), Key(Keys.ForCharacter('a')), Up(5, 5), Up(5, 5), Up(5, 5), Key(Keys.Escape), Up(5, 5)];
        var host = new FrameHost(frames);
        if (Start(host) is not var (ui, _, _, world))
        {
            return;
        }
        (byte[] Pixels, uint[] Palette)? first = null, added = null;
        host.Presented = s =>
        {
            if (host.Left < 2)
            {
                return;
            }
            var shot = ((byte[])s.Pixels.Clone(), (uint[])s.Palette.Clone());
            first ??= shot;
            added = shot;
        };
        Assert.Null(new CharacterCreationScreen(ui, world, () => 1).Run());
        Assert.NotNull(first);
        Assert.NotNull(added);
        var start = Darklands.Core.Characters.Creation.PortraitColours.InnStartPalette;
        static uint Vga(byte r, byte g, byte b) => (uint)(Widen(r) << 16 | Widen(g) << 8 | Widen(b));
        static int Widen(byte v) => (v & 0x3F) << 2 | (v & 0x3F) >> 4;
        for (var i = 0; i < 8; i++)
        {
            Assert.Equal(Vga(start[3 * i], start[3 * i + 1], start[3 * i + 2]), added.Value.Palette[235 + i] & 0xFFFFFF);
        }
        // colours 243 and 244 keep the menu picture's own colours
        Assert.Equal(first.Value.Palette[243] & 0xFFFFFF, added.Value.Palette[243] & 0xFFFFFF);
        Assert.Equal(first.Value.Palette[244] & 0xFFFFFF, added.Value.Palette[244] & 0xFFFFFF);
        // the first slot's row is drawn; slots 1..3 (y 0x28 .. 0x9F, x 0 .. 0x3B) are the menu's picture as before
        Assert.Contains(Enumerable.Range(1, 0x26).SelectMany(y => Enumerable.Range(3, 0x36).Select(x => y * 320 + x)),
            k => added.Value.Pixels[k] != first.Value.Pixels[k]);
        for (var y = 0x28; y < 0xA0; y++)
        {
            for (var x = 0; x < 0x3C; x++)
            {
                Assert.Equal(first.Value.Pixels[y * 320 + x], added.Value.Pixels[y * 320 + x]);
            }
        }
    }

    /// <summary>The generator's exit R037+0x4ADC clears the mouse driver's left-release count too.</summary>
    [Fact]
    public void TheGeneratorLeavesNoReleaseCountBehind()
    {
        // a press and release in the empty area at the bottom right, then Esc (the first step: Esc leaves)
        (int, MouseState)[] frames = [Up(5, 5), Up(5, 5), Held(300, 195), Held(300, 195), Up(300, 195), Up(300, 195), Key(Keys.Escape), Up(5, 5), Up(5, 5)];
        var host = new FrameHost(frames);
        if (Start(host) is not var (ui, _, _, world))
        {
            return;
        }
        var data = Darklands.Content.Formats.Characters.CreationDataDecoder.Decode(ui.Assets.Files.Read("DARKLAND.EXE"));
        var creation = new Darklands.Core.Characters.Creation.CharacterCreation(data.Tables, world.ItemDefinitions,
            new Darklands.Core.Randomness.MscRandom(1), 1, () => 1);
        Assert.Null(new CharacterGeneratorScreen(ui, data, world, creation).Run());
        Assert.False(host.QuitRequested);
        Assert.Equal(0, ui.TakeLeftReleases().Count);
    }

    [Fact]
    public void TheCampActsWhereTheButtonIsLetGo()
    {
        // the bottom menu's second line (Leave) is at x 0x47..0x133, y 0xB3..0xBB
        const int leaveX = 0x80, leaveY = 0xB6;
        (int, MouseState)[] frames =
        [
            Up(5, 5), Up(5, 5),
            // pressed on Leave, let go far away: nothing happens
            Held(leaveX, leaveY), Held(leaveX, leaveY), Up(0x100, 5), Up(0x100, 5), Up(0x100, 5),
            // pressed elsewhere, let go on Leave: the camp ends
            Held(0x100, 5), Held(0x100, 5), Up(leaveX, leaveY), Up(leaveX, leaveY),
        ];
        var host = new FrameHost(frames);
        if (Start(host) is not var (ui, state, panel, world))
        {
            return;
        }
        var formulas = FormulaBook.Load(ui.Assets.Files, world);
        var alchemy = new AlchemyScreen(ui, panel, formulas);
        var sheet = new CharacterSheetScreen(ui, panel, formulas, world.Items.Saints.Select(s => s.FullName).ToList(), alchemy);
        new CampScreen(ui, panel, formulas, alchemy, sheet).CampInTheWild(state, 0);
        Assert.False(host.QuitRequested);
        Assert.InRange(host.Left, 0, 1);
    }

    private static (int, MouseState) Key(int key) => (key, new MouseState(5, 5, MouseButtons.None));

    /// <summary>The sheet played with keys (an idle frame after each) up to Esc: the box the cursor ended on and whether a box was open.</summary>
    private static (int Cursor, bool Open)? Sheet(params int[] keys)
    {
        var frames = new List<(int, MouseState)> { Up(5, 5), Up(5, 5) };
        foreach (var k in keys.Append(Keys.Escape))
        {
            frames.Add(Key(k));
            // the scroll's unrolling (five waits of 20 ms) takes frames: idle ones after Enter
            frames.AddRange(Enumerable.Repeat(Up(5, 5), k == Keys.Enter ? 60 : 1));
        }
        var host = new FrameHost([.. frames]);
        if (Start(host) is not var (ui, state, panel, world))
        {
            return null;
        }
        var formulas = FormulaBook.Load(ui.Assets.Files, world);
        var alchemy = new AlchemyScreen(ui, panel, formulas);
        var sheet = new CharacterSheetScreen(ui, panel, formulas, world.Items.Saints.Select(s => s.FullName).ToList(), alchemy);
        sheet.Show(state, 0);
        Assert.False(host.QuitRequested);
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        return ((int)typeof(CharacterSheetScreen).GetField("_cursor", flags)!.GetValue(sheet)!,
            (bool)typeof(CharacterSheetScreen).GetField("_open", flags)!.GetValue(sheet)!);
    }

    // orig: R031+0x161C: the Ctrl-F1..F5 case tests `slot < party-headcount` (DS:A67E); the character menu's sheet has party-headcount 0
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public void TheSheetsCtrlFKeysTestThePartySize(bool menuSheet, int leader)
    {
        (int, MouseState)[] frames = [Up(5, 5), Up(5, 5), Key(0x5F00), Up(5, 5), Up(5, 5), Key(Keys.Escape), Up(5, 5), Up(5, 5)];
        var host = new FrameHost(frames);
        if (Start(host) is not var (ui, state, panel, world))
        {
            return;
        }
        var formulas = FormulaBook.Load(ui.Assets.Files, world);
        var alchemy = new AlchemyScreen(ui, panel, formulas);
        var sheet = new CharacterSheetScreen(ui, panel, formulas, world.Items.Saints.Select(s => s.FullName).ToList(), alchemy);
        if (menuSheet)
        {
            sheet.PartySize = () => 0;
        }
        state.Party.LeaderIndex = 0;
        sheet.Show(state, 0);
        Assert.False(host.QuitRequested);
        // Ctrl-F2 (0x5F00) makes the second member the leader, unless party-headcount is 0
        Assert.Equal(leader, state.Party.LeaderIndex);
    }

    [Fact]
    public void RightFromTheSecondMemberBoxGoesToTheEmptyTableEntry()
    {
        // R136+0xBBE7: box 13's Right neighbour is entry 17, all zero (every neighbour box 0); the cursor stands on it
        // name box -Left-> 12 -Down-> 13 -Right-> 17
        if (Sheet(Keys.Left, Keys.Down, Keys.Right) is not var (cursor, _))
        {
            return;
        }
        Assert.Equal(17, cursor);
        // and from 17 every arrow goes back to box 0
        Assert.Equal(0, Sheet(Keys.Left, Keys.Down, Keys.Right, Keys.Right)!.Value.Cursor);
        Assert.Equal(0, Sheet(Keys.Left, Keys.Down, Keys.Right, Keys.Up)!.Value.Cursor);
    }

    [Fact]
    public void PackKeysLeaveAnotherOpenBoxOpen()
    {
        // R031+0x1B2C (P) and R031+0x185E / 0x192E / 0x19E2 (D, A, U) return at R031+0x1D40 without the pack open: the saints list
        // (box 11, five Downs from the name box) stays open and Up then scrolls it (at its top: nothing) instead of moving the cursor
        foreach (var key in new[] { 0x1970, 0x1950, 0x2064, 0x1E61, 0x1675 })
        {
            if (Sheet(Keys.Down, Keys.Down, Keys.Down, Keys.Down, Keys.Down, Keys.Enter, key, Keys.Up) is not var (cursor, open))
            {
                return;
            }
            Assert.Equal((11, true), (cursor, open));
        }
        // any other key closes it (the default case R031+0x1C66)
        Assert.Equal((10, false), Sheet(Keys.Down, Keys.Down, Keys.Down, Keys.Down, Keys.Down, Keys.Enter, 0x326D, Keys.Up)!.Value);
    }

    /// <summary>The sheet with member 0's pack (box 9, three Downs from the name box) opened, then <paramref name="keys"/>; the state after.</summary>
    private static Darklands.Core.GameState? PackSheet(Action<Darklands.Core.GameState> setUp, params int[] keys)
    {
        var frames = new List<(int, MouseState)> { Up(5, 5), Up(5, 5) };
        foreach (var k in new[] { Keys.Down, Keys.Down, Keys.Down, Keys.Enter }.Concat(keys).Append(Keys.Escape).Append(Keys.Escape))
        {
            frames.Add(Key(k));
            frames.AddRange(Enumerable.Repeat(Up(5, 5), k == Keys.Enter ? 60 : 1));
        }
        var host = new FrameHost([.. frames]);
        if (Start(host) is not var (ui, state, panel, world))
        {
            return null;
        }
        setUp(state);
        var formulas = FormulaBook.Load(ui.Assets.Files, world);
        var sheet = new CharacterSheetScreen(ui, panel, formulas, world.Items.Saints.Select(s => s.FullName).ToList(), new AlchemyScreen(ui, panel, formulas));
        sheet.Show(state, 0);
        Assert.False(host.QuitRequested);
        return state;
    }

    /// <summary>
    /// A (R031+0x192E) tests only that the pack is open, not the line against the count: on an empty pack it readies entry 0 as it was
    /// left (here a hand weapon left past the end, as a dropped last stack leaves its successor entry), R031+0x5A02.
    /// </summary>
    [Fact]
    public void AReadiesAnEntryPastThePacksEnd()
    {
        var weapon = -1;
        var state = PackSheet(s =>
        {
            weapon = Enumerable.Range(1, 0x40).First(c => (s.Item(c).Flags & Darklands.Core.Items.ItemFlags.HandWeapon) != 0);
            var m = s.Party.Slot(0);
            m.Weapon = Darklands.Core.Characters.EquippedItem.None;
            m.Pack.Stacks = 0;
            m.Pack[0] = new Darklands.Core.Characters.ItemStack(weapon, s.Item(weapon).Type, 30, 1, s.Item(weapon).Weight);
        }, 0x1E61);
        if (state is null)
        {
            return;
        }
        Assert.Equal(state.Item(weapon).Type, state.Party.Slot(0).Weapon.Type);
        Assert.Equal(30, state.Party.Slot(0).Weapon.Quality);
    }

    /// <summary>
    /// P (R031+0x1B2C) has no count test either: a potion entry left past the end is drunk and taken (R031+0x4CC4: its count byte 1 -> 0,
    /// item-erase), which lowers the pack's signed count to -1.
    /// </summary>
    [Fact]
    public void PDrinksAnEntryPastThePacksEnd()
    {
        var state = PackSheet(s =>
        {
            var potion = Enumerable.Range(1, s.Data.ItemDefinitions.Count - 1).First(c => Darklands.Core.Rules.PotionEffects.CanDrink(s, c));
            var m = s.Party.Slot(0);
            m.Pack.Stacks = 0;
            m.Pack[0] = new Darklands.Core.Characters.ItemStack(potion, s.Item(potion).Type, 30, 1, s.Item(potion).Weight);
        }, 0x1970);
        if (state is null)
        {
            return;
        }
        Assert.Equal(-1, state.Party.Slot(0).Pack.Stacks);
    }

    // orig: R037+0x01C0 (a press on the party column, x < 0x3C, opens member-sheet-screen for slot y / 0x28 when the slot is filled)
    [Fact]
    public void TheCreationMenuOpensTheSheetOfAMembersFigure()
    {
        (int, MouseState)[] frames =
        [
            Up(5, 5), Key(Keys.ForCharacter('a')), Up(5, 5), Key(Keys.Down), Up(5, 5), Key(Keys.ForCharacter('a')), Up(5, 5),
            // the second member's figure (slot 1: y 0x28..0x4F), then the sheet's name box: that member becomes the leader
            Held(20, 50), Held(20, 50), Up(20, 50), Up(20, 50), Held(160, 6), Held(160, 6), Up(160, 6), Up(160, 6),
            Key(Keys.Escape), Up(5, 5), Up(5, 5), Key(Keys.ForCharacter('b')), Up(5, 5),
        ];
        var host = new FrameHost(frames);
        if (Start(host) is not var (ui, _, _, world))
        {
            return;
        }
        var game = new CharacterCreationScreen(ui, world, () => 1).Run();
        Assert.NotNull(game);
        Assert.Equal(1, game.Party.LeaderIndex);
    }

    // orig: R037+0x9DB2 case C: party-random-seed (DS:9C4A) = the BIOS tick word at 0040:006C, not time(0); the party "Begin the Adventure" starts holds it
    // (lockstep chargen-created: the original's saved game +0x64 = 0x0B5A where the clock's seed was 7622)
    [Fact]
    public void TheCreatedPartysSeedIsTheBiosTickWordNotTheClocksSeed()
    {
        (int, MouseState)[] frames = [Up(5, 5), Key(Keys.ForCharacter('a')), Up(5, 5), Key(Keys.ForCharacter('b')), Up(5, 5)];
        var host = new FrameHost(frames);
        if (Start(host) is not var (ui, _, _, world))
        {
            return;
        }
        var game = new CharacterCreationScreen(ui, world, () => 7622, () => 0x0B5A).Run();
        Assert.NotNull(game);
        Assert.Equal(0x0B5A, game.Party.Seed);
    }

    // orig: R037+0x7F06 (the formulae box) waits for a key or the button; a press closes it and stays down, so R037+0x4664's loop takes it
    // as a click: here on the list's first row ("Begin childhood"), which leaves the first step
    [Fact]
    public void AClickThatClosesTheGeneratorsListBoxActsWhereItIs()
    {
        (int, MouseState)[] frames = [Up(5, 5), Up(5, 5), Key(Keys.ForCharacter('2')), Up(5, 5), Held(100, 0x30), Held(100, 0x30), Up(100, 0x30), Up(100, 0x30)];
        var host = new FrameHost(frames);
        if (Start(host) is not var (ui, _, _, world))
        {
            return;
        }
        var data = Darklands.Content.Formats.Characters.CreationDataDecoder.Decode(ui.Assets.Files.Read("DARKLAND.EXE"));
        var creation = new Darklands.Core.Characters.Creation.CharacterCreation(data.Tables, world.ItemDefinitions,
            new Darklands.Core.Randomness.MscRandom(1), 1, () => 1);
        Assert.Throws<QuitGameException>(() => new CharacterGeneratorScreen(ui, data, world, creation).Run());
        Assert.Equal(Darklands.Core.Characters.Creation.CreationStage.FamilyBackground, creation.Stage);
    }

    /// <summary>A host that plays scripted steps once each is ready (a step returning null waits a frame), then idles.</summary>
    private sealed class StepHost(params Func<(int Key, MouseState Mouse)?>[] steps) : IHost, IVideo, IInput, IClock
    {
        private readonly Queue<Func<(int Key, MouseState Mouse)?>> _steps = new(steps);
        private int _key;
        private int _idle;
        private long _now;

        public IVideo Video => this;
        public IInput Input => this;
        public IAudio Audio { get; } = new SilentAudio();
        public IClock Clock => this;
        public bool QuitRequested { get; private set; }
        public MouseState Mouse { get; private set; } = new(5, 5, MouseButtons.None);
        public bool ShiftHeld => false;
        public long Milliseconds => _now;
        public int Left => _steps.Count;

        public void Present(Screen screen) { }

        public void Sleep(int milliseconds) => _now += Math.Max(1, milliseconds);

        public bool TryReadKey(out int key)
        {
            key = _key;
            _key = 0;
            return key != 0;
        }

        public void Pump()
        {
            _key = 0;
            if (_steps.TryPeek(out var next) && next() is { } frame)
            {
                _steps.Dequeue();
                (_key, Mouse) = frame;
                return;
            }
            QuitRequested = ++_idle > 5000;
        }
    }

    /// <summary>
    /// The card menu takes Up / Down only with the pointer over one of its lines and never a digit, as the original's card-choice-menu;
    /// GameUi.KeyboardCardMenus (off by default) adds both.
    /// </summary>
    // orig: R043+0x1BD2: Up 0x4800 (R043+0x1CE5) and Down 0x5000 (R043+0x1D46) need DS:ED6C == 1 (card-mouse-handler R043+0x21D4: the pointer inside
    // a line, R043+0x2232), Enter / Space (R043+0x1C77) a highlighted line (DS:E96F != 0xFF); a digit matches none of its compares
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public void TheCardMenuTakesTheCursorKeysOnlyWithThePointerOverALine(bool keyboard, int expected)
    {
        var off = new MouseState(5, 5, MouseButtons.None);
        MouseState Line(int n) => CardScreen.OptionPoint(n) is var (x, y) ? new MouseState(x, y, MouseButtons.None) : off;
        GameUi? ui = null;
        // the steps after the text wait for the menu's own loop (its redraw is Select's Refresh), past the fade and its flush of the keys
        (int, MouseState)? InMenu((int, MouseState) frame) => ui?.Redraw?.Method.Name.Contains("Refresh") == true ? frame : null;
        var host = new StepHost(
            () => (0, off), () => (Keys.Enter, off),
            // with the pointer off the lines: the digit 1, Down three times and Enter (the original drops them all; the convenience chooses line 0 at the digit)
            () => InMenu((Keys.ForCharacter('1'), off)), () => (0, off), () => (Keys.Down, off), () => (0, off), () => (Keys.Down, off),
            () => (0, off), () => (Keys.Down, off), () => (0, off), () => (Keys.Enter, off), () => (0, off),
            // the pointer onto the first line (it is lit), Down to the second, Enter chooses it
            () => (0, Line(0)), () => (0, Line(0)), () => (Keys.Down, Line(0)), () => (0, Line(0)), () => (Keys.Enter, Line(0)), () => (0, Line(0)));
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var world = WorldData.Load(files);
        ui = new GameUi(host, new GameAssets(files)) { KeyboardCardMenus = keyboard };
        var state = NewGame.Quickstart(world, 1, () => 1);
        state.Data = world;
        Darklands.Cards.CardContext? context = null;
        var screen = new CardScreen(ui, () => context!, new PartyPanel(ui.Assets));
        context = new Darklands.Cards.CardContext(state, world, screen);
        screen.Show("$Other00", Darklands.Cards.PictureNames.Of(34));
        var options = Enumerable.Repeat(Darklands.Cards.OptionState.Enabled, 10).ToList();
        Assert.Equal(expected, screen.Choose(0, options));
        Assert.False(host.QuitRequested);
    }

    // orig: R037+0x4664's loop reads DS:6EF0 & 1 and the keys (root 047C:073D / 0744) itself and never calls root 0000:0000: the right
    // button held over the bar's first title and let go on its first item chooses nothing, F10 and the hot keys do nothing
    [Fact]
    public void TheGeneratorRunsNeitherTheMenuBarNorTheHotKeys()
    {
        static (int, MouseState) Right(int x, int y) => (0, new MouseState(x, y, MouseButtons.Right));
        (int, MouseState)[] frames =
        [
            Up(5, 5), Right(70, 4), Right(70, 4), Right(90, 16), Right(90, 16), Up(90, 16), Up(90, 16),
            Key(Keys.F10), Up(5, 5), Key(Keys.Enter), Up(5, 5), Key(0x1000), Up(5, 5),
        ];
        var host = new FrameHost(frames);
        if (Start(host) is not var (ui, _, _, world))
        {
            return;
        }
        var chosen = false;
        var hotKeys = 0;
        ui.MenuBar = new MenuBar(ui, [new MenuBar.Menu("Game", true, [new MenuBar.Item(() => "Save Game", true, "", () => chosen = true)])]);
        ui.GameKey = _ => { hotKeys++; return true; };
        var data = Darklands.Content.Formats.Characters.CreationDataDecoder.Decode(ui.Assets.Files.Read("DARKLAND.EXE"));
        var creation = new Darklands.Core.Characters.Creation.CharacterCreation(data.Tables, world.ItemDefinitions,
            new Darklands.Core.Randomness.MscRandom(1), 1, () => 1);
        Assert.Throws<QuitGameException>(() => new CharacterGeneratorScreen(ui, data, world, creation).Run());
        Assert.False(chosen);
        Assert.Equal(0, hotKeys);
    }

    /// <summary>
    /// A prayer on the creation's member sheet: St.Finnian's three skill boosts report in the side window (R043+0x29D0), each waiting for
    /// a key and eating it (lockstep session carry-create-pray3: the original's stack R034+0x0102 &lt; R039+0x49C4 &lt; R043+0x29D0, and the
    /// begun game agrees only with the reports' waits); the fourth key is left for the screen after.
    /// </summary>
    [Fact]
    public void ASaintsSkillBoostsOnTheCreationSheetWaitForAKeyEach()
    {
        int a = Keys.ForCharacter('a'), b = Keys.ForCharacter('b'), c = Keys.ForCharacter('c'), d = Keys.ForCharacter('d');
        (int, MouseState)[] frames = [Up(5, 5), Key(a), Up(5, 5), Key(b), Up(5, 5), Key(c), Up(5, 5), Key(d), Up(5, 5)];
        var host = new FrameHost(frames);
        if (Start(host) is not var (ui, game, _, world))
        {
            return;
        }
        var sheet = CharacterCreationScreen.SheetState(ui, world, new Darklands.Core.Randomness.MscRandom(3), 5);
        sheet.Party = game.Party;
        var before = sheet.Party.Members[0].Skills[(int)Darklands.Core.Characters.Skill.Streetwise];

        Darklands.Core.Rules.SaintBook.Help(sheet, 0, 50);   // St.Finnian

        Assert.IsType<SideWindowMessages>(sheet.Messages);
        Assert.True(sheet.Party.Members[0].Skills[(int)Darklands.Core.Characters.Skill.Streetwise] > before);
        Assert.Contains(sheet.Events.All(), e => e.Event.Kind == Darklands.Core.Events.EventKinds.TravelBlessing);
        Assert.Equal(d, ui.WaitForAction().Key);
    }

    /// <summary>
    /// A Standing saint's prayer on the creation's member sheet changes the start-up city's standing: the sheet's state holds the world's
    /// location records (the round carry's note: before CarryOverSheetWorld it indexed an empty list).
    /// </summary>
    [Fact]
    public void AStandingSaintOnTheCreationSheetFindsTheStartUpCity()
    {
        var host = new FrameHost(Up(5, 5));
        if (Start(host) is not var (ui, game, _, world))
        {
            return;
        }
        var sheet = CharacterCreationScreen.SheetState(ui, world, new Darklands.Core.Randomness.MscRandom(3), 5);
        sheet.Party = game.Party;
        Assert.Equal(world.CreateLocations().Count, sheet.Locations.Count);
        var before = sheet.Locations[5].Reputation;

        Darklands.Core.Rules.SaintBook.Help(sheet, 0, 6);   // St.Alexis: Standing(10, 20) here first

        Assert.NotEqual(before, sheet.Locations[5].Reputation);
    }

    /// <summary>
    /// The text box (the save's comment, R037+0x43E6) hides the pointer before it draws (root 047C:12C3) and shows it after (root 047C:1293):
    /// no frame shown while it waits has the pointer (vis10 session comm-inn, alts2: the original's screen has none).
    /// </summary>
    [Fact]
    public void TheCommentBoxShowsNoPointerWhileItWaits()
    {
        (int, MouseState)[] frames = [Up(20, 20), Up(20, 20), Key(Keys.ForCharacter('x')), Up(20, 20), Key(Keys.Enter), Up(20, 20)];
        var host = new FrameHost(frames);
        if (Start(host) is not var (ui, _, _, _))
        {
            return;
        }
        var shown = new List<bool>();
        host.Presented = _ => shown.Add(ui.PointerVisible);
        Assert.True(ui.PointerVisible);

        var text = new TextInputBox(ui).Ask("Save Game Comment:", SaveGameDialog.CommentLength, "Darklands");

        Assert.Equal("Darklandsx", text);
        Assert.True(shown.Count > 1);
        Assert.All(shown[..^1], Assert.False);
        Assert.True(shown[^1]);   // the screen put back, then the pointer
    }

    /// <summary>
    /// The y/n question (yes-no-prompt root 0000:2522) hides the pointer before its box (0x257A, root 047C:12C3) and shows it after the box
    /// is put back (0x25E4, root 047C:1293): no frame shown while it waits has the pointer (vis10 sessions D-751050 "Delete this Game?" and
    /// batpal-screens "Quit to DOS?": the original's screens have none).
    /// </summary>
    [Fact]
    public void TheYesNoQuestionShowsNoPointerWhileItWaits()
    {
        (int, MouseState)[] frames = [Up(20, 20), Up(20, 20), Key(Keys.ForCharacter('x')), Up(20, 20), Key(Keys.ForCharacter('n')), Up(20, 20)];
        var host = new FrameHost(frames);
        if (Start(host) is not var (ui, _, _, _))
        {
            return;
        }
        var shown = new List<bool>();
        host.Presented = _ => shown.Add(ui.PointerVisible);
        Assert.True(ui.PointerVisible);

        var yes = new MessageBox(ui).Ask("Quit to DOS?");

        Assert.False(yes);
        Assert.True(shown.Count > 1);
        Assert.All(shown[..^1], Assert.False);
        Assert.True(shown[^1]);   // the screen put back, then the pointer
    }

    /// <summary>
    /// The pause box (root 0000:1080) hides the pointer while it draws its two boxes (0x10AE) and shows it while it waits for the key
    /// (pointer-show 0x10E7 before getch), then hides it while the boxes are put back (0x10F1) and shows it again (0x113A): the frames
    /// of the wait have the pointer (vis10 session leftovers-pause: the original's pointer over "Game Paused").
    /// </summary>
    [Fact]
    public void ThePauseBoxShowsThePointerWhileItWaits()
    {
        (int, MouseState)[] frames = [Up(20, 20), Up(20, 20), Up(20, 20), Key(Keys.ForCharacter('x')), Up(20, 20)];
        var host = new FrameHost(frames);
        if (Start(host) is not var (ui, _, _, _))
        {
            return;
        }
        var shown = new List<bool>();
        host.Presented = _ => shown.Add(ui.PointerVisible);
        Assert.True(ui.PointerVisible);

        new MessageBox(ui).Pause();

        // the two boxes drawn (one frame each) without it, the wait with it, the last frame (the boxes put back) with it again
        Assert.True(shown.Count > 3);
        Assert.False(shown[0]);
        Assert.False(shown[1]);
        Assert.All(shown[2..], Assert.True);
        Assert.True(ui.PointerVisible);
    }
}
