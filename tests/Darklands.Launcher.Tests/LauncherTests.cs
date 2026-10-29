using Darklands.Platform;
using Darklands.Rendering;

namespace Darklands.Launcher.Tests;

/// <summary>A surface that replays scripted input and keeps the frames it is shown; closes when the script ends.</summary>
internal sealed class ScriptedSurface(int width, int height, params LauncherInput[] script) : ILauncherSurface
{
    private readonly Queue<LauncherInput> _script = new(script);

    public (int Width, int Height) Size { get; set; } = (width, height);

    public (int Width, int Height)? DisplaySize { get; set; } = (1920, 1080);

    public List<uint[]> Frames { get; } = [];

    public bool TextInputOn { get; private set; }

    public string? ClipboardText { get; set; }

    public void Present(ReadOnlySpan<uint> pixels) => Frames.Add(pixels.ToArray());

    public IReadOnlyList<LauncherInput> Wait() => [_script.Count > 0 ? _script.Dequeue() : new CloseRequested()];

    public void TextInput(bool on) => TextInputOn = on;

    public string? Clipboard() => ClipboardText;

    public Func<string, string?>? PickFolder { get; set; }
}

/// <summary>A sound test that notes what it was asked to play, and plays until stopped.</summary>
internal sealed class FakeSoundTest(List<string?> played) : ISoundTest
{
    public bool Playing { get; private set; }

    public string? Status => Playing ? "Playing." : null;

    public void Play(SoundTestRequest request)
    {
        played.Add(request.Card);
        Playing = true;
    }

    public void Stop()
    {
        played.Add("stop");
        Playing = false;
    }
}

public sealed class LauncherTests
{
    private const string Game = @"D:\GOG Games\Darklands";
    private const string Notes = "## 1.0\n\n- The launcher: settings before the game.\n- A longer note that runs over more than one line, to see how it wraps under the first.\n- A fix.\n\n## 0.9.1\n\n- Older.";

    private static readonly KeyPressed Tab = new(LauncherKey.Tab);
    private static readonly KeyPressed Up = new(LauncherKey.Up);
    private static readonly KeyPressed Down = new(LauncherKey.Down);
    private static readonly KeyPressed Right = new(LauncherKey.Right);
    private static readonly KeyPressed Enter = new(LauncherKey.Enter);
    private static readonly KeyPressed Escape = new(LauncherKey.Escape);

    private static LauncherContext Context(GameSettings? settings = null, LauncherReason reason = LauncherReason.Asked,
        IReadOnlyDictionary<string, string>? fixedKeys = null, List<string>? links = null, List<string?>? tests = null) => new(
        settings ?? new GameSettings { GameFolder = Game },
        fixedKeys ?? new Dictionary<string, string>(),
        "1.0",
        reason,
        null,
        folder => folder.TrimEnd('\\', '/').EndsWith("Darklands", StringComparison.OrdinalIgnoreCase) ? folder : null,
        () => [(Game, "GOG's registration"), (@"C:\Steam\steamapps\common\Darklands", "Steam's library")],
        folder => new RomStatus(folder ?? "MT32_ROMS", folder is null || folder.EndsWith("ROMS"), false),
        tests is null ? null : new FakeSoundTest(tests),
        Notes,
        link => links?.Add(link));

    private static LauncherContext Nothing() => new(new GameSettings(), new Dictionary<string, string>(), "1.0", LauncherReason.GameNotFound,
        null, _ => null, () => [], _ => new RomStatus("MT32_ROMS", false, false), null, "## 1.0\n- First note.", _ => { });

    private static LauncherResult Run(LauncherContext context, params LauncherInput[] script) =>
        new Launcher().Run(context, new ScriptedSurface(1440, 800, script));

    // from Play (the first focus): Discord, the game folder, Change, then the window size
    private static readonly LauncherInput[] ToWindow = [Tab, Tab, Tab, Tab];

    [Fact]
    public void ClosingAtOnceChangesNothing()
    {
        var result = Run(Context());
        Assert.Equal(LauncherChoice.Quit, result.Choice);
        Assert.Empty(result.Changes);
        Assert.Equal(Game, result.Settings.GameFolder);
    }

    [Fact]
    public void EnterOnPlayStartsTheGame()
    {
        Assert.Equal(LauncherChoice.Play, Run(Context(), Enter).Choice);
    }

    [Fact]
    public void TheArrowsChangeTheSettingsAndTheChangesAreReported()
    {
        var result = Run(Context(), [.. ToWindow,
            Right,              // window: 720x400 -> 1080x600
            Down, Enter,        // fullscreen on
            Down, Down,
            Right, Right,       // sound: AdLib, then Sound Blaster
            Down, Down, Down, Enter,  // past the ROMs and the audio pack: the start-up screen off
            Escape]);
        Assert.Equal(LauncherChoice.Quit, result.Choice);
        Assert.Equal("1080x600", (string)result.Changes["display.resolution"]!);
        Assert.True((bool)result.Changes["display.fullscreen"]!);
        Assert.Equal("sb", (string)result.Changes["sound.card"]!);
        Assert.False((bool)result.Changes["startup.banner"]!);
        Assert.Equal("sb", result.Settings.SoundCard);
        Assert.False(result.Settings.Banner);
    }

    [Fact]
    public void AWholeMultipleOfTheGamesSizeIsSavedAsAScale()
    {
        var result = Run(Context(), [.. ToWindow, Right, Right]);
        Assert.Equal(2, (int)result.Changes["display.scale"]!);
        Assert.Equal("", (string)result.Changes["display.resolution"]!);
        Assert.Equal(2, result.Settings.Scale);
    }

    [Fact]
    public void WithoutAGameFolderTheFirstFoundIsTakenAndPlayWaitsForAValidOne()
    {
        var result = Run(Context(new GameSettings(), LauncherReason.GameNotFound), Enter);
        Assert.Equal(LauncherChoice.Play, result.Choice);
        Assert.Equal(Game.Replace('\\', '/'), (string)result.Changes["gameFolder"]!);

        var refused = Run(Nothing(), [.. Enumerable.Repeat<LauncherInput>(Up, 2), Enter]);
        Assert.Equal(LauncherChoice.Quit, refused.Choice);
        Assert.Empty(refused.Changes);
    }

    [Fact]
    public void ATypedFolderIsCheckedAndADroppedOneTaken()
    {
        var surface = new ScriptedSurface(1440, 800,
            Tab, Tab, Enter,                       // Play, Discord, the game folder: edit it
            new TextTyped(@"\x"), new KeyPressed(LauncherKey.Backspace), new KeyPressed(LauncherKey.Backspace),
            Enter);
        var typed = new Launcher().Run(Context(new GameSettings { GameFolder = @"D:\GOG Games\Darklands" }), surface);
        Assert.False(surface.TextInputOn);
        Assert.Empty(typed.Changes);

        var pasted = new ScriptedSurface(1440, 800, Tab, Tab, new KeyPressed(LauncherKey.Paste), Enter) { ClipboardText = @"E:\Copy\Darklands" };
        Assert.Equal("E:/Copy/Darklands", (string)new Launcher().Run(Context(), pasted).Changes["gameFolder"]!);

        var dropped = Run(Context(), new PathDropped(@"E:\Other\Darklands"));
        Assert.Equal("E:/Other/Darklands", (string)dropped.Changes["gameFolder"]!);
    }

    [Fact]
    public void AFixedSettingIsShownButNotChanged()
    {
        var fixedKeys = new Dictionary<string, string> { ["display.fullscreen"] = "--fullscreen" };
        var result = Run(Context(fixedKeys: fixedKeys), [.. ToWindow, Down, Enter]);
        Assert.False(result.Changes.ContainsKey("display.fullscreen"));
    }

    [Fact]
    public void TheLinkAndTheSoundTestGoToTheHost()
    {
        var links = new List<string>();
        var tests = new List<string?>();
        // Play, Discord (open it), then back up past Play, Save, every start, Opening animation, start-up screen, audio pack to the sound test
        Run(Context(links: links, tests: tests), Tab, Enter, Up, Up, Up, Up, Up, Up, Up, Enter, Enter, Enter);
        Assert.Equal([LauncherScreen.DiscordLink], links);
        Assert.Equal(["config", "stop", "config"], tests);
    }

    [Fact]
    public void TheLauncherAtEveryStartIsASetting()
    {
        // up from Play: Save, then this screen at every start
        var result = Run(Context(), Up, Up, Enter);
        Assert.True((bool)result.Changes["startup.launcher"]!);
        Assert.True(result.Settings.Launcher);
    }

    [Fact]
    public void ARomFolderIsDroppedOrPicked()
    {
        var result = Run(Context(), new PathDropped(@"E:\MY_ROMS"));
        Assert.Equal("E:/MY_ROMS", (string)result.Changes["sound.mt32Roms"]!);
        Assert.Equal(@"E:\MY_ROMS", result.Settings.Mt32Roms);
        // the window size, Fullscreen, HD pack, Sound card, then the ROMs
        var picked = new ScriptedSurface(1440, 800, [.. ToWindow, Down, Down, Down, Down, Enter]) { PickFolder = _ => @"F:\OTHER_ROMS" };
        Assert.Equal("F:/OTHER_ROMS", (string)new Launcher().Run(Context(), picked).Changes["sound.mt32Roms"]!);
    }

    [Fact]
    public void AClickOnFullscreenTurnsItOn()
    {
        var surface = new ScriptedSurface(1080, 600);
        var screen = new LauncherScreen(new LauncherModel(Context(), null), Context(), surface);
        screen.Render(new Drawing.Canvas(1080, 600));
        var row = screen.Area(LauncherScreen.Item.Fullscreen);
        var clicked = new Launcher().Run(Context(), new ScriptedSurface(1080, 600, new PointerPressed(row.X + row.Width / 2, row.Y + row.Height / 2)));
        Assert.True((bool)clicked.Changes["display.fullscreen"]!);
    }

    [Fact]
    public void ReleaseNotesShowTheirItemsShortened()
    {
        var items = ReleaseNotes.Items("""
            A bug-fix release.

            ## Fixed

            - **Crash when the world map opens** (`DivideByZeroException`). The seasonal colours divided by zero.
            - The Quickstart party is no longer drawn black. Parties saved by 0.9.0 get their colours back.
            - A [linked](https://example.org) fix

            ## Getting started

            1. Download.
            - Not an item of the notes.
            """);
        Assert.Equal(["Crash when the world map opens", "The Quickstart party is no longer drawn black.", "A linked fix"], items);
    }

    /// <summary>Two pack folders in a packs folder: one of sounds, one of pictures.</summary>
    private sealed class Packs : IDisposable
    {
        public Packs()
        {
            Directory.CreateDirectory(Sounds);
            File.WriteAllBytes(Path.Combine(Sounds, "inn-1.ogg"), [0]);
            Directory.CreateDirectory(Pictures);
            File.WriteAllBytes(Path.Combine(Pictures, "COMMONSP.IMG#12.png"), [0]);
        }

        public string Root { get; } = Directory.CreateTempSubdirectory("dkpacks").FullName;

        public string Sounds => Path.Combine(Root, "packs", "a-sounds");

        public string Pictures => Path.Combine(Root, "packs", "b-pictures");

        public IReadOnlyList<string> Find(string? gameFolder) => [Sounds, Pictures];

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    // from Play: Discord, the game folder, Change, window, fullscreen, HD pack, sound card, ROMs, then the audio pack (no sound test)
    private static readonly LauncherInput[] ToAudioPack = [Tab, Tab, Tab, Tab, Tab, Tab, Tab, Tab, Tab];

    [Fact]
    public void ThePackRowsGoThroughNoneAndThePacksFoundAndTheSettingsKeepThem()
    {
        using var packs = new Packs();
        var context = Context() with { FindPacks = packs.Find };
        var sound = Run(context, [.. ToAudioPack, Right]);
        Assert.Equal(packs.Sounds.Replace('\\', '/'), (string)sound.Changes["sound.audioPack"]!);
        Assert.Equal(packs.Sounds, sound.Settings.AudioPack);
        Assert.False(sound.Changes.ContainsKey("display.hdPack"));

        // the picture pack row offers only the folder with pictures
        var picture = Run(context, Tab, Tab, Tab, Tab, Tab, Tab, Right);
        Assert.Equal(packs.Pictures.Replace('\\', '/'), (string)picture.Changes["display.hdPack"]!);

        // the settings' pack comes back unchanged; None turns it off
        var kept = Context(new GameSettings { GameFolder = Game, AudioPack = packs.Sounds }) with { FindPacks = packs.Find };
        var unchanged = Run(kept);
        Assert.Empty(unchanged.Changes);
        Assert.Equal(packs.Sounds, unchanged.Settings.AudioPack);
        var off = Run(kept, [.. ToAudioPack, new KeyPressed(LauncherKey.Left)]);
        Assert.Equal("", (string)off.Changes["sound.audioPack"]!);
        Assert.Null(off.Settings.AudioPack);
    }

    [Fact]
    public void APackIsPickedOrDroppedByWhatItHolds()
    {
        using var packs = new Packs();
        var picked = new ScriptedSurface(1440, 800, [.. ToAudioPack, Enter]) { PickFolder = _ => packs.Sounds };
        Assert.Equal(packs.Sounds, new Launcher().Run(Context(), picked).Settings.AudioPack);
        var wrong = new ScriptedSurface(1440, 800, [.. ToAudioPack, Enter]) { PickFolder = _ => packs.Pictures };
        Assert.Empty(new Launcher().Run(Context(), wrong).Changes);

        var dropped = Run(Context(), new PathDropped(packs.Pictures), new PathDropped(packs.Sounds));
        Assert.Equal(packs.Pictures, dropped.Settings.HdPack);
        Assert.Equal(packs.Sounds, dropped.Settings.AudioPack);

        // a pack fixed by the command line is not changed
        var fixedKeys = new Dictionary<string, string> { ["sound.audioPack"] = "--audio-pack" };
        Assert.Empty(Run(Context(fixedKeys: fixedKeys), new PathDropped(packs.Sounds)).Changes);
    }

    [Fact]
    public void TheGuideLinkOpensTheGuide()
    {
        var links = new List<string>();
        // Play, Discord, then the guide
        Run(Context(links: links) with { PackGuide = "https://example.org/PACKS.md" }, Tab, Tab, Enter);
        Assert.Equal(["https://example.org/PACKS.md"], links);
    }

    // ---- the game folder given at the start, typing, pasting and the folder dialog (found on Linux, 2026-10-29) ----

    private const string Given = "/home/player/Games/Darklands";

    private static readonly Dictionary<string, string> ByCommandLine = new() { ["gameFolder"] = "the command line" };

    [Fact]
    public void AFolderGivenOnTheCommandLineIsShownAndPlayStartsTheGame()
    {
        // the file names none (a first run): the folder given is shown, Play (the first focus) starts, and the file gets it
        var surface = new ScriptedSurface(1440, 800, Enter);
        var context = Context(new GameSettings(), LauncherReason.FirstRun, ByCommandLine) with { GivenGameFolder = Given };
        var model = new LauncherModel(context, null);
        Assert.Equal(Given, model.GameFolder);
        Assert.True(model.CanPlay);
        var result = new Launcher().Run(context, surface);
        Assert.Equal(LauncherChoice.Play, result.Choice);
        Assert.Equal(Given, result.Settings.GameFolder);
        Assert.Equal(Given, (string)result.Changes["gameFolder"]!);

        // closed without Play: nothing is saved (so the host can show the launcher again at the next start)
        Assert.Empty(new Launcher().Run(context, new ScriptedSurface(1440, 800, Escape)).Changes);
        var found = Context(new GameSettings(), LauncherReason.FirstRun);
        Assert.Empty(new Launcher().Run(found, new ScriptedSurface(1440, 800, Escape)).Changes);

        // the file names another folder: the given one is played, the file's is not replaced
        var other = Run(Context(fixedKeys: ByCommandLine) with { GivenGameFolder = Given }, Enter);
        Assert.Equal(LauncherChoice.Play, other.Choice);
        Assert.Equal(Given, other.Settings.GameFolder);
        Assert.False(other.Changes.ContainsKey("gameFolder"));
    }

    [Fact]
    public void AFolderGivenWithoutTheGameCanBeReplacedOnTheLauncher()
    {
        // the host does not fix a given folder that holds no game: the player types another, then plays
        var context = Context(new GameSettings(), LauncherReason.GameNotFound) with { GivenGameFolder = "/home/player/nothing" } with
        {
            FoundGameFolders = () => [],
        };
        var result = Run(context, new TextTyped(Given), Enter, Enter);
        Assert.Equal(LauncherChoice.Play, result.Choice);
        Assert.Equal(Given, result.Settings.GameFolder);
    }

    [Fact]
    public void TypingOnTheFocusedFolderEditsItWithoutAClickFirst()
    {
        // nothing found: the focus is on the game folder; the keys typed replace it
        var context = Context(new GameSettings(), LauncherReason.GameNotFound) with { FoundGameFolders = () => [] };
        var surface = new ScriptedSurface(1440, 800, new TextTyped("/home/player/"), new TextTyped("Games/Darklands"), Enter, Enter);
        var result = new Launcher().Run(context, surface);
        Assert.Equal(LauncherChoice.Play, result.Choice);
        Assert.Equal(Given, result.Settings.GameFolder);
        Assert.False(surface.TextInputOn);
    }

    [Fact]
    public void APastedFolderIsCleanedOfQuotesLinesAndLinks()
    {
        Assert.Equal(Given, LauncherScreen.CleanPath("'" + Given + "'\n"));
        Assert.Equal(Given, LauncherScreen.CleanPath("\n  \"" + Given + "\"  \r\nsecond line"));
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Games/DARKLAND"),
            LauncherScreen.CleanPath("~/Games/DARKLAND"));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal("/home/player/My Games/Darklands", LauncherScreen.CleanPath("file:///home/player/My%20Games/Darklands"));
        }
        else
        {
            Assert.Equal(@"C:\My Games\Darklands", LauncherScreen.CleanPath("file:///C:/My%20Games/Darklands"));
        }

        // Ctrl+V on the folder (not editing yet): the clipboard's folder, then Enter
        var context = Context(new GameSettings(), LauncherReason.GameNotFound) with { FoundGameFolders = () => [] };
        var surface = new ScriptedSurface(1440, 800, new KeyPressed(LauncherKey.Paste, Control: true), Enter, Enter) { ClipboardText = "\"" + Given + "\"\n" };
        Assert.Equal(Given, new Launcher().Run(context, surface).Settings.GameFolder);
    }

    [Fact]
    public void WithoutAFolderDialogChangeGoesToTyping()
    {
        // no dialog at all (null), or one that could not be shown (it throws): Change… starts typing in the field
        foreach (var pick in new Func<string, string?>?[] { null, _ => throw new NotSupportedException("No folder dialog on this system.") })
        {
            var context = Context(new GameSettings(), LauncherReason.GameNotFound) with { FoundGameFolders = () => [] };
            var surface = new ScriptedSurface(1080, 600) { PickFolder = pick };
            var screen = new LauncherScreen(new LauncherModel(context, null), context, surface);
            screen.Render(new Drawing.Canvas(1080, 600));
            var change = screen.Area(LauncherScreen.Item.Change);
            screen.Handle(new PointerPressed(change.X + change.Width / 2, change.Y + change.Height / 2));
            Assert.True(screen.Editing);
            Assert.True(surface.TextInputOn);
            Assert.Equal(LauncherScreen.Item.GameFolder, screen.Focus);
            screen.Handle(new TextTyped(Given));
            Assert.Null(screen.Handle(Enter));
            Assert.False(screen.Editing);
            Assert.Equal(LauncherChoice.Play, screen.Handle(Enter));
        }

        // a dialog: its folder is taken, a cancel changes nothing
        var picked = new ScriptedSurface(1440, 800, Tab, Tab, Tab, Enter) { PickFolder = _ => Given };
        Assert.Equal(Given, (string)new Launcher().Run(Context(), picked).Changes["gameFolder"]!);
        var cancelled = new ScriptedSurface(1440, 800, Tab, Tab, Tab, Enter) { PickFolder = _ => null };
        Assert.Empty(new Launcher().Run(Context(), cancelled).Changes);
    }

    [Fact]
    public void AFixedFolderSaysWhereItIsSet()
    {
        var context = Context(fixedKeys: ByCommandLine) with { GivenGameFolder = Given };
        var surface = new ScriptedSurface(1080, 600) { PickFolder = _ => throw new InvalidOperationException("not to be called") };
        var screen = new LauncherScreen(new LauncherModel(context, null), context, surface);
        screen.Render(new Drawing.Canvas(1080, 600));
        var field = screen.Area(LauncherScreen.Item.GameFolder);
        screen.Handle(new PointerPressed(field.X + 2, field.Y + 2));
        Assert.False(screen.Editing);
        screen.Handle(new TextTyped("x"));
        Assert.False(screen.Editing);
        var change = screen.Area(LauncherScreen.Item.Change);
        screen.Handle(new PointerPressed(change.X + 2, change.Y + 2));
        Assert.False(screen.Editing);
    }

    /// <summary>DARKLANDS_LAUNCHER_SHOTS=folder: writes the screen at a few sizes and states, to look at.</summary>
    [Fact]
    public void Screenshots()
    {
        if (Environment.GetEnvironmentVariable("DARKLANDS_LAUNCHER_SHOTS") is not { Length: > 0 } folder)
        {
            return;
        }
        Directory.CreateDirectory(folder);
        void Shot(string name, LauncherContext context, int width, int height, params LauncherInput[] script)
        {
            var surface = new ScriptedSurface(width, height, script);
            new Launcher().Run(context, surface);
            PngCodec.Write(new RgbaImage(width, height, surface.Frames[^1]), Path.Combine(folder, name + ".png"));
        }
        Shot("asked-1440x800", Context(tests: []), 1440, 800, [.. ToWindow, Escape]);
        Shot("packs-1080x600", Context(tests: [], settings: new GameSettings { GameFolder = Game, AudioPack = Path.GetTempPath(), HdPack = @"C:\Packs\hd" })
            with { PackGuide = "https://example.org/PACKS.md", DescribePack = _ => new PackStatus(0, 3, null) }, 1080, 600, [.. ToAudioPack, Tab, Tab, Escape]);
        Shot("roms-help-1080x600", Context(tests: []) with { PackGuide = "https://example.org/PACKS.md" }, 1080, 600, [.. ToAudioPack, Escape]);
        Shot("notfound-1080x600", Nothing(), 1080, 600, Escape);
        Shot("typing-720x400", Context(), 720, 400, Tab, Tab, Enter, new TextTyped(@"Q:\Games"));
        Shot("wide-1920x1080", Context(), 1920, 1080, Escape);
        var real = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "releases", "0.9.1", "release-notes.md");
        var link = "https://github.com/Arkana-Mechanika-Studios/darklands-restoration/releases/tag/v0.9.1";
        if (File.Exists(real))
        {
            Shot("real-notes-1440x800", Context(tests: []) with { ReleaseNotes = File.ReadAllText(real), ReleaseNotesLink = link, Version = "0.9.1" }, 1440, 800, Escape);
        }
        Shot("no-notes-1440x800", Context() with { ReleaseNotes = "", ReleaseNotesLink = link, Version = "0.9.1" }, 1440, 800, Tab, Tab, Escape);
    }
}
