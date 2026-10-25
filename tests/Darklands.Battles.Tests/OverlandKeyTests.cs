using Darklands.Content;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Simulation;
using Darklands.Simulation.Overland;

namespace Darklands.Battles.Tests;

/// <summary>
/// The overland map takes a second waiting key in the same pass (R030+0x0566..0x057B): the two presses a held arrow leaves in
/// the keyboard buffer make one step, so the party stops within a pass of the key's release.
/// </summary>
public sealed class OverlandKeyTests
{
    /// <summary>A host whose keyboard is the BIOS buffer: each Pump plays the next frame's key events into it.</summary>
    private sealed class KeyHost(params int[][] frames) : IHost, IVideo, IInput, IClock
    {
        private readonly Queue<int[]> _frames = new(frames);
        private readonly BiosKeyBuffer _keys = new();
        private long _now;

        public IVideo Video => this;
        public IInput Input => this;
        public IAudio Audio { get; } = new SilentAudio();
        public IClock Clock => this;
        public bool QuitRequested { get; private set; }
        public MouseState Mouse => new(5, 5, MouseButtons.None);
        public bool ShiftHeld => false;
        public long Milliseconds => _now;

        public void Present(Screen screen) { }

        public void Sleep(int milliseconds) => _now += Math.Max(1, milliseconds);

        public bool TryReadKey(out int key) => _keys.TryRead(out key);

        public void Pump()
        {
            if (!_frames.TryDequeue(out var frame))
            {
                QuitRequested = true;
                return;
            }
            foreach (var key in frame)
            {
                if (key == 0)
                {
                    _keys.ScanCode();   // the release
                }
                else
                {
                    _keys.Press(key);
                }
            }
        }
    }

    [Fact]
    public void TheTwoPressesAHeldArrowLeavesMakeOneStep()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var world = WorldData.Load(files);
        // the right arrow held long enough for ten typematic repeats before the map looks again, then idle frames
        int[][] frames = [[], [.. Enumerable.Repeat(Keys.Right, 10)], .. Enumerable.Repeat(Array.Empty<int>(), 40)];
        var host = new KeyHost(frames);
        var ui = new GameUi(host, new GameAssets(files));
        var state = NewGame.Quickstart(world, 1, () => 1);
        state.Data = world;
        var map = new WorldMap(Content.Formats.Maps.WorldMapData.Decode(files.Read("DARKLAND.MAP")), state.Locations);
        var steps = new List<string>();
        var trace = OverlandScreen.Trace;
        OverlandScreen.Trace = steps.Add;
        try
        {
            new OverlandScreen(ui, new PartyPanel(ui.Assets), map).Travel(state);
        }
        catch (QuitGameException)
        {
        }
        finally
        {
            OverlandScreen.Trace = trace;
        }
        // the buffer kept two of the ten presses (BiosKeyBuffer); one pass takes both: a single step (two without the second read, ten without the collapse)
        Assert.Equal(["step d=1,0"], steps.Select(s => s[..10]));
    }

    /// <summary>A host whose frames give keys (a number) and the keyboard interrupt's make / break codes ("d:48" / "u:48").</summary>
    private sealed class HeldKeyHost(params object[][] frames) : IHost, IVideo, IInput, IClock
    {
        private readonly Queue<object[]> _frames = new(frames);
        private readonly Queue<int> _keys = new();
        private readonly Queue<(int, bool)> _transitions = new();
        private long _now;

        public IVideo Video => this;
        public IInput Input => this;
        public IAudio Audio { get; } = new SilentAudio();
        public IClock Clock => this;
        public bool QuitRequested { get; private set; }
        public MouseState Mouse => new(5, 5, MouseButtons.None);
        public bool ShiftHeld => false;
        public long Milliseconds => _now;

        public void Present(Screen screen) { }

        public void Sleep(int milliseconds) => _now += Math.Max(1, milliseconds);

        public bool TryReadKey(out int key) => _keys.TryDequeue(out key);

        public bool TryReadKeyTransition(out int scanCode, out bool released)
        {
            var got = _transitions.TryDequeue(out var t);
            (scanCode, released) = t;
            return got;
        }

        public void Pump()
        {
            if (!_frames.TryDequeue(out var frame))
            {
                QuitRequested = true;
                return;
            }
            foreach (var e in frame)
            {
                if (e is string code)
                {
                    _transitions.Enqueue((Convert.ToInt32(code[2..], 16), code[0] == 'u'));
                }
                else
                {
                    _keys.Enqueue((int)e);
                }
            }
        }
    }

    /// <summary>The frames the map's opening takes before its loop's first pass (the events come after them).</summary>
    private const int Lead = 30;

    private static List<string>? Walk(params object[][] frames)
    {
        if (GameFolder.Files is not { } files)
        {
            return null;
        }
        var world = WorldData.Load(files);
        var ui = new GameUi(new HeldKeyHost([.. Enumerable.Repeat(Array.Empty<object>(), Lead), .. frames, .. Enumerable.Repeat(Array.Empty<object>(), 40)]), new GameAssets(files));
        var state = NewGame.Quickstart(world, 1, () => 1);
        state.Data = world;
        var map = new WorldMap(Content.Formats.Maps.WorldMapData.Decode(files.Read("DARKLAND.MAP")), state.Locations);
        var steps = new List<string>();
        var trace = OverlandScreen.Trace;
        OverlandScreen.Trace = steps.Add;
        try
        {
            new OverlandScreen(ui, new PartyPanel(ui.Assets), map).Travel(state);
        }
        catch (QuitGameException)
        {
        }
        finally
        {
            OverlandScreen.Trace = trace;
        }
        return steps;
    }

    [Fact]
    public void AHeldDirectionReplacesTheKeyReadOnceUntilItIsPressedAgain()
    {
        // R030+0x0580..0x066A: Up held (DS:0881 < 0) and 'x' read: one step up; the bytes are cleared, the key stays held (DS:0884), so the
        // passes after it, and a second make while it is held, make no step
        var steps = Walk(["d:48", 0x2D78], [], [], ["d:48"], [], []);
        if (steps is not null)
        {
            Assert.Equal(["step d=0,-1"], steps.Select(s => s[..11]));
        }
    }

    [Fact]
    public void AHeldDirectionWithNoKeyWaitingWaitsForTheNextKey()
    {
        // R030+0x0549..0x0566: no key waiting but DS:0880 set: get_key waits (the screen stands), then the held Right replaces the key
        var steps = Walk(["d:4D"], [], [], [], [0x2D78], [], ["u:4D"], [0x2D78]);
        if (steps is not null)
        {
            Assert.Equal(["step d=1,0"], steps.Select(s => s[..10]));
            // and with no key at all, no step: the map waits in get_key for good
            Assert.Empty(Walk(["d:4D"])!);
        }
    }

    [Fact]
    public void TheBackquoteKeyHeldWalksUp()
    {
        // DS:088A gives scan code 0x29 (`) the up bit: its BIOS key (0x2960) is replaced by Up
        var steps = Walk(["d:29", 0x2960], [], ["u:29"]);
        if (steps is not null)
        {
            Assert.Equal(["step d=0,-1"], steps.Select(s => s[..11]));
        }
    }

    [Fact]
    public void TheHeldKeyOfTheSigns()
    {
        Assert.Equal(Keys.Home, OverlandScreen.HeldKey(-0x5A, -0x5A, true));
        Assert.Equal(Keys.End, OverlandScreen.HeldKey(-0x7F, 0x7F, false));
        Assert.Equal(Keys.PageUp, OverlandScreen.HeldKey(0x5A, -0x5A, false));
        Assert.Equal(Keys.PageDown, OverlandScreen.HeldKey(0x5A, 0x5A, false));
        Assert.Equal(Keys.Down, OverlandScreen.HeldKey(0, 0x5A, true));
        Assert.Equal(Keys.Enter, OverlandScreen.HeldKey(0, 0, true));
        Assert.Null(OverlandScreen.HeldKey(0, 0, false));
    }

    /// <summary>
    /// R030+0x0C44's rest (A): delay, one step's time, delay, and only then the key test; a key pressed in the first delay still lets
    /// that step happen, and it stays waiting for the map's next pass.
    /// </summary>
    [Fact]
    public void AKeyInTheRestsFirstDelayStillLetsTheStepHappen()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var world = WorldData.Load(files);
        // A, then a key with no map function one frame later (inside the first 75 ms delay), then idle frames
        int[][] frames = [[], [0x1E61], [], [], [0x2C7A], .. Enumerable.Repeat(Array.Empty<int>(), 40)];
        var host = new KeyHost(frames);
        var ui = new GameUi(host, new GameAssets(files));
        var state = NewGame.Quickstart(world, 1, () => 1);
        state.Data = world;
        var map = new WorldMap(Content.Formats.Maps.WorldMapData.Decode(files.Read("DARKLAND.MAP")), state.Locations);
        var steps = new List<string>();
        var trace = OverlandScreen.Trace;
        OverlandScreen.Trace = steps.Add;
        try
        {
            new OverlandScreen(ui, new PartyPanel(ui.Assets), map).Travel(state);
        }
        catch (QuitGameException)
        {
        }
        finally
        {
            OverlandScreen.Trace = trace;
        }
        Assert.Equal(["rest step -> Walked"], steps);
        Assert.Equal(3, state.MapLeaveMode);
    }
}
