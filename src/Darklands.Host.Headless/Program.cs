using Darklands.Audio;
using Darklands.Audio.Packs;
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
//   mapclick:<x>,<y>,<n>         a left click held until n passes of the map's loop have read it (tools/oracle/lockstep.py)
//   shift:on|off, ctrl:on|off    the modifier keys; kdown:<hex> / kup:<hex> a key's make or break scan code
//   wait:<frames>                lets frames pass; shot:<name> saves <name>.png; rec:<n> saves the next n frames
//   seed:<n>                     the game's random seed
//   clock:<n>,<n>,...            the clock's seeds in turn instead (srand((unsigned)time(0)) at the start and wherever the game
//                                reseeds: tools/oracle/lockstep.py gives the original's); the last one stays
//   ticks:<n>,<n>,...            the BIOS tick words the start screen's C and Q keys read as the party seed (0040:006C), in turn, the
//                                last one staying (tools/oracle/lockstep.py gives the word the original's saved game holds); without
//                                the step the first clock seed's low word
//   hdpack:<folder>              replacement graphics (an HD pack, docs/SETTINGS.md display.hdPack; also from --settings): each
//                                shot also saves <name>.hd.png, the frame with the replacements at hdscale:<n> (default 4) x 320x200,
//                                and <name>.assets.txt, the asset names on the screen (the names a pack's PNG files take)
//
// Game steps:
//   saves:<folder>               the saved games' folder (default: SAVES beside the program, never the game folder)
//   banner, intro                run the program start (the BANNER.DAT banner and its checks) and the opening
//   datasafe                     plays with the data-safety fixes of the player's game (OriginalBehaviour.DataSafe); without it the game is Faithful, as the oracles play it
//   arg:<text>                   adds a word to the game's DOS command line (-Q, -O, -A<letter>)
//   sound:config|adlib|sb|sbpro2|mt32|cm32l   runs the game's sound drivers in step with the script's clock (the game timer is
//                                then the sound's, as in the SDL host); mt32roms:<folder> (default MT32_ROMS next to the program)
//   audiopack:<folder>           replacement music and battle effects (an audio pack, docs/PACKS.md; also from --settings
//                                sound.audioPack or DARKLANDS_AUDIO_PACK): the game's calls reach the drivers unchanged; with sound:
//                                the wav has the replacements mixed in; soundfont:<file> plays its .mid files (else sound.soundFont, else
//                                the first .sf2 of SOUNDFONT next to the program); the .mid files are rendered before the run
//   mod:<folder or name>         plays with that mod (docs/MODS.md; one step per mod, in load order; the settings file's mods
//                                are not read here): its cards are registered and its saves get a companion file
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
// ticks:<n>,<n>,... - the BIOS tick words the party seed is taken from (the last stays).
var ticksStep = steps.FirstOrDefault(s => s.StartsWith("ticks:", StringComparison.Ordinal));
var partyTicks = new Queue<ushort>();
if (ticksStep is not null)
{
    steps.Remove(ticksStep);
    foreach (var v in ticksStep[6..].Split(','))
    {
        partyTicks.Enqueue(ushort.Parse(v, System.Globalization.CultureInfo.InvariantCulture));
    }
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
var audioPackStep = TakeStep("audiopack:") ?? resolved.AudioPack;
var soundFontStep = TakeStep("soundfont:") ?? resolved.SoundFont;
if (audioPackStep is null && steps.Any(s => s.StartsWith("mod:", StringComparison.Ordinal)))
{
    // a mod may name music for its cards: an empty pack carries it
    ScriptedHost.ReplacementSound = AudioPack.ForMods(soundFontStep);
}
if (audioPackStep is not null)
{
    var pack = AudioPack.Load(audioPackStep, soundFontStep);
    // decoded (and the .mid files rendered) before the run, so that a scripted run always mixes the same samples
    pack.PreloadAsync(HeadlessSound.Rate, includeMidi: true).GetAwaiter().GetResult();
    pack.Problems.ToList().ForEach(Console.Error.WriteLine);
    Console.Error.WriteLine($"audio pack: {pack.Folder}, {pack.Count} replacement(s), {pack.CardMusics.Count()} card(s) with music");
    ScriptedHost.ReplacementSound = pack;
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
    var panel = new PartyPanel(ui);
    var sheets = new GameScreens2(ui, panel, state, world, new NoGameScreens());
    // the loot screen of a won battle is the game's (Esc or L leaves it)
    var lootScreens = new GameScreens(ui, panel, state, new NoGameScreens());
    var screens = new BattleScreens(ui, () => state, (items, money) => { Console.WriteLine($"loot: {items.Count} items"); lootScreens.Loot(items, money); }) { CharacterSheet = sheets.CharacterSheet, PartyInfo = sheets.PartyInfo };
    sheets.CannotView = screens.CannotViewMember;
    lootScreens.CannotView = screens.LootSkipsMember;
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
// the map's passes: mapclick's holds, and a click into the map's view stays down until a pass has read it (ScriptedHost.MapPass)
OverlandScreen.PassHook = host.MapPass;
// the battle's passes: still:<n> waits for n passes at one combat clock (ScriptedHost.BattlePass)
Darklands.Presentation.Battles.BattleScreen.PassHook = host.BattlePass;
var game = new DarklandsGame(host, OpenGame())
{
    Seed = clockStep is null ? () => seed : NextSeed,
    PartyTicks = () => partyTicks.Count > 1 ? partyTicks.Dequeue() : partyTicks.Count == 1 ? partyTicks.Peek() : (ushort)seed,
    PlayOpening = steps.Remove("intro") || fileSettings.Intro == true,
    ShowStartUp = steps.Remove("banner") || fileSettings.Banner == true,
    CommandLine = commandLine,
    Behaviour = steps.Remove("datasafe") ? Darklands.Core.OriginalBehaviour.DataSafe : Darklands.Core.OriginalBehaviour.Faithful,
    QuickstartCard = cardStep is null ? null : Convert.ToInt32(cardStep[5..], 16),
    StartWithSave = loadStep?[5..],
};
game.Options.Apply(resolved);
// the game's own notes (a saved game's mods missing, a companion file it could not use) on the error output
game.Log = Console.Error.WriteLine;
// mod:<folder or name> (one step per mod): the mods' cards (docs/MODS.md); without the step the game's own cards only
var modSteps = steps.Where(s => s.StartsWith("mod:", StringComparison.Ordinal)).ToList();
if (modSteps.Count > 0)
{
    steps.RemoveAll(modSteps.Contains);
    var modFolders = new List<string>();
    foreach (var step in modSteps)
    {
        if (Darklands.Content.ModFolders.Find(step[4..], game.Ui.Assets.Files.Root, Environment.CurrentDirectory) is { } modFolder)
        {
            modFolders.Add(modFolder);
        }
        else
        {
            Console.Error.WriteLine($"mod {step[4..]}: no folder with a mod.json found (as given, or in a mods folder next to the program or the game)");
        }
    }
    game.Mods = Darklands.Simulation.Mods.ModSet.Load(modFolders.Select(Darklands.Content.ModFolders.Read), Console.Error.WriteLine, game.Ui.Assets.IsGameMessageFile);
    // the played mods' cards that name music (a pack's own music for the same card wins); rendered before the run, as the pack's files
    if (ScriptedHost.ReplacementSound is { } soundPack && game.Mods.Music.Any())
    {
        soundPack.AddModMusic(game.Mods.Music).ToList().ForEach(Console.Error.WriteLine);
        soundPack.PreloadAsync(HeadlessSound.Rate, includeMidi: true).GetAwaiter().GetResult();
    }
}
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
if (Environment.GetEnvironmentVariable("DARKLANDS_SOUND_LOG") == "1")
{
    // a tester's aid (tools/oracle/lockstep.py): every music request and driver command in order, among the "state" lines
    host.Audio = new LoggingAudio(host.Audio, Console.WriteLine);
    game.Ui.SoundLog = Console.WriteLine;
}
var developerConsole = new Darklands.DevConsole.GameConsole();
Darklands.DevConsole.GameCommands.Register(developerConsole, () => game.Playing);
host.State = name =>
{
    if (game.Playing is { } playing)
    {
        var (sav, _) = Darklands.Persistence.OriginalSaves.Write(playing, "Darklands", game.Options.Music, game.Options.SoundEffects, savedCardOf: game.SavedCardOf);
        if (game.Fighting)
        {
            // a state taken while a battle runs: the original's writer (R037+0x2AEF, DS:907A == 1) sets the in-battle bit, whether the battle
            // was begun or taken up from a save; the state step passes no battle (it must not put the members' items away nor reload the events)
            Darklands.Persistence.OriginalSaves.MarkWrittenInBattle(sav);
        }
        File.WriteAllBytes(Path.Combine(output, name + ".sav"), sav);
        var random = playing.Random is Darklands.Core.Randomness.MscRandom msc ? msc.State.ToString("X8", System.Globalization.CultureInfo.InvariantCulture) : "?";
        // " in-battle": a battle runs (tools/oracle/lockstep.py compares no state taken inside a battle on both sides)
        Console.WriteLine($"state {name}: card 0x{playing.CurrentCard:X2} random {random}{(game.Fighting ? " in-battle" : "")}");
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
if (game.ProgramExit is { } ended)
{
    // the program's own end (the original's exit: lockstep.py's original side logs "program exit <code>")
    Console.WriteLine($"program exit {ended.Code}: {ended.Printed.TrimEnd()}");
}
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

    /// <summary>Drops the next step (one the program itself has read).</summary>
    public void Skip() => _steps.Dequeue();


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

/// <summary>
/// The audio of the host with each driver command written as a line "sound n": the numbers of the original's sound-start(n)
/// (root 047C:078A), whoever asked (music-start R043+0x09AE, the battle's effects R014+0x465E, a card's own call).
/// </summary>
internal sealed class LoggingAudio(IAudio inner, Action<string> write) : IAudio
{
    public void PlayMusic(int tune)
    {
        write($"sound {tune}");
        inner.PlayMusic(tune);
    }

    public void PlayMusicAt(int tune, long tick)
    {
        write($"sound {tune}");
        inner.PlayMusicAt(tune, tick);
    }

    public void StopMusic()
    {
        write("sound 1");
        inner.StopMusic();
    }

    public void PlaySound(int effect)
    {
        write($"sound {effect}");
        inner.PlaySound(effect);
    }

    // orig: music-start(5) keeps the AX the driver leaves (DS:9C3D, played back at the end of the prayer screen, the camp, the shell game ...). The
    // original's sessions run on NSOUND.DLC, whose play slot is a bare far return: the AX is what the caller had (0 in the runs seen); the silent
    // host answers -1 (no driver), so the played-back piece would differ (GameSounds.CurrentPiece)
    public int PlayMusicNow(int tune)
    {
        write($"sound {tune}");
        var ax = inner.PlayMusicNow(tune);
        return ax < 0 ? 0 : ax;
    }

    public int RequestCurrentPiece() => PlayMusicNow(5);

    // a read-only notice for a sound pack: not a driver call, so it is not logged
    public void CardShown(int card) => inner.CardShown(card);
}

/// <summary>The game's sound drivers rendered in step with the scripted clock, to a .wav file and a trace.</summary>
internal sealed class HeadlessSound : IDisposable
{
    public const int Rate = 44100;
    private readonly SynthAudio _audio;
    private readonly IPcmSource _source;
    private readonly IMidiSynth? _synth;
    private readonly List<short>? _samples;
    private readonly string? _wav;
    private readonly short[] _buffer = new short[2 * Rate];

    private HeadlessSound(SynthAudio audio, IPcmSource source, IMidiSynth? synth, string? wav, StreamWriter? trace)
    {
        _audio = audio;
        _source = source;
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
        // an audio pack: the drivers render under its mixer (the timer is still theirs), the game's calls reach them unchanged
        var mixer = ScriptedHost.ReplacementSound is { } pack ? new PackMixer(pack, Rate) { Source = audio, RenderedTicks = () => audio.RenderedTicks } : null;
        var sound = new HeadlessSound(audio, (IPcmSource?)mixer ?? audio, synth, wav, writer);
        host.Audio = mixer is null ? audio : new PackAudio(audio, audio, mixer);
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
            _source.Render(span);
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
