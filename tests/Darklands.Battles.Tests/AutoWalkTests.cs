using Darklands.Content;
using Darklands.Core;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Simulation;
using Darklands.Simulation.Overland;

namespace Darklands.Battles.Tests;

/// <summary>
/// Walking on, the Interface switch on Comfort (GameUi.ComfortInterface): a direction key tapped twice, or pressed with Shift, walks on
/// that way with the map's own key, until a key of the player's or the map's edge; Classic makes one step a tap as the original. A
/// recording of it (its keys, make codes and clock) plays back the same walk.
/// </summary>
// the map's trace (OverlandScreen.Trace) is static: the classes that set it run one after the other
[Collection("OverlandTrace")]
public sealed class AutoWalkTests
{
    /// <summary>A host whose frames give keys (a number), make / break codes ("d:4D" / "u:4D") and Shift ("shift" / "noshift").</summary>
    private sealed class TapHost(params object[][] frames) : IHost, IVideo, IInput, IClock
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
        public bool ShiftHeld { get; private set; }
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
                switch (e)
                {
                    case "shift":
                        ShiftHeld = true;
                        break;
                    case "noshift":
                        ShiftHeld = false;
                        break;
                    case string code:
                        _transitions.Enqueue((Convert.ToInt32(code[2..], 16), code[0] == 'u'));
                        break;
                    default:
                        _keys.Enqueue((int)e);
                        break;
                }
            }
        }
    }

    /// <summary>The frames the map's opening takes before its loop's first pass.</summary>
    private const int Lead = 30;

    private sealed record Walked(List<string> Trace, int X, int Y, int Hour, int Day, string Random);

    private static Walked? Walk(bool comfort, int idle, params object[][] frames)
    {
        if (GameFolder.Files is not { } files)
        {
            return null;
        }
        var world = WorldData.Load(files);
        var ui = new GameUi(new TapHost([.. Enumerable.Repeat(Array.Empty<object>(), Lead), .. frames, .. Enumerable.Repeat(Array.Empty<object>(), idle)]), new GameAssets(files))
        {
            ComfortInterface = comfort,
        };
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
        return new Walked(steps, state.MapPosition.X, state.MapPosition.Y, state.Date.Hour, state.Date.Day, state.Random.Below(1_000_000).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static int Steps(Walked w) => w.Trace.Count(s => s.StartsWith("step d=", StringComparison.Ordinal));

    // Right tapped twice: two presses, each with its make and break code, a frame apart
    private static readonly object[][] DoubleTapRight = [["d:4D", Keys.Right], ["u:4D"], ["d:4D", Keys.Right], ["u:4D"]];

    [Fact]
    public void ADoubleTapWalksOnInComfortAndStepsTwiceInClassic()
    {
        if (Walk(comfort: false, idle: 80, DoubleTapRight) is not { } classic || Walk(comfort: true, idle: 80, DoubleTapRight) is not { } comfort)
        {
            return;
        }
        Assert.Equal(2, Steps(classic));
        Assert.DoesNotContain(classic.Trace, s => s.StartsWith("walk on", StringComparison.Ordinal));
        Assert.Contains("walk on 4D00", comfort.Trace);
        Assert.True(Steps(comfort) > 12, string.Join(" | ", comfort.Trace));
        Assert.All(comfort.Trace.Where(s => s.StartsWith("step d=", StringComparison.Ordinal)), s => Assert.StartsWith("step d=1,0", s, StringComparison.Ordinal));
    }

    [Fact]
    public void TwoTapsTooFarApartAreTwoSteps()
    {
        // the second make code long after the first (the clock runs on with the map's passes)
        object[][] slow = [["d:4D", Keys.Right], ["u:4D"], .. Enumerable.Repeat(Array.Empty<object>(), 100), ["d:4D", Keys.Right], ["u:4D"]];
        if (Walk(comfort: true, idle: 30, slow) is { } walked)
        {
            Assert.Equal(2, Steps(walked));
        }
    }

    [Fact]
    public void ShiftWithADirectionWalksOnAndAnyOtherKeyStopsIt()
    {
        // Shift+Down once, twenty passes, then 'x': the walk stops there; the key is the player's (no step: it is no direction)
        object[][] frames = [["shift", "d:50", Keys.Down], ["u:50", "noshift"], .. Enumerable.Repeat(Array.Empty<object>(), 80), [0x2D78]];
        if (Walk(comfort: true, idle: 30, frames) is not { } walked || Walk(comfort: false, idle: 30, frames) is not { } classic)
        {
            return;
        }
        Assert.Contains("walk on 5000", walked.Trace);
        Assert.Contains("walk on stops: the player's key or click", walked.Trace);
        var stop = walked.Trace.IndexOf("walk on stops: the player's key or click");
        Assert.DoesNotContain(walked.Trace.Skip(stop), s => s.StartsWith("step d=", StringComparison.Ordinal));
        Assert.True(Steps(walked) > 5);
        Assert.Equal(1, Steps(classic));
    }

    [Fact]
    public void AWalkWestStopsByItselfAtNightfallTheEdgeOrBlockedGround()
    {
        // the quickstart party stands near the map's west edge (column 8), late in the day: walking on west ends without a key (here at
        // nightfall, which comes inside a step: that step ends, and no other follows)
        object[][] tapLeft = [["d:4B", Keys.Left], ["u:4B"], ["d:4B", Keys.Left], ["u:4B"]];
        if (Walk(comfort: true, idle: 600, tapLeft) is not { } walked)
        {
            return;
        }
        if (Environment.GetEnvironmentVariable("DARKLANDS_AUTOWALK_TRACE") is { Length: > 0 } file)
        {
            File.WriteAllLines(file, walked.Trace);
        }
        Assert.Contains("walk on 4B00", walked.Trace);
        var stop = walked.Trace.FindIndex(s => s.StartsWith("walk on stops:", StringComparison.Ordinal));
        Assert.True(stop > 0, string.Join(" | ", walked.Trace.TakeLast(8)));
        Assert.DoesNotContain("the player's key or click", walked.Trace[stop], StringComparison.Ordinal);
        Assert.DoesNotContain(walked.Trace.Skip(stop + 2), s => s.StartsWith("step d=", StringComparison.Ordinal));
    }

    [Fact]
    public void ARecordingWithWalkingOnPlaysBackTheSameWalk()
    {
        // the same keys, make codes and clock twice: the same steps, place, time and generator, as a replay of a bug report plays it
        object[][] frames = [.. DoubleTapRight, .. Enumerable.Repeat(Array.Empty<object>(), 25), [Keys.Up], .. Enumerable.Repeat(Array.Empty<object>(), 5), ["shift", "d:4B", Keys.Left], ["u:4B", "noshift"]];
        if (Walk(comfort: true, idle: 30, frames) is not { } first || Walk(comfort: true, idle: 30, frames) is not { } again)
        {
            return;
        }
        Assert.Equal(first.Trace, again.Trace);
        Assert.Equal((first.X, first.Y, first.Hour, first.Day, first.Random), (again.X, again.Y, again.Hour, again.Day, again.Random));
        Assert.Contains("walk on 4B00", first.Trace);
    }
}
