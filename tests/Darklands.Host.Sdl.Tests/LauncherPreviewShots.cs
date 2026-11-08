using Darklands.Launcher;
using Darklands.Platform;
using Darklands.Presentation.Graphics;

namespace Darklands.Host.Sdl.Tests;

/// <summary>
/// The launcher with the host's real picture filter previews (from the game folder DARKLANDS_DIR names), for a person to look at;
/// not a check. With DARKLANDS_LAUNCHER_PREVIEW_SHOTS set to a folder: the Picture row chosen with a few filters at 1280x720 and
/// one at 960x540, and CRT+ with the settings file's curvature, as launcher-preview-&lt;filter&gt;-&lt;size&gt;.png.
/// </summary>
public sealed class LauncherPreviewShots
{
    private sealed class Surface(int width, int height) : ILauncherSurface
    {
        public (int Width, int Height) Size => (width, height);

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
    public async Task DrawTheLauncherWithThePreviews()
    {
        if (Environment.GetEnvironmentVariable("DARKLANDS_LAUNCHER_PREVIEW_SHOTS") is not { Length: > 0 } output
            || Environment.GetEnvironmentVariable("DARKLANDS_DIR") is not { Length: > 0 } game)
        {
            return;
        }
        Directory.CreateDirectory(output);
        foreach (var (width, height, curved, filters) in new[]
        {
            (1280, 720, false, new[] { PictureFilter.Soft, PictureFilter.Hq, PictureFilter.CrtPlus, PictureFilter.SoftCrt }),
            (960, 540, false, new[] { PictureFilter.Crt }),
            (1280, 720, true, new[] { PictureFilter.CrtPlus }),
        })
        {
            var previews = new FilterPreviews();
            var settings = new GameSettings { GameFolder = game, CrtCurvature = curved };
            var context = new LauncherContext(settings, new Dictionary<string, string>(), "1.0", LauncherReason.Asked, null,
                folder => Darklands.Content.GameFiles.IsGameFolder(folder) ? folder : null, () => [], folder => new RomStatus(folder ?? "", true, false),
                null, "", _ => { }, Previews: previews);
            var model = new LauncherModel(context, null);
            var screen = new LauncherScreen(model, context, new Surface(width, height));
            var canvas = new Darklands.Launcher.Drawing.Canvas(width, height);
            screen.Render(canvas);   // the first frame asks for the previews
            await previews.Work.WaitAsync(TimeSpan.FromSeconds(60));
            while (screen.Focus != LauncherScreen.Item.Picture)
            {
                screen.Handle(new KeyPressed(LauncherKey.Tab));
            }
            foreach (var filter in filters)
            {
                while (model.Filter != PictureFilters.Name(filter))
                {
                    screen.Handle(new KeyPressed(LauncherKey.Right));
                }
                screen.Render(canvas);
                PngWriter.Write(canvas.Pixels.Select(p => p | 0xFF000000u).ToArray(), width, height,
                    Path.Combine(output, $"launcher-preview-{PictureFilters.Name(filter)}{(curved ? "-curved" : "")}-{width}x{height}.png"));
            }
        }
    }
}
