using Darklands.Launcher.Drawing;
using Darklands.Platform;

namespace Darklands.Launcher.Tests;

/// <summary>
/// The settings panel's layout (user check of 2026-11-08: a broken pack's problem ran past the panel and over the buttons) and
/// what a click does on the rows with a dialog behind them (a click opened the folder browser at once).
/// </summary>
public sealed class LauncherLayoutTests
{
    private const string Game = @"D:\GOG Games\Darklands";

    private const string LongFolder = @"C:\Users\A Player With A Long Name\Games\Darklands Restoration 1.0\packs\a-pack-with-a-rather-long-name-for-testing";

    /// <summary>The launcher's window sizes (the smallest is its minimum), and odd shapes a resized window takes.</summary>
    public static readonly TheoryData<int, int> Sizes = new()
    {
        { 960, 540 }, { 960, 760 }, { 1024, 576 }, { 1080, 608 }, { 1280, 720 }, { 1366, 768 }, { 1600, 900 }, { 1920, 540 }, { 1920, 1080 }, { 2560, 1440 },
    };

    /// <summary>A pack problem as the host reports it: a long path and a long parser message.</summary>
    internal static string BrokenManifest(string folder) =>
        $@"Audio pack: {folder}\audio.json: Expected depth to be zero at the end of the JSON payload. There is an open JSON object or array that should be closed. Path: $ | LineNumber: 4 | BytePositionInLine: 0.; manifest ignored";

    internal static LauncherContext Broken(LauncherReason reason = LauncherReason.Asked, string? problem = null) => new(
        new GameSettings
        {
            GameFolder = Game,
            HdPack = LongFolder + "-pictures",
            AudioPack = LongFolder,
            SoundFont = @"E:\Somewhere\Far\Away\In\A\Deep\Folder\Of\SoundFonts\missing-soundfont-with-a-long-name.sf2",
            Mt32Roms = @"C:\Users\A Player With A Long Name\Games\Darklands Restoration 1.0\MT32_ROMS",
        },
        new Dictionary<string, string>(),
        "1.0.0-rc2",
        reason,
        problem,
        folder => folder.TrimEnd('\\', '/').EndsWith("Darklands", StringComparison.OrdinalIgnoreCase) ? folder : null,
        () => [(Game, "GOG's registration")],
        folder => new RomStatus(folder ?? "MT32_ROMS", false, false),
        new FakeSoundTest([]),
        "## 1.0\n\n- A note.",
        _ => { },
        "https://example.org/notes",
        _ => [],
        folder => new PackStatus(0, 2, BrokenManifest(folder)),
        "https://example.org/PACKS.md",
        null,
        _ => null,
        () => "Report saved: " + LongFolder + @"\darklands-report-2026-11-08-120000.zip. The folder and the report form are open; attach the zip to the form. Nothing was sent.");

    private static void AssertLaidOut(LauncherScreen screen, string state)
    {
        var panel = screen.SettingsPanel;
        var blocks = screen.Blocks;
        foreach (var (area, what) in blocks)
        {
            Assert.True(area.Width > 0 && area.Height > 0, $"{state}: {what} is empty ({area})");
            Assert.True(panel.Holds(area), $"{state}: {what} {area} leaves the settings panel {panel}");
        }
        for (var i = 0; i < blocks.Count; i++)
        {
            for (var j = i + 1; j < blocks.Count; j++)
            {
                Assert.False(blocks[i].Area.Intersects(blocks[j].Area),
                    $"{state}: {blocks[i].What} {blocks[i].Area} overlaps {blocks[j].What} {blocks[j].Area}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void NothingOverlapsOrLeavesThePanelWithLongPackErrorsAtAnySize(int width, int height)
    {
        foreach (var context in new[] { Broken(), Broken(LauncherReason.StartFailed, "The game could not start: " + BrokenManifest(LongFolder)) })
        {
            var surface = new ScriptedSurface(width, height) { PickFolder = (_, _) => null, PickFile = (_, _) => null, Frameless = true };
            var screen = new LauncherScreen(new LauncherModel(context, null), context, surface);
            var canvas = new Canvas(width, height);
            // every control focused in turn (each shows its own details, the rows of both tabs), then a long message (a dropped folder that holds nothing)
            var tabsSeen = new HashSet<bool>();
            for (var i = 0; i < 25; i++)
            {
                screen.Render(canvas);
                tabsSeen.Add(screen.AdvancedShown);
                AssertLaidOut(screen, $"{width}x{height}, {(screen.AdvancedShown ? "Advanced" : "General")}, {screen.Focus}");
                // why the game could not start: in the notice over the buttons; the keyboard's choice has its tooltip, in the window
                Assert.Equal(context.Reason == LauncherReason.StartFailed, screen.Blocks.Any(b => b.What == "notice"));
                if (i > 0)
                {
                    Assert.Equal(screen.Focus, screen.Tooltip);
                    var tip = screen.TooltipArea;
                    Assert.True(tip.Width > 0 && tip.X >= 0 && tip.Y >= 0 && tip.Right <= width && tip.Bottom <= height, $"{width}x{height}: the tooltip of {screen.Focus} {tip}");
                }
                // the window's buttons: in its top-right corner, clear of the settings panel
                foreach (var button in new[] { LauncherScreen.Item.Minimise, LauncherScreen.Item.Close })
                {
                    var area = screen.Area(button);
                    Assert.True(area.X >= 0 && area.Y >= 0 && area.Right <= width && area.Bottom <= height, $"{width}x{height}: {button} {area} off the window");
                    Assert.False(area.Intersects(screen.SettingsPanel), $"{width}x{height}: {button} {area} over the panel {screen.SettingsPanel}");
                }
                screen.Handle(new KeyPressed(LauncherKey.Tab));
            }
            Assert.Equal(2, tabsSeen.Count);
            screen.Handle(new PathDropped(LongFolder + @"\not\a\pack\nor\a\game\folder\at\all"));
            screen.Render(canvas);
            AssertLaidOut(screen, $"{width}x{height}, a long message");
            while (screen.Focus != LauncherScreen.Item.Report)
            {
                screen.Handle(new KeyPressed(LauncherKey.Tab));
            }
            screen.Handle(new KeyPressed(LauncherKey.Enter));
            screen.Render(canvas);
            AssertLaidOut(screen, $"{width}x{height}, the report's message");
        }
    }

    [Fact]
    public void ThePacksProblemIsShortenedOnItsLineAndShownInFullInTheDetails()
    {
        Assert.Equal("audio.json: Expected depth", LauncherScreen.ShortProblem(LongFolder, $@"Audio pack: {LongFolder}\audio.json: Expected depth"));
        var pack = Path.Combine(Path.GetTempPath(), "x", "a-pack");   // this system's own path form: a Windows path is no path on Linux
        Assert.Equal("folder a-pack not found", LauncherScreen.ShortProblem(pack, $"HD pack: folder {pack} not found"));

        // long words (paths) are broken to the width; nothing is lost
        var face = new TextFace(Assets.Italic, 12, null);
        var lines = LauncherScreen.WrapAll(face, "Problem: " + BrokenManifest(LongFolder), 200);
        Assert.All(lines, l => Assert.True(face.Measure(l) <= 200, l));
        Assert.Equal(("Problem: " + BrokenManifest(LongFolder)).Replace(" ", ""), string.Concat(lines).Replace(" ", ""));
    }

    // ---- a click chooses a row; Enter or the row's Browse… opens the dialog ----

    private static (LauncherScreen Screen, List<string> Asked) Screen()
    {
        var asked = new List<string>();
        var context = Broken();
        var surface = new ScriptedSurface(1280, 720)
        {
            PickFolder = (title, _) => { asked.Add("folder: " + title); return null; },
            PickFile = (title, _) => { asked.Add("file: " + title); return null; },
        };
        var screen = new LauncherScreen(new LauncherModel(context, null), context, surface);
        screen.Render(new Canvas(1280, 720));
        return (screen, asked);
    }

    private static void Click(LauncherScreen screen, Rect area, int dx = -1)
    {
        var (x, y) = (dx < 0 ? area.X + area.Width / 2 : area.X + dx, area.Y + area.Height / 2);
        screen.Handle(new PointerPressed(x, y));
        screen.Handle(new PointerReleased(x, y));
    }

    [Theory]
    [InlineData("HdPack")]
    [InlineData("AudioPack")]
    [InlineData("SoundFont")]
    [InlineData("Roms")]
    public void AClickOnARowOnlyChoosesItAndItsButtonOrEnterOpensTheDialog(string row)
    {
        var item = Enum.Parse<LauncherScreen.Item>(row);
        var (screen, asked) = Screen();
        if (item is LauncherScreen.Item.SoundFont or LauncherScreen.Item.Roms)
        {
            Click(screen, screen.Area(LauncherScreen.Item.TabAdvanced));
            screen.Render(new Canvas(1280, 720));
        }
        Click(screen, screen.Area(item), dx: 10);
        Assert.Equal(item, screen.Focus);
        Assert.Empty(asked);

        screen.Render(new Canvas(1280, 720));
        Click(screen, screen.Area(item, LauncherScreen.Part.Browse));
        Assert.Single(asked);
        Assert.Equal(item, screen.Focus);

        screen.Handle(new KeyPressed(LauncherKey.Enter));
        Assert.Equal(2, asked.Count);
    }

    [Fact]
    public void APlateActsWhenLetGoOverItAndNotWhenTheButtonLeavesIt()
    {
        var (screen, asked) = Screen();
        var play = screen.Area(LauncherScreen.Item.Play);
        var browse = screen.Area(LauncherScreen.Item.HdPack, LauncherScreen.Part.Browse);

        // held down: nothing yet; let go elsewhere: nothing at all
        Assert.Null(screen.Handle(new PointerPressed(play.X + 4, play.Y + 4)));
        Assert.Null(screen.Handle(new PointerReleased(play.X - 40, play.Y - 40)));
        Assert.Null(screen.Handle(new PointerPressed(browse.X + 4, browse.Y + 4)));
        Assert.Empty(asked);
        Assert.Null(screen.Handle(new PointerReleased(browse.X + 4, browse.Y + 4)));
        Assert.Single(asked);

        // pressed and let go over Play: the game starts
        screen.Render(new Canvas(1280, 720));
        play = screen.Area(LauncherScreen.Item.Play);
        Assert.Null(screen.Handle(new PointerPressed(play.X + 4, play.Y + 4)));
        Assert.Equal(LauncherChoice.Play, screen.Handle(new PointerReleased(play.X + 6, play.Y + 6)));
    }

    [Fact]
    public void AfterAClickTheArrowsAndTheRowsArrowsStepThePacks()
    {
        // two pack folders that exist (the launcher offers only those)
        var root = Directory.CreateTempSubdirectory("dkpackrows").FullName;
        var packs = new[] { Path.Combine(root, "one"), Path.Combine(root, "two") };
        foreach (var pack in packs)
        {
            Directory.CreateDirectory(pack);
        }
        var context = Broken() with { FindPacks = _ => packs, DescribePack = _ => new PackStatus(1, 1, null) };
        var asked = 0;
        var surface = new ScriptedSurface(1280, 720) { PickFolder = (_, _) => { asked++; return null; } };
        var model = new LauncherModel(context, null);
        var screen = new LauncherScreen(model, context, surface);
        var canvas = new Canvas(1280, 720);
        screen.Render(canvas);

        Click(screen, screen.Area(LauncherScreen.Item.HdPack), dx: 10);
        Assert.Equal(LauncherSettingsPack(context), model.HdPack);
        // the settings' pack is last in the list (it is not in a packs folder): → goes round to None, then the first found
        screen.Handle(new KeyPressed(LauncherKey.Right));
        Assert.Null(model.HdPack);
        screen.Handle(new KeyPressed(LauncherKey.Right));
        Assert.Equal(packs[0], model.HdPack);

        screen.Render(canvas);
        Click(screen, screen.Area(LauncherScreen.Item.HdPack, LauncherScreen.Part.Next));
        Assert.Equal(packs[1], model.HdPack);
        screen.Render(canvas);
        Click(screen, screen.Area(LauncherScreen.Item.HdPack, LauncherScreen.Part.Previous));
        Assert.Equal(packs[0], model.HdPack);
        Assert.Equal(0, asked);
        Directory.Delete(root, recursive: true);
    }

    private static string? LauncherSettingsPack(LauncherContext context) => context.Settings.HdPack;

    private static byte[] Embedded(string name)
    {
        using var stream = typeof(Assets).Assembly.GetManifestResourceStream("Darklands.Launcher." + name)!;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    [Fact]
    public void TheTitleFaceDrawsTheLogosDoubleStoreyA()
    {
        // Grenze Black's own "a" is single-storey; the original logo's is double-storey, taken from Grenze Gotisch Black
        Assert.NotNull(Assets.Display.Lender);
        Assert.Equal("a", Assets.Display.BorrowedCharacters);
        var own = new TextFace(new TrueTypeFont(Embedded("Grenze-Black.ttf")), 60, null);
        var title = new TextFace(Assets.Display, 60, null);
        var a = new Canvas(80, 80);
        var b = new Canvas(80, 80);
        own.Draw(a, "a", 4, 4, 0xFFFFFF);
        title.Draw(b, "a", 4, 4, 0xFFFFFF);
        Assert.NotEqual(a.Pixels, b.Pixels);
    }

    // ---- no hint bar: a row's whole problem and its help in a tooltip; messages in a notice only while there is one ----

    [Fact]
    public void ARowsWholeProblemIsInItsTooltipWhenThePointerRestsOnItsStatusLine()
    {
        // a pack folder that is there, with a broken manifest (its problem: a long path and a long message)
        var folder = Directory.CreateTempSubdirectory("dkbrokenpack").FullName;
        var context = Broken() with { Settings = new GameSettings { GameFolder = Game, HdPack = folder } };
        var screen = new LauncherScreen(new LauncherModel(context, null), context, new ScriptedSurface(1280, 720));
        var canvas = new Canvas(1280, 720);
        screen.Render(canvas);
        // no box over the buttons: nothing to tell
        Assert.DoesNotContain(screen.Blocks, b => b.What is "notice" or "hints");

        var status = screen.Area(LauncherScreen.Item.HdPack, LauncherScreen.Part.Status);
        screen.Handle(new PointerMoved(status.X + 4, status.Y + 2));
        Assert.True(screen.TooltipPending);
        Assert.Equal(LauncherScreen.Item.None, screen.Tooltip);
        // the pointer rests: the tooltip, with the problem in full, then what the row does
        screen.Handle(new TimePassed());
        Assert.Equal(LauncherScreen.Item.HdPack, screen.Tooltip);
        var parts = screen.TooltipParts(LauncherScreen.Item.HdPack).ToList();
        // the whole message (the folder's path, shown on the row, left out)
        Assert.Equal("Problem: " + LauncherScreen.ShortProblem(folder, BrokenManifest(folder)), parts[0].Text);
        Assert.EndsWith("manifest ignored", parts[0].Text);
        Assert.StartsWith("Replacement pictures", parts[1].Text);
        screen.Render(canvas);
        Assert.True(screen.TooltipArea.Width > 0);
        Assert.True(screen.TooltipArea.Y >= status.Bottom, "the tooltip is under the line");

        // the pointer leaves for the painting: no tooltip
        screen.Handle(new PointerMoved(600, 400));
        screen.Render(canvas);
        Assert.Equal(LauncherScreen.Item.None, screen.Tooltip);
        Assert.Equal(0, screen.TooltipArea.Width);
        // a click on the status line chooses its row
        Click(screen, status);
        Assert.Equal(LauncherScreen.Item.HdPack, screen.Focus);
        Directory.Delete(folder);
    }

    [Fact]
    public void TheKeyboardsChoiceShowsItsTooltipAtOnceAndThePointerHidesIt()
    {
        var (screen, _) = Screen();
        screen.Handle(new KeyPressed(LauncherKey.Up));
        Assert.Equal(LauncherScreen.Item.Save, screen.Focus);
        Assert.Equal(LauncherScreen.Item.Save, screen.Tooltip);
        Assert.False(screen.TooltipPending);
        screen.Render(new Canvas(1280, 720));
        screen.Handle(new PointerMoved(600, 400));
        Assert.Equal(LauncherScreen.Item.None, screen.Tooltip);
    }

    [Fact]
    public void AMessageShowsInTheNoticeAndItsWholeTextInItsTooltip()
    {
        var problem = "The game could not start: " + BrokenManifest(LongFolder);
        var context = Broken(LauncherReason.StartFailed, problem);
        var screen = new LauncherScreen(new LauncherModel(context, null), context, new ScriptedSurface(1280, 720));
        screen.Render(new Canvas(1280, 720));
        var notice = screen.Blocks.Single(b => b.What == "notice").Area;
        Assert.True(notice.Bottom <= screen.Area(LauncherScreen.Item.Play).Y, "the notice is over the buttons");
        Assert.Equal(problem, screen.TooltipParts(LauncherScreen.Item.Notice).Single().Text);

        // without a reason, nothing; the report's message comes and shows
        var (quiet, _) = Screen();
        Assert.DoesNotContain(quiet.Blocks, b => b.What == "notice");
        while (quiet.Focus != LauncherScreen.Item.Report)
        {
            quiet.Handle(new KeyPressed(LauncherKey.Tab));
        }
        quiet.Handle(new KeyPressed(LauncherKey.Enter));
        quiet.Render(new Canvas(1280, 720));
        Assert.Contains(quiet.Blocks, b => b.What == "notice");
    }

    [Fact]
    public void PlatesLightUpUnderThePointerAndSofterForTheKeyboardWithoutAFrame()
    {
        // the Play plate's brightness: as drawn, chosen with the keyboard (its focus at the start), under the pointer
        static long Light(LauncherScreen screen, Rect area)
        {
            var canvas = new Canvas(1280, 720);
            screen.Render(canvas);
            long sum = 0;
            for (var y = area.Y - 6; y < area.Bottom + 6; y++)
            {
                for (var x = area.X - 6; x < area.Right + 6; x++)
                {
                    var c = canvas.Pixels[y * 1280 + x];
                    sum += (c >> 16 & 255) + (c >> 8 & 255) + (c & 255);
                }
            }
            return sum;
        }

        var (screen, _) = Screen();
        var play = screen.Area(LauncherScreen.Item.Play);
        Assert.Equal(LauncherScreen.Item.Play, screen.Focus);
        var chosen = Light(screen, play);
        screen.Handle(new KeyPressed(LauncherKey.Up));
        var plain = Light(screen, play);
        screen.Handle(new PointerMoved(play.X + 10, play.Y + 10));
        var pointed = Light(screen, play);
        Assert.True(plain < chosen && chosen < pointed, $"{plain} < {chosen} < {pointed}");

        // no gold frame around it any more (the old one: 3 pixels out, 2 wide)
        var canvas = new Canvas(1280, 720);
        screen.Render(canvas);
        var gold = 0;
        for (var x = play.X - 3; x < play.Right + 3; x++)
        {
            gold += canvas.Pixels[(play.Y - 3) * 1280 + x] == 0xFFD2A94E ? 1 : 0;
        }
        Assert.Equal(0, gold);
    }

    // ---- the tabs, and the frameless window's own buttons ----

    [Fact]
    public void TheTabsSwitchByAClickOrCtrlTabAndFollowTheFocus()
    {
        var (screen, _) = Screen();
        var canvas = new Canvas(1280, 720);
        Assert.False(screen.AdvancedShown);
        Assert.Throws<InvalidOperationException>(() => screen.Area(LauncherScreen.Item.Roms));

        // a click on Advanced: its rows, the focus on the first
        Click(screen, screen.Area(LauncherScreen.Item.TabAdvanced));
        screen.Render(canvas);
        Assert.True(screen.AdvancedShown);
        Assert.Equal(LauncherScreen.Item.Roms, screen.Focus);
        screen.Area(LauncherScreen.Item.SoundFont);
        screen.Area(LauncherScreen.Item.MouseWheel);
        Assert.Throws<InvalidOperationException>(() => screen.Area(LauncherScreen.Item.Window));

        // Ctrl+Tab: General again, then Advanced
        screen.Handle(new KeyPressed(LauncherKey.Tab, Control: true));
        screen.Render(canvas);
        Assert.False(screen.AdvancedShown);
        Assert.Equal(LauncherScreen.Item.GameFolder, screen.Focus);
        screen.Area(LauncherScreen.Item.Window);
        screen.Handle(new KeyPressed(LauncherKey.Tab, Control: true));
        screen.Render(canvas);
        Assert.True(screen.AdvancedShown);

        // Tab from General's last row (the audio pack) goes on to the ROMs, and Advanced opens
        Click(screen, screen.Area(LauncherScreen.Item.TabGeneral));
        screen.Render(canvas);
        while (screen.Focus != LauncherScreen.Item.AudioPack)
        {
            screen.Handle(new KeyPressed(LauncherKey.Tab));
        }
        screen.Handle(new KeyPressed(LauncherKey.Tab));
        screen.Render(canvas);
        Assert.Equal(LauncherScreen.Item.Roms, screen.Focus);
        Assert.True(screen.AdvancedShown);
        // and Shift+Tab back
        screen.Handle(new KeyPressed(LauncherKey.Tab, Shift: true));
        screen.Render(canvas);
        Assert.False(screen.AdvancedShown);
    }

    [Fact]
    public void TheWindowsOwnButtonsCloseAndMinimiseAndShowOnlyWhenTheWindowHasNoFrame()
    {
        var context = Broken();
        var framed = new LauncherScreen(new LauncherModel(context, null), context, new ScriptedSurface(1280, 720));
        framed.Render(new Canvas(1280, 720));
        Assert.Throws<InvalidOperationException>(() => framed.Area(LauncherScreen.Item.Close));

        var surface = new ScriptedSurface(1280, 720) { Frameless = true };
        var screen = new LauncherScreen(new LauncherModel(context, null), context, surface);
        screen.Render(new Canvas(1280, 720));
        var close = screen.Area(LauncherScreen.Item.Close);
        var minimise = screen.Area(LauncherScreen.Item.Minimise);
        Assert.True(close.Right <= 1280 && close.Right > 1240 && close.Y < 20, close.ToString());
        Assert.True(minimise.Right < close.X, minimise.ToString());

        // minimise: the surface is asked; the focus stays where it was
        var focus = screen.Focus;
        Assert.Null(screen.Handle(new PointerPressed(minimise.X + 4, minimise.Y + 4)));
        Assert.Null(screen.Handle(new PointerReleased(minimise.X + 4, minimise.Y + 4)));
        Assert.Equal(1, surface.Minimised);
        Assert.Equal(focus, screen.Focus);
        // close: as Esc
        screen.Render(new Canvas(1280, 720));
        Assert.Null(screen.Handle(new PointerPressed(close.X + 4, close.Y + 4)));
        Assert.Equal(LauncherChoice.Quit, screen.Handle(new PointerReleased(close.X + 4, close.Y + 4)));

        // a window that fills the screen has nothing to minimise to
        surface.FillsScreen = true;
        screen.Render(new Canvas(1280, 720));
        screen.Area(LauncherScreen.Item.Close);
        Assert.Throws<InvalidOperationException>(() => screen.Area(LauncherScreen.Item.Minimise));
    }

    [Fact]
    public void TheSurfaceIsToldWhereTheControlsAre()
    {
        var context = Broken();
        var surface = new ScriptedSurface(1280, 720, new KeyPressed(LauncherKey.Escape)) { Frameless = true };
        new Launcher().Run(context, surface);
        var areas = surface.ControlAreas;
        // the controls the player clicks are there (Play, the close button, a tab); the painting and the title are not
        var screen = new LauncherScreen(new LauncherModel(context, null), context, new ScriptedSurface(1280, 720) { Frameless = true });
        screen.Render(new Canvas(1280, 720));
        foreach (var item in new[] { LauncherScreen.Item.Play, LauncherScreen.Item.Close, LauncherScreen.Item.TabAdvanced })
        {
            var a = screen.Area(item);
            Assert.Contains((a.X, a.Y, a.Width, a.Height), areas);
        }
        Assert.DoesNotContain(areas, a => a.X <= 620 && 620 < a.X + a.Width && a.Y <= 400 && 400 < a.Y + a.Height);
        Assert.DoesNotContain(areas, a => a.X <= 343 && 343 < a.X + a.Width && a.Y <= 80 && 80 < a.Y + a.Height);
    }

    [Fact]
    public void WithoutASoundFontTheRowSaysWhereToGetAFreeOne()
    {
        var context = Broken() with { Settings = new GameSettings { GameFolder = Game } };
        var surface = new ScriptedSurface(1280, 720);
        var screen = new LauncherScreen(new LauncherModel(context, null), context, surface);
        screen.Handle(new KeyPressed(LauncherKey.Tab, Control: true));
        // whole on its line (not cut: a cut status line would head the row's tooltip) at every window size, the smallest included; the
        // free one is named in the tooltip
        foreach (var (width, height) in new[] { (1280, 720), (960, 540) })
        {
            screen.Render(new Canvas(width, height));
            Assert.Contains(screen.Blocks, b => b.What == "status None found: put any General MIDI .sf2 in SOUNDFONT.");
            var tip = screen.TooltipParts(LauncherScreen.Item.SoundFont).Select(p => p.Text).ToList();
            Assert.DoesNotContain(tip, t => t.StartsWith("None found", StringComparison.Ordinal));
            Assert.Contains(tip, t => t.Contains("FluidR3_GM.sf2", StringComparison.Ordinal));
        }
    }

    /// <summary>The What's new card gives each of its items a line of detail at the usual sizes, and stays clear of the card's foot.</summary>
    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1366, 768)]
    [InlineData(1920, 1080)]
    public void WhatsNewShowsALineOfDetailUnderEachItem(int width, int height)
    {
        var notes = """
            ## New

            - **A new launcher.** A painted window without a title bar (drag it by the painting).
            - **Picture filters.** How the 320×200 picture is drawn on a large window: Sharp, Even.
            - **The mouse wheel in menus.** In card menus and lists the wheel moves the highlight; not on the map.
            - **Character creation.** A right click lowers the chosen attribute or skill; + and − on every layout.
            """;
        var context = Broken() with { Settings = new GameSettings { GameFolder = Game }, ReleaseNotes = notes, Version = "1.0.0-rc2" };
        var screen = new LauncherScreen(new LauncherModel(context, null), context, new ScriptedSurface(width, height));
        screen.Render(new Canvas(width, height));
        var details = screen.NoteDetailsShown;
        Assert.Equal(4, details.Count);
        Assert.Equal("A painted window without a title bar", details[0].Text);
        Assert.Equal("In card menus and lists the wheel moves the highlight", details[2].Text);
        // one under the other, each in the card's width, and the card's links below them (they still fit above its foot)
        Assert.All(details.Zip(details.Skip(1)), p => Assert.True(p.First.Area.Bottom <= p.Second.Area.Y));
        var discord = screen.Area(LauncherScreen.Item.Discord);
        Assert.All(details, d => Assert.True(d.Area.Bottom < discord.Y && d.Area.Right < screen.SettingsPanel.X));
        Assert.True(discord.Bottom < height);
    }

    /// <summary>
    /// A tooltip covers no other control, heading, label or status line: under its control, else over it, else in the free band over
    /// Play (the Advanced tab's ROM folder: under it are the status line and the General MIDI heading).
    /// </summary>
    [Theory]
    [InlineData(1280, 720)]
    [InlineData(960, 540)]
    public void ATooltipLeavesTheOtherControlsAndTextsVisible(int width, int height)
    {
        var context = Broken() with { Settings = new GameSettings { GameFolder = Game } };
        var surface = new ScriptedSurface(width, height);
        var screen = new LauncherScreen(new LauncherModel(context, null), context, surface);
        screen.Handle(new KeyPressed(LauncherKey.Tab, Control: true));
        var canvas = new Canvas(width, height);
        screen.Render(canvas);
        var seen = 0;
        foreach (var item in new[] { LauncherScreen.Item.Roms, LauncherScreen.Item.SoundFont, LauncherScreen.Item.Banner, LauncherScreen.Item.Intro })
        {
            var row = screen.Area(item);
            screen.Handle(new PointerMoved(row.X + 4, row.Y + row.Height / 2));
            screen.Handle(new TimePassed());
            screen.Render(canvas);
            if (screen.TooltipArea.Width == 0)
            {
                continue;
            }
            seen++;
            var tip = screen.TooltipArea;
            var covered = screen.Blocks.Where(b => !b.Area.Intersects(row) && b.Area.Intersects(tip)).Select(b => b.What).ToList();
            Assert.True(covered.Count == 0, $"{width}x{height} {item}: the tooltip {tip} covers {string.Join(", ", covered)}");
        }
        Assert.True(seen > 0, "no tooltip shown");
    }
}
