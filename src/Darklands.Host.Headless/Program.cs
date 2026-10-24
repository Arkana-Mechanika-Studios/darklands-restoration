using Darklands.Audio;
using Darklands.Content;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Presentation.Graphics;
using Darklands.Rendering;

// Plays a scripted session without a window and saves screenshots.
//   Darklands.Host.Headless [--settings <file>] <game folder> <output folder> <step> [<step> ...]
// --settings <file>: a darklands.settings.json (docs/SETTINGS.md), the only settings source here (a scripted run does not read
//   a file found by chance): game folder (write - for <game folder>), saves folder, sound card, configDrk, mt32Roms, text font and
//   the startup keys; the steps win over it.
//
// Input and output steps:
//   key:<c>                      a character; key:enter|space|esc|tab|backspace|up|down|left|right|home|end|pgup|pgdn|f1..f6|f10|alto (Alt-O, the marching order)
//   click:<x>,<y>                a left click; rclick:<x>,<y> a right click; move:<x>,<y> moves the pointer (and drags a held button)
//   ldown:<x>,<y> / lup          the left button held and let go; rdown:<x>,<y> / rup the right one (the menu bar)
//   shift:on|off, ctrl:on|off    the modifier keys; kdown:<hex> / kup:<hex> a key's make or break scan code
//   wait:<frames>                lets frames pass; shot:<name> saves <name>.png; rec:<n> saves the next n frames
//   seed:<n>                     the game's random seed
//   clock:<n>,<n>,...            the clock's seeds in turn instead (srand((unsigned)time(0)) at the start and wherever the game
//                                reseeds: tools/oracle/lockstep.py gives the original's); the last one stays
//   hdpack:<folder>              replacement graphics (an HD pack, docs/SETTINGS.md display.hdPack; also from --settings): each
//                                shot also saves <name>.hd.png, the frame with the replacements at hdscale:<n> (default 4) x 320x200
//
// Game steps:
//   saves:<folder>               the saved games' folder (default: SAVES beside the program, never the game folder)
//   banner, intro                run the program start (the BANNER.DAT banner and its checks) and the opening
//   arg:<text>                   adds a word to the game's DOS command line (-Q, -O, -A<letter>)
//   sound:config|adlib|sb|sbpro2|mt32|cm32l   runs the game's sound drivers in step with the script's clock (the game timer is
//                                then the sound's, as in the SDL host); mt32roms:<folder> (default MT32_ROMS next to the program)
//   wav:<file>                   saves what the drivers play; trace:<file> writes the ticks, tunes, MIDI bytes, sample
//                                transfers and cels shown (tools/intro/opening_timer.py compares the opening with the original's)
//
// Debug steps:
//   card:<hex>                   starts a Quickstart game at that card (key:q still chooses Quickstart)
//   option:<n>                   the pointer onto line n of the card menu being chosen from (then key:enter chooses it)
//   state:<name>                 writes the game being played as a saved game, <name>.sav in the output folder (the comment
//                                "Darklands"; tools/oracle/lockstep.py compares it with the original's at the same point)
//   load:<file>                  starts with that saved game of the saves folder; a game saved in a battle resumes it
//   console:<command>            a developer console command on the game being played ("console:money add 10" as one argument)
//   screen:barter|store|loot     opens that screen on a Quickstart game instead of playing
//   saint:<n>[,<member>[,party]] (first step) opens the prayer screen for that saint over the Quickstart party
//   battle:<battlefield>,<seed>[,<type>,<quality>,<count> ...]   a Quickstart party goes straight into that battle, paused in
//                                the leader's order mode (key:space resumes it); with it:
//     untilover                  runs the battle to its end and saves battle-end.png
//     tail:<n>                   saves the battle's last n frames
//     give:<code>,<quality>,<count>   gives every member that stack first
//     picture:<name>             draws that picture (and its colours) before the battle starts
//     click:cbt:<i>              points at combatant i's hot spot (also move, ldown, rdown); dump prints the combatants
//     persistent                 fights it as the main loop does (combat flag 1); with saves:<folder> Alt-S saves it there
//   A won battle opens the game's loot screen.
//
// The text screen is drawn with the 8x16 font named by DARKLANDS_TEXT_FONT (a 4096-byte BIOS font dump or its base64), else the
// built-in one.

var argList = args.ToList();
GameSettings fileSettings = new();
var settingsIndex = argList.IndexOf("--settings");
if (settingsIndex >= 0 && settingsIndex + 1 < argList.Count)
{
    var load = SettingsFile.Load(argList[settingsIndex + 1]);
    load.Problems.ToList().ForEach(Console.Error.WriteLine);
    fileSettings = load.Settings;
    argList.RemoveRange(settingsIndex, 2);
}
if (argList.Count < 3)
{
    Console.Error.WriteLine("usage: Darklands.Host.Headless [--settings <file>] <game folder> <output folder> <steps...>");
    return 1;
}
var gameFolder = argList[0] == "-" ? null : argList[0];
ResolvedSettings resolved;
try
{
    resolved = ResolvedSettings.Resolve(new CommandLineOptions { GameFolder = gameFolder }, Environment.GetEnvironmentVariable, fileSettings);
}
catch (Exception e) when (e is FormatException or ArgumentException or OverflowException)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}
gameFolder = resolved.GameFolder;
if (gameFolder is null)
{
    Console.Error.WriteLine("no game folder: give it as the first argument (or - with a settings file's gameFolder, or DARKLANDS_DIR).");
    return 1;
}
ScriptedHost.SettingsTextFont = resolved.TextFont;
GameFiles OpenGame()
{
    var opened = new GameFiles(resolved.GameFolder!);
    if (resolved.ConfigDrk is { } config)
    {
        opened.OverrideConfig(config.Card, config.Port, config.Irq, config.Dma);
    }
    return opened;
}
var output = Directory.CreateDirectory(argList[1]).FullName;
var steps = argList.Skip(2).ToList();
uint seed = 1;
var seedStep = steps.FirstOrDefault(s => s.StartsWith("seed:", StringComparison.Ordinal));
if (seedStep is not null)
{
    seed = uint.Parse(seedStep[5..], System.Globalization.CultureInfo.InvariantCulture);
    steps.Remove(seedStep);
}
// clock:<n>,<n>,... - the seeds the clock gives in turn (each reseed takes the next; the last stays).
var clockStep = steps.FirstOrDefault(s => s.StartsWith("clock:", StringComparison.Ordinal));
var clockSeeds = new Queue<uint>();
if (clockStep is not null)
{
    steps.Remove(clockStep);
    foreach (var v in clockStep[6..].Split(','))
    {
        clockSeeds.Enqueue(uint.Parse(v, System.Globalization.CultureInfo.InvariantCulture));
    }
    seed = clockSeeds.Peek();
}
uint NextSeed()
{
    var value = clockSeeds.Count > 1 ? clockSeeds.Dequeue() : clockSeeds.Count == 1 ? clockSeeds.Peek() : seed;
    Console.WriteLine($"clock seed {value}");
    return value;
}
// saves:<folder> - the folder of the saved games (default for scripted runs: SAVES beside the program, never the game folder).
var savesStep = steps.FirstOrDefault(s => s.StartsWith("saves:", StringComparison.Ordinal));
if (savesStep is not null)
{
    steps.Remove(savesStep);
}
string? TakeStep(string prefix)
{
    var step = steps.FirstOrDefault(s => s.StartsWith(prefix, StringComparison.Ordinal));
    if (step is not null)
    {
        steps.Remove(step);
    }
    return step?[prefix.Length..];
}
var soundStep = TakeStep("sound:") ?? (fileSettings.SoundCard is { } card and not "none" ? card : null);
var wavStep = TakeStep("wav:");
var traceStep = TakeStep("trace:");
var romsStep = TakeStep("mt32roms:") ?? resolved.Mt32Roms;
var hdPackStep = TakeStep("hdpack:") ?? fileSettings.HdPack;
var hdScaleStep = TakeStep("hdscale:");
if (hdPackStep is not null)
{
    var pack = HdPack.Load(hdPackStep);
    pack.Problems.ToList().ForEach(Console.Error.WriteLine);
    ScriptedHost.HdRenderer = new HdRenderer(pack);
    ScriptedHost.HdScale = hdScaleStep is null ? 4 : int.Parse(hdScaleStep, System.Globalization.CultureInfo.InvariantCulture);
}
var host = new ScriptedHost(steps, output);
var battleStep = steps.FirstOrDefault(s => s.StartsWith("battle:", StringComparison.Ordinal));
if (battleStep is not null)
{
    steps.Remove(battleStep);
    var gives = steps.Where(s => s.StartsWith("give:", StringComparison.Ordinal)).ToList();
    steps.RemoveAll(gives.Contains);
    // persistent: the battle is fought as the main loop fights a card's battle (combat flag 1: the battlefield persists, the game can be saved)
    var persistent = steps.Remove("persistent");
    var pictureStep = steps.FirstOrDefault(s => s.StartsWith("picture:", StringComparison.Ordinal));
    steps.Remove(pictureStep!);
    host = new ScriptedHost(steps, output);
    var numbers = battleStep[7..].Split(',').Select(v => int.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
    var files = OpenGame();
    var ui = new GameUi(host, new GameAssets(files));
    var state = Darklands.Simulation.NewGame.Quickstart(WorldData.Load(files), seed, () => seed);
    var groups = new List<(int, int, int)>();
    for (var k = 2; k + 2 < numbers.Length; k += 3)
    {
        groups.Add((numbers[k], numbers[k + 1], numbers[k + 2]));
    }
    while (groups.Count < 3)
    {
        groups.Add((-1, 0, 0));
    }
    var world = WorldData.Load(files);
    state.Data = world;
    // give:<code>,<quality>,<count> (debug): every member gets that stack before the battle (potions to throw …).
    foreach (var give in gives)
    {
        var g = give[5..].Split(',').Select(v => int.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var item = state.Item(g[0]);
        foreach (var m in state.Party.Members)
        {
            m.Pack.Add(new Darklands.Core.Characters.ItemStack(g[0], (byte)item.Type, g[1], g[2], (byte)item.Weight));
        }
    }
    var panel = new PartyPanel(ui.Assets);
    var sheets = new GameScreens2(ui, panel, state, world, new NoGameScreens());
    // the loot screen of a won battle is the game's (Esc or L leaves it)
    var lootScreens = new GameScreens(ui, panel, state, new NoGameScreens());
    var screens = new BattleScreens(ui, () => state, (items, money) => { Console.WriteLine($"loot: {items.Count} items"); lootScreens.Loot(items, money); }) { CharacterSheet = sheets.CharacterSheet, PartyInfo = sheets.PartyInfo };
    // with saves:<folder>, Alt-S in the battle saves the game there (as the game does inside a battle)
    if (savesStep is not null)
    {
        var folder = new Darklands.Persistence.SaveFolder(savesStep[6..]);
        screens.Save = tb => Console.WriteLine($"saved {new SaveGameDialog(ui, folder).Run(state, tb)}");
    }
    // picture:<name> (debug): the screen before the battle shows that picture in its colours, as a card's would
    if (pictureStep is not null)
    {
        ui.Canvas.DrawPicture(ui.Assets.Picture(pictureStep[8..]));
        ui.Present();
    }
    // cbt:<i> in click/move/ldown/rdown: the centre of combatant i's hot spot; dump prints the combatants.
    host.Locate = who => screens.Current?.HotSpotCentre(who);
    host.Dump = () =>
    {
        if (screens.Current?.Tactics is { } tb)
        {
            for (var i = 0; i < Darklands.Core.Battles.Combatant.Count; i++)
            {
                var c = tb.Battle[i];
                if (c.Status != Darklands.Core.Characters.CharacterStatus.Empty)
                {
                    Console.WriteLine($"cbt {i}: {c.Status} at ({c.Position.X},{c.Position.Y}) floor {c.Floor} action {(int)c.Action} target {c.Target} hot spot {screens.Current.HotSpot(i)}");
                }
            }
            Console.WriteLine($"selected {tb.Selected} order {tb.OrderAction} cursor {tb.Cursor} target {tb.CursorTarget} point ({tb.CursorPoint.X},{tb.CursorPoint.Y}) camera ({screens.Current.CameraX},{screens.Current.CameraY})");
        }
    };
    try
    {
        var result = screens.Battle(new Darklands.Cards.BattleRequest(numbers[0], numbers.Length > 1 ? numbers[1] : 1, groups, Persistent: persistent));
        Console.WriteLine($"battle result {result}, card {screens.LastResultCard}");
        host.Shot("battle-end");
        host.SaveTail();
    }
    catch (QuitGameException)
    {
        Console.WriteLine("battle left unfinished");
        host.SaveTail();
    }
    Console.WriteLine($"{host.ShotsTaken} screenshot(s) in {output}");
    return 0;
}
if (steps.Remove("chargen"))
{
    // Debug entry: the "Create characters" menu alone; prints the party it starts.
    var files = OpenGame();
    var ui = new GameUi(host, new GameAssets(files));
    try
    {
        var started = new CharacterCreationScreen(ui, WorldData.Load(files), () => seed).Run();
        Console.WriteLine(started is null
            ? "returned to the start screen"
            : $"city {started.CurrentLocation}, card 0x{started.CurrentCard:X2}: " + string.Join("; ", started.Party.Members.Select(m =>
                $"{m.Name} ({m.ShortName}) {(m.IsFemale ? "F" : "M")} {m.Age} attrs {string.Join(",", m.Attributes)} skills {string.Join(",", m.Skills)} " +
                $"items {string.Join(",", m.Pack.Select(p => $"{p.Code}x{p.Count}"))} saints {string.Join(",", m.Saints)} formulas {string.Join(",", m.Formulas)}")));
    }
    catch (QuitGameException)
    {
        Console.WriteLine("script ended");
    }
    Console.WriteLine($"{host.ShotsTaken} screenshot(s) in {output}");
    return 0;
}
var screenStep = steps.FirstOrDefault(s => s.StartsWith("screen:", StringComparison.Ordinal));
if (screenStep is not null)
{
    // Debug entry: opens one full screen on the Quickstart party
    // (screen:sheet|party|camp<mode>|alchemy<member>|barter|store|loot).
    steps.Remove(screenStep);
    host = new ScriptedHost(steps, output);
    var which = screenStep[7..];
    if (which is "barter" or "store" or "loot")
    {
        ScreenPreview.Run(host, OpenGame(), which, seed);
    }
    else
    {
        DebugScreens.Run(host, OpenGame(), which, seed);
    }
    Console.WriteLine($"{host.ShotsTaken} screenshot(s) in {output}");
    return 0;
}
if (steps.Count > 0 && steps[0].StartsWith("saint:", StringComparison.Ordinal))
{
    var spec = steps[0][6..].Split(',');
    host.Skip();
    var files = OpenGame();
    var world = WorldData.Load(files);
    var ui = new GameUi(host, new GameAssets(files));
    var state = Darklands.Simulation.NewGame.Quickstart(world, seed);
    var panel = new PartyPanel(ui.Assets);
    ui.Canvas.Fill(0, 0, Canvas.Width, Canvas.Height, Palette.Blank);
    panel.Draw(ui.Canvas, state.Party);
    var mode = spec.Length > 2 ? Darklands.Core.Rules.PrayerMode.ForParty : Darklands.Core.Rules.PrayerMode.ForMember;
    try
    {
        var outcome = new SaintPrayerScreen(ui, panel).Pray(state, spec.Length > 1 ? int.Parse(spec[1], System.Globalization.CultureInfo.InvariantCulture) : 0,
            int.Parse(spec[0], System.Globalization.CultureInfo.InvariantCulture), mode, Darklands.Core.Rules.SaintEffects.Instance);
        Console.WriteLine($"prayer: {outcome}");
        ui.Present();
        while (true)
        {
            ui.WaitForAction();
        }
    }
    catch (QuitGameException)
    {
    }
    Console.WriteLine($"{host.ShotsTaken} screenshot(s) in {output}");
    return 0;
}
// load:<file> (debug) starts the game with that saved game of the saves folder (a game saved in a battle resumes the battle).
var loadStep = steps.FirstOrDefault(s => s.StartsWith("load:", StringComparison.Ordinal));
if (loadStep is not null)
{
    steps.Remove(loadStep);
    host = new ScriptedHost(steps, output);
}
var cardStep = steps.FirstOrDefault(s => s.StartsWith("card:", StringComparison.Ordinal));
if (cardStep is not null)
{
    steps.Remove(cardStep);
    host = new ScriptedHost(steps, output);
}
List<string> commandLine = [.. resolved.CommandLine, .. TakeArguments(steps)];
using var sound = soundStep is null ? null : HeadlessSound.Attach(host, OpenGame(), soundStep, romsStep, wavStep, traceStep, commandLine);
var game = new DarklandsGame(host, OpenGame())
{
    Seed = clockStep is null ? () => seed : NextSeed,
    PlayOpening = steps.Remove("intro") || fileSettings.Intro == true,
    ShowStartUp = steps.Remove("banner") || fileSettings.Banner == true,
    CommandLine = commandLine,
    QuickstartCard = cardStep is null ? null : Convert.ToInt32(cardStep[5..], 16),
    StartWithSave = loadStep?[5..],
};
if (savesStep is not null)
{
    game.SaveDirectory = savesStep[6..];
}
else if (resolved.SavesFolder is not null)
{
    game.SaveDirectory = resolved.SavesFolder;
}
else
{
    // Scripted runs never write into the game folder: their saves go beside the program unless a folder is given.
    game.SaveDirectory = Path.Combine(AppContext.BaseDirectory, "SAVES");
}
if (sound?.Trace is { } traceWriter)
{
    game.Ui.Trace = traceWriter.WriteLine;
}
var developerConsole = new Darklands.DevConsole.GameConsole();
Darklands.DevConsole.GameCommands.Register(developerConsole, () => game.Playing);
host.State = name =>
{
    if (game.Playing is { } playing)
    {
        var (sav, _) = Darklands.Persistence.OriginalSaves.Write(playing, "Darklands", game.Options.Music, game.Options.SoundEffects);
        File.WriteAllBytes(Path.Combine(output, name + ".sav"), sav);
        var random = playing.Random is Darklands.Core.Randomness.MscRandom msc ? msc.State.ToString("X8", System.Globalization.CultureInfo.InvariantCulture) : "?";
        Console.WriteLine($"state {name}: card 0x{playing.CurrentCard:X2} random {random}");
    }
    else
    {
        Console.WriteLine($"state {name}: no game");
    }
};
host.ConsoleCommand = line =>
{
    foreach (var text in developerConsole.Execute(line).Lines)
    {
        Console.WriteLine("console: " + text);
    }
};
game.Run();
Console.WriteLine($"{host.ShotsTaken} screenshot(s) in {output}");
return 0;

static List<string> TakeArguments(List<string> steps)
{
    var words = steps.Where(s => s.StartsWith("arg:", StringComparison.Ordinal)).ToList();
    steps.RemoveAll(words.Contains);
    return [.. words.Select(w => w[4..])];
}

/// <summary>A host that replays a list of player actions and saves the screen on request.</summary>
internal sealed class ScriptedHost(List<string> steps, string output) : IHost, IVideo, IInput, IClock
{
    /// <summary>Runs a console:&lt;command&gt; step (the developer console's commands on the game being played).</summary>
    public Action<string>? ConsoleCommand { get; set; }

    /// <summary>Runs a state:&lt;name&gt; step (the game being played written as a saved game).</summary>
    public Action<string>? State { get; set; }

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

    private readonly TextFont _font = TextFont.FromEnvironment(SettingsTextFont ?? LocalPaths.Get("textfont", "DARKLANDS_TEXT_FONT"));
    private Screen? _last;
    private TextScreen? _lastText;
    private int _idleFrames;
    private bool _held;
    private long _now;

    public int ShotsTaken { get; private set; }

    /// <summary>Resolves cbt:&lt;i&gt; points (the battle's combatants) for click, move, ldown and rdown.</summary>
    public Func<int, (int X, int Y)?>? Locate { get; set; }

    /// <summary>The dump step (the battle's combatants on the console).</summary>
    public Action? Dump { get; set; }

    /// <summary>Drops the next step (one the program itself has read).</summary>
    public void Skip() => _steps.Dequeue();


    public IVideo Video => this;
    public IInput Input => this;
    public IAudio Audio { get; set; } = new SilentAudio();
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
    public long Milliseconds => _now;

    public void Present(Screen screen)
    {
        _last = screen;
        _lastText = null;
        if (_record > 0)
        {
            _record--;
            _recorded++;
            Shot($"rec{_recorded:D5}");
        }
        if (_tailSize > 0)
        {
            _tail.Enqueue((_now, (byte[])screen.Pixels.Clone(), (uint[])screen.Palette.Clone()));
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

    private int _record, _recorded;

    /// <summary>The rec step (and DARKLANDS_RECORD_FROM, a frame count after which every frame is saved): saves the next n frames presented.</summary>
    public void Record(int frames) => _record = frames;

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
            PngWriter.Write(_lastText, _font, _now, Path.Combine(output, name + ".png"));
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
            }
            ShotsTaken++;
        }
    }

    public void Sleep(int milliseconds)
    {
        _now += Math.Max(1, milliseconds);
        Advanced?.Invoke(_now);
    }

    public bool TryReadKey(out int key) => _keys.TryDequeue(out key);

    private int _clickPumps;

    public void Pump()
    {
        _now += 16;
        Advanced?.Invoke(_now);
        if (Mouse.Buttons != MouseButtons.None && !_held)
        {
            // a click stays down over two pumps (a screen may present twice a frame before it polls)
            if (++_clickPumps < 2)
            {
                return;
            }
            _clickPumps = 0;
            Mouse = Mouse with { Buttons = MouseButtons.None };
            return;
        }
        if (_idleFrames > 0)
        {
            _idleFrames--;
            return;
        }
        if (!_steps.TryDequeue(out var step))
        {
            QuitRequested = true;
            return;
        }
        var (kind, value) = (step.Split(':', 2)[0], step.Contains(':') ? step.Split(':', 2)[1] : "");
        switch (kind)
        {
            case "key":
                _keys.Enqueue(KeyFor(value));
                _idleFrames = 2;
                break;
            case "click":
                var (x, y) = Point(value);
                Mouse = new MouseState(x, y, MouseButtons.Left);
                _idleFrames = 2;
                break;
            case "rclick":
                // the right button pressed for two pumps as click does the left (tools/oracle/orig_side.py: the driver's right-button bit)
                var (rx, ry) = Point(value);
                Mouse = new MouseState(rx, ry, MouseButtons.Right);
                _idleFrames = 2;
                break;
            case "move":
                var (mx, my) = Point(value);
                Mouse = Mouse with { X = mx, Y = my };
                break;
            case "rdown":
            case "ldown":
                // a button held down (the menu bar with the right one, the hover cursor with the left) until rup/lup
                var (hx, hy) = Point(value);
                _held = true;
                Mouse = new MouseState(hx, hy, kind == "rdown" ? MouseButtons.Right : MouseButtons.Left);
                _idleFrames = 2;
                break;
            case "rup":
            case "lup":
                _held = false;
                Mouse = Mouse with { Buttons = MouseButtons.None };
                _idleFrames = 2;
                break;
            case "shift":
                ShiftHeld = value == "on";
                break;
            case "ctrl":
                CtrlHeld = value == "on";
                break;
            case "kdown":
            case "kup":
                // a key's make or break code (hex, set 1) for the game's keyboard interrupt (kdown:48 holds Up)
                _transitions.Enqueue((Convert.ToInt32(value, 16), kind == "kup"));
                break;
            case "wait":
                _idleFrames = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                break;
            case "untilover":
                // the battle runs on until it ends (the screen returns)
                _idleFrames = int.MaxValue;
                break;
            case "shot":
                Shot(value);
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
        "f10" => Keys.F10,
        _ => Keys.ForCharacter(name[0]),
    };
}

/// <summary>Opens the sheet, party information, camp or alchemy screen directly (a debugging aid).</summary>
internal static class DebugScreens
{
    public static void Run(IHost host, GameFiles files, string which, uint seed)
    {
        var world = WorldData.Load(files);
        var ui = new GameUi(host, new GameAssets(files));
        var state = Darklands.Simulation.NewGame.Quickstart(world, seed);
        state.Data = world;
        var panel = new PartyPanel(ui.Assets);
        var screens = new GameScreens2(ui, panel, state, world, new NoOtherScreens());
        try
        {
            switch (which)
            {
                case "sheet": screens.CharacterSheet(0); break;
                case "party": screens.PartyInfo(); break;
                case ['a', 'l', ..]: screens.Alchemy(which.Length > 7 ? which[7] - '0' : 0, 0); break;
                default: screens.Camp(which.Length > 4 ? which[4] - '0' : 0); break;
            }
        }
        catch (QuitGameException)
        {
        }
    }

    private sealed class NoOtherScreens : Darklands.Cards.IGameScreens
    {
        public void Barter(int shop, string name, long categories) { }
        public void Camp(int mode) { }
        public void Alchemy(int member, int place) { }
        public void ItemStore() { }
        public void CharacterSheet(int member) { }
        public void PartyInfo() { }
        public void Loot(IList<Darklands.Core.Characters.ItemStack> items, Darklands.Core.Economy.Money money) { }
        public int Battle(Darklands.Cards.BattleRequest request) => 0;
    }
}

/// <summary>No other screens (the headless battle opens only the character sheet and the party information).</summary>
internal sealed class NoGameScreens : Darklands.Cards.IGameScreens
{
    public void Barter(int shop, string name, long categories) { }
    public void Camp(int mode) { }
    public void Alchemy(int member, int place) { }
    public void ItemStore() { }
    public void CharacterSheet(int member) { }
    public void PartyInfo() { }
    public void Loot(IList<Darklands.Core.Characters.ItemStack> items, Darklands.Core.Economy.Money money) { }
    public int Battle(Darklands.Cards.BattleRequest request) => 0;
}

/// <summary>The game's sound drivers rendered in step with the scripted clock, to a .wav file and a trace.</summary>
internal sealed class HeadlessSound : IDisposable
{
    private const int Rate = 44100;
    private readonly SynthAudio _audio;
    private readonly IMidiSynth? _synth;
    private readonly List<short>? _samples;
    private readonly string? _wav;
    private readonly short[] _buffer = new short[2 * Rate];

    private HeadlessSound(SynthAudio audio, IMidiSynth? synth, string? wav, StreamWriter? trace)
    {
        _audio = audio;
        _synth = synth;
        _wav = wav;
        _samples = wav is null ? null : new List<short>();
        Trace = trace;
    }

    public StreamWriter? Trace { get; }

    public static HeadlessSound? Attach(ScriptedHost host, GameFiles files, string choice, string? roms, string? wav, string? trace,
        IReadOnlyList<string> commandLine)
    {
        var selected = Darklands.Audio.Mt32.SoundSelection.Select(files, choice == "config" ? null : choice, roms, Rate,
            line => (Darklands.Audio.Mt32.SoundSelection.IsWarning(line) ? Console.Error : Console.Out).WriteLine("sound: " + line), commandLine);
        if (selected is null)
        {
            return null;
        }
        var setup = selected.Setup;
        var synth = selected.Midi;
        var writer = trace is null ? null : new StreamWriter(trace);
        writer?.WriteLine($"rate {Rate}");
        Action<AudioEvent>? traced = writer is null ? null : e => writer.WriteLine(e.Kind switch
        {
            AudioEventKind.Tick => $"tick {e.Tick} {e.Frame}",
            AudioEventKind.Request => $"request {e.Tick} {e.Frame} {e.Value:X2}",
            AudioEventKind.MidiByte => $"midi {e.Tick} {e.Frame} {e.Context} {e.Value:X2}",
            _ => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"sample {e.Tick} {e.Frame} {e.Context} {e.Value} {e.Delay:F6}"),
        });
        var audio = new SynthAudio(files, Rate, setup, synth, traced);
        var sound = new HeadlessSound(audio, synth, wav, writer);
        host.Audio = audio;
        host.Timer = audio;
        host.Advanced = sound.RenderUntil;
        return sound;
    }

    /// <summary>Renders up to <paramref name="milliseconds"/> of the script's clock.</summary>
    private void RenderUntil(long milliseconds)
    {
        var frames = milliseconds * Rate / 1000 - _audio.RenderedFrames;
        while (frames > 0)
        {
            var n = (int)Math.Min(frames, _buffer.Length / 2);
            var span = _buffer.AsSpan(0, n * 2);
            _audio.Render(span);
            _samples?.AddRange(span.ToArray());
            frames -= n;
        }
    }

    public void Dispose()
    {
        if (_wav is not null && _samples is not null)
        {
            WavFile.Write(_wav, _samples.ToArray(), Rate);
            Console.WriteLine($"sound: {_samples.Count / 2 / (double)Rate:0.0} s in {_wav}{(_audio.Fault is null ? "" : ", driver fault: " + _audio.Fault)}");
        }
        Trace?.Dispose();
        _synth?.Dispose();
    }
}
