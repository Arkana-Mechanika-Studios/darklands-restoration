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

    public Func<string, string, string?>? PickFolder { get; set; }

    public Func<string, string, string?>? PickFile { get; set; }

    public bool Frameless { get; set; }

    public bool FillsScreen { get; set; }

    public int Minimised { get; private set; }

    public void Minimise() => Minimised++;

    public IReadOnlyList<(int X, int Y, int Width, int Height)> ControlAreas { get; private set; } = [];

    public void Controls(IReadOnlyList<(int X, int Y, int Width, int Height)> areas) => ControlAreas = areas;
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
            Down, Down, Down,   // past the picture filter and the HD pack
            Right, Right,       // sound: AdLib, then Sound Blaster
            Down, Down, Down, Down, Enter,  // past the audio pack, the ROMs (Advanced opens) and the SoundFont: the start-up screen off
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
        // Play, Discord (open it), then back up past Play, Save, the mouse wheel, Opening animation, start-up screen, SoundFont, ROMs, audio pack to the sound test
        Run(Context(links: links, tests: tests), Tab, Enter, Up, Up, Up, Up, Up, Up, Up, Up, Up, Up, Enter, Enter, Enter);
        Assert.Equal([LauncherScreen.DiscordLink], links);
        Assert.Equal(["config", "stop", "config"], tests);
    }

    [Fact]
    public void TheMouseWheelInMenusIsASettingOnByDefault()
    {
        // up from Play: Save, the card menu keys, then the mouse wheel (on: the file has no input.mouseWheel)
        var kept = Run(Context(), Up, Up, Up, Right, Right);
        Assert.False(kept.Changes.ContainsKey("input.mouseWheel"));
        Assert.True(kept.Settings.MouseWheel);
        var off = Run(Context(), Up, Up, Up, Enter);
        Assert.False((bool)off.Changes["input.mouseWheel"]!);
        Assert.False(off.Settings.MouseWheel);
    }

    [Fact]
    public void TheMouseWheelFixedByTheCommandLineIsShownButNotChanged()
    {
        var fixedKeys = new Dictionary<string, string> { ["input.mouseWheel"] = "--no-mouse-wheel" };
        var result = Run(Context(fixedKeys: fixedKeys), Up, Up, Up, Enter);
        Assert.False(result.Changes.ContainsKey("input.mouseWheel"));
    }

    [Fact]
    public void TheCardMenuKeysAreASettingOnByDefault()
    {
        // up from Play: Save, then the card menu keys (on: the file has no input.cardMenuKeys)
        var kept = Run(Context(), Up, Up, Right, Right);
        Assert.False(kept.Changes.ContainsKey("input.cardMenuKeys"));
        Assert.True(kept.Settings.CardMenuKeys);
        var off = Run(Context(), Up, Up, Enter);
        Assert.False((bool)off.Changes["input.cardMenuKeys"]!);
        Assert.False(off.Settings.CardMenuKeys);
        var fixedKeys = new Dictionary<string, string> { ["input.cardMenuKeys"] = "--no-card-menu-keys" };
        Assert.False(Run(Context(fixedKeys: fixedKeys), Up, Up, Enter).Changes.ContainsKey("input.cardMenuKeys"));
    }

    [Fact]
    public void ARomFolderIsDroppedOrPicked()
    {
        var result = Run(Context(), new PathDropped(@"E:\MY_ROMS"));
        Assert.Equal("E:/MY_ROMS", (string)result.Changes["sound.mt32Roms"]!);
        Assert.Equal(@"E:\MY_ROMS", result.Settings.Mt32Roms);
        // the window size, Fullscreen, HD pack, Sound card, audio pack, then the ROMs (on Advanced)
        var picked = new ScriptedSurface(1440, 800, [.. ToWindow, Down, Down, Down, Down, Down, Down, Enter]) { PickFolder = (_, _) => @"F:\OTHER_ROMS" };
        Assert.Equal("F:/OTHER_ROMS", (string)new Launcher().Run(Context(), picked).Changes["sound.mt32Roms"]!);
    }

    /// <summary>
    /// The player's request (2026-11-08): a dialog opens at what its row names when that exists (a folder, or a file shown in its
    /// folder), else in the program's own folder.
    /// </summary>
    [Fact]
    public void DialogsOpenAtTheRowsFolderElseInTheProgramsFolder()
    {
        var program = AppContext.BaseDirectory;
        var folder = Directory.CreateTempSubdirectory("dialog-start").FullName;
        try
        {
            var file = Path.Combine(folder, "mine.sf2");
            File.WriteAllText(file, "x");
            Assert.Equal(program, LauncherScreen.DialogStart(null, program));
            Assert.Equal(program, LauncherScreen.DialogStart("", program));
            Assert.Equal(program, LauncherScreen.DialogStart(Path.Combine(folder, "gone"), program));
            Assert.Equal(folder, LauncherScreen.DialogStart(folder, program));
            Assert.Equal(file, LauncherScreen.DialogStart("\"" + file + "\"", program));

            // the ROM row: none named, then a folder that exists
            var starts = new List<string>();
            LauncherInput[] toRoms = [.. ToWindow, Down, Down, Down, Down, Down, Down, Enter];
            new Launcher().Run(Context(), new ScriptedSurface(1440, 800, toRoms) { PickFolder = (_, start) => { starts.Add(start); return null; } });
            new Launcher().Run(Context(new GameSettings { Mt32Roms = folder }), new ScriptedSurface(1440, 800, toRoms) { PickFolder = (_, start) => { starts.Add(start); return null; } });
            Assert.Equal([program, folder], starts);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
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

    /// <summary>The What's new card's line under each item: what follows the bold lead, up to its first "(", ";", ":" or sentence end.</summary>
    [Fact]
    public void ReleaseNotesGiveEachBoldItemALineOfDetail()
    {
        var items = ReleaseNotes.Detailed("""
            ## New

            - **A new launcher.** A painted window without a title bar (drag it by the painting), the settings on two tabs.
            - **Picture filters.** How the 320×200 picture is drawn on a large window: **Sharp**, **Even**.
            - **No console window.** The game opens only its own window; `--log-window` shows the messages.
            - **Buttons stay pressed long enough to see** (at least a tenth of a second), as they did.
            - A plain item. With two sentences.

            ## Files

            - **Not shown.** Past the end.
            """);
        Assert.Equal(
            [
                ("A new launcher", "A painted window without a title bar"),
                ("Picture filters", "How the 320×200 picture is drawn on a large window"),
                ("No console window", "The game opens only its own window"),
                ("Buttons stay pressed long enough to see", ""),
                ("A plain item.", ""),
            ],
            items);
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

    // from Play: Discord, the game folder, Change, window, fullscreen, picture, HD pack, sound card, then the audio pack (no sound test)
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
        var picture = Run(context, Tab, Tab, Tab, Tab, Tab, Tab, Tab, Right);
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
        var picked = new ScriptedSurface(1440, 800, [.. ToAudioPack, Enter]) { PickFolder = (_, _) => packs.Sounds };
        Assert.Equal(packs.Sounds, new Launcher().Run(Context(), picked).Settings.AudioPack);
        var wrong = new ScriptedSurface(1440, 800, [.. ToAudioPack, Enter]) { PickFolder = (_, _) => packs.Pictures };
        Assert.Empty(new Launcher().Run(Context(), wrong).Changes);

        var dropped = Run(Context(), new PathDropped(packs.Pictures), new PathDropped(packs.Sounds));
        Assert.Equal(packs.Pictures, dropped.Settings.HdPack);
        Assert.Equal(packs.Sounds, dropped.Settings.AudioPack);

        // a pack fixed by the command line is not changed
        var fixedKeys = new Dictionary<string, string> { ["sound.audioPack"] = "--audio-pack" };
        Assert.Empty(Run(Context(fixedKeys: fixedKeys), new PathDropped(packs.Sounds)).Changes);
    }

    [Fact]
    public void TheSoundFontRowPicksOrDropsAnSf2FileAndLeftGoesBackToTheDefault()
    {
        var folder = Directory.CreateTempSubdirectory("dksf").FullName;
        try
        {
            var font = Path.Combine(folder, "mine.sf2");
            File.WriteAllText(font, "x");
            var text = Path.Combine(folder, "notes.txt");
            File.WriteAllText(text, "x");
            LauncherInput[] toSoundFont = [.. ToAudioPack, Tab, Tab];
            var picked = new ScriptedSurface(1440, 800, [.. toSoundFont, Enter]) { PickFile = (_, _) => font };
            var result = new Launcher().Run(Context(), picked);
            Assert.Equal(font, result.Settings.SoundFont);
            Assert.Equal(font.Replace('\\', '/'), (string)result.Changes["sound.soundFont"]!);

            // a file that is not a SoundFont is refused; a dropped .sf2 is taken
            Assert.Empty(new Launcher().Run(Context(), new ScriptedSurface(1440, 800, [.. toSoundFont, Enter]) { PickFile = (_, _) => text }).Changes);
            Assert.Equal(font, Run(Context(), new PathDropped(font)).Settings.SoundFont);

            // left: the default again; the settings' file comes back unchanged
            var kept = Context(new GameSettings { GameFolder = Game, SoundFont = font });
            Assert.Empty(Run(kept).Changes);
            var off = Run(kept, [.. toSoundFont, new KeyPressed(LauncherKey.Left)]);
            Assert.Equal("", (string)off.Changes["sound.soundFont"]!);
            Assert.Null(off.Settings.SoundFont);

            // fixed by --soundfont: not changed
            var fixedKeys = new Dictionary<string, string> { ["sound.soundFont"] = "--soundfont" };
            Assert.Empty(Run(Context(fixedKeys: fixedKeys), new PathDropped(font)).Changes);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
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
        foreach (var pick in new Func<string, string, string?>?[] { null, (_, _) => throw new NotSupportedException("No folder dialog on this system.") })
        {
            var context = Context(new GameSettings(), LauncherReason.GameNotFound) with { FoundGameFolders = () => [] };
            var surface = new ScriptedSurface(1080, 600) { PickFolder = pick };
            var screen = new LauncherScreen(new LauncherModel(context, null), context, surface);
            screen.Render(new Drawing.Canvas(1080, 600));
            var change = screen.Area(LauncherScreen.Item.Change);
            screen.Handle(new PointerPressed(change.X + change.Width / 2, change.Y + change.Height / 2));
            screen.Handle(new PointerReleased(change.X + change.Width / 2, change.Y + change.Height / 2));
            Assert.True(screen.Editing);
            Assert.True(surface.TextInputOn);
            Assert.Equal(LauncherScreen.Item.GameFolder, screen.Focus);
            screen.Handle(new TextTyped(Given));
            Assert.Null(screen.Handle(Enter));
            Assert.False(screen.Editing);
            Assert.Equal(LauncherChoice.Play, screen.Handle(Enter));
        }

        // a dialog: its folder is taken, a cancel changes nothing
        var picked = new ScriptedSurface(1440, 800, Tab, Tab, Tab, Enter) { PickFolder = (_, _) => Given };
        Assert.Equal(Given, (string)new Launcher().Run(Context(), picked).Changes["gameFolder"]!);
        var cancelled = new ScriptedSurface(1440, 800, Tab, Tab, Tab, Enter) { PickFolder = (_, _) => null };
        Assert.Empty(new Launcher().Run(Context(), cancelled).Changes);
    }

    [Fact]
    public void AFixedFolderSaysWhereItIsSet()
    {
        var context = Context(fixedKeys: ByCommandLine) with { GivenGameFolder = Given };
        var surface = new ScriptedSurface(1080, 600) { PickFolder = (_, _) => throw new InvalidOperationException("not to be called") };
        var screen = new LauncherScreen(new LauncherModel(context, null), context, surface);
        screen.Render(new Drawing.Canvas(1080, 600));
        var field = screen.Area(LauncherScreen.Item.GameFolder);
        screen.Handle(new PointerPressed(field.X + 2, field.Y + 2));
        Assert.False(screen.Editing);
        screen.Handle(new TextTyped("x"));
        Assert.False(screen.Editing);
        var change = screen.Area(LauncherScreen.Item.Change);
        screen.Handle(new PointerPressed(change.X + 2, change.Y + 2));
        screen.Handle(new PointerReleased(change.X + 2, change.Y + 2));
        Assert.False(screen.Editing);
    }

    [Fact]
    public void TheProblemReportLinkAsksTheHostAndShowsItsAnswer()
    {
        var asked = 0;
        var context = Context() with { ReportProblem = () => ++asked == 1 ? "Report saved: X.zip" : "again" };
        var surface = new ScriptedSurface(1080, 600);
        var screen = new LauncherScreen(new LauncherModel(context, null), context, surface);
        screen.Render(new Drawing.Canvas(1080, 600));

        var link = screen.Area(LauncherScreen.Item.Report);
        screen.Handle(new PointerPressed(link.X + 2, link.Y + 2));

        Assert.Equal(1, asked);
        // reachable by the keyboard too: the item is in the tab order
        screen.Handle(Tab);
        var seen = new HashSet<LauncherScreen.Item>();
        for (var i = 0; i < 25; i++)
        {
            seen.Add(screen.Focus);
            screen.Handle(Tab);
        }
        Assert.Contains(LauncherScreen.Item.Report, seen);
    }

    [Fact]
    public void WithoutAReportServiceTheLauncherShowsNoLink()
    {
        var screen = new LauncherScreen(new LauncherModel(Context(), null), Context(), new ScriptedSurface(1080, 600));
        screen.Render(new Drawing.Canvas(1080, 600));
        Assert.Throws<InvalidOperationException>(() => screen.Area(LauncherScreen.Item.Report));
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
            var surface = new ScriptedSurface(width, height, script) { Frameless = true };
            new Launcher().Run(context, surface);
            PngCodec.Write(new RgbaImage(width, height, surface.Frames[^1]), Path.Combine(folder, name + ".png"));
        }
        // the launcher reworked (2026-11-08): as it opens, with broken packs chosen (the audio pack's row focused), and at its smallest size
        var guide = Context(tests: []) with { PackGuide = "https://example.org/PACKS.md", ReportProblem = () => "Report saved.", ReleaseNotesLink = "https://example.org" };
        Shot("normal-1280x720", guide, 1280, 720, Escape);
        // from Play with the three links: Discord, the guide, the report, the notes, then the game folder ... (picture: 9 tabs, HD pack: 10, audio pack: 13)
        static LauncherInput[] Tabs(int count) => [.. Enumerable.Repeat<LauncherInput>(Tab, count), Escape];
        Shot("broken-packs-1280x720", LauncherLayoutTests.Broken(), 1280, 720, Tabs(13));
        // the picture filter's row (beside Fullscreen) chosen with the keyboard, its tooltip, Soft CRT (the longest label) shown; and at the smallest size
        Shot("picture-row-1280x720", guide, 1280, 720, [.. Enumerable.Repeat<LauncherInput>(Tab, 9), Right, Right, Right, Escape]);
        Shot("picture-row-960x540", guide, 960, 540, [.. Enumerable.Repeat<LauncherInput>(Tab, 9), Right, Right, Right, Escape]);
        Shot("advanced-1280x720", guide, 1280, 720, new KeyPressed(LauncherKey.Tab, Control: true), Escape);
        // the Advanced tab with the keyboard on the ROM folder and on the SoundFont: their tooltips clear of the other rows and headings
        Shot("advanced-roms-tooltip-1280x720", guide, 1280, 720, new KeyPressed(LauncherKey.Tab, Control: true));
        Shot("advanced-soundfont-tooltip-1280x720", guide, 1280, 720, new KeyPressed(LauncherKey.Tab, Control: true), Tab);
        Shot("advanced-soundfont-tooltip-960x540", guide, 960, 540, new KeyPressed(LauncherKey.Tab, Control: true), Tab);
        // the What's new card with the release notes of a real release (DARKLANDS_ROOT's releases folder), at the largest and smallest size
        if (Environment.GetEnvironmentVariable("DARKLANDS_ROOT") is { Length: > 0 } root
            && Path.Combine(root, "releases", "1.0.0-rc2", "release-notes.md") is var real2 && File.Exists(real2))
        {
            var notes = guide with { ReleaseNotes = File.ReadAllText(real2), Version = "1.0.0-rc2" };
            Shot("whats-new-1280x720", notes, 1280, 720, Escape);
            Shot("whats-new-960x540", notes, 960, 540, Escape);
        }
        // the pointer resting on a problem line (its tooltip), and on Save and quit (lit; Play, the keyboard's choice, softly)
        var probe = new LauncherScreen(new LauncherModel(LauncherLayoutTests.Broken(), null), LauncherLayoutTests.Broken(), new ScriptedSurface(1280, 720) { Frameless = true });
        probe.Render(new Drawing.Canvas(1280, 720));
        var line = probe.Area(LauncherScreen.Item.AudioPack, LauncherScreen.Part.Status);
        var save = probe.Area(LauncherScreen.Item.Save);
        Shot("tooltip-problem-1280x720", LauncherLayoutTests.Broken(), 1280, 720, new PointerMoved(line.X + 8, line.Y + 3), new TimePassed(), Escape);
        Shot("hover-save-1280x720", guide, 1280, 720, new PointerMoved(save.X + 20, save.Y + 10), Escape);
        Shot("keyboard-tooltip-1280x720", guide, 1280, 720, [.. ToWindow, Escape]);
        Shot("advanced-broken-960x540", LauncherLayoutTests.Broken(), 960, 540, new KeyPressed(LauncherKey.Tab, Control: true), Escape);
        Shot("broken-packs-hd-1280x720", LauncherLayoutTests.Broken(), 1280, 720, Tabs(9));
        Shot("broken-packs-960x540", LauncherLayoutTests.Broken(), 960, 540, Tabs(12));
        Shot("no-soundfont-960x540", guide, 960, 540, Tabs(14));
        Shot("start-failed-960x540", LauncherLayoutTests.Broken(LauncherReason.StartFailed, "The game could not start: " + LauncherLayoutTests.BrokenManifest(@"E:\Games\packs\x")), 960, 540, Escape);
        Shot("asked-1440x800", Context(tests: []), 1440, 800, [.. ToWindow, Escape]);
        Shot("packs-1080x600", Context(tests: [], settings: new GameSettings { GameFolder = Game, AudioPack = Path.GetTempPath(), HdPack = @"C:\Packs\hd" })
            with { PackGuide = "https://example.org/PACKS.md", DescribePack = _ => new PackStatus(0, 3, null) }, 1080, 600, [.. ToAudioPack, Tab, Tab, Escape]);
        Shot("soundfont-1080x600", Context(tests: []), 1080, 600, [.. ToAudioPack, Tab, Tab, Escape]);
        Shot("roms-help-1080x600", Context(tests: []) with { PackGuide = "https://example.org/PACKS.md" }, 1080, 600, [.. ToAudioPack, Escape]);
        var reportContext = Context(tests: []) with { PackGuide = "https://example.org/PACKS.md", ReportProblem = () => "Report saved." };
        Shot("report-1080x600", reportContext, 1080, 600, Escape);
        Shot("report-720x400", reportContext, 720, 400, Escape);
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
