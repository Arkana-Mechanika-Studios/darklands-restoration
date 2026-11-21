using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Darklands.Workshop.Text;

namespace Darklands.Workshop.Tests;

/// <summary>
/// The first user test of the 1.1 Workshop: a TrueType pack's preview at a readable resolution, an empty row that is no translation,
/// a pack opened at its translated work, a group that folds with its text chosen, and the pack's own variables behind one chip.
/// </summary>
[Collection(WorkshopWindowTests.Collection)]
public sealed class EditorFeedbackTests
{
    private const string Card = "msg:$BARBE00#0";

    // a pack's slot 3 in a TrueType font (the launcher's EB Garamond), as a TrueType translation pack has it
    private static void TrueTypeSlot3(string pack)
    {
        var fonts = Directory.CreateDirectory(Path.Combine(pack, "fonts")).FullName;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Darklands.Launcher", "Resources", "EBGaramond12-Regular.ttf")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        File.Copy(Path.Combine(dir.FullName, "src", "Darklands.Launcher", "Resources", "EBGaramond12-Regular.ttf"), Path.Combine(fonts, "EBGaramond12-Regular.ttf"));
        File.WriteAllText(Path.Combine(fonts, "slot3.json"), "{ \"ttf\": \"EBGaramond12-Regular.ttf\", \"size\": 9, \"spacing\": 0, \"lineSpacing\": 0 }");
    }

    [Fact]
    public void ATrueTypePacksCardIsPreviewedAtAHigherResolution()
    {
        if (GameFolder.Files is null || Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") is not { } game)
        {
            return;
        }
        var folder = Path.Combine(Path.GetTempPath(), "dl-ttf-preview-" + Guid.NewGuid().ToString("N"));
        try
        {
            var project = TextProject.Create(folder, "de", "Test", null, game);
            TrueTypeSlot3(folder);
            // as the Workshop's application sets them
            Darklands.Presentation.FontPack.LoadScalable = json => (Darklands.Rendering.TtfFont.Load(json, out var why), why);
            TextPreview.DrawScalable = (screen, scale) =>
                new Darklands.Rendering.TextLayer().Item(screen, new Darklands.Platform.DisplayRectangle(0, 0, Darklands.Platform.Screen.Width * scale, Darklands.Platform.Screen.Height * scale))?.Texture.Argb;
            var card = project.Entries.First(e => e.Key == Card);
            var english = new TextPreview(project).Render(card, null);
            Assert.True(english.Untranslated);
            Assert.Equal(TextPreview.HighScale, english.Scale);
            Assert.Equal(english.Width * english.Scale * english.Height * english.Scale, english.Argb.Length);
            // drawn at that resolution: blocks of 4x4 output pixels that are not one colour (a 320x200 picture enlarged has none)
            var (w, k) = (english.Width * english.Scale, english.Scale);
            var mixed = 0;
            for (var by = 0; by < english.Height; by++)
            {
                for (var bx = 0; bx < english.Width; bx++)
                {
                    var first = english.Argb[by * k * w + bx * k];
                    var same = true;
                    for (var y = 0; y < k && same; y++)
                    {
                        for (var x = 0; x < k && same; x++)
                        {
                            same = english.Argb[(by * k + y) * w + bx * k + x] == first;
                        }
                    }
                    mixed += same ? 0 : 1;
                }
            }
            Assert.True(mixed > 500, $"{mixed} game pixels drawn finer");
            if (Environment.GetEnvironmentVariable("DARKLANDS_WORKSHOP_SHOTS") is { Length: > 0 } shots)
            {
                Directory.CreateDirectory(shots);
                Darklands.Rendering.PngCodec.Write(new Darklands.Rendering.RgbaImage(w, english.Height * k, english.Argb), Path.Combine(shots, "9-ttf-preview.png"));
            }
            Assert.False(new TextPreview(project).Render(card, card.English).Untranslated);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ACardTextOfEmptyRowsIsNoTranslation()
    {
        if (GameFolder.Files is null || Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") is not { } game)
        {
            return;
        }
        var folder = Path.Combine(Path.GetTempPath(), "dl-empty-rows-" + Guid.NewGuid().ToString("N"));
        try
        {
            var project = TextProject.Create(folder, "de", "Test", null, game);
            var card = project.Entries.First(e => e.Key == Card);
            var english = CardRows.Parse(card.English);
            var empty = CardRows.Write(english.Select(r => r with { Content = "" }));
            Assert.Contains("<opt>", empty, StringComparison.Ordinal);
            project.Set(card, empty);
            Assert.Null(card.Translation);
            var one = CardRows.Write(english.Select((r, i) => i == 1 ? r.WithField("die Läden besuchen.") : r with { Content = "" }));
            project.Set(card, one);
            Assert.Equal(one, card.Translation);
            // emptied again: no translation
            project.Set(card, empty);
            Assert.Null(card.Translation);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task APackOpensAtItsWorkAndItsGroupsFold()
    {
        if (GameFolder.Files is null || Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") is not { } game)
        {
            return;
        }
        var folder = Path.Combine(Path.GetTempPath(), "dl-feedback-" + Guid.NewGuid().ToString("N"));
        try
        {
            var project = TextProject.Create(folder, "ru", "Test", null, game);
            var church = project.Entries.First(e => e.Key == "msg:$CITYC00#0");
            project.Set(church, church.English.Replace("You come to the", "Вы приходите в", StringComparison.Ordinal));
            project.SetVariable("his1", "её", "его");
            project.Save();
            await WorkshopWindowTests.Session.Value.Dispatch(() =>
            {
                var window = new WorkshopWindow(new WorkshopSettings { GameFolder = game }, [folder]);
                window.Show();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var editor = window.Editor!;
                // its translated work: the Done filter, its first translated text
                Assert.Equal(2, editor.Filter);
                Assert.Equal(church.Key, editor.CurrentKey);

                // a card with no translation; its group (the current text's) folds and opens again
                editor.Select(Card);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                bool Listed() => editor.Listed.Any(i => i.Entry?.Key == Card);
                Assert.True(Listed());
                TextEditorView.SideItem Heading() => editor.Listed.First(i => i.Kind == 1 && i.Group == "msg:$BARBE00");
                editor.Click(Heading());
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.False(Listed());
                editor.Click(Heading());
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.True(Listed());

                // a row box given the cursor, or a word typed and taken back: no translation
                var fields = window.GetVisualDescendants().OfType<TranslationBox>().Where(b => b.IsEffectivelyVisible).ToList();
                fields[1].FocusText();
                fields[1].Text = "";
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Null(editor.Current!.Translation);
                fields[1].Text = "x";
                fields[1].Text = "";
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Null(editor.Current!.Translation);

                // the pack's own variable: behind one chip, not a chip of its own
                Assert.DoesNotContain(editor.Chips, c => c.Tag as string == "$his1");
                Assert.Contains(editor.Chips, c => c.Content is string s && s.StartsWith("Pack variables (1)", StringComparison.Ordinal));
                if (Environment.GetEnvironmentVariable("DARKLANDS_WORKSHOP_SHOTS") is { Length: > 0 } shots)
                {
                    Directory.CreateDirectory(shots);
                    window.CaptureRenderedFrame()?.Save(Path.Combine(shots, "8-feedback-ru.png"));
                }
                window.Close();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                return 0;
            }, CancellationToken.None);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
