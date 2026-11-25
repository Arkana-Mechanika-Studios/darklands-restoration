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
//   settings:<name>[:<keys>]     opens the settings screen (1.2) over the frame shown, takes the keys one a frame (up,down,left,right,
//                                enter,space,esc,f7 or a letter, comma-separated), then Esc; saves each frame as <name>-NN.png
//   settingsline                 the Game menu ends with the settings line, as in the SDL host (DARKLANDS_SETTINGS_LINE=1: every run)
//   seed:<n>                     the game's random seed
//   clock:<n>,<n>,...            the clock's seeds in turn instead (srand((unsigned)time(0)) at the start and wherever the game
//                                reseeds: tools/oracle/lockstep.py gives the original's); the last one stays
//   ticks:<n>,<n>,...            the BIOS tick words the start screen's C and Q keys read as the party seed (0040:006C), in turn, the
//                                last one staying (tools/oracle/lockstep.py gives the word the original's saved game holds); without
//                                the step the first clock seed's low word
//   hdpack:<folder>              replacement graphics (an HD pack, docs/SETTINGS.md display.hdPack; also from --settings): each
//                                shot also saves <name>.hd.png, the frame with the replacements at hdscale:<n> (default 4) x 320x200,
//                                and <name>.assets.txt, the asset names on the screen (the names a pack's PNG files take)
//   languagepack:<folder>        a translation pack (docs/LOCALISATION.md; also from --settings): the screens' words in its language
//   fontpack:<folder>            a font pack (pack.json "fonts": true; also from --settings): the text in its fonts
//
// Game steps:
//   saves:<folder>               the saved games' folder (default: SAVES beside the program, never the game folder)
//   banner, intro                run the program start (the BANNER.DAT banner and its checks) and the opening
//   datasafe                     plays with the data-safety fixes of the player's game (OriginalBehaviour.DataSafe); without it the game is Faithful, as the oracles play it
//   interface:comfort            the Interface switch on Comfort (GameUi.ComfortInterface: the comfort features); without it Classic, as the oracles play it
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
//   startat:<card:$FILE#n|screen:Name>   (with languagepack:) the translation test's start (DarklandsRP --start-at): no start screen
//   option:<n>                   the pointer onto line n of the card menu being chosen from (then key:enter chooses it)
//   state:<name>                 writes the game being played as a saved game, <name>.sav in the output folder (the comment
//                                "Darklands"; tools/oracle/lockstep.py compares it with the original's at the same point)
//   load:<file>                  starts with that saved game of the saves folder; a game saved in a battle resumes it
//   console:<command>            a developer console command on the game being played ("console:money add 10" as one argument)
//   inspect:<x>,<y>              (developer builds only: DevTools) what drew that pixel of the last frame, and the steps taken before it
//   (developer builds only) the environment variable DARKLANDS_TEXT_BOXES=<folder> writes the box of every keyed text drawn (the
//                                widest English, the least room: docs/LOCALISATION.md, the translator's template) to
//                                <folder>/boxes-<process id>.json at the end (tools/oracle/rerun_all.py's sessions pass it on)
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

// the tester's traces and the oracle's feeds (tools/oracle/lockstep.py sets them in the environment)
TesterHooks.Apply(Environment.GetEnvironmentVariable);
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
// replay:<recording> (R-1): plays an input recording again (a problem report's recording.dlrec) on this game, its saves in
//   <output>/saves; says whether the tape played to its end and each game it saved came out with the same bytes. packs:<folder>
//   is where its packs are looked for by name (<folder>/languages/<name>, <folder>/fonts/<name>; also the program's packs folder).
// tapedump:<recording>:<from>:<count> (R-1, a developer's aid): the tape's entries from <from>, one a line
if (steps.FirstOrDefault(s => s.StartsWith("tapedump:", StringComparison.Ordinal)) is { } dumpStep)
{
    var parts = dumpStep[9..].Split(':');
    var (file, from, count) = (string.Join(":", parts[..^2]), long.Parse(parts[^2], System.Globalization.CultureInfo.InvariantCulture), long.Parse(parts[^1], System.Globalization.CultureInfo.InvariantCulture));
    var reader = new Darklands.Platform.Recording.TapeReader(Darklands.Platform.Recording.InputRecording.Read(file).Tape);
    for (long n = 0; n < from + count && reader.Next() is { } entry; n++)
    {
        if (n >= from)
        {
            Console.WriteLine($"{n} {entry.Kind} {entry.Value}{(entry.Note is null ? "" : " " + entry.Note)}");
        }
    }
    return 0;
}
if (steps.FirstOrDefault(s => s.StartsWith("replay:", StringComparison.Ordinal)) is { } replayStep)
{
    var packsRoot = steps.FirstOrDefault(s => s.StartsWith("packs:", StringComparison.Ordinal))?[6..];
    Darklands.Platform.Recording.InputRecording recorded;
    try
    {
        recorded = Darklands.Platform.Recording.InputRecording.Read(replayStep[7..]);
    }
    catch (Exception e) when (e is InvalidDataException or System.Text.Json.JsonException or IOException)
    {
        Console.WriteLine("replay refused: " + e.Message);
        return 3;
    }
    string? FindPack(string kindAndName)
    {
        var (kind, name) = (kindAndName[..kindAndName.IndexOf(':')], kindAndName[(kindAndName.IndexOf(':') + 1)..]);
        var sub = kind == "language" ? "languages" : "fonts";
        return new[] { packsRoot, Path.Combine(AppContext.BaseDirectory, Darklands.Platform.PackLayout.Packs) }
            .Where(r => r is not null).Select(r => Path.Combine(r!, sub, name)).FirstOrDefault(Directory.Exists);
    }
    Darklands.Simulation.Mods.ModSet? FindMods(IReadOnlyList<string> names)
    {
        var folders = names.Select(n => Darklands.Content.ModFolders.Find(n, resolved.GameFolder!, Environment.CurrentDirectory)).ToList();
        return folders.Any(f => f is null) ? null
            : Darklands.Simulation.Mods.ModSet.Load(folders.Select(f => Darklands.Content.ModFolders.Read(f!)), Console.Error.WriteLine, n => true);
    }
    // the developer console's commands in the recording run on the game commands (the SDL host's own ones are its options: noted apart)
    Action<string> ReplayConsole(DarklandsGame replayed)
    {
        var replayConsole = new Darklands.DevConsole.GameConsole();
        Darklands.DevConsole.GameCommands.Register(replayConsole, () => replayed.Playing);
        return line => replayConsole.Execute(line);
    }
    // anybuild: play a recording another build made (a developer's try: it may part)
    var result = Darklands.Presentation.Recording.GameReplay.Run(recorded, OpenGame(), output, FindPack, FindMods, console: ReplayConsole,
        anyBuild: steps.Contains("anybuild"));
    if (result.Refused is { } refused)
    {
        Console.WriteLine("replay refused: " + refused);
        return 3;
    }
    foreach (var note in result.Notes)
    {
        Console.WriteLine("replay note: " + note);
    }
    Console.WriteLine(result.Parted is { } parted ? "replay parted: " + parted : $"replay played {result.Entries} of {result.TapeEntries} entries");
    foreach (var (name, same) in result.Saves)
    {
        Console.WriteLine($"replay save {name}: {(same ? "same bytes" : "DIFFERS")}");
    }
    Console.WriteLine(result.Same ? "replay: same" : "replay: differs");
    return result.Same ? 0 : 3;
}
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
var languagePackStep = TakeStep("languagepack:") ?? resolved.LanguagePack;
// a font pack (fontpack:<folder> or the settings file's text.fontPack): its fonts for the slots the translation pack has none for
var fontPackStep = TakeStep("fontpack:") ?? resolved.FontPack;
// the packs on a game's screens: the translation pack's words, the translation's and the font pack's fonts; with a TrueType font each
//   shot also saves <name>.hd.png with its glyphs at hdscale (the renderer's text layer)
void UsePacks(GameUi ui)
{
    if (languagePackStep is null && fontPackStep is null)
    {
        return;
    }
    // the fonts' pictures read with the program's PNG reader (font packs)
    Darklands.Presentation.FontPack.ReadImage = PngCodec.ReadAlpha;
    Darklands.Presentation.Graphics.PackCapitals.ReadImage = path => { var image = PngCodec.Read(path); return (image.Width, image.Height, image.Argb); };
    Darklands.Presentation.FontPack.LoadScalable = json => (TtfFont.Load(json, out var why), why);
    if (languagePackStep is not null)
    {
        ui.UseLanguagePack(languagePackStep, Console.Error.WriteLine);
    }
    if (fontPackStep is not null)
    {
        ui.UseFontPack(fontPackStep, Console.Error.WriteLine);
    }
    if (ui.PackFonts.Values.Any(f => f is Darklands.Platform.IScalableFont) && ScriptedHost.HdRenderer is null)
    {
        ScriptedHost.HdRenderer = new HdRenderer(HdPack.Empty);
        ScriptedHost.HdScale = hdScaleStep is null ? 4 : int.Parse(hdScaleStep, System.Globalization.CultureInfo.InvariantCulture);
    }
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
    HeadlessSettings.Offer(host, ui, null, steps.Contains("interface:comfort"), SettingsLine(steps));
    // a translation pack (the battle's words and names) and a font pack
    UsePacks(ui);
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
        foreach (var m in state.Party.Slots)
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
                    Console.WriteLine($"cbt {i}: {c.Status} at ({c.Position.X},{c.Position.Y}) floor {c.Level} action {(int)c.Action} target {c.Target} hot spot {screens.Current.HotSpot(i)}");
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
            : $"city {started.CurrentLocation}, card 0x{started.CurrentCard:X2}: " + string.Join("; ", started.Party.Slots.Select(m =>
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
    // (screen:sheet|party|camp<mode>|alchemy<member>|saint<n>|formula<n>|barter|store|loot).
    steps.Remove(screenStep);
    host = new ScriptedHost(steps, output);
    var which = screenStep[7..];
    if (which is "barter" or "store" or "loot")
    {
        ScreenPreview.Run(host, OpenGame(), which, seed, UsePacks);
    }
    else
    {
        DebugScreens.Run(host, OpenGame(), which, seed, UsePacks);
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
    // a translation pack (the saint screen's words and names) and a font pack
    UsePacks(ui);
    var state = Darklands.Simulation.NewGame.Quickstart(world, seed);
    var panel = new PartyPanel(ui);
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
// startat:<card:$FILE#n | screen:Name> (with languagepack:): the translation test's start, as DarklandsRP --start-at
var startAtStep = steps.FirstOrDefault(s => s.StartsWith("startat:", StringComparison.Ordinal));
if (startAtStep is not null)
{
    steps.Remove(startAtStep);
    host = new ScriptedHost(steps, output);
}
List<string> commandLine = [.. resolved.CommandLine, .. TakeArguments(steps)];
using var sound = soundStep is null ? null : HeadlessSound.Attach(host, OpenGame(), soundStep, romsStep, wavStep, traceStep, commandLine);
// the map's passes: mapclick's holds, and a click into the map's view stays down until a pass has read it (ScriptedHost.MapPass)
OverlandScreen.PassHook = host.MapPass;
// the battle's passes: still:<n> waits for n passes at one combat clock (ScriptedHost.BattlePass)
Darklands.Presentation.Battles.BattleScreen.PassHook = host.BattlePass;
// the Interface switch (interface:comfort): Classic without it, as the oracles play
var comfortInterface = steps.Remove("interface:comfort");
// record:<file> (R-1): the session's input recording, written to <file> when the run ends (replay: plays it again)
//   (or the environment variable DARKLANDS_RECORD=<file>: a whole lockstep run recorded as it plays, tools/oracle)
var recordStep = steps.FirstOrDefault(s => s.StartsWith("record:", StringComparison.Ordinal));
if (recordStep is not null)
{
    steps.Remove(recordStep);
}
else if (Environment.GetEnvironmentVariable("DARKLANDS_RECORD") is { Length: > 0 } recordTo)
{
    recordStep = "record:" + (recordTo == "1" ? Path.Combine(output, Darklands.Platform.Recording.InputRecording.FileName) : recordTo);
}
using var recording = recordStep is null ? null
    : new Darklands.Presentation.Recording.GameRecording(host, savesStep?[6..] ?? resolved.SavesFolder ?? Path.Combine(AppContext.BaseDirectory, "SAVES"));
var game = new DarklandsGame(recording?.Host ?? (Darklands.Platform.IHost)host, OpenGame())
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
// a translation pack (languagepack:<folder> or the settings file's text.languagePack) changes the screens' words only; a font pack
//   (fontpack:) their fonts
UsePacks(game.Ui);
game.Ui.ComfortInterface = comfortInterface;
// the settings screen (settings: steps): its rows over this game, its changes logged (no window, no settings file written)
HeadlessSettings.Offer(host, game.Ui, rules => game.Behaviour = rules, comfortInterface, SettingsLine(steps));
if (startAtStep is not null)
{
    if (languagePackStep is null)
    {
        Console.Error.WriteLine("startat: needs languagepack:<folder>");
        return 2;
    }
    var at = game.ResolveStartAt(startAtStep[8..], out var startError);
    Console.WriteLine(at is null ? "startat: " + startError : $"startat: {startAtStep[8..]}: card {at.Card}, screen {at.Screen ?? "-"}, variant {at.Variant}, {at.Steps.Count} steps{(at.Reached ? "" : ", " + at.Notice)}");
    game.StartAt = at;
    game.PlayOpening = false;
}
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
#if DEV_TOOLS
// the developer's inspector (inspect:x,y steps): only in builds with the property DevTools (not in release builds); its text plane
//   (ScreenTextPlane) is kept from the start
Darklands.Platform.ScreenTextPlane.Recording = true;
var textBoxesFolder = Environment.GetEnvironmentVariable("DARKLANDS_TEXT_BOXES");
if (!string.IsNullOrEmpty(textBoxesFolder))
{
    // the screen of each text too (the translators' editor groups the texts by screen)
    Darklands.Platform.ScreenTextPlane.ScreenOf = Darklands.Presentation.ScreenNames.Current;
}
host.Inspect = (frame, x, y, step) =>
{
    foreach (var line in Darklands.DevTools.Inspector.Describe(frame, x, y, new Darklands.DevTools.InspectedMoment(step, game.Ui.Inspect?.Invoke(x, y))))
    {
        Console.WriteLine("inspect: " + line);
    }
};
#endif
var recordingWritten = false;
void WriteRecording()
{
    if (recording is null || recordingWritten)
    {
        return;
    }
    recordingWritten = true;
    var made = recording.Snapshot(Darklands.Platform.Recording.InputRecording.Checksum(game.Ui.Assets.Files.Read("DARKLAND.EXE")));
    made.Write(recordStep![7..]);
    Console.WriteLine($"recording: {made.Header.Entries} entries, tape {made.Tape.Length} bytes, {made.Header.Written.Count} file(s) saved{(made.Header.FinalState.Length > 0 ? ", a game in play" : "")}, written to {recordStep[7..]}");
}
if (recording is not null)
{
    recording.Attach(game);
    host.StepsEnded = WriteRecording;
    if (game.QuickstartCard is { } quickCard)
    {
        recording.StartSettings["quickstartCard"] = quickCard.ToString("X", System.Globalization.CultureInfo.InvariantCulture);
    }
    if (game.StartWithSave is { } firstSave)
    {
        recording.StartSettings["startWithSave"] = firstSave;
    }
    if (languagePackStep is not null)
    {
        recording.StartSettings["pack.language"] = Path.GetFileName(Path.TrimEndingDirectorySeparator(languagePackStep));
    }
    if (fontPackStep is not null)
    {
        recording.StartSettings["pack.font"] = Path.GetFileName(Path.TrimEndingDirectorySeparator(fontPackStep));
    }
    if (modSteps.Count > 0)
    {
        recording.StartSettings["mods"] = string.Join("\u001f", modSteps.Select(s => s[4..]));
    }
}
host.ConsoleCommand = line =>
{
    foreach (var text in developerConsole.Execute(line).Lines)
    {
        Console.WriteLine("console: " + text);
    }
};
try
{
    game.Run();
}
finally
{
    // a run that ended before its steps did (the game quit): the recording up to there
    WriteRecording();
}
#if DEV_TOOLS
if (!string.IsNullOrEmpty(textBoxesFolder))
{
    // the boxes of the keyed texts drawn (L-7w): keys and numbers, never the words
    Directory.CreateDirectory(textBoxesFolder);
    Darklands.Localization.TextBoxes.Write(Path.Combine(textBoxesFolder, $"boxes-{Environment.ProcessId}.json"),
        Darklands.Platform.ScreenTextPlane.KeyedTexts().Select(e => new Darklands.Localization.TextBox(e.Key!, e.WidestDrawn,
            e.Room == Darklands.Platform.ScreenTextPlane.NoRoom ? -1 : e.Room, e.Windowed, e.Font,
            e.Values?.ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal)) { Screens = [.. e.Screens], X = e.At?.X, Y = e.At?.Y }));
}
#endif
if (game.ProgramExit is { } ended)
{
    // the program's own end (the original's exit: lockstep.py's original side logs "program exit <code>")
    Console.WriteLine($"program exit {ended.Code}: {ended.Printed.TrimEnd()}");
}
Console.WriteLine($"{host.ShotsTaken} screenshot(s) in {output}");
return 0;

// the Game menu's settings line (1.2): the step settingsline, or DARKLANDS_SETTINGS_LINE=1 (a whole replay with it, tools/oracle/rerun_all.py)
static bool SettingsLine(List<string> steps) => steps.Remove("settingsline") || Environment.GetEnvironmentVariable("DARKLANDS_SETTINGS_LINE") == "1";

static List<string> TakeArguments(List<string> steps)
{
    var words = steps.Where(s => s.StartsWith("arg:", StringComparison.Ordinal)).ToList();
    steps.RemoveAll(words.Contains);
    return [.. words.Select(w => w[4..])];
}

// the settings screen's host without a window: its changes logged, nothing written
internal sealed class HeadlessSettings : Darklands.Presentation.Views.ISettingsHost
{
    /// <summary>The settings screen over <paramref name="ui"/> for the host's settings: steps (its rows as the defaults give them).</summary>
    public static void Offer(ScriptedHost host, GameUi ui, Action<Darklands.Core.OriginalBehaviour>? newGameRules, bool comfort, bool line)
    {
        var model = new Darklands.Presentation.Views.SettingsModel(new HeadlessSettings(), ui, newGameRules,
            ResolvedSettings.Resolve(new CommandLineOptions(), _ => null, new GameSettings { InterfaceMode = comfort ? "comfort" : "classic" }),
            new GameSettings(), new Dictionary<string, string>());
        host.SettingsView = () => model.View(Keys.AltT);
        // the Game menu's settings line, only when asked for (the step settingsline, or DARKLANDS_SETTINGS_LINE=1 for a whole replay): the
        //   oracle's menus are the original's
        if (line)
        {
            ui.SettingsKeyLabel = "\x0fT";
            ui.OpenSettings = () => Console.WriteLine("settings: chosen in the Game menu");
        }
    }

    public void SetFilter(Darklands.Platform.PictureFilter filter) => Console.WriteLine("settings: picture " + Darklands.Platform.PictureFilters.Name(filter));

    public void SetWindow(int width, int height, bool fullscreen) => Console.WriteLine($"settings: window {width}x{height}{(fullscreen ? " full screen" : "")}");

    public void SetCurvature(bool on) => Console.WriteLine("settings: curvature " + (on ? "on" : "off"));

    public void SetMouseWheel(bool on) => Console.WriteLine("settings: mouse wheel " + (on ? "on" : "off"));

    public (int Width, int Height)? UsableScreen => (1920, 1040);

    public (IReadOnlyList<string> Packs, IReadOnlyList<string> SoundFonts) Found() => ([], []);

    public Darklands.Presentation.Views.SettingsPackKind KindOf(string folder) => Darklands.Presentation.Views.SettingsPackKind.None;

    public void Write(IReadOnlyDictionary<string, System.Text.Json.Nodes.JsonNode?> values) =>
        Console.WriteLine("settings: would write " + string.Join(", ", values.Select(v => $"{v.Key} = {v.Value?.ToJsonString() ?? "null"}")));
}
