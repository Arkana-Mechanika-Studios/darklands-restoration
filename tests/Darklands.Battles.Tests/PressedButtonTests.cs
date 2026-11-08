using System.Security.Cryptography;
using Darklands.Content;
using Darklands.Core;
using Darklands.Persistence;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Simulation;

namespace Darklands.Battles.Tests;

/// <summary>
/// A pressed button's picture stays in the player's window at least GameUi.PressedButtonMilliseconds (the SDL host's 100 ms) before
/// the command runs: the inn roster's and the creation menu's commands, the load screen's Load and Delete. With 0 (Host.Headless, the
/// oracles) the press is shown in one frame and no time passes, as before. The time is the host clock's; the button's column is hashed
/// in every frame shown.
/// </summary>
public sealed class PressedButtonTests
{
    private sealed class ClockHost(params int[] keys) : IHost, IVideo, IInput, IClock
    {
        private readonly Queue<int> _keys = new(keys.SelectMany(k => new[] { k, 0, 0 }).Prepend(0).Prepend(0));
        private readonly Queue<int> _buffer = new();
        private int _idle;
        private long _now;

        public IVideo Video => this;
        public IInput Input => this;
        public IAudio Audio { get; } = new SilentAudio();
        public IClock Clock => this;
        public bool QuitRequested { get; private set; }
        public MouseState Mouse => new(5, 5, MouseButtons.None);
        public bool ShiftHeld => false;
        public long Milliseconds => _now;

        /// <summary>The button column (x, y, width, height) hashed in each frame shown.</summary>
        public (int X, int Y, int Width, int Height) Column { get; set; } = (0x41, 0x14, 0x84, 0xB0);

        /// <summary>Each frame shown: the host time and the column's hash.</summary>
        public List<(long Time, string Column)> Shown { get; } = [];

        public void Present(Screen screen)
        {
            var (x, y, w, h) = Column;
            var bytes = Enumerable.Range(y, h).SelectMany(r => screen.Pixels.AsSpan(r * Screen.Width + x, w).ToArray()).ToArray();
            Shown.Add((_now, Convert.ToHexString(SHA256.HashData(bytes))[..12]));
        }

        public void Sleep(int milliseconds) => _now += Math.Max(1, milliseconds);

        // a key waits in the buffer until the game reads it, as in the BIOS's (a wait that reads nothing keeps it)
        public bool TryReadKey(out int key) => _buffer.TryDequeue(out key);

        public void Pump()
        {
            if (!_keys.TryDequeue(out var key))
            {
                QuitRequested = ++_idle > 1000;
            }
            else if (key != 0)
            {
                _buffer.Enqueue(key);
            }
        }

        /// <summary>How long the column showed something else than before the key: from the first such frame to the first frame after.</summary>
        public long PressedFor(int from = 0)
        {
            var before = Shown[from].Column;
            var start = Shown.FindIndex(from, f => f.Column != before);
            if (start < 0)
            {
                return -1;
            }
            var pressed = Shown[start].Column;
            var end = Shown.FindIndex(start, f => f.Column != pressed);
            return (end < 0 ? _now : Shown[end].Time) - Shown[start].Time;
        }
    }

    private static (GameUi Ui, GameState State, PartyPanel Panel, WorldData World)? Start(ClockHost host, int pressed)
    {
        if (GameFolder.Files is not { } files)
        {
            return null;
        }
        var world = WorldData.Load(files);
        var ui = new GameUi(host, new GameAssets(files)) { PressedButtonMilliseconds = pressed };
        var state = NewGame.Quickstart(world, 1, () => 1);
        state.Data = world;
        return (ui, state, new PartyPanel(ui.Assets), world);
    }

    /// <summary>The inn roster's Heraldry (H): the button at (0x43, 0x4A) pressed, the next shield, the button back.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(GameUi.PressedButtonFloor)]
    public void TheInnRostersCommandButtonStaysPressedTheMinimum(int minimum)
    {
        var host = new ClockHost(Keys.ForCharacter('h'), Keys.Escape) { Column = (0x43, 0x4A, 0x82, 0x0E) };
        if (Start(host, minimum) is not var (ui, state, panel, world))
        {
            return;
        }
        new PartyRosterScreen(ui, panel, world).Run(state, 0);
        Assert.False(host.QuitRequested);
        var held = host.PressedFor();
        Assert.True(held >= 0, "the pressed button was never shown");
        if (minimum == 0)
        {
            // as the original: drawn, the command run, copied back: shown in one frame, no host time passing
            Assert.Equal(0, held);
        }
        else
        {
            Assert.InRange(held, minimum, minimum + 40);
        }
    }

    /// <summary>The creation menu's Heraldry (H) on the first character of SAVES\DEFAULT: the button pressed, the shield, the button back.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(GameUi.PressedButtonFloor)]
    public void TheCreationMenusCommandButtonStaysPressedTheMinimum(int minimum)
    {
        var host = new ClockHost(Keys.ForCharacter('h'), Keys.Escape, Keys.Escape) { Column = (0x43, 0x19, 0x82, 0x96) };
        if (Start(host, minimum) is not var (ui, _, _, world))
        {
            return;
        }
        var screen = new CharacterCreationScreen(ui, world, () => 1);
        try
        {
            screen.Run();
        }
        catch (QuitGameException)
        {
        }
        var held = host.PressedFor(1);
        Assert.True(held >= 0, "the pressed button was never shown");
        if (minimum == 0)
        {
            Assert.Equal(0, held);
        }
        else
        {
            Assert.InRange(held, minimum, minimum + 40);
        }
    }

    /// <summary>The load screen's Load and Delete: the pressed button shown, held the minimum before the load runs or the question is asked.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(GameUi.PressedButtonFloor)]
    public void TheLoadScreensButtonsStayPressedTheMinimumBeforeTheirCommand(int minimum)
    {
        if (GameFolder.Files is not { } files || !File.Exists(Path.Combine(files.Root, "SAVES", "DKSAVE1.SAV")))
        {
            return;
        }
        var copy = TempCopies.Create("darklands-pressed-");
        File.Copy(Path.Combine(files.Root, "SAVES", "DKSAVE1.SAV"), Path.Combine(copy, "DKSAVE1.SAV"));
        var folder = new SaveFolder(copy);
        // Delete, "no", then Load (refused: the screen as it was), then Esc
        var host = new ClockHost(Keys.ForCharacter('d'), Keys.ForCharacter('n'), Keys.ForCharacter('l'), Keys.Escape) { Column = (0x41, 0x2C, 0x84, 0x1E) };
        var ui = new GameUi(host, new GameAssets(files)) { PressedButtonMilliseconds = minimum };
        var screen = new LoadGameScreen(ui, folder, Content.Formats.Saves.LoadScreenTexts.Decode(files.Read("DARKLAND.EXE")));
        long? loadAt = null;
        var keyFrames = 0;
        Assert.Null(screen.Run(fromStartScreen: true, _ =>
        {
            loadAt = host.Milliseconds;
            keyFrames = host.Shown.Count;
            return null;
        }));
        Assert.NotNull(loadAt);
        // the Load button's press: the frames before the load that show it (the last run of one column), from the first of them to the load
        var shown = host.Shown.Take(keyFrames).ToList();
        var first = shown.FindLastIndex(f => f.Column != shown[^1].Column) + 1;
        Assert.True(first > 0, "the pressed Load button was never shown");
        Assert.InRange(loadAt!.Value - shown[first].Time, minimum, minimum + 40);
        Assert.Equal(["DKSAVE1.SAV"], folder.Names());
    }
}
