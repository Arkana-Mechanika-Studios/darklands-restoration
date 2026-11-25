using Darklands.Content;
using Darklands.Platform;
using Darklands.Presentation.Graphics;
using Darklands.Workshop.Text;

namespace Darklands.Workshop.Tests;

/// <summary>The editor's live preview (W-0 + E-text): the game's own drawing of a text, measured against its box.</summary>
public sealed class TextPreviewTests
{
    [Fact]
    public void ADescriptionIsDrawnInItsScreensColumnAndMeasuredAgainstItsFoot()
    {
        if (GameFolder.Files is null || LocalPaths.Get("game", "DARKLANDS_DIR") is not { } game)
        {
            return;
        }
        var folder = Path.Combine(Path.GetTempPath(), "dl-preview-desc-" + Guid.NewGuid().ToString("N"));
        try
        {
            TextProject.Create(folder, "de", "Test", null, game);
            var project = TextProject.Open(folder, game);
            var preview = new TextPreview(project);
            // a saint's description (1.1.3): listed under the prayer screen, its room the column's lines
            var saint = project.Find(Darklands.Localization.KeyMangling.ToFluentId(Darklands.Content.DataNames.SaintDescription(1)))!;
            Assert.Equal(["SaintPrayerScreen"], saint.Screens);
            var english = preview.Render(saint, null);
            Assert.True(english.CardText);
            Assert.Equal("its box on the prayer screen", english.Column);
            Assert.Equal(Darklands.Presentation.DescriptionBoxes.Saint.Foot, english.FootLimit);
            Assert.Equal(0x9C, english.Room);
            Assert.False(english.Overflows);
            var longer = preview.Render(saint, string.Concat(Enumerable.Repeat("Ein langer Satz, der immer weiter geht. ", 20)));
            Assert.True(longer.Overflows);
            // a formula's: the box it fills most, the formula page's narrower column
            var formula = project.Find(Darklands.Localization.KeyMangling.ToFluentId(Darklands.Content.DataNames.FormulaDescription(1)))!;
            Assert.Equal(["AlchemyScreen"], formula.Screens);
            var page = preview.Render(formula, string.Concat(Enumerable.Repeat("Ein langer Satz. ", 12)));
            Assert.True(page.Overflows);
            Assert.Equal("its box on the formula page", page.Column);
            // a city's description: one line in the card window's font, drawn in the cards that show $PlaceDesc
            var city = project.Find(Darklands.Localization.KeyMangling.ToFluentId(Darklands.Content.DataNames.CityDescription(3)))!;
            Assert.All(city.Screens, s => Assert.StartsWith("card ", s, StringComparison.Ordinal));
            var line = preview.Render(city, null);
            Assert.False(line.CardText);
            Assert.Equal("the card texts that show $PlaceDesc", line.Column);
            var cardFont = new TextPainter(new GameAssets(GameFolder.Files!).Font(3));
            Assert.Equal(cardFont.Measure(city.English), line.Measured);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AScreenTextIsDrawnAsTheGameDrawsItAndMeasuredAgainstItsBox()
    {
        if (GameFolder.Files is not { } files || LocalPaths.Get("game", "DARKLANDS_DIR") is not { } game)
        {
            return;
        }
        var folder = Path.Combine(Path.GetTempPath(), "dl-preview-" + Guid.NewGuid().ToString("N"));
        try
        {
            var painter = new TextPainter(new GameAssets(files).Font(1));
            var words = Darklands.Content.Formats.Text.Strings.OriginalWords.Load(files).Exe("exe:DS:2FF2");
            var room = painter.Measure(words) + 10;
            var project = TextProject.Create(folder, "de", "Test", null, game);
            File.WriteAllText(Path.Combine(folder, "boxes.json"),
                "{ \"base\": \"483.07\", \"boxes\": [ { \"key\": \"exe:DS:2FF2\", \"english\": " + painter.Measure(words) + ", \"room\": " + room +
                ", \"windowed\": true, \"font\": 1, \"args\": null } ] }");
            project = TextProject.Open(folder, game);
            var preview = new TextPreview(project);
            var leave = project.Find(Darklands.Localization.KeyMangling.ToFluentId("exe:DS:2FF2"))!;
            var english = preview.Render(leave, null);
            Assert.Equal(painter.Measure(words), english.Measured);
            Assert.Equal(room, english.Room);
            Assert.False(english.Overflows);
            // the same pixels as the game's painter (the preview's picture is the frame's corner, coloured)
            var canvas = new Canvas(new Screen());
            painter.Draw(canvas, 3, 3, words, 15);
            for (var y = 0; y < english.Height; y++)
            {
                for (var x = 0; x < english.Width; x++)
                {
                    var drawn = canvas.Screen.Pixels[y * Screen.Width + x] != 0;
                    Assert.Equal(drawn, english.Argb[y * english.Width + x] is not (0xFF241C14u or 0xFFFF4040u));
                }
            }
            var longer = preview.Render(leave, "Den Ort verlassen und weiterziehen");
            Assert.True(longer.Overflows);
            Assert.True(longer.Measured > room);
            Assert.NotNull(preview.Render(leave, "{ $").Problem);

            var card = project.Entries.First(e => e.Key.StartsWith("msg:", StringComparison.Ordinal) && e.English.Contains("<opt>", StringComparison.Ordinal));
            Assert.False(preview.Render(card, null).Overflows);
            // a text too long for the window: the game shows it in pages (CardPaging), so it does not overflow
            var paged = preview.Render(card, string.Concat(Enumerable.Repeat("Ein langer Satz, der immer weiter geht. ", 80)) + card.English);
            Assert.False(paged.Overflows);
            Assert.True(paged.Pages > 1, $"{paged.Pages} page(s)");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
