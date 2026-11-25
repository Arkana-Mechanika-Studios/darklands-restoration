using Darklands.Content;
using Darklands.Platform;
using Darklands.Platform.Recording;
using Darklands.Presentation;
using Darklands.Presentation.Recording;

namespace Darklands.Host.Sdl.Tests;

/// <summary>
/// The input recording in the real SDL window (R-1): a game played by SDL events pushed as a player's (a Quickstart party, the
/// mouse moved, the game's own pause, the settings screen with the picture filter, the window's size, the interface, the rules and
/// clean drawing changed while the game is paused, a card option taken, a save), the recording taken by Ctrl-F12 as a report
/// takes it, then played again with no window: the same saved game and the same game at the end. Needs the SDL3 library the host
/// ships and the game files (DARKLANDS_DIR); skipped without them.
/// </summary>
[Collection("SDL")]
public sealed class InputRecordingSdlTests
{
    // SDL3's keys: scancodes (USB usage) and keycodes
    private const int ScanQ = 20, ScanP = 19, ScanS = 22, ScanT = 23, Scan1 = 30, ScanReturn = 40, ScanEscape = 41, ScanSpace = 44;
    private const int ScanRight = 79, ScanDown = 81;
    private const int KeyRight = 0x4000004F, KeyDown = 0x40000051, Key1 = '1';
    private const ushort ModAlt = 0x0100, ModCtrl = 0x0040;

    [Fact]
    public void AGamePlayedInTheWindowReplaysToTheSameSaveAndState()
    {
        var library = Path.Combine(AppContext.BaseDirectory, "SDL3.dll");
        if (!File.Exists(library) || LocalPaths.Get("game", "DARKLANDS_DIR") is not { } gameFolder || !GameFiles.IsGameFolder(gameFolder))
        {
            return;
        }
        SdlNative.ConfigureLibrary(library);
        var work = Path.Combine(Path.GetTempPath(), "dl-recording-" + Guid.NewGuid().ToString("N"));
        var saves = Path.Combine(work, "saves");
        Directory.CreateDirectory(saves);
        try
        {
            InputRecording? taken = null;
            var settingsLog = new List<string>();
            var files = new GameFiles(gameFolder);
            using (var host = new SdlHost("recording test"))
            {
                using var recording = new GameRecording(host);
                var game = new DarklandsGame(recording.Host, files)
                {
                    ShowStartUp = false,
                    PlayOpening = false,
                    Behaviour = Core.OriginalBehaviour.Of(Core.RulePreset.Original),
                    RecordedRules = true,
                    SaveDirectory = saves,
                };
                // the settings screen as the program has it (its file a temporary one)
                // the game starts Classic (the interface's row then goes to Comfort)
                var running = ResolvedSettings.Resolve(new CommandLineOptions { GameFolder = gameFolder }, _ => null, new GameSettings { InterfaceMode = "classic" });
                var log = settingsLog;
                var model = new Presentation.Views.SettingsModel(new SettingsScreen(host, Path.Combine(work, "darklands.settings.json"), gameFolder, log.Add),
                    game.Ui, rules => game.Behaviour = rules, running, new GameSettings(), new Dictionary<string, string>());
                void OpenSettings() => host.OpenView(model.View(Keys.AltT));
                host.SettingsRequested = OpenSettings;
                game.Ui.OpenSettings = OpenSettings;
                game.Ui.PressedButtonMilliseconds = GameUi.PressedButtonFloor;
                game.Ui.EdgeScrollMilliseconds = 100;
                recording.TakeSaves(saves);
                recording.Attach(game);
                host.ReportRequested = () => taken = recording.Snapshot("test", "", InputRecording.Checksum(files.Read("DARKLAND.EXE")));
                var player = new Thread(() => Play()) { IsBackground = true };
                player.Start();
                game.Run();
                player.Join(TimeSpan.FromSeconds(30));
            }
            Assert.NotNull(taken);
            if (Environment.GetEnvironmentVariable("DARKLANDS_RECORDING_MEASURE_OUT") is { Length: > 0 } measured)
            {
                File.WriteAllText(measured, $"entries {taken!.Header.Entries} tape {taken.Tape.Length} bytes" + Environment.NewLine);
            }
            Assert.True(taken!.Header.Written.Count >= 2, $"{taken.Header.Written.Count} file(s) saved");
            Assert.NotEqual("", taken.Header.FinalState);
            // the settings changed while paused are on the tape
            var reader = new TapeReader(taken.Tape);
            var notes = new List<string>();
            while (reader.Next() is { } entry)
            {
                if (entry.Note is { } note)
                {
                    notes.Add(note);
                }
            }
            Assert.True(notes.Any(n => n.Contains("comfort=1", StringComparison.Ordinal)), "notes: " + string.Join(" | ", notes) + "; settings log: " + string.Join(" | ", settingsLog));

            var replay = GameReplay.Run(taken, new GameFiles(gameFolder), Path.Combine(work, "replay"));
            Assert.True(replay.Parted is null, replay.Parted);
            Assert.True(replay.ReachedEnd);
            Assert.All(replay.Saves, s => Assert.True(s.Same, s.Name + " differs"));
            Assert.True(replay.FinalStateSame, "the game at the end differs");
            Assert.True(replay.Same);
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    // the player: SDL events pushed as the keyboard and the mouse give them, with a player's pauses
    private static void Play()
    {
        Thread.Sleep(1500);
        Mouse(300, 200);
        Key(ScanQ, 'q');                                  // the start screen: Quickstart
        Thread.Sleep(2500);
        Mouse(500, 300);
        Key(ScanP, 'p', ModAlt);                          // the game's own pause (Alt-P), a key goes on
        Thread.Sleep(800);
        Key(ScanSpace, ' ');
        Thread.Sleep(800);
        Key(ScanT, 't', ModAlt);                          // the settings screen: the game is paused while it is open
        Thread.Sleep(800);
        Key(ScanRight, KeyRight);                         // the picture filter
        Key(ScanDown, KeyDown);
        Key(ScanRight, KeyRight);                         // the window's size: the window is resized
        Thread.Sleep(600);
        Key(ScanDown, KeyDown);
        Key(ScanDown, KeyDown);
        Key(ScanDown, KeyDown);
        Key(ScanRight, KeyRight);                         // the interface: Comfort
        Key(ScanDown, KeyDown);
        Key(ScanRight, KeyRight);                         // the rules of new games
        for (var i = 0; i < 6; i++)
        {
            Key(ScanDown, KeyDown);
        }
        Key(ScanRight, KeyRight);                         // clean drawing
        Thread.Sleep(500);
        Key(ScanEscape, 27);                              // closed: the game goes on
        Thread.Sleep(800);
        Mouse(400, 250);
        Key(Scan1, Key1);                                 // the card's first option (Comfort's card menu keys)
        Thread.Sleep(2000);
        Key(ScanReturn, 13);
        Thread.Sleep(1500);
        Key(ScanS, 's', ModAlt);                          // a save
        Thread.Sleep(800);
        Key(ScanReturn, 13);
        Thread.Sleep(2000);
        // DARKLANDS_RECORDING_MEASURE=<seconds> (a developer's measure of the tape's size): that long more of a moving mouse and a key
        //   every few seconds, as a player's hand, before the recording is taken
        if (int.TryParse(Environment.GetEnvironmentVariable("DARKLANDS_RECORDING_MEASURE"), out var seconds))
        {
            var end = DateTime.UtcNow.AddSeconds(seconds);
            for (var n = 0; DateTime.UtcNow < end; n++)
            {
                Mouse(200 + 150 * MathF.Sin(n / 20f), 200 + 100 * MathF.Cos(n / 31f));
                if (n % 60 == 59)
                {
                    Key(ScanSpace, ' ');
                }
            }
        }
        Key(SdlNative.ScancodeF12, SdlNative.KeyF12, ModCtrl);  // the recording, as a problem report takes it
        Thread.Sleep(800);
        var quit = new SdlNative.SdlEvent { Type = SdlNative.EventQuit };
        SdlNative.SDL_PushEvent(ref quit);
    }

    private static void Key(int scancode, int key, ushort mod = 0)
    {
        foreach (var down in new[] { true, false })
        {
            var e = new SdlNative.SdlEvent();
            e.Keyboard.Type = down ? SdlNative.EventKeyDown : SdlNative.EventKeyUp;
            e.Keyboard.Scancode = scancode;
            e.Keyboard.Key = key;
            e.Keyboard.Mod = mod;
            e.Keyboard.Down = (byte)(down ? 1 : 0);
            SdlNative.SDL_PushEvent(ref e);
            Thread.Sleep(120);
        }
    }

    private static void Mouse(float x, float y)
    {
        var e = new SdlNative.SdlEvent();
        e.Motion.Type = SdlNative.EventMouseMotion;
        e.Motion.X = x;
        e.Motion.Y = y;
        SdlNative.SDL_PushEvent(ref e);
        Thread.Sleep(100);
    }
}
