using Darklands.Content;
using Darklands.Content.Formats.Battles;
using Darklands.Content.Formats.Graphics;
using Darklands.Platform;
using Darklands.Presentation.Graphics;

namespace Darklands.Battles.Tests;

/// <summary>
/// The owner buffer beside the screen (Screen.Owners, for replacement graphics): what each drawing call records, and that
/// recording it changes no pixel and no colour (the canvas against <see cref="PixelOnlyCanvas"/>, the drawing from before it).
/// </summary>
public sealed class CanvasOwnershipTests
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

    private static int Owner(Canvas canvas, int x, int y) => canvas.Screen.Owners[y * Canvas.Width + x];

    [Fact]
    public void NamesAreInternedOnceAndZeroIsNoOwner()
    {
        Assert.Equal(0, ScreenOwners.IdOf(null));
        Assert.Null(ScreenOwners.NameOf(0));
        var id = ScreenOwners.IdOf("test:interned");
        Assert.NotEqual(0, id);
        Assert.Equal(id, ScreenOwners.IdOf("test:interned"));
        Assert.NotEqual(id, ScreenOwners.IdOf("test:other"));
        Assert.Equal("test:interned", ScreenOwners.NameOf(id));
    }

    [Fact]
    public void ADrawOwnsTheRectangleItWritesClippedToTheScreen()
    {
        var canvas = new Canvas(new Screen());
        var id = ScreenOwners.IdOf("test:draw");
        canvas.Draw(Image(10, 4, "test:draw", (_, _) => 7), 315, 198);
        Assert.Equal(id, Owner(canvas, 315, 198));
        Assert.Equal(id, Owner(canvas, 319, 199));
        Assert.Equal(0, Owner(canvas, 314, 198));
        Assert.Equal(0, Owner(canvas, 319, 197));
        // an unnamed image and raw pixels own nothing
        canvas.Draw(Image(2, 2, null, (_, _) => 3), 316, 198);
        Assert.Equal(0, Owner(canvas, 316, 198));
        Assert.Equal(id, Owner(canvas, 318, 198));
        canvas.Draw([1, 1], 2, 1, 318, 198);
        Assert.Equal(0, Owner(canvas, 318, 198));
    }

    [Fact]
    public void ATransparentDrawOwnsOnlyThePixelsItWrites()
    {
        var canvas = new Canvas(new Screen());
        var under = ScreenOwners.IdOf("test:under");
        var over = ScreenOwners.IdOf("test:over");
        canvas.Draw(Image(4, 4, "test:under", (_, _) => 9), 0, 0);
        canvas.Draw(Image(4, 4, "test:over", (x, y) => (byte)((x + y) % 2 == 0 ? 5 : 0)), 0, 0, transparent: true);
        Assert.Equal(over, Owner(canvas, 0, 0));
        Assert.Equal(under, Owner(canvas, 1, 0));
        Assert.Equal(9, canvas[1, 0]);
        Assert.Equal(over, Owner(canvas, 1, 1));
        // the picture of a mirrored draw: colour 0 is always transparent
        canvas.DrawMirrored(Image(2, 1, "test:mirror", (x, _) => (byte)(x == 0 ? 4 : 0)), 0, 0, flipX: true, flipY: false);
        Assert.Equal(over, Owner(canvas, 0, 0));
        Assert.Equal(ScreenOwners.IdOf("test:mirror"), Owner(canvas, 1, 0));
    }

    [Fact]
    public void FillsOutlinesSinglePixelsAndClearsOwnNothing()
    {
        var canvas = new Canvas(new Screen());
        var id = ScreenOwners.IdOf("test:fill");
        var whole = Image(Canvas.Width, Canvas.Height, "test:fill", (_, _) => 1);
        canvas.Draw(whole, 0, 0);
        canvas.Fill(10, 10, 5, 5, 2);
        Assert.Equal(0, Owner(canvas, 10, 10));
        Assert.Equal(0, Owner(canvas, 14, 14));
        Assert.Equal(id, Owner(canvas, 15, 15));
        canvas.Outline(30, 30, 4, 4, 3);
        Assert.Equal(0, Owner(canvas, 30, 30));
        Assert.Equal(0, Owner(canvas, 34, 34));
        Assert.Equal(id, Owner(canvas, 32, 32));
        canvas[50, 50] = 4;
        Assert.Equal(0, Owner(canvas, 50, 50));
        canvas.Clear(0);
        Assert.All(canvas.Screen.Owners, o => Assert.Equal(0, o));
    }

    [Fact]
    public void ARecolourKeepsTheOwners()
    {
        var canvas = new Canvas(new Screen());
        var id = ScreenOwners.IdOf("test:recolour");
        canvas.Draw(Image(8, 8, "test:recolour", (x, _) => (byte)(x < 4 ? 1 : 2)), 0, 0);
        canvas.Recolor(0, 0, 8, 8, 1, 6);
        Assert.Equal(6, canvas[0, 0]);
        Assert.Equal(id, Owner(canvas, 0, 0));
        Assert.Equal(id, Owner(canvas, 7, 7));
    }

    [Fact]
    public void SavedRegionsBringTheirOwnersBackAndNest()
    {
        var canvas = new Canvas(new Screen());
        var back = ScreenOwners.IdOf("test:back");
        var box = ScreenOwners.IdOf("test:box");
        var inner = ScreenOwners.IdOf("test:inner");
        canvas.Draw(Image(Canvas.Width, Canvas.Height, "test:back", (x, y) => (byte)(x ^ y)), 0, 0);
        var outer = canvas.Save(20, 20, 60, 40);
        canvas.Draw(Image(60, 40, "test:box", (_, _) => 8), 20, 20);
        var nested = canvas.Save(30, 30, 10, 10);
        canvas.Draw(Image(10, 10, "test:inner", (_, _) => 9), 30, 30);
        Assert.Equal(inner, Owner(canvas, 35, 35));

        canvas.Restore(nested);
        Assert.Equal(box, Owner(canvas, 35, 35));
        Assert.Equal(8, canvas[35, 35]);
        canvas.Restore(outer);
        Assert.Equal(back, Owner(canvas, 35, 35));
        Assert.Equal(back, Owner(canvas, 20, 20));
        Assert.Equal((byte)(35 ^ 35), canvas[35, 35]);

        // a region saved off the screen's edge restores only what lies on it
        var edge = canvas.Save(310, 190, 20, 20);
        Assert.Equal(0, edge.Owners![15 * 20 + 15]);
        canvas.Fill(0, 0, Canvas.Width, Canvas.Height, 1);
        canvas.Restore(edge);
        Assert.Equal(back, Owner(canvas, 319, 199));
        Assert.Equal(0, Owner(canvas, 309, 199));
    }

    [Fact]
    public void ATransparentRestoreBringsBackOnlyTheOwnersOfItsNonZeroPixels()
    {
        var canvas = new Canvas(new Screen());
        canvas.Draw(Image(4, 1, "test:saved", (x, _) => (byte)(x % 2 == 0 ? 0 : 7)), 0, 0);
        var saved = canvas.Save(0, 0, 4, 1);
        canvas.Draw(Image(4, 1, "test:cover", (_, _) => 3), 0, 0);
        canvas.RestoreTransparent(saved);
        Assert.Equal(ScreenOwners.IdOf("test:cover"), Owner(canvas, 0, 0));
        Assert.Equal(ScreenOwners.IdOf("test:saved"), Owner(canvas, 1, 0));
    }

    [Fact]
    public void SavedScreensAndBackPagesCarryTheirOwners()
    {
        var canvas = new Canvas(new Screen());
        var id = ScreenOwners.IdOf("test:page");
        canvas.Draw(Image(Canvas.Width, Canvas.Height, "test:page", (_, _) => 2), 0, 0);
        var page = canvas.SaveScreen();
        canvas.Clear(0);
        canvas.CopyFrom(page, 10, 10, 5, 5);
        Assert.Equal(id, Owner(canvas, 12, 12));
        Assert.Equal(0, Owner(canvas, 9, 9));
        canvas.Restore(page);
        Assert.All(canvas.Screen.Owners, o => Assert.Equal(id, o));
        // a page made of bare pixels (no owners) owns nothing
        canvas.Restore(new SavedScreen(new byte[Canvas.Width * Canvas.Height], new uint[256]));
        Assert.All(canvas.Screen.Owners, o => Assert.Equal(0, o));
    }

    [Fact]
    public void ASpriteOwnsTheClippedPixelsItDrawsRemappedOrNot()
    {
        var canvas = new Canvas(new Screen());
        // width byte 2 (3 wide), 2 rows: [3 pixels from column 0], [1 pixel from column 1]
        var sprite = RowRunSprite.Decode([2, 2, 3, 0, 5, 0, 6, 1, 1, 7], 0, "test:sprite");
        var id = ScreenOwners.IdOf("test:sprite");
        SpriteDrawing.Draw(canvas, sprite, 100, 50, new Clip(0, 0, 102, Canvas.Height), new byte[256]);
        Assert.Equal(id, Owner(canvas, 100, 50));
        Assert.Equal(0, Owner(canvas, 101, 50));
        Assert.Equal(0, Owner(canvas, 102, 50));
        Assert.Equal(id, Owner(canvas, 101, 51));
        Assert.Equal("test:sprite", sprite.Recolored(new byte[256]).AssetName);
    }

    /// <summary>Random drawing on the canvas and on the drawing from before the owner buffer leaves the same pixels and colours.</summary>
    [Fact]
    public void RecordingOwnersChangesNoPixel()
    {
        for (var seed = 1; seed <= 20; seed++)
        {
            var random = new Random(seed);
            var canvas = new Canvas(new Screen());
            var reference = new PixelOnlyCanvas(new Screen());
            var regions = new List<(SavedRegion Ours, (int, int, int, int, byte[]) Theirs)>();
            var screens = new List<(SavedScreen Ours, (byte[], uint[]) Theirs)>();
            for (var step = 0; step < 300; step++)
            {
                int X() => random.Next(-40, Canvas.Width + 40);
                int Y() => random.Next(-40, Canvas.Height + 40);
                byte C() => (byte)(random.Next(3) == 0 ? 0 : random.Next(256));
                IndexedImage RandomImage() =>
                    Image(random.Next(1, 70), random.Next(1, 50), random.Next(2) == 0 ? null : $"test:random{random.Next(8)}", (_, _) => C());
                switch (random.Next(13))
                {
                    case 0:
                    {
                        var (image, x, y, t) = (RandomImage(), X(), Y(), random.Next(2) == 0);
                        canvas.Draw(image, x, y, t);
                        reference.Draw(image, x, y, t);
                        break;
                    }
                    case 1:
                    {
                        var (image, x, y, fx, fy) = (RandomImage(), X(), Y(), random.Next(2) == 0, random.Next(2) == 0);
                        canvas.DrawMirrored(image, x, y, fx, fy);
                        reference.DrawMirrored(image, x, y, fx, fy);
                        break;
                    }
                    case 2:
                    {
                        var (x, y, w, h, c) = (X(), Y(), random.Next(-5, 120), random.Next(-5, 90), C());
                        canvas.Fill(x, y, w, h, c);
                        reference.Fill(x, y, w, h, c);
                        break;
                    }
                    case 3:
                    {
                        var (x, y, w, h, c) = (X(), Y(), random.Next(0, 120), random.Next(0, 90), C());
                        canvas.Outline(x, y, w, h, c);
                        reference.Outline(x, y, w, h, c);
                        break;
                    }
                    case 4:
                    {
                        var (x, y, c) = (X(), Y(), C());
                        canvas[x, y] = c;
                        reference[x, y] = c;
                        break;
                    }
                    case 5:
                    {
                        var (x, y, w, h, from, to) = (X(), Y(), random.Next(0, 160), random.Next(0, 120), C(), C());
                        canvas.Recolor(x, y, w, h, from, to);
                        reference.Recolor(x, y, w, h, from, to);
                        break;
                    }
                    case 6:
                    {
                        var (x, y, w, h) = (X(), Y(), random.Next(0, 90), random.Next(0, 70));
                        regions.Add((canvas.Save(x, y, w, h), reference.Save(x, y, w, h)));
                        break;
                    }
                    case 7 when regions.Count > 0:
                    {
                        // the latest region most often (nested boxes close in order), now and then an older one
                        var k = random.Next(3) == 0 ? random.Next(regions.Count) : regions.Count - 1;
                        var t = random.Next(2) == 0;
                        if (t)
                        {
                            canvas.RestoreTransparent(regions[k].Ours);
                        }
                        else
                        {
                            canvas.Restore(regions[k].Ours);
                        }
                        reference.Restore(regions[k].Theirs, t);
                        regions.RemoveAt(k);
                        break;
                    }
                    case 8:
                        screens.Add((canvas.SaveScreen(), reference.SaveScreen()));
                        break;
                    case 9 when screens.Count > 0:
                    {
                        var k = random.Next(screens.Count);
                        if (random.Next(2) == 0)
                        {
                            canvas.Restore(screens[k].Ours);
                            reference.Restore(screens[k].Theirs);
                        }
                        else
                        {
                            var (x, y, w, h) = (X(), Y(), random.Next(0, 200), random.Next(0, 150));
                            canvas.CopyFrom(screens[k].Ours, x, y, w, h);
                            reference.CopyFrom(screens[k].Theirs.Item1, x, y, w, h);
                        }
                        break;
                    }
                    case 10:
                    {
                        var index = random.Next(256);
                        var rgb = new Rgb24((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
                        canvas.SetColor(index, rgb);
                        reference.Screen.Palette[index] = 0xFF000000u | (uint)rgb.R << 16 | (uint)rgb.G << 8 | rgb.B;
                        break;
                    }
                    case 11:
                    {
                        var sprite = RandomSprite(random);
                        var (x, y) = (X(), Y());
                        var clip = new Clip(random.Next(0, 160), random.Next(0, 100), random.Next(160, 321), random.Next(100, 201));
                        var remap = random.Next(2) == 0 ? null : Enumerable.Range(0, 256).Select(_ => (byte)random.Next(256)).ToArray();
                        SpriteDrawing.Draw(canvas, sprite, x, y, clip, remap);
                        reference.DrawSprite(sprite, x, y, (clip.Left, clip.Top, clip.Right, clip.Bottom), remap);
                        break;
                    }
                    default:
                    {
                        var c = C();
                        if (random.Next(20) == 0)
                        {
                            canvas.Clear(c);
                            reference.Clear(c);
                        }
                        break;
                    }
                }
                Assert.True(canvas.Screen.Pixels.AsSpan().SequenceEqual(reference.Screen.Pixels), $"seed {seed}, step {step}: pixels differ");
                Assert.True(canvas.Screen.Palette.AsSpan().SequenceEqual(reference.Screen.Palette), $"seed {seed}, step {step}: palette differs");
            }
        }
    }

    private static RowRunSprite RandomSprite(Random random)
    {
        var height = random.Next(1, 40);
        var bytes = new List<byte> { (byte)random.Next(0, 60), (byte)height };
        for (var row = 0; row < height; row++)
        {
            var count = random.Next(0, 50);
            bytes.Add((byte)count);
            bytes.Add((byte)random.Next(0, 20));
            for (var i = 0; i < count; i++)
            {
                bytes.Add((byte)(random.Next(3) == 0 ? 0 : random.Next(256)));
            }
        }
        return RowRunSprite.Decode(bytes.ToArray(), 0, random.Next(2) == 0 ? null : "test:sprite");
    }

    /// <summary>Every picture of the PICS folder and every sprite of COMMONSP.IMG and BATTLEGR.IMG draws the same pixels as before.</summary>
    [Fact]
    public void TheGamesPicturesAndSpritesDrawAsBefore()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var assets = new GameAssets(files);
        var canvas = new Canvas(new Screen());
        var reference = new PixelOnlyCanvas(new Screen());
        var pictures = files.List("PICS", "*.PIC");
        Assert.NotEmpty(pictures);
        foreach (var path in pictures)
        {
            Pic picture;
            try
            {
                picture = assets.Picture(path);
            }
            catch (ArgumentException)
            {
                continue; // a file the game never shows (an empty image)
            }
            Assert.StartsWith(@"PICS\", picture.Image.AssetName);
            foreach (var transparent in new[] { false, true })
            {
                canvas.DrawPicture(picture, 0, 0, transparent);
                reference.DrawPicture(picture, 0, 0, transparent);
                canvas.DrawMirrored(picture.Image, 7, 3, true, true);
                reference.DrawMirrored(picture.Image, 7, 3, true, true);
                Assert.True(canvas.Screen.Pixels.AsSpan().SequenceEqual(reference.Screen.Pixels), path);
                Assert.True(canvas.Screen.Palette.AsSpan().SequenceEqual(reference.Screen.Palette), path);
            }
        }
        var banks = new[]
        {
            assets.CommonSprites.Bank,
            ImageBank.Decode(files.Read("BATTLEGR.IMG"), BattleImages.Count, "BATTLEGR.IMG"),
        };
        foreach (var sprite in banks.SelectMany(b => b.Images))
        {
            SpriteDrawing.Draw(canvas, sprite, 150, 90);
            reference.DrawSprite(sprite, 150, 90, (0, 0, Canvas.Width, Canvas.Height), null);
            Assert.True(canvas.Screen.Pixels.AsSpan().SequenceEqual(reference.Screen.Pixels), sprite.AssetName);
        }
    }

    [Fact]
    public void TheGamesAssetsAreNamedByTheScheme()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var assets = new GameAssets(files);
        Assert.Equal(@"PICS\STARTSCR.PIC", assets.Picture("startscr").Image.AssetName);
        Assert.Equal("COMMONSP.IMG#0", assets.CommonSprites.Pointer.AssetName);
        Assert.Equal("COMMONSP.IMG#12", assets.CommonSprites.ColumnBackground.AssetName);
        Assert.Equal("dropcap:D", DropCaps.Get(assets, (byte)'D')?.AssetName);
        var walk = new CombatAnimations(files).Load("F01", "WK", "SW");
        Assert.Equal("F01C.CAT/F01WKSW.IMC#0.3", walk?.Frame(0, 3).AssetName);
    }
}
