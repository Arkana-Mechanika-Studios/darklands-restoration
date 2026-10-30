using Darklands.Content;
using Darklands.Platform;
using Darklands.Presentation;

namespace Darklands.Battles.Tests;

/// <summary>The menu bar: right-button dragging, the F10 keyboard mode, greyed entries and the items' actions.</summary>
public sealed class MenuBarTests
{
    /// <summary>A host that plays one frame of input per Pump: a key, or the mouse at a place with buttons held.</summary>
    private sealed class FrameHost(params (int Key, MouseState Mouse)[] frames) : IHost, IVideo, IInput, IClock
    {
        private readonly Queue<(int Key, MouseState Mouse)> _frames = new(frames);
        private int _key;
        private long _now;
        private int _idle;

        public IVideo Video => this;
        public IInput Input => this;
        public IAudio Audio { get; } = new SilentAudio();
        public IClock Clock => this;
        public bool QuitRequested { get; private set; }
        public MouseState Mouse { get; private set; } = new(0, 100, MouseButtons.None);
        public bool ShiftHeld => false;
        public long Milliseconds => _now;

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
            if (!_frames.TryDequeue(out var frame))
            {
                // the script is over: nothing held, no keys (and the window "closes" if a loop keeps waiting)
                QuitRequested = ++_idle > 1000;
                (_key, Mouse) = (0, Mouse with { Buttons = MouseButtons.None });
                return;
            }
            (_key, Mouse) = frame;
        }
    }

    private static (int, MouseState) Key(int key) => (key, new MouseState(0, 100, MouseButtons.None));

    private static (int, MouseState) Right(int x, int y) => (0, new MouseState(x, y, MouseButtons.Right));

    private static (int, MouseState) Up(int x, int y) => (0, new MouseState(x, y, MouseButtons.None));

    private static (MenuBar Bar, List<string> Log)? Build(FrameHost host)
    {
        if (GameFolder.Files is not { } files)
        {
            return null;
        }
        var ui = new GameUi(host, new GameAssets(files));
        var log = new List<string>();
        MenuBar.Item Item(string name, IReadOnlyList<MenuBar.SubItem>? sub = null) => new(() => name, true, "", () => log.Add(name), sub);
        MenuBar.SubItem Sub(string name) => new(() => name, true, "", 0, () => log.Add(name));
        var bar = new MenuBar(ui,
        [
            new("First", true, [Item("one"), Item("two"), Item("three", [Sub("three-a"), Sub("three-b")])]),
            new("Grey", false, []),
            new("Last", true, [Item("last-one")]),
        ]);
        return (bar, log);
    }

    [Fact]
    public void TheKeyboardWalksIntoASubMenuAndEnterRunsTheItemThenTheSubItem()
    {
        var host = new FrameHost(Key(0), Key(Keys.Down), Key(Keys.Down), Key(Keys.Down), Key(Keys.Right), Key(Keys.Down), Key(Keys.Enter));
        if (Build(host) is not var (bar, log))
        {
            return;
        }
        // (the first frame shows the bar: the menu opens under the cursor before the keys move it)
        bar.Run(keyboard: true);
        // the item's flag stays set on the way into its sub-menu: both run, the item first (menu-bar-loop)
        Assert.Equal(["three", "three-b"], log);
    }

    [Fact]
    public void EscapeLeavesTheKeyboardModeWithoutChoosing()
    {
        var host = new FrameHost(Key(0), Key(Keys.Down), Key(Keys.Escape));
        if (Build(host) is not var (bar, log))
        {
            return;
        }
        bar.Run(keyboard: true);
        Assert.Empty(log);
    }

    /// <summary>
    /// In the keyboard mode the keys go to root 0000:0DD2, which takes Alt-M and does nothing with it (its default case); outside the
    /// mode root 0000:0CAA's case 0x3200 toggles the music.
    /// </summary>
    [Fact]
    public void AltMInTheKeyboardModeIsTakenWithoutTogglingTheMusic()
    {
        var host = new FrameHost(Key(Keys.F10), Key(Keys.AltM), Key(Keys.Escape), Key(0), Key(Keys.AltM));
        if (MusicTests.Start(host) is not var (ui, state, _, _))
        {
            return;
        }
        var keys = new HotKeys(ui, state, ui.Options);
        ui.GameKey = keys.Handle;
        ui.MenuBar = MenuBar.ForGame(ui, state, ui.Options, keys);
        Assert.Null(ui.Poll());                 // F10: the mode, Alt-M taken in it, Esc leaves it
        Assert.True(ui.Options.Music);
        Assert.Null(ui.Poll());
        Assert.Null(ui.Poll());                 // Alt-M outside the mode: the hot key
        Assert.False(ui.Options.Music);
    }

    [Fact]
    public void RightFromTheFirstEntrySkipsTheGreyedOne()
    {
        var host = new FrameHost(Key(Keys.Right), Key(Keys.Down), Key(Keys.Enter));
        if (Build(host) is not var (bar, log))
        {
            return;
        }
        bar.Run(keyboard: true);
        Assert.Equal(["last-one"], log);
    }

    /// <summary>
    /// Right with no item chosen tests items[-1].sub, the word before the entry's item table (root 0000:0EE4): when it is not 0 the cursor goes
    /// into a sub-menu of item -1 (DS:9C41 = 0) and stays on the entry (without the word: Right, Down, Enter chooses "last-one").
    /// </summary>
    [Fact]
    public void RightWithNoItemAndAWordBeforeTheItemsStaysOnTheEntry()
    {
        // (Down in that sub-menu reads memory that is not modelled: it does not move here, DIVERGENCE of MenuBar.KeyboardStep)
        var host = new FrameHost(Key(Keys.Right), Key(Keys.Down), Key(Keys.Enter));
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var ui = new GameUi(host, new GameAssets(files));
        var log = new List<string>();
        MenuBar.Item Item(string name) => new(() => name, true, "", () => log.Add(name));
        var bar = new MenuBar(ui,
        [
            new("First", true, [Item("one"), Item("two")], BeforeItems: () => 0x0100),
            new("Grey", false, []),
            new("Last", true, [Item("last-one")]),
        ]);
        bar.Run(keyboard: true);
        Assert.Empty(log);
    }

    /// <summary>
    /// The Game items' word before the table is DS:991C, whose high byte DS:991D is top-scoring-member: after a query that left top-scoring-member != 0,
    /// F10 and Right do not move to Party (root 0000:0EE4; direct call of the original: DS:9C41 = 0 for top-scoring-member 1, 2, 4, 0xFF, DS:9C2F = 3 for 0).
    /// </summary>
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(4, false)]
    public void F10AndRightMoveToPartyOnlyWithBestMemberZero(int bestMember, bool movesToParty)
    {
        var host = new FrameHost(Key(Keys.F10), Key(Keys.Right), Key(Keys.Down), Key(Keys.Down), Key(Keys.Enter));
        if (MusicTests.Start(host) is not var (ui, state, _, _))
        {
            return;
        }
        state.Party.Last.Member = bestMember;
        var chosen = false;
        var keys = new HotKeys(ui, state, ui.Options) { ChoosePartyOrder = () => chosen = true };
        ui.GameKey = keys.Handle;
        ui.MenuBar = MenuBar.ForGame(ui, state, ui.Options, keys);
        Assert.Null(ui.Poll());
        Assert.Equal(movesToParty, chosen);
    }

    /// <summary>
    /// On Party (the last entry) Right with no item chosen reads DS:987B, the right edge of the dropdown just opened: never 0 there, so the
    /// cursor goes into item -1's sub-menu; Left takes it out again and Down then walks the items as before.
    /// </summary>
    [Fact]
    public void RightOnPartyGoesIntoItemMinusOnesSubMenuAndLeftTakesItOut()
    {
        var host = new FrameHost(Key(Keys.F10), Key(Keys.Right), Key(Keys.Right), Key(Keys.Enter), Key(0), Key(Keys.F10), Key(Keys.Right), Key(Keys.Right),
            Key(Keys.Left), Key(Keys.Down), Key(Keys.Down), Key(Keys.Enter));
        if (MusicTests.Start(host) is not var (ui, state, _, _))
        {
            return;
        }
        state.Party.Last.Member = 0;
        var chosen = 0;
        var keys = new HotKeys(ui, state, ui.Options) { ChoosePartyOrder = () => chosen++ };
        ui.GameKey = keys.Handle;
        var bar = MenuBar.ForGame(ui, state, ui.Options, keys);
        ui.MenuBar = bar;
        Assert.Null(ui.Poll());
        Assert.NotEqual(0, bar.DropdownRight);
        Assert.Equal(0, chosen);
        Assert.Null(ui.Poll());
        Assert.Equal(1, chosen);
    }

    [Fact]
    public void DraggingWithTheRightButtonChoosesTheItemWhereItIsLetGo()
    {
        // the bar is at the top of the card window (x 60 on); the first title starts at window x 3, items from y 0xE, 8 apart
        var host = new FrameHost(Right(66, 3), Right(66, 3), Right(66, 0x0E + 8 + 2), Right(66, 0x0E + 8 + 2), Up(66, 0x0E + 8 + 2));
        if (Build(host) is not var (bar, log))
        {
            return;
        }
        bar.Run(keyboard: false);
        Assert.Equal(["two"], log);
    }

    [Fact]
    public void LettingGoOutsideTheMenusChoosesNothing()
    {
        var host = new FrameHost(Right(66, 3), Right(66, 0x0E + 2), Right(300, 150), Up(300, 150));
        if (Build(host) is not var (bar, log))
        {
            return;
        }
        bar.Run(keyboard: false);
        Assert.Empty(log);
    }
}
