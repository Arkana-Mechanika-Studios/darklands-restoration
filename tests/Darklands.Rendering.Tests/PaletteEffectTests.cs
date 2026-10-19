using Darklands.Content;
using Darklands.Content.Formats.Graphics;
using Darklands.Platform;
using Darklands.Presentation.Graphics;

namespace Darklands.Rendering.Tests;

/// <summary>
/// Palette effects on replacements: a picture whose colours the palette fades, greys or remaps shows its replacement
/// through the same change; text drawn by xor into a picture stays the picture's.
/// </summary>
public sealed class PaletteEffectTests
{
    private const int Size = 16;

    /// <summary>A 16x16 picture of colours 0x20..0x2F (left half) and 0xA0..0xAF (right half), drawn at (40, 30) in its own colours.</summary>
    private static (Screen Screen, Canvas Canvas, int Id) Picture(string name)
    {
        var screen = new Screen();
        var canvas = new Canvas(screen);
        for (var i = 0; i < 256; i++)
        {
            canvas.SetColor(i, new Rgb24((byte)(i * 7), (byte)(255 - i), (byte)(i * 3)));
        }
        var pixels = new byte[Size * Size];
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                pixels[y * Size + x] = (byte)((x < Size / 2 ? 0x20 : 0xA0) + (x + y) % 16);
            }
        }
        var image = new IndexedImage(Size, Size, pixels, name);
        canvas.Draw(image, 40, 30);
        var id = ScreenOwners.IdOf(name);
        ScreenOwners.SetOwnColors(id, pixels, screen.Palette);
        return (screen, canvas, id);
    }

    private static uint Marker(int x, int y) => 0xFF000000u | (uint)(40 + x * 5) << 16 | (uint)(200 - y * 3) << 8 | (uint)(90 + (x ^ y));

    private static HdRenderer Renderer(Scratch scratch, string name)
    {
        var image = new RgbaImage(Size * 2, Size * 2);
        for (var y = 0; y < Size * 2; y++)
        {
            for (var x = 0; x < Size * 2; x++)
            {
                image.Argb[y * Size * 2 + x] = Marker(x, y);
            }
        }
        scratch.Png(HdPack.Key(name) + ".png", image);
        return new HdRenderer(HdPack.Load(scratch.Path));
    }

    private static readonly DisplayRectangle Output = new(0, 0, 640, 400);

    private static uint Blend(uint from, uint to, double t)
    {
        uint Channel(int shift) => (uint)Math.Round(((from >> shift) & 0xFF) * (1 - t) + ((to >> shift) & 0xFF) * t, MidpointRounding.AwayFromZero) << shift;
        return 0xFF000000u | Channel(16) | Channel(8) | Channel(0);
    }

    private static void AssertNear(uint expected, uint actual, string where)
    {
        for (var shift = 0; shift < 24; shift += 8)
        {
            Assert.True(Math.Abs((int)((expected >> shift) & 0xFF) - (int)((actual >> shift) & 0xFF)) <= 2,
                $"{where}: expected {expected:X8}, got {actual:X8}");
        }
    }

    [Fact]
    public void InItsOwnColoursAPictureHasNoEffect()
    {
        using var scratch = new Scratch();
        var (screen, _, _) = Picture("test:fx-none");
        var renderer = Renderer(scratch, "test:fx-none");
        var item = renderer.Render(screen, Output).Items[1];
        Assert.Null(item.Placement!.Effect);
        Assert.Equal(Marker(0, 0), item.Texture.Argb[0]);
    }

    /// <summary>The card's fade: one range all the way to grey, the other 70% toward a light colour; each half of the replacement follows exactly.</summary>
    [Fact]
    public void TwoFadesAreFollowedExactly()
    {
        using var scratch = new Scratch();
        var (screen, _, _) = Picture("test:fx-fade");
        var renderer = Renderer(scratch, "test:fx-fade");
        const uint grey = 0xFF808080u, light = 0xFFF0E0C0u;
        for (var i = 0x20; i < 0x30; i++)
        {
            screen.Palette[i] = grey;
        }
        for (var i = 0xA0; i < 0xB0; i++)
        {
            screen.Palette[i] = Blend(screen.Palette[i], light, 0.7);
        }
        var item = renderer.Render(screen, Output).Items[1];
        var effect = item.Placement!.Effect!;
        Assert.Equal(2, effect.Lines.Lines.Count);
        for (var y = 0; y < Size * 2; y++)
        {
            for (var x = 0; x < Size * 2; x++)
            {
                var expected = x < Size ? grey : Blend(Marker(x, y), light, 0.7);
                AssertNear(expected, item.Texture.Argb[y * Size * 2 + x], $"({x},{y})");
            }
        }
    }

    [Fact]
    public void AFadeToBlackDarkensTheReplacement()
    {
        using var scratch = new Scratch();
        var (screen, _, _) = Picture("test:fx-black");
        var renderer = Renderer(scratch, "test:fx-black");
        for (var i = 0; i < 256; i++)
        {
            screen.Palette[i] = Blend(screen.Palette[i], 0xFF000000u, 0.75);
        }
        var texture = renderer.Render(screen, Output).Items[1].Texture;
        for (var i = 0; i < texture.Argb.Length; i++)
        {
            AssertNear(Blend(Marker(i % (Size * 2), i / (Size * 2)), 0xFF000000u, 0.75), texture.Argb[i], $"pixel {i}");
        }
    }

    /// <summary>
    /// Pixels remapped to one colour (the card's grey) show that colour flat. While less than half the picture changed,
    /// the rest keeps its own colours, an untouched speck too; once most of it changed, the untouched pixels follow.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemappedPixelsShowFlatAndUntouchedOnesFollowAMostlyChangedPicture(bool all)
    {
        using var scratch = new Scratch();
        var name = "test:fx-remap-" + all;
        var (screen, canvas, _) = Picture(name);
        var renderer = Renderer(scratch, name);
        canvas.SetColor(7, new Rgb24(0x55, 0x55, 0x55));
        for (var y = 30; y < 30 + Size; y++)
        {
            for (var x = 40; x < 40 + Size; x++)
            {
                if ((all || screen.Pixels[y * Screen.Width + x] is >= 0x20 and < 0x80) && (x, y) != (43, 35))
                {
                    screen.Pixels[y * Screen.Width + x] = 7; // as the card's grey remap: written in place, the owner stays
                }
            }
        }
        var texture = renderer.Render(screen, Output).Items[1].Texture;
        var speck = texture.Argb[(5 * 2) * Size * 2 + 3 * 2];
        Assert.Equal(0xFF555555u, texture.Argb[0]);
        if (all)
        {
            Assert.Equal(0xFF555555u, speck);
        }
        else
        {
            Assert.Equal(Marker(3 * 2, 5 * 2), speck);
            Assert.Equal(Marker(Size * 2 - 1, 0), texture.Argb[Size * 2 - 1]);
        }
    }

    /// <summary>The pointer moving over a faded picture changes neither its lines nor its texture (the overlay draws the pointer).</summary>
    [Fact]
    public void ThePointerOverAFadedPictureRepaintsNothing()
    {
        using var scratch = new Scratch();
        var (screen, canvas, _) = Picture("test:fx-pointer");
        var renderer = Renderer(scratch, "test:fx-pointer");
        for (var i = 0; i < 256; i++)
        {
            screen.Palette[i] = Blend(screen.Palette[i], 0xFF000000u, 0.5);
        }
        var first = renderer.Render(screen, Output).Items[1];
        var version = first.Texture.Version;
        var pointer = new IndexedImage(3, 3, [9, 9, 9, 9, 9, 9, 9, 9, 9], "test:fx-pointer-sprite");
        foreach (var (x, y) in new[] { (41, 31), (44, 33), (50, 40) })
        {
            var saved = canvas.Save(x, y, 3, 3);
            canvas.Draw(pointer, x, y);
            var item = renderer.Render(screen, Output).Items[1];
            Assert.Same(first.Texture, item.Texture);
            Assert.Equal(version, item.Texture.Version);
            Assert.Same(first.Placement!.Effect!.Lines, item.Placement!.Effect!.Lines);
            canvas.Restore(saved);
        }
    }

    /// <summary>When only some pixels' lines change (one more pixel greyed like others of its colour), only their part is repainted.</summary>
    [Fact]
    public void ALineChangeRepaintsOnlyItsArea()
    {
        using var scratch = new Scratch();
        var (screen, canvas, _) = Picture("test:fx-area");
        var renderer = Renderer(scratch, "test:fx-area");
        canvas.SetColor(7, new Rgb24(0x55, 0x55, 0x55));
        // picture pixels (1, 0) and (0, 1) both have colour 0x21; the first is greyed, then the second
        canvas.Repaint(41, 30, 7);
        var texture = renderer.Render(screen, Output).Items[1].Texture;
        var version = texture.Version;
        canvas.Repaint(40, 31, 7);
        var item = renderer.Render(screen, Output).Items[1];
        Assert.Same(texture, item.Texture);
        Assert.Equal(version + 1, texture.Version);
        Assert.Equal((0, 2, 2, 2), texture.LastChange);
        Assert.Equal(0xFF555555u, texture.Argb[2 * Size * 2]);
        Assert.Equal(0xFF555555u, texture.Argb[2]);
        Assert.Equal(Marker(4, 0), texture.Argb[4]);
    }


    [Fact]
    public void AnUnchangedEffectKeepsItsTexture()
    {
        using var scratch = new Scratch();
        var (screen, _, _) = Picture("test:fx-keep");
        var renderer = Renderer(scratch, "test:fx-keep");
        screen.Palette[0x21] = 0xFF102030u;
        var first = renderer.Render(screen, Output).Items[1].Texture;
        Assert.Same(first, renderer.Render(screen, Output).Items[1].Texture);
        screen.Palette[0x21] = 0xFF302010u;
        Assert.NotSame(first, renderer.Render(screen, Output).Items[1].Texture);
    }

    [Fact]
    public void RepaintKeepsTheOwner()
    {
        var (screen, canvas, id) = Picture("test:fx-repaint");
        canvas.Repaint(41, 31, 0x99);
        Assert.Equal(0x99, screen.Pixels[31 * Screen.Width + 41]);
        Assert.Equal(id, screen.Owners[31 * Screen.Width + 41]);
        Assert.Equal(ScreenTexel.Pack(1, 1), screen.Texels[31 * Screen.Width + 41]);
    }

    /// <summary>The card's option lines: text drawn by xor into a picture leaves the picture its pixels; copied text owns nothing.</summary>
    [Fact]
    public void XorTextStaysThePicturesCopiedTextIsNot()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var painter = new TextPainter(new GameAssets(files).Font(1));
        var (screen, canvas, id) = Picture("test:fx-text");
        var before = (byte[])screen.Pixels.Clone();
        painter.Draw(canvas, 40, 30, TextPainter.Encode("Wm"), 0x80, TextMode.Xor);
        var changed = Enumerable.Range(0, before.Length).Where(i => screen.Pixels[i] != before[i]).ToList();
        Assert.NotEmpty(changed);
        Assert.All(changed, i => Assert.Equal(id, screen.Owners[i]));
        painter.Draw(canvas, 40, 30, TextPainter.Encode("Wm"), 0x0F);
        Assert.Contains(Enumerable.Range(0, before.Length), i => screen.Pixels[i] == 0x0F && screen.Owners[i] == 0);
    }
}
