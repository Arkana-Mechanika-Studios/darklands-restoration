using Darklands.Content;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Simulation;
using Darklands.Simulation.Overland;

namespace Darklands.Battles.Tests;

/// <summary>
/// Ctrl-Break as the BIOS delivers it (the buffer emptied, the key word 0000h stored, the game's INT 1Bh handler setting DS:07F4) and
/// what the game makes of it: key-waiting-test sees no key behind the 0000h, read-key takes it (key 0), quit-check root 0000:1338 ends
/// the program; and R030+0x0C44's default case, which steps again in place after a walk (R030+0x10AB) and leaves the marker's frame for
/// key 0 (R030+0x12E7). Lockstep sessions kbreak-map-walk, tk-map-walk-key, kbreak-card-quit, kbreak-menu-quit.
/// </summary>
// the map's trace (OverlandScreen.Trace) is static: the classes that set it run one after the other
[Collection("OverlandTrace")]
public sealed class CtrlBreakTests
{
    /// <summary>A host whose keyboard is a key queue; the test presses keys and Ctrl-Break when it likes, as a scripted host does.</summary>
    private sealed class BreakHost : IHost, IVideo, IInput, IClock
    {
        private readonly Queue<int> _keys = new();
        private long _now;
        private bool _break;
        private int _breakAfter;

        public int Pumps { get; private set; }
        public int PumpLimit { get; set; } = 200;
        public IVideo Video => this;
        public IInput Input => this;
        public IAudio Audio { get; } = new SilentAudio();
        public IClock Clock => this;
        public bool QuitRequested { get; private set; }
        public MouseState Mouse => new(5, 5, MouseButtons.None);
        public bool ShiftHeld => false;
        public long Milliseconds => _now;
        public Action<int>? OnPump { get; set; }

        public void Present(Screen screen) { }

        public void Sleep(int milliseconds) => _now += Math.Max(1, milliseconds);

        public void Press(int key) => _keys.Enqueue(key);

        /// <summary>Ctrl-Break now: the keys waiting are gone.</summary>
        public void CtrlBreak()
        {
            _keys.Clear();
            _break = true;
        }

        /// <summary>The key pressed, and Ctrl-Break right after the game has looked at it or read it.</summary>
        public void PressThenBreak(int key)
        {
            _keys.Enqueue(key);
            _breakAfter = key;
        }

        public bool TryReadKey(out int key)
        {
            if (!_keys.TryDequeue(out key))
            {
                return false;
            }
            if (_breakAfter != 0 && key == _breakAfter)
            {
                _breakAfter = 0;
                CtrlBreak();
            }
            return true;
        }

        public bool TakeCtrlBreak()
        {
            var b = _break;
            _break = false;
            return b;
        }

        public void Pump()
        {
            Pumps++;
            if (Pumps > PumpLimit)
            {
                QuitRequested = true;
                return;
            }
            OnPump?.Invoke(Pumps);
        }
    }

    [Fact]
    public void TheWordOfACtrlBreakHidesTheKeysBehindItUntilAReadTakesIt()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var host = new BreakHost();
        var ui = new GameUi(host, new GameAssets(files));
        host.Press(Keys.Enter);
        host.CtrlBreak();
        host.Press(Keys.Space);
        // key-waiting-test finds 0000h: no key (Enter is gone, Space waits behind it)
        Assert.False(ui.KeyWaiting);
        Assert.False(ui.TryReadKey(out _));
        Assert.True(ui.CtrlBreakPressed);
        // read-key takes 0000h at once, then Space is the next key
        Assert.Equal(Keys.None, ui.ReadKey());
        Assert.True(ui.KeyWaiting);
        Assert.Equal(Keys.Space, ui.ReadKey());
        // quit-check ends the program (exit(0) after the shutdown's text screen)
        var exit = Assert.Throws<ProgramExitException>(ui.QuitCheck);
        Assert.Equal(0, exit.Code);
        Assert.Equal("", exit.Printed);
    }

    [Fact]
    public void ACtrlBreakBetweenTheLookAndTheReadGivesTheReadTheWord()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var host = new BreakHost();
        var ui = new GameUi(host, new GameAssets(files));
        host.PressThenBreak(Keys.Escape);
        // the look finds Escape; the BIOS empties the buffer before the read, which takes 0000h: `if (test()) key = read()` has no key,
        // the map's handler asks for key 0
        Assert.Null(ui.Poll());
        Assert.True(ui.TakeBreakKeyRead());
        Assert.False(ui.TakeBreakKeyRead());
        Assert.False(ui.KeyWaiting);
        Assert.True(ui.CtrlBreakPressed);
    }

    [Fact]
    public void WithoutCtrlBreakQuitCheckGoesOn()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var ui = new GameUi(new BreakHost(), new GameAssets(files));
        ui.QuitCheck();
        Assert.False(ui.CtrlBreakPressed);
    }

    private static List<string> Travel(GameFiles files, BreakHost host, Action<GameUi, List<string>> onStep)
    {
        var world = WorldData.Load(files);
        var ui = new GameUi(host, new GameAssets(files));
        var state = NewGame.Quickstart(world, 1, () => 1);
        state.Data = world;
        var map = new WorldMap(Content.Formats.Maps.WorldMapData.Decode(files.Read("DARKLAND.MAP")), state.Locations);
        var steps = new List<string>();
        var trace = OverlandScreen.Trace;
        OverlandScreen.Trace = line =>
        {
            steps.Add(line);
            onStep(ui, steps);
        };
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

    /// <summary>A key with no map function in the pass after a walked step: the default case steps again in place (DS:8676 kept).</summary>
    [Fact]
    public void ALetterRightAfterAWalkStepsAgainInPlace()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var host = new BreakHost();
        host.OnPump = n =>
        {
            if (n == 2)
            {
                host.Press(Keys.Right);
            }
        };
        var steps = Travel(files, host, (ui, s) =>
        {
            if (s.Count == 1)
            {
                host.Press(0x2D78);     // x, read by the next pass
            }
        });
        Assert.Equal(["step d=1,0", "step in place d=3 -> Walked"], steps.Select(s => s.StartsWith("step d=", StringComparison.Ordinal) ? s[..10] : s));
    }

    /// <summary>A letter after an idle pass (DS:8676 = 0 again): no step.</summary>
    [Fact]
    public void ALetterAfterAnIdlePassDoesNotStep()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var host = new BreakHost();
        host.OnPump = n =>
        {
            if (n == 2)
            {
                host.Press(Keys.Right);
            }
            else if (n == 40)
            {
                host.Press(0x2D78);
            }
        };
        var steps = Travel(files, host, (_, _) => { });
        Assert.Equal(["step d=1,0"], steps.Select(s => s[..10]));
    }

    /// <summary>
    /// Ctrl-Break between the map's look and its read in the pass after a walk: 0c44 gets key 0 (R030+0x12E7 taken), and the default
    /// case steps in place; the keyboard works again after the word is read.
    /// </summary>
    [Fact]
    public void KeyZeroFromACtrlBreakAfterAWalkStepsInPlace()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var host = new BreakHost();
        host.OnPump = n =>
        {
            if (n == 2)
            {
                host.Press(Keys.Right);
            }
        };
        GameUi? seen = null;
        var steps = Travel(files, host, (ui, s) =>
        {
            seen = ui;
            if (s.Count == 1)
            {
                host.PressThenBreak(0x2D78);
            }
            else if (s.Count == 2)
            {
                host.Press(Keys.Left);
            }
        });
        Assert.Equal(["step d=1,0", "step in place d=3 -> Walked", "step d=-1,0"], steps.Select(s => s.StartsWith("step d=", StringComparison.Ordinal) ? s[..(s[7] == '-' ? 11 : 10)] : s));
        Assert.True(seen!.CtrlBreakPressed);
    }
}
