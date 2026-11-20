using System.Globalization;
using Darklands.Content;
using Darklands.Platform;
using Darklands.Presentation.Fonts;
using Darklands.Presentation.Graphics;

namespace Darklands.Rendering.Tests;

/// <summary>
/// Font packs in the game's own pixel style (PixelFontBuilder, FontPackMaker): every glyph of the player's fonts kept as it is, the
/// accented Latin letters, Greek and Cyrillic made at the font's height with sane widths, a made pack drawing the game's letters as the
/// game does, and the drop caps' fallback.
/// </summary>
public sealed class PixelFontTests
{
    [Fact]
    public void TheGamesGlyphsAreKeptAndTheNewLettersMade()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var assets = new GameAssets(files);
        foreach (var slot in new[] { 1, 2, 3 })
        {
            var font = assets.Font(slot);
            var glyphs = PixelFontBuilder.Build(font, FontScripts.Latin | FontScripts.Greek | FontScripts.Cyrillic);
            var painter = new TextPainter(font);
            for (var b = 0x20; b < 0x80; b++)
            {
                var c = Darklands.Core.GameTextCodec.ToChar((byte)b);
                var g = glyphs[c];
                Assert.Equal(painter.Advance((byte)b) - font.CharacterSpacing, g.Width);
                for (var y = 0; y < font.CharacterHeight; y++)
                {
                    var row = font.GetGlyphRow((byte)b, y);
                    var bits = row[0] << 8 | (row.Length > 1 ? row[1] : 0);
                    for (var x = 0; x < g.Width && x < 16; x++)
                    {
                        Assert.True(g[x, y] == ((bits & (0x8000 >> x)) != 0), $"slot {slot} '{c}' pixel {x},{y}");
                    }
                }
            }
            var maxWidth = Enumerable.Range(0x20, 0x60).Max(b => painter.Advance((byte)b));
            var letters = Enumerable.Range(0x00C0, 0x017F - 0x00C0 + 1).Concat(Enumerable.Range(0x0391, 0x03C9 - 0x0391 + 1)).Concat(Enumerable.Range(0x0410, 0x044F - 0x0410 + 1))
                .Where(c => char.IsLetter((char)c) && c is not (0x00D7 or 0x00F7 or 0x03A2 or 0x0138 or 0x0149 or 0x017F))
                .ToList();
            var missing = letters.Where(c => !glyphs.ContainsKey(c)).Select(c => ((char)c).ToString()).ToList();
            Assert.True(missing.Count == 0, $"slot {slot}: no glyph for {string.Join(" ", missing)}");
            Assert.All(glyphs.Values, g => Assert.Equal(font.CharacterHeight, g.Height));
            Assert.All(letters, c => Assert.InRange(glyphs[c].Width, 1, maxWidth + 2));
            // an accented letter has more ink than its base (the mark is there)
            Assert.True(glyphs['é'].Pixels.Count(p => p) > glyphs['e'].Pixels.Count(p => p));
            Assert.True(glyphs['Ç'].Pixels.Count(p => p) > glyphs['C'].Pixels.Count(p => p));
        }
    }

    [Fact]
    public void AMadePackDrawsTheGamesLettersAsTheGameDoes()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var folder = Path.Combine(Path.GetTempPath(), "dl-pixelfont-" + Guid.NewGuid().ToString("N"));
        try
        {
            var counts = FontPackMaker.Make(files, folder, FontScripts.Latin | FontScripts.Cyrillic);
            Assert.Equal(3, counts.Count);
            Assert.True(File.Exists(Path.Combine(folder, "charts", "slot3-cyrillic.png")));
            var font = BitmapFont.Load(Path.Combine(folder, "fonts", "slot3.json"), PngCodec.ReadAlpha, out var problem);
            Assert.True(font is not null, problem);
            var game = new TextPainter(new GameAssets(files).Font(3));
            var pack = new TextPainter(new GameAssets(files).Font(3)) { PackFont = font };
            var byGame = new Canvas(new Screen());
            var byPack = new Canvas(new Screen());
            game.Draw(byGame, 4, 4, "Thorn, St. Jakobi", 15);
            pack.Draw(byPack, 4, 4, "Thorn, St. Jakobi", 15);
            Assert.Equal(byGame.Screen.Pixels, byPack.Screen.Pixels);
            Assert.Equal(game.Measure("Thorn, St. Jakobi"), pack.Measure("Thorn, St. Jakobi"));
            Assert.True(font!.Has('é') && font.Has('Ж') && font.Has('ł'));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData('A', 'A')]
    [InlineData('É', 'E')]
    [InlineData('Ł', '\0')]
    [InlineData('В', 'B')]
    [InlineData('Α', 'A')]
    [InlineData('Φ', '\0')]
    [InlineData('a', '\0')]
    public void ADropCapIsTheLettersLatinCapitalOrNone(char letter, char cap) =>
        Assert.Equal((byte)cap, PixelFontBuilder.DropCapOf(letter));

    [Fact]
    public void TheProjectsOwnGlyphsCoverTheScripts()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var glyphs = PixelFontBuilder.Build(new GameAssets(files).Font(3), FontScripts.Greek | FontScripts.Cyrillic);
        foreach (var c in "БГДЖЗИЛПФЦЧШЩЪЫЬЭЮЯбвгджзиклмнптфцчшщъыьэюяΓΔΘΛΞΠΣΦΨΩαβγδεζηθικλμνξπρσςτυφχψω")
        {
            Assert.True(glyphs.ContainsKey(c), c.ToString(CultureInfo.InvariantCulture));
        }
    }
}
