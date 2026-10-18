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
}
