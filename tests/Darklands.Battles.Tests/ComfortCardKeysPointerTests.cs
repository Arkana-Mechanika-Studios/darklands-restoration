using Darklands.Content;
using Darklands.Core;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Simulation;

namespace Darklands.Battles.Tests;

/// <summary>
/// GitHub issue 11 (senner007): with the Comfort interface's card menu keys, ↑ / ↓ with the pointer off the lines put the pointer at
/// the start of the line it lights, and the hand covered the line's first words. The pointer now goes just past the end of the
/// menu's widest line, the same column for every line, and still lights the line (Enter chooses it). Classic has no such keys.
/// </summary>
public sealed class ComfortCardKeysPointerTests
{
    /// <summary>A host that plays scripted steps once each is ready, moves its pointer where the game puts it, and records each move.</summary>
    private sealed class PointerHost(params Func<(int Key, MouseState? Mouse)?>[] steps) : IHost, IVideo, IInput, IClock
    {
        private readonly Queue<Func<(int Key, MouseState? Mouse)?>> _steps = new(steps);
        private int _key;
        private int _idle;
        private long _now;

        public List<(int X, int Y)> Moves { get; } = [];

        public IVideo Video => this;
        public IInput Input => this;
        public IAudio Audio { get; } = new SilentAudio();
        public IClock Clock => this;
        public bool QuitRequested { get; private set; }
        public MouseState Mouse { get; private set; } = new(5, 5, MouseButtons.None);
        public bool ShiftHeld => false;
        public long Milliseconds => _now;

        public void Present(Screen screen) { }

        public void Sleep(int milliseconds) => _now += Math.Max(1, milliseconds);

        public void MovePointer(int x, int y)
        {
            Moves.Add((x, y));
            Mouse = new MouseState(x, y, MouseButtons.None);
        }

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
                _key = frame.Key;
                Mouse = frame.Mouse ?? Mouse;
                return;
            }
            QuitRequested = ++_idle > 5000;
        }
    }

    [Fact]
    public void TheKeysPutThePointerPastTheWidestLineAndItStillLightsTheLine()
    {
        var off = new MouseState(5, 5, MouseButtons.None);
        GameUi? ui = null;
        var lineStart = -1;
        // the steps after the text wait for the menu's own loop (its redraw is Select's Refresh); the lines' start is the column
        //   OptionPoint gives (x + 1), read while the menu is up
        (int, MouseState?)? InMenu((int, MouseState?) frame)
        {
            if (ui?.Redraw?.Method.Name.Contains("Refresh") != true)
            {
                return null;
            }
            lineStart = CardScreen.OptionPoint(0) is var (x, _) ? x : -1;
            return frame;
        }
        var host = new PointerHost(
            () => (0, off), () => (Keys.Enter, off),
            // the pointer off the lines: Down puts it onto the first line; Down again (now over a line) onto the second; Enter
            () => InMenu((Keys.Down, off)), () => (0, null), () => (0, null), () => (Keys.Down, null), () => (0, null), () => (Keys.Enter, null));
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var world = WorldData.Load(files);
        ui = new GameUi(host, new GameAssets(files)) { KeyboardCardMenus = true };
        var state = NewGame.Quickstart(world, 1, () => 1);
        state.Data = world;
        Darklands.Cards.CardContext? context = null;
        var screen = new CardScreen(ui, () => context!, new PartyPanel(ui.Assets));
        context = new Darklands.Cards.CardContext(state, world, screen);
        screen.Show("$Other00", Darklands.Cards.PictureNames.Of(34));
        var options = Enumerable.Repeat(Darklands.Cards.OptionState.Enabled, 10).ToList();
        var chosen = screen.Choose(0, options);
        Assert.False(host.QuitRequested);

        // the second line is chosen: the pointer over the first line lit it, and Down from there moved on (the original's keys)
        Assert.Equal(1, chosen);
        Assert.Equal(2, host.Moves.Count);
        var (first, second) = (host.Moves[0], host.Moves[1]);
        // one column for both lines, well past the lines' start (the column OptionPoint gives, x + 1), on two lines one below the other
        Assert.True(lineStart > 0);
        Assert.Equal(first.X, second.X);
        Assert.True(first.X > lineStart + 40, $"pointer at {first.X}, the lines start at {lineStart}");
        Assert.True(second.Y > first.Y);
    }
}
