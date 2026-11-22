using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Darklands.Workshop.Text;

namespace Darklands.Workshop.Tests;

/// <summary>
/// A game older than the Workshop needs beside it (WorkshopVersion.NeedsGame): the start page says so in words, and Play in game
/// says so instead of starting a game that would refuse its options and quit without a word. Played on a real older release's
/// folder, DARKLANDS_WORKSHOP_OLD_GAME (the one holding its DarklandsRP, e.g. the 1.0.0 release unzipped); without it, nothing is
/// played. With DARKLANDS_WORKSHOP_SHOTS set, the start page and the dialog are saved there as pictures.
/// </summary>
[Collection(WorkshopWindowTests.Collection)]
public sealed class OlderGameTests
{
    [Fact]
    public async Task AnOlderGameBesideTheWorkshopIsSaidSoInWords()
    {
        if (GameFolder.Files is null || Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") is not { } game
            || Environment.GetEnvironmentVariable("DARKLANDS_WORKSHOP_OLD_GAME") is not { Length: > 0 } old)
        {
            return;
        }
        var shots = Environment.GetEnvironmentVariable("DARKLANDS_WORKSHOP_SHOTS");
        var root = Path.Combine(Path.GetTempPath(), "dl-oldgame-" + Guid.NewGuid().ToString("N"));
        var before = WorkshopApp.GameProgramFolder;
        try
        {
            WorkshopApp.GameProgramFolder = old;
            var version = WorkshopApp.GameProgramVersion();
            Assert.NotNull(version);
            Assert.True(WorkshopVersion.IsOlderGame(version), version);
            var pack = Path.Combine(root, "fr");
            TextProject.Create(pack, "fr", "Exemple", "a translator", game).Save();
            var session = WorkshopWindowTests.Session.Value;
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
                static string Words(Window window) =>
                    string.Join("\n", window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text));

                var start = new WorkshopWindow(new WorkshopSettings { GameFolder = game, RecentPacks = [pack] }, []) { Width = 1280, Height = 800 };
                start.Show();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Contains("Update the game to play your pack", Words(start), StringComparison.Ordinal);
                Assert.Contains($"version {version}, and the Workshop needs version {WorkshopVersion.NeedsGame} or later", Words(start), StringComparison.Ordinal);
                Assert.Contains("· game " + version, Words(start), StringComparison.Ordinal);
                Shot(start, "older-game-1-start");
                start.Close();

                var window = new WorkshopWindow(new WorkshopSettings { GameFolder = game, RecentPacks = [pack] }, [pack]) { Width = 1280, Height = 800 };
                window.Show();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var status = window.PlayInGameForTests(atText: false);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal($"Play in game needs the game {WorkshopVersion.NeedsGame} or later; the one beside the Workshop is {version}.", status);
                var dialog = Assert.Single(window.OwnedWindows);
                Assert.Equal("The game is older than the Workshop", dialog.Title);
                Assert.Contains("Download the game's", Words(dialog), StringComparison.Ordinal);
                Shot(window, "older-game-2-play-in-game");
                Shot(dialog, "older-game-3-dialog");
                dialog.Close();
                window.Close();
            }, CancellationToken.None);
        }
        finally
        {
            WorkshopApp.GameProgramFolder = before;
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
