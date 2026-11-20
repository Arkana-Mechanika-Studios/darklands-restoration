using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Darklands.Workshop.Text;

namespace Darklands.Workshop.Tests;

/// <summary>The Workshop's window without a screen (W-0): it opens a pack and lists its texts; a picture of it when asked.</summary>
public sealed class WorkshopWindowTests
{
    /// <summary>The headless application (Skia drawing, so a frame can be captured).</summary>
    public static class HeadlessApp
    {
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<WorkshopApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }

    [Fact]
    public async Task TheWindowOpensAPackAndListsItsTexts()
    {
        if (GameFolder.Files is null || Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") is not { } game)
        {
            return;
        }
        var folder = Path.Combine(Path.GetTempPath(), "dl-window-" + Guid.NewGuid().ToString("N"));
        try
        {
            var project = TextProject.Create(folder, "de", "Test", null, game);
            project.Set(project.Entries.First(e => e.Key == "exe:290E:0112"), "<c12> M</c>ehr Chargen");
            var card = project.Entries.First(e => e.Key.StartsWith("msg:$OTHER00#0", StringComparison.Ordinal));
            project.Set(card, "Übersetzt " + card.English);
            project.Save();
            var glossary = Glossary.Load(folder);
            glossary.Set(new GlossaryTerm("party", "Gruppe", ""));
            glossary.Save(folder);
            using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessApp));
            var shown = await session.Dispatch(() =>
            {
                var window = new WorkshopWindow(new WorkshopSettings { GameFolder = game }, [folder]);
                window.Show();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var list = window.GetVisualDescendants().OfType<ListBox>().First(l => l.ItemsSource is IEnumerable<TextEntry>);
                // the menu text with its translation: the preview and the width bar beside it
                list.SelectedItem = list.ItemsSource!.Cast<TextEntry>().First(e => e.Key == (Environment.GetEnvironmentVariable("DARKLANDS_WORKSHOP_KEY") ?? "exe:290E:0112"));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                if (Environment.GetEnvironmentVariable("DARKLANDS_WORKSHOP_SHOT") is { Length: > 0 } shot)
                {
                    window.CaptureRenderedFrame()?.Save(shot);
                }
                var count = list.ItemCount;
                window.Close();
                return count;
            }, CancellationToken.None);
            Assert.True(shown > 4000, $"{shown} texts listed");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
