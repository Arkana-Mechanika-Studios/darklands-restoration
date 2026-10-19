using Darklands.Content;
using Darklands.Content.Formats.Battles;
using Darklands.Content.Formats.Graphics;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Presentation.Graphics;

namespace Darklands.Rendering.Tests;

/// <summary>A scratch folder under the test program's own folder (never the system temp folder).</summary>
public sealed class Scratch : IDisposable
{
    public Scratch() => Path = System.IO.Path.Combine(AppContext.BaseDirectory, "scratch-" + Guid.NewGuid().ToString("N"));

    public string Path { get; }

    /// <summary>Writes a picture into the folder (sub-folders made as needed) and returns its full path.</summary>
    public string Png(string relative, RgbaImage image)
    {
        var path = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        PngCodec.Write(image, path);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, true);
        }
    }
}

/// <summary>
/// The HD backend: without replacements the frame is exactly the scaled game frame; a replacement draws only over the
/// pixels its asset still owns, at the asset's place, turned as it was drawn.
/// </summary>
public sealed class HdRendererTests
{
    private static IndexedImage Image(int width, int height, string? name, Func<int, int, byte> pixel)
    {
        var pixels = new byte[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                pixels[y * width + x] = pixel(x, y);
            }
        }
        return new IndexedImage(width, height, pixels, name);
    }

    private static RgbaImage Picture(int width, int height, Func<int, int, uint> argb)
    {
        var image = new RgbaImage(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                image.Argb[y * width + x] = argb(x, y);
            }
        }
        return image;
    }

    /// <summary>A recognisable opaque colour per replacement pixel.</summary>
    private static uint Marker(int x, int y) => 0xFF000000u | (uint)(x & 0xFF) << 16 | (uint)(y & 0xFF) << 8 | 0x5A;

    /// <summary>A screen of random pixels, colours and owners (with places), as frames look after many drawings.</summary>
    private static Screen RandomScreen(int seed)
    {
        var random = new Random(seed);
        var screen = new Screen();
        var canvas = new Canvas(screen);
        for (var i = 0; i < 256; i++)
        {
            canvas.SetColor(i, new Rgb24((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)));
        }
        random.NextBytes(screen.Pixels);
        for (var k = 0; k < 30; k++)
        {
            var (w, h) = (random.Next(1, 80), random.Next(1, 60));
            canvas.Draw(Image(w, h, $"test:random{random.Next(6)}", (_, _) => (byte)random.Next(256)), random.Next(-40, 330), random.Next(-30, 210), random.Next(2) == 0);
        }
        return screen;
    }

    /// <summary>The picture hosts showed before the backend: each output pixel the nearest frame pixel's colour (opaque).</summary>
    private static uint[] ScaledFrame(Screen screen, int width, int height)
    {
        var argb = new uint[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                argb[y * width + x] = screen.Palette[screen.Pixels[y * Screen.Height / height * Screen.Width + x * Screen.Width / width]] | 0xFF000000u;
            }
        }
        return argb;
    }

    private static HdFrame Render(HdPack pack, Screen screen, int scale) =>
        new HdRenderer(pack).Render(screen, new DisplayRectangle(0, 0, Screen.Width * scale, Screen.Height * scale));

    private static RgbaImage Compose(HdFrame frame, int scale) => FrameCompositor.Compose(frame, Screen.Width * scale, Screen.Height * scale);

    [Fact]
    public void WithoutAPackTheFrameIsTheScaledGameFrameExactly()
    {
        for (var seed = 1; seed <= 5; seed++)
        {
            var screen = RandomScreen(seed);
            foreach (var scale in new[] { 1, 2, 3, 4 })
            {
                var frame = Render(HdPack.Empty, screen, scale);
                var item = Assert.Single(frame.Items);
                Assert.Null(item.Placement);
                Assert.Equal(DrawFilter.Nearest, item.Filter);
                Assert.Equal(frame.Output, item.Destination);
                Assert.Equal((Screen.Width, Screen.Height), (item.Texture.Width, item.Texture.Height));
                Assert.Equal(ScaledFrame(screen, Screen.Width * scale, Screen.Height * scale), Compose(frame, scale).Argb);
            }
            // the base layer is the frame as PngWriter and the SDL host colour it
            var base320 = Render(HdPack.Empty, screen, 1).Base.Texture.Argb;
            Assert.Equal(screen.Pixels.Select(p => screen.Palette[p] | 0xFF000000u), base320);
        }
    }

    [Fact]
    public void APackWithoutTheScreensAssetsChangesNothing()
    {
        using var scratch = new Scratch();
        scratch.Png("test_not-drawn.png", Picture(4, 4, Marker));
        var pack = HdPack.Load(scratch.Path);
        Assert.Equal(1, pack.Count);
        var screen = RandomScreen(9);
        var frame = Render(pack, screen, 2);
        Assert.Single(frame.Items);
        Assert.Equal(ScaledFrame(screen, 640, 400), Compose(frame, 2).Argb);
    }

    [Fact]
    public void AReplacementDrawsOnlyOverThePixelsItsAssetStillOwns()
    {
        using var scratch = new Scratch();
        var screen = RandomScreen(3);
        var canvas = new Canvas(screen);
        // an 8x6 asset at (10, 20) with transparent holes, partly covered by an unnamed image, a fill and another asset
        canvas.Draw(Image(8, 6, "test:hd-asset", (x, y) => (byte)((x + y) % 5 == 0 ? 0 : 40 + x)), 10, 20, transparent: true);
        canvas.Draw(Image(2, 2, null, (_, _) => 7), 12, 22);
        canvas.Fill(17, 25, 3, 3, 9);
        canvas.Draw(Image(2, 1, "test:other", (_, _) => 8), 10, 20);
        scratch.Png("TEST_HD-ASSET.png", Picture(32, 24, Marker));
        var pack = HdPack.Load(scratch.Path);
        var asset = ScreenOwners.IdOf("test:hd-asset");

        var frame = Render(pack, screen, 4);
        // the frame, the replacement (whole: nothing else replaced in its rectangle), the overlay
        Assert.Equal(3, frame.Items.Count);
        var item = frame.Items[1];
        Assert.Null(frame.Items[2].Placement);
        Assert.Equal(DrawFilter.Nearest, frame.Items[2].Filter);
        Assert.Equal(DrawFilter.Linear, item.Filter);
        Assert.Equal(new DisplayRectangle(40, 80, 32, 24), item.Destination);
        var placement = item.Placement!;
        Assert.Equal(("test:hd-asset", 10, 20, 8, 6, false, false), (placement.Asset, placement.X, placement.Y, placement.Width, placement.Height, placement.FlipX, placement.FlipY));
        for (var row = 0; row < 6; row++)
        {
            for (var column = 0; column < 8; column++)
            {
                Assert.Equal(screen.Owners[(20 + row) * Screen.Width + 10 + column] == asset, placement.Owns(column, row));
            }
        }
        Assert.Contains(false, placement.Owned);
        Assert.Contains(true, placement.Owned);

        // every output pixel: the replacement's where the asset owns the screen pixel, the frame's everywhere else
        var composed = Compose(frame, 4).Argb;
        var scaled = ScaledFrame(screen, 1280, 800);
        for (var y = 0; y < 800; y++)
        {
            for (var x = 0; x < 1280; x++)
            {
                var owned = screen.Owners[y / 4 * Screen.Width + x / 4] == asset;
                Assert.Equal(owned ? Marker(x - 40, y - 80) : scaled[y * 1280 + x], composed[y * 1280 + x]);
            }
        }
    }

    [Fact]
    public void AMirroredDrawingTurnsItsReplacement()
    {
        using var scratch = new Scratch();
        var screen = new Screen();
        var canvas = new Canvas(screen);
        canvas.DrawMirrored(Image(4, 2, "test:hd-mirror", (_, _) => 1), 100, 100, flipX: true, flipY: false);
        // the replacement's left half (the asset's columns 0 and 1) red, its right half blue
        scratch.Png("test_hd-mirror.png", Picture(8, 4, (x, _) => x < 4 ? 0xFFFF0000u : 0xFF0000FFu));
        var frame = Render(HdPack.Load(scratch.Path), screen, 2);
        var placement = frame.Items[1].Placement!;
        Assert.True(placement.FlipX);
        Assert.Equal((100, 100), (placement.X, placement.Y));
        var composed = Compose(frame, 2);
        uint At(int screenX, int screenY) => composed.Argb[screenY * 2 * composed.Width + screenX * 2];
        // drawn turned over: the asset's column 0 is the screen's right column
        Assert.Equal(0xFFFF0000u, At(103, 100));
        Assert.Equal(0xFFFF0000u, At(102, 101));
        Assert.Equal(0xFF0000FFu, At(101, 100));
        Assert.Equal(0xFF0000FFu, At(100, 101));
        Assert.Equal(0xFF000000u, At(104, 100));
    }

    [Fact]
    public void EachDrawingOfAnAssetIsItsOwnItemAndClippedDrawingsStayOnTheirPixels()
    {
        using var scratch = new Scratch();
        var screen = new Screen();
        var canvas = new Canvas(screen);
        var image = Image(8, 6, "test:hd-twice", (_, _) => 3);
        canvas.Draw(image, 50, 50);
        canvas.Draw(image, 200, 120);
        canvas.Draw(image, -3, 196);
        scratch.Png("test_hd-twice.png", Picture(16, 12, Marker));
        var frame = Render(HdPack.Load(scratch.Path), screen, 2);
        Assert.Equal([(50, 50), (200, 120), (-3, 196)], frame.Items.Skip(1).SkipLast(1).Select(i => (i.Placement!.X, i.Placement.Y)));
        Assert.Equal(new DisplayRectangle(-6, 392, 16, 12), frame.Items[3].Destination);
        Assert.Equal(5 * 4, frame.Items[3].Placement!.Owned.Count(o => o));
        // each draws the pack's picture itself, the clipped one too: the overlay keeps the frame outside its pixels
        Assert.Same(frame.Items[1].Texture, frame.Items[2].Texture);
        Assert.Same(frame.Items[1].Texture, frame.Items[3].Texture);
        var composed = Compose(frame, 2).Argb;
        Assert.Equal(Marker(6, 0), composed[392 * 640 + 0]);
        Assert.Equal(Marker(0, 0), composed[100 * 640 + 100]);
        Assert.Equal(Marker(15, 11), composed[(240 + 11) * 640 + 400 + 15]);
    }

    [Fact]
    public void SavedRegionsAndScreensCarryThePlaces()
    {
        using var scratch = new Scratch();
        var screen = new Screen();
        var canvas = new Canvas(screen);
        var image = Image(6, 4, "test:hd-saved", (_, _) => 5);
        canvas.Draw(image, 30, 40);
        var region = canvas.Save(28, 38, 12, 10);
        var whole = canvas.SaveScreen();
        canvas.Fill(0, 0, Canvas.Width, Canvas.Height, 1);
        scratch.Png("test_hd-saved.png", Picture(6, 4, Marker));
        var renderer = new HdRenderer(HdPack.Load(scratch.Path));
        var output = new DisplayRectangle(0, 0, 320, 200);
        Assert.Single(renderer.Render(screen, output).Items);
        canvas.Restore(region);
        var restored = renderer.Render(screen, output).Items[1].Placement!;
        Assert.Equal((30, 40), (restored.X, restored.Y));
        Assert.All(restored.Owned, Assert.True);
        canvas.Fill(0, 0, Canvas.Width, Canvas.Height, 1);
        canvas.CopyFrom(whole, 30, 40, 3, 4);
        var half = renderer.Render(screen, output).Items[1].Placement!;
        Assert.Equal((30, 40, 12), (half.X, half.Y, half.Owned.Count(o => o)));
        // owners saved without places (made elsewhere): the pixels keep the frame's colours
        canvas.Restore(new SavedRegion(30, 40, 6, 4, new byte[24], Enumerable.Repeat(ScreenOwners.IdOf("test:hd-saved"), 24).ToArray()));
        Assert.Single(renderer.Render(screen, output).Items);
    }

    /// <summary>Something passing over a drawing (a fill, the pointer) changes only the overlay, never the replacement's texture.</summary>
    [Fact]
    public void ADrawingCoveredBySomethingElseKeepsItsTexture()
    {
        using var scratch = new Scratch();
        var screen = new Screen();
        var canvas = new Canvas(screen);
        canvas.Draw(Image(10, 10, "test:hd-keep", (_, _) => 2), 0, 0);
        canvas.Fill(0, 0, 2, 2, 0);
        scratch.Png("test_hd-keep.png", Picture(20, 20, Marker));
        var renderer = new HdRenderer(HdPack.Load(scratch.Path));
        var output = new DisplayRectangle(0, 0, 640, 400);
        var first = renderer.Render(screen, output).Items[1].Texture;
        var version = first.Version;
        var second = renderer.Render(screen, output).Items[1].Texture;
        Assert.Same(first, second);
        Assert.Equal(version, second.Version);
        canvas.Fill(5, 5, 1, 1, 0);
        var frame = renderer.Render(screen, output);
        Assert.Same(first, frame.Items[1].Texture);
        Assert.Equal(version, first.Version);
        Assert.Equal(0u, frame.Items[2].Texture.Argb[3 * Screen.Width + 3]);
        Assert.NotEqual(0u, frame.Items[2].Texture.Argb[0]);
        Assert.NotEqual(0u, frame.Items[2].Texture.Argb[5 * Screen.Width + 5]);
        // the base layer is one picture whose version moves every frame
        var baseLayer = renderer.Render(screen, output).Base.Texture;
        var baseVersion = baseLayer.Version;
        Assert.Same(baseLayer, renderer.Render(screen, output).Base.Texture);
        Assert.NotEqual(baseVersion, baseLayer.Version);
    }

    [Fact]
    public void RelayoutPlacesTheSameItemsForAnotherWindow()
    {
        using var scratch = new Scratch();
        var screen = new Screen();
        new Canvas(screen).Draw(Image(8, 8, "test:hd-relayout", (_, _) => 2), 16, 10);
        scratch.Png("test_hd-relayout.png", Picture(8, 8, Marker));
        var frame = Render(HdPack.Load(scratch.Path), screen, 2);
        // a 720x400 window: the 640x400 picture centred
        var moved = HdRenderer.Relayout(frame, DisplayLayout.Graphics(720, 400));
        Assert.Equal(new DisplayRectangle(40, 0, 640, 400), moved.Base.Destination);
        Assert.Equal(new DisplayRectangle(72, 20, 16, 16), moved.Items[1].Destination);
        Assert.Same(frame.Items[1].Texture, moved.Items[1].Texture);
    }

    [Fact]
    public void SpritesAreReplacedWhereTheyDraw()
    {
        using var scratch = new Scratch();
        var screen = new Screen();
        var canvas = new Canvas(screen);
        // header: width 4 (stored less one), 2 rows; row 0 starts at column 1 with 3 pixels, row 1 at column 0 with 2
        var sprite = RowRunSprite.Decode([3, 2, 3, 1, 9, 9, 9, 2, 0, 9, 9], 0, "test:hd-sprite");
        SpriteDrawing.Draw(canvas, sprite, 60, 70);
        scratch.Png("test_hd-sprite.png", Picture(4, 2, Marker));
        var frame = Render(HdPack.Load(scratch.Path), screen, 1);
        var placement = frame.Items[1].Placement!;
        Assert.Equal((60, 70, 4, 2), (placement.X, placement.Y, placement.Width, placement.Height));
        Assert.Equal([false, true, true, true, true, true, false, false], placement.Owned);
        var composed = Compose(frame, 1).Argb;
        Assert.Equal(Marker(1, 0), composed[70 * 320 + 61]);
        Assert.Equal(Marker(0, 1), composed[71 * 320 + 60]);
        Assert.Equal(0xFF000000u, composed[70 * 320 + 60]);
    }

    /// <summary>
    /// The game's own pictures and sprites as their own replacement (each scaled 4x by the nearest pixel, transparent where
    /// the original is): the HD frame is exactly the scaled frame, every asset placed to the pixel.
    /// </summary>
    [Fact]
    public void TheGamesAssetsReplacedByThemselvesLookTheSame()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        using var scratch = new Scratch();
        var assets = new GameAssets(files);
        var screen = new Screen();
        var canvas = new Canvas(screen);
        var picture = assets.Picture("startscr");
        canvas.DrawPicture(picture);
        var pointer = assets.CommonSprites.Pointer;
        SpriteDrawing.Draw(canvas, pointer, 150, 90);
        canvas.DrawMirrored(picture.Image, 200, 150, true, false);
        RgbaImage Scaled(int width, int height, Func<int, int, byte?> pixel) =>
            Picture(width * 4, height * 4, (x, y) => pixel(x / 4, y / 4) is { } p ? screen.Palette[p] | 0xFF000000u : 0u);
        scratch.Png("PICS/STARTSCR.PIC.png", Scaled(picture.Image.Width, picture.Image.Height, (x, y) => picture.Image.Pixels[y * picture.Image.Width + x]));
        scratch.Png("COMMONSP.IMG#0.png", Scaled(pointer.Width, pointer.Height, (x, y) =>
        {
            var (start, pixels) = y < pointer.RowCount ? pointer.Row(y) : (0, []);
            return x >= start && x - start < pixels.Length ? pixels[x - start] : null;
        }));
        var frame = Render(HdPack.Load(scratch.Path), screen, 4);
        // the picture, the pointer and the turned picture over the frame
        Assert.Equal(5, frame.Items.Count);
        Assert.Equal(ScaledFrame(screen, 1280, 800), Compose(frame, 4).Argb);
    }
}
