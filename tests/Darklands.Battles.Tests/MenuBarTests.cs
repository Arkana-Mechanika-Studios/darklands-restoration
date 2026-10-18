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
        // the item's flag stays set on the way into its sub-menu: both run, the item first (menu_bar_run)
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
