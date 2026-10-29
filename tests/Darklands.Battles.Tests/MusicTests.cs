using Darklands.Content;
using Darklands.Core;
using Darklands.Core.Randomness;
using Darklands.Core.Rules;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Simulation;

namespace Darklands.Battles.Tests;

/// <summary>
/// The music the screens ask for, in the original's order: the pieces of the cards, the map, the camp and the
/// prayer screen, the piece kept with driver command 5 and played back, and the camp's random draw.
/// </summary>
public sealed class MusicTests
{
    /// <summary>A towns-and-map driver reduced to its bookkeeping: the numbers asked for, and the word command 5 answers.</summary>
    internal sealed class MusicRecorder : IAudio
    {
        private int _last;

        public List<int> Requests { get; } = [];

        public void PlayMusic(int tune) => PlayMusicNow(tune);

        public int PlayMusicNow(int tune)
        {
            Requests.Add(tune);
            if (tune == 5)
            {
                return _last;
            }
            _last = tune;
            return 0;
        }

        public void StopMusic() => PlayMusic(1);

        public void PlaySound(int effect) => Requests.Add(0x100 + effect);
    }

    /// <summary>The headless host's steps (key, wait, move), with its clock: 16 ms a frame.</summary>
    internal sealed class StepHost(MusicRecorder audio, params string[] steps) : IHost, IVideo, IInput, IClock
    {
        private readonly Queue<string> _steps = new(steps);
        private readonly Queue<int> _keys = new();
        private int _idle;
        private long _now;

        public IVideo Video => this;
        public IInput Input => this;
        public IAudio Audio => audio;
        public IClock Clock => this;
        public bool QuitRequested { get; private set; }
        public MouseState Mouse { get; private set; } = new(160, 100, MouseButtons.None);
        public bool ShiftHeld => false;
        public long Milliseconds => _now;
        public int Left => _steps.Count;

        /// <summary>Each frame presented (tests that look at the screen).</summary>
        public Action<Screen>? Presented { get; set; }

        public void Present(Screen screen) => Presented?.Invoke(screen);

        public void Sleep(int milliseconds) => _now += Math.Max(1, milliseconds);

        public void MovePointer(int x, int y) => Mouse = Mouse with { X = x, Y = y };

        /// <summary>Each key as it is read (tests that look at the state a key is read in).</summary>
        public Action<int>? Read { get; set; }

        public bool TryReadKey(out int key)
        {
            if (!_keys.TryDequeue(out key))
            {
                return false;
            }
            Read?.Invoke(key);
            return true;
        }

        public void Pump()
        {
            _now += 16;
            if (_idle > 0)
            {
                _idle--;
                return;
            }
            if (!_steps.TryDequeue(out var step))
            {
                QuitRequested = true;
                return;
            }
            var (kind, value) = (step.Split(':', 2)[0], step.Split(':', 2)[1]);
            switch (kind)
            {
                case "key":
                    _keys.Enqueue(value switch
                    {
                        "space" => Keys.Space,
                        "enter" => Keys.Enter,
                        "esc" => Keys.Escape,
                        "up" => Keys.Up,
                        _ => Keys.ForCharacter(value[0]),
                    });
                    _idle = 2;
                    break;
                case "code":
                    // a key code in hex (the Alt and function keys)
                    _keys.Enqueue(int.Parse(value, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture));
                    _idle = 2;
                    break;
                case "wait":
                    _idle = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "move":
                    var xy = value.Split(',');
                    Mouse = Mouse with { X = int.Parse(xy[0], System.Globalization.CultureInfo.InvariantCulture), Y = int.Parse(xy[1], System.Globalization.CultureInfo.InvariantCulture) };
                    break;
            }
        }
    }

    private static string[] Steps(params string[] steps) => steps;

    private static string[] Card(string key) => ["key:" + key, "wait:40", "key:space", "wait:20"];

    /// <summary>
    /// Quickstart (seed 27) → the inn → the main street → the churches → the main street → the market → the main street →
    /// the gate → the map → the camp, and back: each place's piece in turn (the street's silence 6, the inn's 9 or 10,
    /// the church's 8, the market's 0x0B), the map's piece kept and drawn, the camp's 0x15 and the map's piece again.
    /// </summary>
    [Fact]
    public void AWalkThroughGoslarPlaysEachPlacesMusic()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        string[] steps =
        [
            "key:q", "wait:10", "key:space", "wait:10", "key:space", "wait:10", "key:space", "wait:10", "key:space", "wait:10",
            "key:space", "wait:40",
            .. Card("1"), .. Card("7"), .. Card("4"), .. Card("6"), .. Card("3"), .. Card("7"),
            "move:10,195", "wait:5", "key:up", "wait:5", "key:enter", "wait:40", "key:space", "wait:20",
            // the map takes the C key 61 frames after the gate (later, the hour that passes on the map throws waiting keys away)
            "key:1", "wait:61", "key:c", "wait:60", "key:esc", "wait:40",
        ];
        var audio = new MusicRecorder();
        var host = new StepHost(audio, steps);
        var game = new DarklandsGame(host, files)
        {
            Seed = () => 27,
            PlayOpening = false,
            ShowStartUp = false,
            SaveDirectory = Path.Combine(Path.GetTempPath(), "darklands-music-tests"),
        };
        // the walk chooses the card lines by their digits: the keyboard convenience (the original's card menu ignores digits)
        game.Ui.KeyboardCardMenus = true;
        game.Run();
        Assert.Equal(0, host.Left);
        // the inn's draw gave 10; the map's draw 0x13; command 5 on the map answers the street's 6, in the camp the map's 0x13
        Assert.Equal([0x0A, 6, 8, 6, 0x0B, 6, 5, 0x13, 5, 0x15, 0x13], audio.Requests);
    }

    internal static (GameUi Ui, GameState State, PartyPanel Panel, WorldData World)? Start(IHost host)
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

    internal static CampScreen Camp(GameUi ui, PartyPanel panel, WorldData world)
    {
        var formulas = FormulaBook.Load(ui.Assets.Files, world);
        var alchemy = new AlchemyScreen(ui, panel, formulas);
        var sheet = new CharacterSheetScreen(ui, panel, formulas, world.Items.Saints.Select(s => s.FullName).ToList(), alchemy);
        return new CampScreen(ui, panel, formulas, alchemy, sheet);
    }

    /// <summary>
    /// The camp keeps the piece playing (command 5), plays the inn's 9 or 10 by one draw of the game's generator — the
    /// first draw of the stay — and plays the kept piece back on leaving; in the wild 0x15 and no draw.
    /// </summary>
    [Theory]
    [InlineData(0, 1u)]
    [InlineData(0, 2u)]
    [InlineData(0, 3u)]
    [InlineData(0, 4u)]
    [InlineData(1, 1u)]
    [InlineData(2, 5u)]
    public void TheCampPlaysItsPieceAndThenTheKeptOne(int mode, uint seed)
    {
        var audio = new MusicRecorder();
        var host = new StepHost(audio, "wait:2", "key:esc", "wait:5");
        if (Start(host) is not var (ui, state, panel, world))
        {
            return;
        }
        ui.PlayMusic(0x0B);
        state.Random.Seed(seed);
        var expected = new MscRandom();
        expected.Seed(seed);
        var piece = mode == 1 ? 0x15 : expected.Below(2) != 0 ? 9 : 10;
        Camp(ui, panel, world).Stay(state, mode);
        Assert.Equal([0x0B, 5, piece, 0x0B], audio.Requests);
        Assert.Equal(0x0B, ui.SavedMusic.Value);
        // the stay itself draws nothing when nobody does anything: the next number is the one after the music's draw
        Assert.Equal(expected.NextRaw(), state.Random.NextRaw());
    }

    /// <summary>With music off every music request plays the stop, command 5 included; the kept AX (0) plays the stop again.</summary>
    [Fact]
    public void WithMusicOffTheCampPlaysTheStop()
    {
        var audio = new MusicRecorder();
        var host = new StepHost(audio, "wait:2", "key:esc", "wait:5");
        if (Start(host) is not var (ui, state, panel, world))
        {
            return;
        }
        ui.Options.Music = false;
        state.Random.Seed(7);
        var expected = new MscRandom();
        expected.Seed(7);
        expected.Below(2);
        Camp(ui, panel, world).Stay(state, 0);
        Assert.Equal([1, 1, 1], audio.Requests);
        Assert.Equal(0, ui.SavedMusic.Value);
        Assert.Equal(expected.NextRaw(), state.Random.NextRaw());
    }

    /// <summary>The prayer screen keeps the piece, plays 13 and plays the kept piece back, also when the player backs out.</summary>
    [Theory]
    [InlineData(PrayerMode.ForParty, false)]
    [InlineData(PrayerMode.ForMember, false)]
    [InlineData(PrayerMode.ForParty, true)]
    public void ThePrayerScreenPlaysThirteen(PrayerMode mode, bool inBattle)
    {
        var audio = new MusicRecorder();
        var host = new StepHost(audio, "wait:2", "key:esc", "wait:5");
        if (Start(host) is not var (ui, state, panel, _))
        {
            return;
        }
        ui.PlayMusic(8);
        var outcome = new SaintPrayerScreen(ui, panel) { InBattle = () => inBattle }.Pray(state, 0, 0, mode, SaintEffects.Instance);
        Assert.Equal(PrayerOutcome.Cancelled, outcome);
        Assert.Equal([8, 5, 13, 8], audio.Requests);
    }

    /// <summary>A member's prayer in a battle leaves the music alone (saint_pray tests DS:907A).</summary>
    [Fact]
    public void AMembersPrayerInABattleLeavesTheMusicAlone()
    {
        var audio = new MusicRecorder();
        var host = new StepHost(audio, "wait:2", "key:esc", "wait:5");
        if (Start(host) is not var (ui, state, panel, _))
        {
            return;
        }
        new SaintPrayerScreen(ui, panel) { InBattle = () => true }.Pray(state, 0, 0, PrayerMode.ForMember, SaintEffects.Instance);
        Assert.Empty(audio.Requests);
    }

    /// <summary>The music hot key turned off plays the stop (raw: the option is already off).</summary>
    [Fact]
    public void TurningMusicOffPlaysTheStop()
    {
        var audio = new MusicRecorder();
        var host = new StepHost(audio, "wait:200");
        if (Start(host) is not var (ui, state, _, _))
        {
            return;
        }
        var keys = new HotKeys(ui, state, ui.Options);
        keys.ToggleMusic();
        keys.ToggleMusic();
        Assert.Equal([1], audio.Requests);
    }
}
