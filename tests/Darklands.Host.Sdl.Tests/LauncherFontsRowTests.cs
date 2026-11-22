using Darklands.Launcher;
using Darklands.Platform;

namespace Darklands.Host.Sdl.Tests;

/// <summary>
/// The launcher's Fonts row on the program's own folder as the release lays it out (packs/fonts/hd-fonts from the build, read by the
/// host's pack search and description): its › and the → key go from None to the HD fonts and back, as the other pack rows do.
/// </summary>
public sealed class LauncherFontsRowTests
{
    private sealed class Surface : ILauncherSurface
    {
        public (int Width, int Height) Size => (1280, 720);

        public (int Width, int Height)? DisplaySize => (1920, 1080);

        public bool Frameless => true;

        public Func<string, string, string?>? PickFolder => null;

        public void Present(ReadOnlySpan<uint> pixels)
        {
        }

        public IReadOnlyList<LauncherInput> Wait() => [];

        public void TextInput(bool on)
        {
        }

        public string? Clipboard() => null;
    }

    [Fact]
    public void TheFontsRowOffersTheShippedHdFonts()
    {
        var hd = Path.Combine(AppContext.BaseDirectory, "packs", "fonts", "hd-fonts");
        Assert.True(File.Exists(Path.Combine(hd, "pack.json")));
        // a game folder taken as found (the rows below it are what is looked at)
        var game = Path.Combine(AppContext.BaseDirectory, "game");
        var context = new LauncherContext(new GameSettings { GameFolder = game }, new Dictionary<string, string>(), "1.1.0", LauncherReason.Asked, null,
            folder => folder, () => [], folder => new RomStatus(folder ?? "MT32_ROMS", false, false), null, "", _ => { })
        {
            FindPacks = LauncherHost.PackFolders,
            DescribePack = LauncherHost.DescribePack,
        };
        var model = new LauncherModel(context, null);
        var screen = new LauncherScreen(model, context, new Surface());
        var canvas = new Darklands.Launcher.Drawing.Canvas(1280, 720);
        screen.Render(canvas);
        // the Advanced tab (Ctrl+Tab), then down its rows to Fonts
        screen.Handle(new KeyPressed(LauncherKey.Tab, Control: true));
        screen.Render(canvas);
        for (var tab = 0; tab < 30 && screen.Focus != LauncherScreen.Item.FontPack; tab++)
        {
            screen.Handle(new KeyPressed(LauncherKey.Tab));
            screen.Render(canvas);
        }
        Assert.Equal(LauncherScreen.Item.FontPack, screen.Focus);
        Assert.Null(model.FontPack);
        Assert.Equal([null, hd], model.PackChoices(PackKind.Fonts));
        // → : the HD fonts; → again: None
        screen.Handle(new KeyPressed(LauncherKey.Right));
        Assert.Equal(hd, model.FontPack);
        screen.Handle(new KeyPressed(LauncherKey.Right));
        Assert.Null(model.FontPack);
        // the row's ›, clicked
        screen.Render(canvas);
        var next = screen.Area(LauncherScreen.Item.FontPack, LauncherScreen.Part.Next);
        screen.Handle(new PointerPressed(next.X + next.Width / 2, next.Y + next.Height / 2));
        Assert.Equal(hd, model.FontPack);
        Assert.Equal(hd, model.Settings().FontPack);
    }
}
