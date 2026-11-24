using Darklands.Audio;
using Darklands.Audio.Packs;
using Darklands.Content;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Presentation.Graphics;
using Darklands.Rendering;

/// <summary>A host that replays a list of player actions and saves the screen on request.</summary>
internal sealed class ScriptedHost(List<string> steps, string output) : IHost, IVideo, IInput, IClock
{
    /// <summary>Runs a console:&lt;command&gt; step (the developer console's commands on the game being played).</summary>
    public Action<string>? ConsoleCommand { get; set; }

    /// <summary>Runs a state:&lt;name&gt; step (the game being played written as a saved game).</summary>
    public Action<string>? State { get; set; }

    /// <summary>The settings screen's view (null: the step settings does nothing): the program makes one over its game.</summary>
    public Func<Darklands.Presentation.Views.IRegistryView>? SettingsView { get; set; }

    private readonly Queue<string> _steps = new(steps);
    private readonly Queue<int> _keys = new();
    private readonly Queue<(int, bool)> _transitions = new();

    public bool TryReadKeyTransition(out int scanCode, out bool released)
    {
        if (_transitions.TryDequeue(out var t))
        {
            (scanCode, released) = t;
            return true;
        }
        (scanCode, released) = (0, false);
        return false;
    }
    /// <summary>The text font of --settings (the environment variable and local/paths.json come after it).</summary>
    public static string? SettingsTextFont { get; set; }

    /// <summary>The hdpack step's replacement graphics (null: none): each shot also saves the frame drawn with them.</summary>
    public static HdRenderer? HdRenderer { get; set; }

    /// <summary>The HD shots' size: this many times 320x200.</summary>
    public static int HdScale { get; set; } = 4;

    /// <summary>The audiopack step's replacement sound (null: none): the host's audio goes through its decorator.</summary>
    public static AudioPack? ReplacementSound { get; set; }

    private readonly TextFont _font = TextFont.FromEnvironment(SettingsTextFont ?? LocalPaths.Get("textfont", "DARKLANDS_TEXT_FONT"));
    private Screen? _last;
    private TextScreen? _lastText;
    private int _framesToWait;
    private bool _buttonHeldByScript;
    private long _clockMs;

    public int ShotsTaken { get; private set; }

    /// <summary>Resolves cbt:&lt;i&gt; points (the battle's combatants) for click, move, ldown and rdown.</summary>
    public Func<int, (int X, int Y)?>? Locate { get; set; }

    /// <summary>The dump step (the battle's combatants on the console).</summary>
    public Action? Dump { get; set; }

    /// <summary>
    /// Runs an inspect:x,y step on the last frame shown: the developer's inspector (frame, x, y, the number of steps taken before
    /// this one); null in a build without it (the build property DevTools).
    /// </summary>
    public Action<Screen, int, int, int>? Inspect { get; set; }

    private int _stepsTaken;

    // DARKLANDS_VIEW_AT (the true pause's proof, 1.2): before which steps an empty registry view opens and closes, inside the pump that
    //   takes the step (so no step comes later): step numbers counted from 0, comma-separated, or "half" (the step in the middle of the
    //   script); DARKLANDS_VIEW_FRAMES the frames it stays open (30). The game's clock and timer stand still meanwhile (the host's clock
    //   moves only when the game sleeps or pumps), and its frames are not the game's: nothing of the session may change.
    private readonly HashSet<int> _viewAt = ViewSteps(Environment.GetEnvironmentVariable("DARKLANDS_VIEW_AT"), steps.Count);
    private static readonly int ViewFrames = int.TryParse(Environment.GetEnvironmentVariable("DARKLANDS_VIEW_FRAMES"), out var frames) && frames > 0 ? frames : 30;

    private static HashSet<int> ViewSteps(string? setting, int count)
    {
        var at = new HashSet<int>();
        foreach (var word in (setting ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (word == "half")
            {
                at.Add(count / 2);
            }
            else if (int.TryParse(word, out var n))
            {
                at.Add(n);
            }
        }
        return at;
    }

    // an empty registry view opened over the last frame and closed after ViewFrames frames, the game's clock as it was
    private void OpenEmptyView()
    {
        var before = _clockMs;
        var view = new Darklands.Presentation.Views.EmptyView(ViewFrames);
        Darklands.Presentation.Views.ViewHost.Run(view, new HeadlessViewSurface(_last));
        Console.WriteLine($"view: opened before step {_stepsTaken} at {before} ms, shown {view.Shown} frames, closed at {_clockMs} ms");
    }

    // the step settings:<name>[:<keys>] (the 1.2 settings screen, for its shots and tests): the screen opens over the frame shown, inside the
    //   pump that takes the step; it takes the keys one a frame (up, down, left, right, enter, space, esc, f7, or a letter), then Esc; each
    //   frame it shows is saved as <name>-NN.png. The game's clock stands still meanwhile, as for any registry view.
    private void OpenSettings(string value)
    {
        if (SettingsView is not { } make)
        {
            Console.WriteLine("settings: no settings screen in this program");
            return;
        }
        var parts = value.Split(':', 2);
        var keys = parts.Length > 1 ? parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(ViewKey).ToList() : [];
        var before = _clockMs;
        var surface = new ShotViewSurface(_last, keys, parts[0], output);
        Darklands.Presentation.Views.ViewHost.Run(make(), surface);
        Console.WriteLine($"settings: {parts[0]}: {surface.Frames} frames shown, opened at {before} ms, closed at {_clockMs} ms");
    }

    private static int ViewKey(string name) => name.Trim().ToLowerInvariant() switch
    {
        "up" => Keys.Up,
        "down" => Keys.Down,
        "left" => Keys.Left,
        "right" => Keys.Right,
        "enter" => Keys.Enter,
        "space" => Keys.Space,
        "esc" => Keys.Escape,
        "f7" => Keys.F7,
        var s when s.Length == 1 => Keys.ForCharacter(s[0]),
        var s => throw new FormatException($"settings: no key '{s}'"),
    };

    // a registry view's surface for the settings step: the keys one a frame, then Esc; every frame shown saved as a picture
    private sealed class ShotViewSurface(Screen? under, IReadOnlyList<int> keys, string name, string folder) : Darklands.Presentation.Views.IViewSurface
    {
        private int _input;

        public Screen? Under => under;

        public bool QuitRequested => _input > keys.Count + 1;

        public int Frames { get; private set; }

        public IReadOnlyList<Darklands.Presentation.Views.ViewInput> Inputs()
        {
            // the first frame as it opens, then a key a frame, then Esc
            var n = _input++;
            return n == 0 ? [] : n <= keys.Count ? [new(keys[n - 1], default, false)] : [new(Keys.Escape, default, false)];
        }

        public void Show(Screen screen)
        {
            PngWriter.Write(screen, Path.Combine(folder, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{name}-{Frames:D2}.png")));
            Frames++;
        }

        public void WaitFrame()
        {
        }
    }

    // the headless host's surface for a registry view: no input, its frames not recorded, no time of the game's passing
    private sealed class HeadlessViewSurface(Screen? under) : Darklands.Presentation.Views.IViewSurface
    {
        public Screen? Under => under;

        public bool QuitRequested => false;

        public IReadOnlyList<Darklands.Presentation.Views.ViewInput> Inputs() => [];

        public void Show(Screen screen)
        {
        }

        public void WaitFrame()
        {
        }
    }

    /// <summary>Drops the next step (one the program itself has read).</summary>
    public void Skip() => Take();

    private string Take()
    {
        _stepsTaken++;
        return _steps.Dequeue();
    }


    public IVideo Video => this;
    public IInput Input => this;
    public IAudio Audio { get; set; } = ReplacementSound is { } pack
        ? new PackAudio(new SilentAudio(), null, new PackMixer(pack, HeadlessSound.Rate))
        : new SilentAudio();
    public IClock Clock => this;
    public IGameTimer Timer { get => _timer ??= new ClockGameTimer(this); set => _timer = value; }
    private IGameTimer? _timer;

    /// <summary>Called when the clock moves on (the sound renders up to the new time).</summary>
    public Action<long>? Advanced { get; set; }
    public bool QuitRequested { get; private set; }
    public MouseState Mouse { get; private set; } = new(160, 100, MouseButtons.None);
    public bool ShiftHeld { get; private set; }

    public bool CtrlHeld { get; private set; }

    public void MovePointer(int x, int y) => Mouse = Mouse with { X = x, Y = y };
    public long Milliseconds => _clockMs;

    public void Present(Screen screen)
    {
        _last = screen;
        _lastText = null;
        if (_framesToRecord > 0)
        {
            _framesToRecord--;
            _recordedCount++;
            Shot($"rec{_recordedCount:D5}");
        }
        if (_tailSize > 0)
        {
            _tail.Enqueue((_clockMs, (byte[])screen.Pixels.Clone(), (uint[])screen.Palette.Clone()));
            while (_tail.Count > _tailSize)
            {
                _tail.Dequeue();
            }
        }
    }

    private int _tailSize;
    private readonly Queue<(long Time, byte[] Pixels, uint[] Palette)> _tail = new();

    /// <summary>The tail step's frames (the last n presented), saved as tail00001.png … with the time of each (the end of a battle).</summary>
    public void SaveTail()
    {
        var k = 0;
        foreach (var (time, pixels, palette) in _tail)
        {
            var screen = new Screen();
            pixels.CopyTo(screen.Pixels, 0);
            palette.CopyTo(screen.Palette, 0);
            k++;
            PngWriter.Write(screen, Path.Combine(output, $"tail{k:D5}.png"));
            Console.WriteLine($"tail{k:D5} at {time} ms");
        }
        _tail.Clear();
    }

    private int _framesToRecord, _recordedCount;

    /// <summary>The rec step: saves the next n frames presented.</summary>
    public void Record(int frames) => _framesToRecord = frames;

    public void PresentText(TextScreen screen)
    {
        _lastText ??= new TextScreen();
        screen.CopyTo(_lastText);
    }

    /// <summary>Saves the last screen shown (the text screen at 720×400).</summary>
    public void Shot(string name)
    {
        if (_lastText is not null)
        {
            PngWriter.Write(_lastText, _font, _clockMs, Path.Combine(output, name + ".png"));
            ShotsTaken++;
        }
        else if (_last is not null)
        {
            PngWriter.Write(_last, Path.Combine(output, name + ".png"));
            WriteIndexed(_last, Path.Combine(output, name + ".idx"));
            if (HdRenderer is not null)
            {
                var (width, height) = (Screen.Width * HdScale, Screen.Height * HdScale);
                var frame = HdRenderer.Render(_last, new DisplayRectangle(0, 0, width, height));
                PngCodec.Write(FrameCompositor.Compose(frame, width, height), Path.Combine(output, name + ".hd.png"));
                // for pack makers: the assets seen, with their size, pixels shown and whether the pack replaces them
                File.WriteAllLines(Path.Combine(output, name + ".assets.txt"), _last.Owners.Where(id => id != 0).GroupBy(id => id)
                    .Select(g => (Name: ScreenOwners.NameOf(g.Key) ?? "?", Size: ScreenOwners.SizeOf(g.Key), Pixels: g.Count()))
                    .OrderBy(a => a.Name, StringComparer.Ordinal)
                    .Select(a => $"{a.Name}\t{a.Size.Width}x{a.Size.Height}\t{a.Pixels} px{(HdRenderer.Pack.Has(a.Name) ? "\treplaced" : "")}"));
            }
            ShotsTaken++;
        }
    }

    public void Sleep(int milliseconds)
    {
        _clockMs += Math.Max(1, milliseconds);
        Advanced?.Invoke(_clockMs);
    }

    public bool TryReadKey(out int key)
    {
        if (!_keys.TryDequeue(out key))
        {
            return false;
        }
        if (_breakKey != Keys.None && key == _breakKey)
        {
            // the kbreak's key, looked at (GameUi.KeyWaiting) or read: the Ctrl-Break comes just before the game's read of it (GameUi.TakeKey)
            _breakKey = Keys.None;
            _keys.Clear();
            _breakArmed = true;
            Console.WriteLine($"kbreak after the key at {_clockMs} ms");
        }
        return true;
    }

    // kbreak:<n>[:<key>]: Ctrl-Break n ticks of the 60 Hz timer later on the host's clock (tools/oracle/ctrl_break.py), as tkey's keys: the keys
    // waiting are gone and the game's next look or read finds the word 0000h (IInput.TakeCtrlBreak, GameUi). With a key, the key is pressed
    // then and the Ctrl-Break comes just before the game's read of it (the read takes 0000h); no step after it is taken before
    private long _breakAt;
    private string? _breakStep;
    private int _breakKey;
    private bool _breakArmed;

    /// <summary>The next step counts timer ticks from now (tkey, tclick, kbreak).</summary>
    private bool NextCountsTicks() => _steps.TryPeek(out var next) && (next.StartsWith("tkey:", StringComparison.Ordinal) || next.StartsWith("tclick:", StringComparison.Ordinal) || next.StartsWith("kbreak:", StringComparison.Ordinal));

    public bool TakeCtrlBreak()
    {
        if (!_breakArmed)
        {
            return false;
        }
        _breakArmed = false;
        return true;
    }

    private int _clickPumps;

    // mapclick:<x>,<y>,<n>: the left button stays down until n passes of the map's loop have read it (OverlandScreen.PassHook), as the
    // original's click spanned n map passes (tools/oracle/lockstep.py map_holds); let go too when the map is left
    private int _mapHold;

    // a click into the map's view (where a pass steers toward it) made while the map runs stays down until a pass has read it: a pass
    // lasts one BIOS tick (GameUi.DelayClock), two to four pumps, so two pumps could fall between two passes' looks at the button (at most
    // six pumps: a box the map opens meanwhile sees it let go)
    private bool _onMap;
    private bool _clickRead;

    /// <summary>OverlandScreen.PassHook: a map pass read the button (true) or the map was left (false).</summary>
    public void MapPass(bool read)
    {
        _onMap = read;
        _clickRead |= read && Mouse.Left;
        if (_mapHold > 0 && (!read || --_mapHold == 0))
        {
            _mapHold = 0;
            Mouse = Mouse with { Buttons = MouseButtons.None };
        }
    }

    // tkey:<n>:<key>[,<key>...]: the keys are pressed n ticks of the game's 60 Hz timer later on the host's clock (tools/oracle/tick_keys.py: the
    // original's timer callback n more times, six in seven frames of 70.086 Hz), all at once, whatever the game is doing; no step after it is
    // taken before. The clock, not IGameTimer.Ticks: the headless host's timer follows its sound and lags its clock
    private long _tickKeysAt;
    private int[]? _tickKeys;

    // tclick:<n>:<x>,<y>,<ms>: the left button pressed at (x, y) n ticks of the 60 Hz timer later on the host's clock, as tkey's keys
    // (tools/oracle/tick_mouse.py), and held <ms> of the clock (the original's hold, from its log: let go CLICK_MS after the game read it);
    // no step is taken before it is let go. A press inside the map's rest (OverlandScreen.RestLoop's button test)
    private long _tickClickAt;
    private (int X, int Y, int Hold)? _tickClick;
    private long _tickClickUntil = -1;

    // still:<n>: the next step comes at the battle pass that is the n-th at one combat clock value (counted from the head of each pass,
    // a new battle starting again), as tools/oracle/orig_side.py gives the key of a battle's start pause (its BATTLE_PAUSE_PASSES); the
    // frames before the battle (a load, the set-up) do not count, which a wait in frames cannot follow. At most 20000 frames.
    private int _stillTarget, _stillPasses, _stillClock = -1, _stillFrames;

    /// <summary>BattleScreen.PassHook: a pass at combat clock <paramref name="clock"/> (-1: the battle's loop ended).</summary>
    public void BattlePass(int clock)
    {
        _stillPasses = clock >= 0 && clock == _stillClock ? _stillPasses + 1 : clock >= 0 ? 1 : 0;
        _stillClock = clock;
    }

    public void Pump()
    {
        _clockMs += 16;
        Advanced?.Invoke(_clockMs);
        if (_tickClickUntil >= 0)
        {
            if (_clockMs < _tickClickUntil)
            {
                return;
            }
            _tickClickUntil = -1;
            Mouse = Mouse with { Buttons = MouseButtons.None };
            Console.WriteLine($"tclick let go at {_clockMs} ms");
            _framesToWait = 2;
            return;
        }
        if (_tickClick is { } click)
        {
            if (_clockMs < _tickClickAt)
            {
                return;
            }
            Mouse = new MouseState(click.X, click.Y, MouseButtons.Left);
            _tickClickUntil = _clockMs + Math.Max(1, click.Hold);
            _tickClick = null;
            Console.WriteLine($"tclick {click.X},{click.Y} at {_clockMs} ms");
            return;
        }
        if (Mouse.Buttons != MouseButtons.None && !_buttonHeldByScript && _mapHold == 0)
        {
            // a click stays down over two pumps (a screen may present twice a frame before it polls)
            if (++_clickPumps < 2 || _onMap && Mouse.Buttons == MouseButtons.Left && Mouse.X > 60 && Mouse.Y >= 16 && !_clickRead && _clickPumps < 6)
            {
                return;
            }
            _clickPumps = 0;
            _clickRead = false;
            Mouse = Mouse with { Buttons = MouseButtons.None };
            return;
        }
        if (_tickKeys is not null)
        {
            if (_clockMs < _tickKeysAt)
            {
                return;
            }
            foreach (var key in _tickKeys)
            {
                _keys.Enqueue(key);
            }
            Console.WriteLine($"tkey {_tickKeys.Length} key(s) at {_clockMs} ms");
            _tickKeys = null;
            // a tkey, tclick or kbreak right after it counts its ticks from these keys (the original's from the read that takes them)
            _framesToWait = NextCountsTicks() ? 0 : 2;
            return;
        }
        if (_breakStep is not null)
        {
            if (_clockMs < _breakAt)
            {
                return;
            }
            if (_breakStep.Length > 0)
            {
                _breakKey = KeyFor(_breakStep);
                _keys.Enqueue(_breakKey);
                Console.WriteLine($"kbreak {_breakStep} pressed at {_clockMs} ms");
            }
            else
            {
                _keys.Clear();
                _breakArmed = true;
                Console.WriteLine($"kbreak at {_clockMs} ms");
            }
            _breakStep = null;
            _framesToWait = NextCountsTicks() ? 0 : 2;
            return;
        }
        if (_framesToWait > 0)
        {
            _framesToWait--;
            return;
        }
        if (_stillTarget > 0)
        {
            if (_stillPasses < _stillTarget && ++_stillFrames < 20000)
            {
                return;
            }
            _stillTarget = 0;
        }
        if (_viewAt.Remove(_stepsTaken))
        {
            OpenEmptyView();
        }
        if (!_steps.TryDequeue(out var step))
        {
            QuitRequested = true;
            return;
        }
        _stepsTaken++;
        var (kind, value) = (step.Split(':', 2)[0], step.Contains(':') ? step.Split(':', 2)[1] : "");
        switch (kind)
        {
            case "key":
                _keys.Enqueue(KeyFor(value));
                // the battle takes it in this pass: the count of still: starts again (orig_side.py: a key ends the battle's pause)
                _stillPasses = 0;
                // a tkey or tclick after it counts its ticks from this key (the original's from the read that takes it): taken at the next pump
                _framesToWait = NextCountsTicks() ? 0 : 2;
                break;
            case "click":
                var (x, y) = Point(value);
                Mouse = new MouseState(x, y, MouseButtons.Left);
                _clickRead = false;
                _framesToWait = 2;
                break;
            case "mapclick":
                var at = value.Split(',');
                Mouse = new MouseState(int.Parse(at[0], System.Globalization.CultureInfo.InvariantCulture), int.Parse(at[1], System.Globalization.CultureInfo.InvariantCulture), MouseButtons.Left);
                _mapHold = int.Parse(at[2], System.Globalization.CultureInfo.InvariantCulture);
                _framesToWait = 2;
                break;
            case "rclick":
                // the right button pressed for two pumps as click does the left (tools/oracle/orig_side.py: the driver's right-button bit)
                var (rx, ry) = Point(value);
                Mouse = new MouseState(rx, ry, MouseButtons.Right);
                _framesToWait = 2;
                break;
            case "move":
                var (mx, my) = Point(value);
                Mouse = Mouse with { X = mx, Y = my };
                break;
            case "rdown":
            case "ldown":
                // a button held down (the menu bar with the right one, the hover cursor with the left) until rup/lup
                var (hx, hy) = Point(value);
                _buttonHeldByScript = true;
                Mouse = new MouseState(hx, hy, kind == "rdown" ? MouseButtons.Right : MouseButtons.Left);
                _framesToWait = 2;
                break;
            case "rup":
            case "lup":
                _buttonHeldByScript = false;
                Mouse = Mouse with { Buttons = MouseButtons.None };
                _framesToWait = 2;
                break;
            case "shift":
                ShiftHeld = value == "on";
                break;
            case "ctrl":
                CtrlHeld = value == "on";
                break;
            case "tkey":
                var (ticks, names) = (value.Split(':', 2)[0], value.Split(':', 2)[1]);
                _tickKeysAt = _clockMs + (long)Math.Round(int.Parse(ticks, System.Globalization.CultureInfo.InvariantCulture) * 1000.0 / 60);
                _tickKeys = names.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(KeyFor).ToArray();
                break;
            case "kbreak":
                var (breakTicks, breakKey) = (value.Split(':', 2)[0], value.Contains(':') ? value.Split(':', 2)[1] : "");
                _breakAt = _clockMs + (long)Math.Round(int.Parse(breakTicks, System.Globalization.CultureInfo.InvariantCulture) * 1000.0 / 60);
                _breakStep = breakKey;
                break;
            case "tclick":
                var (clickTicks, clickAt) = (value.Split(':', 2)[0], value.Split(':', 2)[1].Split(','));
                _tickClickAt = _clockMs + (long)Math.Round(int.Parse(clickTicks, System.Globalization.CultureInfo.InvariantCulture) * 1000.0 / 60);
                _tickClick = (int.Parse(clickAt[0], System.Globalization.CultureInfo.InvariantCulture), int.Parse(clickAt[1], System.Globalization.CultureInfo.InvariantCulture),
                    clickAt.Length > 2 ? int.Parse(clickAt[2], System.Globalization.CultureInfo.InvariantCulture) : 300);
                break;
            case "kdown":
            case "kup":
                // a key's make or break code (hex, set 1) for the game's keyboard interrupt (kdown:48 holds Up)
                _transitions.Enqueue((Convert.ToInt32(value, 16), kind == "kup"));
                break;
            case "wait":
                _framesToWait = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                break;
            case "still":
                _stillTarget = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                _stillFrames = 0;
                break;
            case "untilover":
                // the battle runs on until it ends (the screen returns)
                _framesToWait = int.MaxValue;
                break;
            case "shot":
                Shot(value);
                break;
            case "settings":
                OpenSettings(value);
                break;
            case "console":
                ConsoleCommand?.Invoke(value);
                break;
            case "state":
                State?.Invoke(value);
                break;
            case "option":
                // the pointer onto line n of the card menu being chosen from (tools/oracle/lockstep.py does the same in the original)
                if (CardScreen.OptionPoint(int.Parse(value, System.Globalization.CultureInfo.InvariantCulture)) is { } point)
                {
                    Mouse = Mouse with { X = point.X, Y = point.Y };
                }
                else
                {
                    Console.WriteLine($"option {value}: no menu");
                }
                break;
            case "tail":
                _tailSize = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                break;
            case "rec":
                Record(int.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case "dump":
                Dump?.Invoke();
                break;
            case "inspect":
                // what drew a pixel of the last frame (DevTools builds only); it reads the frame and changes nothing
                var (ix, iy) = Point(value);
                if (Inspect is null)
                {
                    Console.WriteLine("inspect: not in this build (DevTools is false)");
                }
                else if (_last is null)
                {
                    Console.WriteLine("inspect: no frame shown yet");
                }
                else
                {
                    Inspect(_last, ix, iy, _stepsTaken - 1);
                }
                break;
        }
    }

    /// <summary>The frame as palette indices (64000 bytes) followed by the 256 colours as R, G, B (pixel comparisons).</summary>
    private static void WriteIndexed(Screen screen, string path)
    {
        var bytes = new byte[Screen.Width * Screen.Height + 768];
        screen.Pixels.CopyTo(bytes, 0);
        for (var i = 0; i < 256; i++)
        {
            bytes[64000 + i * 3] = (byte)(screen.Palette[i] >> 16);
            bytes[64001 + i * 3] = (byte)(screen.Palette[i] >> 8);
            bytes[64002 + i * 3] = (byte)screen.Palette[i];
        }
        File.WriteAllBytes(path, bytes);
    }

    private (int, int) Point(string value)
    {
        if (value.StartsWith("cbt:", StringComparison.Ordinal))
        {
            var spot = Locate?.Invoke(int.Parse(value[4..], System.Globalization.CultureInfo.InvariantCulture));
            return spot ?? (0, 0);
        }
        var parts = value.Split(',');
        return (int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
    }

    private static int KeyFor(string name) => name.ToLowerInvariant() switch
    {
        "enter" => Keys.Enter,
        "space" => Keys.Space,
        "esc" => Keys.Escape,
        "up" => Keys.Up,
        "down" => Keys.Down,
        "left" => Keys.Left,
        "right" => Keys.Right,
        "home" => Keys.Home,
        "end" => Keys.End,
        "pgup" => Keys.PageUp,
        "pgdn" => Keys.PageDown,
        "center" => Keys.Center,
        "tab" => Keys.Tab,
        "backspace" => Keys.Backspace,
        "f1" => Keys.F1,
        "f2" => Keys.F2,
        "f3" => Keys.F3,
        "f4" => Keys.F4,
        "f5" => Keys.F5,
        "f6" => Keys.F6,
        "altq" => Keys.AltQ,
        "alts" => Keys.AltS,
        "altl" => Keys.AltL,
        "altp" => Keys.AltP,
        "altd" => Keys.AltD,
        "altc" => Keys.AltC,
        "alto" => Keys.AltO,
        "altm" => Keys.AltM,
        "altf" => Keys.AltF,
        "f7" => Keys.F7,
        "f8" => Keys.F8,
        "f9" => Keys.F9,
        "f10" => Keys.F10,
        // Ctrl-F1..F10 (scan 0x5E..0x67) and the keypad with Num Lock on (scan 0x47..0x53 with its character), as the tester's key names
        var k when k.StartsWith("ctrlf", StringComparison.Ordinal) && int.TryParse(k[5..], out var n) && n is >= 1 and <= 10 => (0x5D + n) << 8,
        "kp7" => 0x4737,
        "kp8" => 0x4838,
        "kp9" => 0x4939,
        "kp4" => 0x4B34,
        "kp5" => Keys.Center,
        "kp6" => 0x4D36,
        "kp1" => 0x4F31,
        "kp2" => 0x5032,
        "kp3" => 0x5133,
        "kp0" => 0x5230,
        "kpminus" => 0x4A2D,
        "kpplus" => 0x4E2B,
        "kpdot" => 0x532E,
        _ => Keys.ForCharacter(name[0]),
    };
}
