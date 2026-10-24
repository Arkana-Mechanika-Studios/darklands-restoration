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
}
