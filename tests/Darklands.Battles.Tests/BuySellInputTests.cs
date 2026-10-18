using Darklands.Content;
using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Simulation;

namespace Darklands.Battles.Tests;

/// <summary>
/// The buy/sell pages (barter, item store, loot) read the button's state as the original does: a menu click waits for the
/// release and takes the line under the pointer after it, and the screen does not leave with the button still held.
/// </summary>
public sealed class BuySellInputTests
{
    /// <summary>A host that plays one frame of input per Pump (the mouse at a place with buttons held), counting the frames.</summary>
    private sealed class FrameHost(params MouseState[] frames) : IHost, IVideo, IInput, IClock
    {
        private readonly Queue<MouseState> _frames = new(frames);
        private int _idle;
        private long _now;

        public IVideo Video => this;
        public IInput Input => this;
        public IAudio Audio { get; } = new SilentAudio();
        public IClock Clock => this;
        public bool QuitRequested { get; private set; }
        public MouseState Mouse { get; private set; } = new(160, 190, MouseButtons.None);
        public bool ShiftHeld => false;
        public long Milliseconds => _now;
        public int Pumped { get; private set; }

        public void Present(Screen screen) { }

        public void Sleep(int milliseconds) => _now += Math.Max(1, milliseconds);

        public bool TryReadKey(out int key)
        {
            key = 0;
            return false;
        }

        public void Pump()
        {
            Pumped++;
            if (!_frames.TryDequeue(out var frame))
            {
                QuitRequested = ++_idle > 1000;
                Mouse = Mouse with { Buttons = MouseButtons.None };
                return;
            }
            Mouse = frame;
        }
    }

    private const int MenuX = 0x60;

    private static MouseState Held(int x, int y) => new(x, y, MouseButtons.Left);

    private static MouseState Up(int x, int y) => new(x, y, MouseButtons.None);

    private static int MenuLine(int line) => 0x1B + line * 8 + 3;

    private static (FrameHost Host, int Pumped)? RunLoot(params MouseState[] frames)
    {
        if (GameFolder.Files is not { } files)
        {
            return null;
        }
        var world = WorldData.Load(files);
        var host = new FrameHost(frames);
        var ui = new GameUi(host, new GameAssets(files));
        var state = NewGame.Quickstart(world, 1, () => 1);
        state.Data = world;
        var page = new BuySellPage(ui, new PartyPanel(ui.Assets));
        new LootScreen(page, state, new List<ItemStack>()).Run(Money.From(0, 0, 0));
        return (host, host.Pumped);
    }

    [Fact]
    public void LeavingByTheMenuWaitsForTheButtonToBeLetGo()
    {
        // the Leave line (3) pressed and held for five frames, then let go
        var frames = Enumerable.Repeat(Held(MenuX, MenuLine(3)), 5).Append(Up(MenuX, MenuLine(3))).ToArray();
        if (RunLoot(frames) is not var (host, pumped))
        {
            return;
        }
        Assert.False(host.QuitRequested);
        Assert.True(pumped >= 6, $"the screen left after {pumped} frames, before the release");
        Assert.False(host.Mouse.Left);
    }

    [Fact]
    public void TheMenuLineIsTheOneUnderThePointerAtTheRelease()
    {
        // pressed on Get (line 0), dragged down to Leave (line 3) and let go there: the screen leaves
        MouseState[] frames = [Held(MenuX, MenuLine(0)), Held(MenuX, MenuLine(1)), Held(MenuX, MenuLine(3)), Up(MenuX, MenuLine(3))];
        if (RunLoot(frames) is not var (host, _))
        {
            return;
        }
        Assert.False(host.QuitRequested);
    }

    [Fact]
    public void AButtonHeldWhenTheScreenOpensIsWaitedOutFirst()
    {
        // the button still held from the click that opened the screen, over Leave: nothing happens until it is let go,
        // and the next press of Leave leaves
        MouseState[] frames = [Held(MenuX, MenuLine(3)), Held(MenuX, MenuLine(3)), Up(MenuX, MenuLine(0)), Up(MenuX, MenuLine(0)),
            Held(MenuX, MenuLine(3)), Up(MenuX, MenuLine(3))];
        if (RunLoot(frames) is not var (host, pumped))
        {
            return;
        }
        Assert.False(host.QuitRequested);
        Assert.True(pumped >= 6);
    }
}
