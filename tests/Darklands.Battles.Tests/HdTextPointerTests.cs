using Darklands.Content;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Rendering;

namespace Darklands.Battles.Tests;

/// <summary>
/// The "HD fonts" font pack in the card screen (a Discord report, 1.1.3): the pointer moved over a card menu's text must stay over
/// the letters (it showed under them, and letters next to it went missing), and a card left for another screen and come back to
/// must not show that screen's letters again over its picture (fragments of the store's lines showed over the market's picture).
/// The game plays the market card of a Quickstart game with the pack the program ships (built here from the repository's files);
/// the frames are drawn at 1728x1080, the picture of a 1920x1080 window. Skipped without the game's files.
/// </summary>
public sealed class HdTextPointerTests
{
    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Darklands.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return dir ?? ".";
    }

    // the HD fonts pack as the program ships it (Darklands.Host.Sdl.csproj): docs/hd-fonts-pack with the fonts the repository has
    private static string HdFontsPack()
    {
        var root = RepoRoot();
        var folder = Path.Combine(Path.GetTempPath(), "dl-hdfonts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "fonts"));
        File.Copy(Path.Combine(root, "docs", "hd-fonts-pack", "pack.json"), Path.Combine(folder, "pack.json"));
        foreach (var slot in Directory.GetFiles(Path.Combine(root, "docs", "hd-fonts-pack", "fonts")))
        {
            File.Copy(slot, Path.Combine(folder, "fonts", Path.GetFileName(slot)));
        }
        File.Copy(Path.Combine(root, "src", "Darklands.Host.Sdl", "Resources", "DejaVuSansMono.ttf"), Path.Combine(folder, "fonts", "DejaVuSansMono.ttf"));
        File.Copy(Path.Combine(root, "src", "Darklands.Launcher", "Resources", "EBGaramond12-Regular.ttf"), Path.Combine(folder, "fonts", "EBGaramond12-Regular.ttf"));
        return folder;
    }

    // a copy of a frame as the host gets it: pixels, palette, owners, text plane and what is on top
    private static Screen Copy(Screen screen)
    {
        var copy = new Screen();
        screen.Pixels.CopyTo(copy.Pixels, 0);
        screen.Palette.CopyTo(copy.Palette, 0);
        screen.CopyOwnershipTo(copy);
        return copy;
    }

    private static (DarklandsGame Game, List<Screen> Frames, string Pack)? Play(params string[] script)
    {
        if (GameFolder.Files is not { } files)
        {
            return null;
        }
        var frames = new List<Screen>();
        var host = new RcPlayTests.RcHost(script) { OnPresent = s => frames.Add(Copy(s)) };
        var saves = Path.Combine(Path.GetTempPath(), "dl-hdsaves-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(saves);
        var game = new DarklandsGame(host, files)
        {
            Seed = () => 27,
            PartyTicks = () => 27,
            PlayOpening = false,
            ShowStartUp = false,
            SaveDirectory = saves,
            QuickstartCard = Darklands.Cards.Scripts.Marketplace.CardNumber,
            Behaviour = Darklands.Core.OriginalBehaviour.Faithful,
        };
        game.Ui.KeyboardCardMenus = true;
        var pack = HdFontsPack();
        // the TrueType fonts drawn as the hosts draw them
        FontPack.LoadScalable = json => (TtfFont.Load(json, out var why), why);
        var reports = new List<string>();
        Assert.True(game.Ui.UseFontPack(pack, reports.Add), string.Join(" / ", reports));
        game.Run();
        return (game, frames, pack);
    }

    private static readonly DisplayRectangle Output = new(0, 0, 1728, 1080);

    // with DARKLANDS_HDPOINTER_SHOTS=<folder>: the frame as a 1920x1080 full screen shows it (the picture 1728x1080 in the middle),
    //   for a person to look at; without it nothing
    private static void Shot(Screen screen, string name)
    {
        if (Environment.GetEnvironmentVariable("DARKLANDS_HDPOINTER_SHOTS") is not { Length: > 0 } folder)
        {
            return;
        }
        Directory.CreateDirectory(folder);
        var frame = new HdRenderer(HdPack.Empty).Render(screen, new DisplayRectangle(96, 0, 1728, 1080));
        PngCodec.Write(FrameCompositor.Compose(frame, 1920, 1080), Path.Combine(folder, name + ".png"));
    }

    // the market's text, its menu, the pointer onto the second line
    private static readonly string[] AtTheMenu = ["k:q", "w:60", "k:space", "w:60", "m:140,84", "w:10"];

    [Fact]
    public void ThePointerStaysOverAFontPacksLettersInTheCardMenu()
    {
        if (Play(AtTheMenu) is not var (_, frames, pack))
        {
            return;
        }
        try
        {
            // the last frame with the pointer over letters
            var screen = frames.Last(f => f.OnTop.Values.Any(t => t <= -2));
            Shot(screen, "1-pointer-over-the-menu");
            var renderer = new HdRenderer(HdPack.Empty);
            var composed = FrameCompositor.Compose(renderer.Render(screen, Output), Output.Width, Output.Height);
            var (sx, sy) = ((double)Output.Width / Screen.Width, (double)Output.Height / Screen.Height);
            // every pixel of the pointer shows the pointer's colour in the window (the text layer leaves it), over letters too
            foreach (var at in screen.OnTop.Keys)
            {
                var (x, y) = (at % Screen.Width, at / Screen.Width);
                var shown = composed.Argb[(int)((y + 0.5) * sy) * Output.Width + (int)((x + 0.5) * sx)] | 0xFF000000u;
                Assert.Equal(screen.Palette[screen.Pixels[at]] | 0xFF000000u, shown);
            }
            // a letter the pointer covers in part is still drawn where it is not covered: some glyph of the text layer has pixels
            //   both under the pointer and beside it
            var covered = screen.OnTop.Values.Where(t => t <= -2).ToHashSet();
            var beside = Enumerable.Range(0, screen.Texels.Length).Count(i => screen.Owners[i] == 0 && covered.Contains(screen.Texels[i]) && !screen.OnTop.ContainsKey(i));
            Assert.True(beside > 0, "no letter partly under the pointer");
        }
        finally
        {
            Directory.Delete(pack, recursive: true);
        }
    }

    [Fact]
    public void ACardLeftForAStoreAndComeBackToShowsNoLetterOfTheStore()
    {
        // the merchants (option 1), the store's Leave (Esc), the market again
        if (Play([.. AtTheMenu, "k:1", "w:80", "k:esc", "w:40", "k:esc", "w:80", "m:300,30", "w:10"]) is not var (game, frames, pack))
        {
            return;
        }
        try
        {
            var card = game.Ui.CardText.PackFont;
            Assert.NotNull(card);
            // letters in the card's window that are not of the card's font: the store's (or the panel's) font
            int Foreign(Screen s) => Enumerable.Range(0, s.Texels.Length).Count(i => i % Screen.Width >= CardScreen.WindowX && s.Owners[i] == 0
                && s.Texels[i] <= -2 && TextGlyphs.Of(s.Texels[i]) is { } g && !ReferenceEquals(g.Font, card));
            // the store was shown (its lines are in the menu bar's font, over the card's window)
            Assert.Contains(frames, f => Foreign(f) > 0);
            // back at the market: none of its letters is left in the window
            var back = frames[^1];
            Shot(frames.Last(f => Foreign(f) > 0), "2-the-store");
            Shot(back, "3-back-at-the-market");
            Assert.Contains(Enumerable.Range(0, back.Texels.Length), i => back.Owners[i] == 0 && back.Texels[i] <= -2);
            Assert.Equal(0, Foreign(back));
        }
        finally
        {
            Directory.Delete(pack, recursive: true);
        }
    }
}
