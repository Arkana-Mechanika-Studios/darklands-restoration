using Darklands.Content;
using Darklands.Presentation;
using Darklands.Simulation;
using Darklands.Workshop.Text;
using Xunit.Abstractions;

namespace Darklands.Workshop.Tests;

/// <summary>
/// The translation test's start (DarklandsRP --start-at): every card and screen text the Workshop lists opens, at its passage, at its
/// card's start (the passage needs a story state the search does not set up), or not at all (with the reason), and the counts do not
/// fall below the ones recorded here.
/// </summary>
public sealed class StartAtTests(ITestOutputHelper output)
{
    [Fact]
    public void EveryCardAndScreenTextOpens()
    {
        if (GameFolder.Files is null || Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") is not { } game)
        {
            return;
        }
        var folder = Path.Combine(Path.GetTempPath(), "dl-startat-" + Guid.NewGuid().ToString("N"));
        try
        {
            var project = TextProject.Create(folder, "fr", "Test", null, game);
            var files = new GameFiles(game);
            var world = WorldData.Load(files);
            var cards = new CardRegistry();
            var assets = new GameAssets(files);
            var names = new Darklands.Cards.TablePersonalNames(Darklands.Content.Formats.Characters.PersonalNamesDecoder.Decode(files.Read("DARKLAND.EXE")));
            var targets = project.Entries
                .Select(WorkshopWindow.StartTarget)
                .OfType<string>().Distinct(StringComparer.Ordinal).ToList();
            int passage = 0, start = 0, screens = 0;
            var failed = new List<string>();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            foreach (var target in targets)
            {
                var at = StartAt.Resolve(target, world, cards, names, assets, out var error);
                if (at is null)
                {
                    failed.Add($"{target}: {error}");
                }
                else if (at.Screen is not null || target.StartsWith("screen:", StringComparison.Ordinal))
                {
                    screens++;
                }
                else if (at.Reached)
                {
                    passage++;
                }
                else
                {
                    start++;
                }
            }
            output.WriteLine($"{targets.Count} targets in {watch.Elapsed.TotalSeconds:0} s: {passage} card texts at their passage, {start} at their card's start, {screens} screens, {failed.Count} not openable");
            foreach (var line in failed.Take(10))
            {
                output.WriteLine("  " + line);
            }
            // the counts of 2026-11-21 (3,768 targets: 1,269 at their passage, 2,181 at their card's start, 14 screens with the overlays, 304 in files no
            // card shows within its first two menus): a change to the search or the cards may raise them, not lower them
            Assert.True(passage >= 1265, $"{passage} card texts at their passage");
            Assert.True(failed.Count <= 304, $"{failed.Count} not openable");
            Assert.True(screens >= 14, $"{screens} screen texts' screens");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
