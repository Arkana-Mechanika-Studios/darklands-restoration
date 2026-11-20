using Darklands.Content;
using Darklands.Platform;
using Darklands.Presentation.Graphics;
using Darklands.Workshop.Text;

namespace Darklands.Workshop.Tests;

/// <summary>The editor's live preview (W-0 + E-text): the game's own drawing of a text, measured against its box.</summary>
public sealed class TextPreviewTests
{
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
