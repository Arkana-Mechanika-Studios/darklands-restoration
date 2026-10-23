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

    // orig: R037+0x01C0 (a press on the party column, x < 0x3C, opens member_sheet for slot y / 0x28 when the slot is filled)
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
}
