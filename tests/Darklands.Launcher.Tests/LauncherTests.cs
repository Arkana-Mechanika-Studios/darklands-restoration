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
            Down, Down, Enter,  // past the ROMs: the start-up screen off
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
        // Play, Discord (open it), then back up past Play, Save, every start, Opening animation, start-up screen to the sound test
        Run(Context(links: links, tests: tests), Tab, Enter, Up, Up, Up, Up, Up, Up, Enter, Enter, Enter);
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
