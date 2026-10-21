using Darklands.Platform;
using Darklands.Audio;
using Darklands.Content;
using Darklands.Host.Sdl;
using Darklands.Launcher;
using Darklands.Presentation;
using Darklands.Rendering;

// Darklands, on SDL2.
//   DarklandsRP [game folder] [--settings file] [--write-settings file] [--sdl path\SDL2.dll] [--screenshots folder]
//                      [--no-audio] [--sound adlib|sb|sbpro2|mt32|cm32l|speaker|none] [--mt32-roms folder] [--saves folder]
//                      [--text-font file] [--no-banner] [--no-intro] [--scale N | --resolution WxH] [--fullscreen] [--no-console]
//                      [--hd-pack folder] [--setup | --no-launcher] (or hold Shift as it starts for the launcher)
//                      [--version] [-- <the game's DOS command line: -Q, -O, -A<letter>>]
// The options come from the command line, else the environment variables, else the settings file darklands.settings.json
// (--settings, else next to the program, else %APPDATA%\DarklandsRestoration; without one a commented template is written on the
// first run, --write-settings writes it where asked; docs/SETTINGS.md), else the defaults.
// The window is 720x400 (the text screen's own resolution; the 320x200 graphics screen is doubled to 640x400 and centred),
// N times that with --scale N, or any size with --resolution (both pictures keep their aspect).
// The program start shows the BANNER.DAT banner on the 80x25 text screen; its 8x16 font is the file of --text-font or of
// DARKLANDS_TEXT_FONT (a 4096-byte VGA BIOS font dump, or its base64), else a built-in one.
// The saved games (DKSAVEn.SAV) go to the game folder's SAVES, shared with the original; savesFolder in the settings file,
// DARKLANDS_SAVES or --saves <folder> put them elsewhere. When none is set and the game folder's SAVES cannot be written (a game
// under Program Files), they go to %APPDATA%\DarklandsRestoration\SAVES, with a warning on the error output (SaveLocation).
// The sound is the game's choice: CONFIG.DRK's card and the driver files of its letter, or of the DOS command line's -A<letter>
// (a/p/r/i/n + SOUND.DLC/.DLB; i: the PC speaker; in the GOG release these are the Roland driver for "Roland + Sound Blaster");
// --sound names another card. The Roland driver's MIDI is played
// by Munt's mt32emu with the ROMs of --mt32-roms, DARKLANDS_MT32_ROMS, the settings file, else MT32_ROMS next to the program
// (a folder with the ROM files or with MT32, CM32L subfolders); without them the OPL3 FM drivers
// play instead (a warning on the error output). The settings file's sound.configDrk replaces CONFIG.DRK's card, port, IRQ and DMA.
// The text screen's font: --text-font, DARKLANDS_TEXT_FONT, the settings file or local/paths.json "textfont".
// The game folder defaults to %DARKLANDS_DIR%, the settings file, then a few usual places.
// F11 opens the developer console (Darklands.DevConsole, a host feature outside the game; the settings file's console section,
// --no-console turns it off): commands that show and change the game being played, drawn over the window in a TrueType font.
// Sound diagnostics (environment only): DARKLANDS_AUDIO_TRACE=<file> writes the game's calls to the sound, the numbers the drivers
// carry out and the MIDI they send; DARKLANDS_AUDIO_RECORD=<file> writes what the audio device is given (raw 16-bit stereo).

// the release version (Directory.Build.props): the window title, the start screen's corner, the console, --version
var version = typeof(SdlHost).Assembly
    .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
    .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "";
if (args.Contains("--version"))
{
    Console.WriteLine("Darklands Restoration Project " + version);
    return 0;
}

var arguments = args.ToList();
var dash = arguments.IndexOf("--");
List<string>? commandLine = dash < 0 ? null : arguments[(dash + 1)..];
if (dash >= 0)
{
    arguments.RemoveRange(dash, arguments.Count - dash);
}
string? Option(string name)
{
    var i = arguments.IndexOf(name);
    if (i < 0 || i + 1 >= arguments.Count)
    {
        return null;
    }
    var value = arguments[i + 1];
    arguments.RemoveRange(i, 2);
    return value;
}

var settingsFile = Option("--settings");
var writeSettings = Option("--write-settings");
if (writeSettings is not null)
{
    var written = SettingsFile.TryWriteTemplate(writeSettings, out var writeMessage);
    (written ? Console.Out : Console.Error).WriteLine(writeMessage);
    return written ? 0 : 1;
}
// the launcher (Darklands.Launcher, the settings screen before the game): --setup, Shift held as the program starts or the
// settings file's startup.launcher show it, --no-launcher never; else it comes on the first run, when the game cannot be
// found and when the game could not start
var setup = arguments.Remove("--setup");
var noLauncher = arguments.Remove("--no-launcher");
var cli = new CommandLineOptions
{
    SdlLibrary = Option("--sdl"),
    NoAudio = arguments.Remove("--no-audio"),
    Sound = Option("--sound"),
    ScreenshotsFolder = Option("--screenshots"),
    SavesFolder = Option("--saves"),
    TextFont = Option("--text-font"),
    Mt32Roms = Option("--mt32-roms"),
    NoBanner = arguments.Remove("--no-banner"),
    NoIntro = arguments.Remove("--no-intro"),
    Scale = Option("--scale"),
    Resolution = Option("--resolution"),
    Fullscreen = arguments.Remove("--fullscreen"),
    HdPack = Option("--hd-pack"),
    NoConsole = arguments.Remove("--no-console"),
    GameCommandLine = commandLine,
    GameFolder = arguments.FirstOrDefault(),
};
var settingsPath = SettingsFile.Locate(settingsFile);
var firstRun = settingsPath is null;
GameSettings fileSettings = new();
if (settingsPath is null)
{
    settingsPath = SettingsFile.WriteFirstRunTemplate(Console.Error.WriteLine);
}
else
{
    var load = SettingsFile.Load(settingsPath);
    foreach (var problem in load.Problems)
    {
        Console.Error.WriteLine(problem);
    }
    fileSettings = load.Settings;
}
if (Resolve(fileSettings) is not { } settings)
{
    return 1;
}
var folder = FindGameFolder(settings.GameFolder, settingsPath);
LauncherReason? launch = noLauncher ? null
    : setup || settings.AlwaysLaunch || StartKeys.ShiftHeldBeforeWindow() ? LauncherReason.Asked
    : folder is null ? LauncherReason.GameNotFound
    : firstRun ? LauncherReason.FirstRun
    : null;
if (folder is null && launch is null)
{
    Console.Error.WriteLine("Darklands needs the original game files (DARKLAND.EXE and its data).");
    Console.Error.WriteLine("Usage: DarklandsRP <game folder>, or set gameFolder in the settings file or DARKLANDS_DIR.");
    return 1;
}

// SDL2: the settings file or --sdl, else the SDL2.dll next to the program, else DARKLANDS_SDL2_PATH or the developer's
// local/paths.json "sdl", else the system's
var besideProgram = Path.Combine(AppContext.BaseDirectory, "SDL2.dll");
SdlNative.ConfigureLibrary(settings.SdlLibrary ?? (File.Exists(besideProgram) ? besideProgram : LocalPaths.Get("sdl", "DARKLANDS_SDL2_PATH")));
var title = "Darklands Restoration Project " + version;
var window = settings.Window ?? DisplayLayout.WindowSize(null, null);
using var host = launch is null ? new SdlHost(title, window.Width, window.Height, settings.Fullscreen) : LauncherHost.Open(title);
if (launch is null && !noLauncher && StartKeys.ShiftHeldInWindow())
{
    // where the keyboard can only be read through the window (not Windows): the window becomes the launcher's
    launch = LauncherReason.Asked;
    LauncherHost.Fit(host);
}
string? startProblem = null;
while (true)
{
    if (launch is { } reason)
    {
        if (LauncherHost.Run(host, reason, startProblem, fileSettings, cli, settingsPath, version) is not { } chosen)
        {
            return 0;
        }
        fileSettings = chosen;
        if (Resolve(fileSettings) is not { } resolved)
        {
            return 1;
        }
        settings = resolved;
        folder = FindGameFolder(settings.GameFolder, settingsPath);
        if (folder is null)
        {
            (launch, startProblem) = (LauncherReason.GameNotFound, null);
            continue;
        }
        window = settings.Window ?? DisplayLayout.WindowSize(null, null);
        host.Resize(window.Width, window.Height, settings.Fullscreen);
    }
    var start = new StartProgress();
    try
    {
        return Play(host, settings, folder!, version, start);
    }
    catch (Exception e) when (!start.Running && !noLauncher)
    {
        // the game could not start (a damaged HD pack, a missing file ...): back to the launcher, which says why
        Console.Error.WriteLine("Start: " + e.Message);
        (launch, startProblem) = (LauncherReason.StartFailed, "The game could not start: " + e.Message);
        LauncherHost.Fit(host);
    }
}

ResolvedSettings? Resolve(GameSettings file)
{
    try
    {
        return ResolvedSettings.Resolve(cli, Environment.GetEnvironmentVariable, file);
    }
    catch (Exception e) when (e is FormatException or ArgumentException or OverflowException)
    {
        Console.Error.WriteLine(e.Message);
        return null;
    }
}

// The game in the window, with its sound, console and saves; returns the program's exit code.
static int Play(SdlHost host, ResolvedSettings settings, string folder, string version, StartProgress start)
{
    host.Renderer = null;
    if (settings.HdPack is { } hdPack)
    {
        // replacement graphics (docs/SETTINGS.md, display.hdPack): drawn by Darklands.Rendering, the game plays the same
        var pack = HdPack.Load(hdPack);
        pack.Problems.ToList().ForEach(Console.Error.WriteLine);
        _ = pack.PreloadAsync();
        host.Renderer = new HdRenderer(pack);
    }
    var textFont = settings.TextFont ?? LocalPaths.Get("textfont", "DARKLANDS_TEXT_FONT");
    if (textFont is not null)
    {
        host.TextFont = Darklands.Platform.TextFont.Load(textFont);
    }
    var files = new GameFiles(folder);
    if (settings.ConfigDrk is { } config)
    {
        // the game and the sound selection then read these words as CONFIG.DRK's
        files.OverrideConfig(config.Card, config.Port, config.Irq, config.Dma);
    }
    // DARKLANDS_AUDIO_TRACE=<file>: the requests the drivers carry out and the MIDI they send, as the headless host's trace: writes them
    var audioTrace = Environment.GetEnvironmentVariable("DARKLANDS_AUDIO_TRACE") is { Length: > 0 } tracePath
        ? TextWriter.Synchronized(new StreamWriter(tracePath) { AutoFlush = true })
        : null;
    using var audioOutput = settings.NoAudio ? null : StartAudio(host, files, settings.SoundChoice, settings.Mt32Roms, settings.CommandLine, audioTrace);
    var game = new DarklandsGame(host, files)
    {
        ShowStartUp = settings.ShowBanner,
        PlayOpening = settings.PlayIntro,
        CommandLine = settings.CommandLine,
    };
    if (audioTrace is not null)
    {
        game.Ui.Trace = audioTrace.WriteLine;
    }
    using var console = settings.ConsoleEnabled ? CreateConsole(game, settings, version) : null;
    host.Console = console;
    using var corner = new CornerText(() => game.OnStartScreen ? "v" + version : null);
    host.Corner = corner;
    game.Ui.ScreenshotFolder = settings.ScreenshotsFolder ?? Environment.CurrentDirectory;
    var saves = SaveLocation.Choose(settings.SavesFolder, game.SaveDirectory, SaveLocation.UserFolder(), SaveLocation.CanWrite);
    if (saves.FellBack)
    {
        Directory.CreateDirectory(saves.Folder);
        Console.Error.WriteLine("Saves: the game folder's " + game.SaveDirectory + " cannot be written; saved games go to " + saves.Folder
            + " (set savesFolder in the settings file, DARKLANDS_SAVES or --saves to choose another folder).");
    }
    game.SaveDirectory = saves.Folder;
    start.Running = true;
    game.Run();
    audioTrace?.Dispose();
    return 0;
}

// The game's sound (chosen by Darklands.Audio.Mt32.SoundSelection, as any host does) through an SDL audio device.
static IDisposable? StartAudio(SdlHost host, GameFiles files, string? choice, string? romFolder, IReadOnlyList<string> commandLine,
    TextWriter? trace)
{
    var output = SdlAudioOutput.TryOpen(out var error);
    if (output is null)
    {
        Console.Error.WriteLine("Sound: no audio device (" + error + "); no sound.");
        return null;
    }
    var sound = Darklands.Audio.Mt32.SoundSelection.Select(files, choice, romFolder,
        output.SampleRate, line => (Darklands.Audio.Mt32.SoundSelection.IsWarning(line) ? Console.Error : Console.Out).WriteLine("Sound: " + line),
        commandLine);
    if (sound is null)
    {
        output.Dispose();
        return null;
    }
    Action<AudioEvent>? traced = trace is null ? null : e =>
    {
        var line = e.Kind switch
        {
            AudioEventKind.Request => $"request {e.Tick} {e.Frame} {e.Value:X2}",
            AudioEventKind.MidiByte => $"midi {e.Tick} {e.Frame} {e.Context} {e.Value:X2}",
            _ => null,
        };
        if (line is not null)
        {
            trace.WriteLine(line);
        }
    };
    var audio = sound.Start(files, output, traced);
    host.Audio = trace is null ? audio : new TracedCalls(audio, audio, trace);
    host.Timer = audio;
    return new Disposables(output, sound);
}

/// <summary>
/// The game folder: the one named (command line, DARKLANDS_DIR, settings file; the GOG or Steam install folder is fine, its DARKLAND
/// subfolder is taken), else the developer's local/paths.json "game", else a search of the usual places, written into the settings
/// file; null when nothing is found (or the named folder is wrong): the launcher then asks for it.
/// </summary>
static string? FindGameFolder(string? named, string? settingsPath)
{
    if (GameFolderSearch.GameFolderIn(named, GameFiles.IsGameFolder) is { } given)
    {
        return given;
    }
    if (named is not null)
    {
        Console.Error.WriteLine($"Game folder: {named} holds no DARKLAND.EXE (nor its DARKLAND subfolder).");
    }
    else
    {
        if (GameFolderSearch.GameFolderIn(LocalPaths.Get("game", "DARKLANDS_DIR"), GameFiles.IsGameFolder) is { } local)
        {
            return local;
        }
        if (GameFolderSearch.Find(GameFolderSearch.Candidates(AppContext.BaseDirectory), GameFiles.IsGameFolder) is var (found, source))
        {
            Console.WriteLine($"Game folder: found Darklands in {found} ({source}).");
            Remember(found, replace: false);
            return found;
        }
    }
    return null;

    void Remember(string gameFolder, bool replace)
    {
        if (settingsPath is not null)
        {
            SettingsFileEdits.TrySetGameFolder(settingsPath, gameFolder, replace, out var message);
            Console.WriteLine("Settings: " + message);
        }
    }
}

/// <summary>The developer console: the game commands on the game being played, and the host's own.</summary>
static ConsoleOverlay CreateConsole(DarklandsGame game, ResolvedSettings settings, string version)
{
    var console = new Darklands.DevConsole.GameConsole();
    Darklands.DevConsole.GameCommands.Register(console, () => game.Playing);
    console.Add(new Darklands.DevConsole.ConsoleCommand("music", "music on|off  (the game's music option, as Alt-M sets it)", words =>
    {
        var on = words.Length == 1 && words[0].Equals("on", StringComparison.OrdinalIgnoreCase);
        if (words.Length != 1 || (!on && !words[0].Equals("off", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("music on or music off?");
        }
        game.Options.Music = on;
        if (!on)
        {
            game.Ui.Audio.PlayMusic(GameSounds.Stop);
        }
        return Darklands.DevConsole.ConsoleResult.Ok("music " + (on ? "on (from the next piece)" : "off"));
    }));
    console.Write($"Darklands Restoration Project {version} developer console. Type help for the commands; F11 or Esc closes it.");
    return new ConsoleOverlay(console, settings.ConsoleFont, settings.ConsoleFontSize, Console.Error.WriteLine);
}

/// <summary>Writes each call of the game to the audio, with the tick it is asked for (DARKLANDS_AUDIO_TRACE).</summary>
sealed file class TracedCalls(IAudio audio, IGameTimer timer, TextWriter trace) : IAudio
{
    private void Write(string call, int number) => trace.WriteLine($"call {call} {number:X2} at {timer.RequestTick} heard {timer.Ticks}");

    public void PlayMusic(int tune)
    {
        Write("music", tune);
        audio.PlayMusic(tune);
    }

    public void PlayMusicAt(int tune, long tick)
    {
        Write("music-at-" + tick, tune);
        audio.PlayMusicAt(tune, tick);
    }

    public void StopMusic()
    {
        Write("stop", 1);
        audio.StopMusic();
    }

    public void PlaySound(int effect)
    {
        Write("sound", effect);
        audio.PlaySound(effect);
    }

    public int PlayMusicNow(int tune)
    {
        var ax = audio.PlayMusicNow(tune);
        trace.WriteLine($"call now {tune:X2} gives {ax}");
        return ax;
    }

    public int RequestCurrentPiece()
    {
        var ax = audio.RequestCurrentPiece();
        trace.WriteLine($"call current gives {ax}");
        return ax;
    }
}

/// <summary>Whether the game got as far as running (a failure before is a failed start, shown on the launcher).</summary>
sealed file class StartProgress
{
    public bool Running { get; set; }
}

/// <summary>Closes the audio device, then the synthesiser it pulled from.</summary>
sealed file class Disposables(IDisposable output, IDisposable? midi) : IDisposable
{
    public void Dispose()
    {
        output.Dispose();
        midi?.Dispose();
    }
}
