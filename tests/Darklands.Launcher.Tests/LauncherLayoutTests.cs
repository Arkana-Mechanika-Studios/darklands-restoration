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
        { 720, 427 }, { 760, 600 }, { 800, 480 }, { 900, 533 }, { 1080, 640 }, { 1280, 720 }, { 1440, 853 }, { 1600, 427 }, { 1920, 1080 }, { 2560, 1440 },
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
        Assert.Contains(blocks, b => b.What == "details");
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
            var surface = new ScriptedSurface(width, height) { PickFolder = _ => null, PickFile = _ => null };
            var screen = new LauncherScreen(new LauncherModel(context, null), context, surface);
            var canvas = new Canvas(width, height);
            // every control focused in turn (each shows its own details), then a long message (a dropped folder that holds nothing)
            for (var i = 0; i < 25; i++)
            {
                screen.Render(canvas);
                AssertLaidOut(screen, $"{width}x{height}, {screen.Focus}");
                screen.Handle(new KeyPressed(LauncherKey.Tab));
            }
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
        Assert.Equal("folder a-pack not found", LauncherScreen.ShortProblem(@"E:\x\a-pack", @"HD pack: folder E:\x\a-pack not found"));

        // long words (paths) are broken to the width; nothing is lost
        var face = new TextFace(Assets.Panel, 12, null);
        var lines = LauncherScreen.WrapAll(face, "Problem: " + BrokenManifest(LongFolder), 200);
        Assert.All(lines, l => Assert.True(face.Measure(l) <= 200, l));
        Assert.Equal(("Problem: " + BrokenManifest(LongFolder)).Replace(" ", ""), string.Concat(lines).Replace(" ", ""));
    }

    // ---- a click chooses a row; Enter or the row's Browse… opens the dialog ----

    private static (LauncherScreen Screen, List<string> Asked) Screen()
    {
        var asked = new List<string>();
        var context = Broken();
        var surface = new ScriptedSurface(1080, 640)
        {
            PickFolder = title => { asked.Add("folder: " + title); return null; },
            PickFile = title => { asked.Add("file: " + title); return null; },
        };
        var screen = new LauncherScreen(new LauncherModel(context, null), context, surface);
        screen.Render(new Canvas(1080, 640));
        return (screen, asked);
    }

    private static void Click(LauncherScreen screen, Rect area, int dx = -1) =>
        screen.Handle(new PointerPressed(dx < 0 ? area.X + area.Width / 2 : area.X + dx, area.Y + area.Height / 2));

    [Theory]
    [InlineData("HdPack")]
    [InlineData("AudioPack")]
    [InlineData("SoundFont")]
    [InlineData("Roms")]
    public void AClickOnARowOnlyChoosesItAndItsButtonOrEnterOpensTheDialog(string row)
    {
        var item = Enum.Parse<LauncherScreen.Item>(row);
        var (screen, asked) = Screen();
        Click(screen, screen.Area(item), dx: 10);
        Assert.Equal(item, screen.Focus);
        Assert.Empty(asked);

        screen.Render(new Canvas(1080, 640));
        Click(screen, screen.Area(item, LauncherScreen.Part.Browse));
        Assert.Single(asked);
        Assert.Equal(item, screen.Focus);

        screen.Handle(new KeyPressed(LauncherKey.Enter));
        Assert.Equal(2, asked.Count);
    }

    [Fact]
    public void AfterAClickTheArrowsAndTheRowsArrowsStepThePacks()
    {
        var packs = new[] { @"C:\packs\one", @"C:\packs\two" };
        var context = Broken() with { FindPacks = _ => packs, DescribePack = _ => new PackStatus(1, 1, null) };
        var asked = 0;
        var surface = new ScriptedSurface(1080, 640) { PickFolder = _ => { asked++; return null; } };
        var model = new LauncherModel(context, null);
        var screen = new LauncherScreen(model, context, surface);
        var canvas = new Canvas(1080, 640);
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
    }

    private static string? LauncherSettingsPack(LauncherContext context) => context.Settings.HdPack;

    [Fact]
    public void WithoutASoundFontTheRowSaysWhereToGetAFreeOne()
    {
        var context = Broken() with { Settings = new GameSettings { GameFolder = Game } };
        var surface = new ScriptedSurface(1080, 640);
        var screen = new LauncherScreen(new LauncherModel(context, null), context, surface);
        screen.Render(new Canvas(1080, 640));
        Assert.Contains(screen.Blocks, b => b.What.StartsWith("status None found: any General MIDI .sf2 works (a free one: FluidR3_GM.sf2, MIT)", StringComparison.Ordinal));
    }
}
