using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Darklands.Workshop.Text;

namespace Darklands.Workshop.Tests;

/// <summary>
/// The Workshop's window in four states, drawn without a screen at 1280x800: the start with no pack, a card text with its tags, a
/// screen text with its preview and width bar, and a check's results. With DARKLANDS_WORKSHOP_SHOTS set, each is saved there as a
/// picture (the before / after pictures of a change to the window); without it, the states are only opened.
/// </summary>
public sealed class WorkshopShotsTests
{
    [Fact]
    public async Task TheWindowsStatesOpen()
    {
        if (GameFolder.Files is null || Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") is not { } game)
        {
            return;
        }
        var shots = Environment.GetEnvironmentVariable("DARKLANDS_WORKSHOP_SHOTS");
        var folder = Path.Combine(Path.GetTempPath(), "dl-shots-" + Guid.NewGuid().ToString("N"));
        try
        {
            var project = TextProject.Create(folder, "fr", "Exemple", "a translator", game);
            var card = project.Entries.First(e => e.Key == "msg:$CITYC00#0");
            project.Set(card, card.English.Replace("You come to the", "Vous arrivez à", StringComparison.Ordinal));
            var leave = project.Entries.First(e => e.Key == "exe:DS:2FF2");
            project.Set(leave, "Quitter la boutique et revenir plus tard");
            var menu = project.Entries.First(e => e.Key == "exe:290E:0112");
            project.Set(menu, "<c12> W</c>eitere Chargen");
            project.Save();
            using var session = HeadlessUnitTestSession.StartNew(typeof(WorkshopWindowTests.HeadlessApp));
            await session.Dispatch(() =>
            {
                void Shot(Window window, string name)
                {
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    if (shots is { Length: > 0 })
                    {
                        Directory.CreateDirectory(shots);
                        window.CaptureRenderedFrame()?.Save(Path.Combine(shots, name + ".png"));
                    }
                }
                var empty = new WorkshopWindow(new WorkshopSettings { GameFolder = game }, []) { Width = 1280, Height = 800 };
                empty.Show();
                Shot(empty, "1-start");
                empty.Close();

                var window = new WorkshopWindow(new WorkshopSettings { GameFolder = game }, [folder]) { Width = 1280, Height = 800 };
                window.Show();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var list = window.GetVisualDescendants().OfType<ListBox>().First(l => l.ItemsSource is IEnumerable<TextEntry>);
                list.SelectedItem = list.ItemsSource!.Cast<TextEntry>().First(e => e.Key == card.Key);
                Shot(window, "2-card-text");
                list.SelectedItem = list.ItemsSource!.Cast<TextEntry>().First(e => e.Key == leave.Key);
                Shot(window, "3-screen-text");
                window.RunCheckForTests();
                list.SelectedItem = list.ItemsSource!.Cast<TextEntry>().First(e => e.Key == menu.Key);
                Shot(window, "4-check");
                window.Close();
                return 0;
            }, CancellationToken.None);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
