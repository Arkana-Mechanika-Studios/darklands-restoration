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
            int passage = 0, screens = 0;
            var failed = new List<string>();
            var why = new Dictionary<StartAt.Miss, int>();
            var notices = new Dictionary<StartAt.Miss, string>();
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
                    // every text not reached says why, in words: none is left without a notice
                    Assert.NotEqual(StartAt.Miss.None, at.Why);
                    Assert.False(string.IsNullOrWhiteSpace(at.Notice), target);
                    why[at.Why] = why.GetValueOrDefault(at.Why) + 1;
                    notices.TryAdd(at.Why, $"{target}: {at.Notice}");
                }
            }
            output.WriteLine($"{targets.Count} targets in {watch.Elapsed.TotalSeconds:0} s: {passage} card texts at their passage, {screens} screens, {failed.Count} not openable; "
                + string.Join(", ", why.Select(w => $"{w.Value} {w.Key}")));
            foreach (var line in failed.Take(10).Concat(notices.Values))
            {
                output.WriteLine("  " + line);
            }
            // the counts of 2026-11-22 (3,768 targets: 1,416 at their passage, 14 screens with the overlays; not reached: the files no card shows,
            // the texts never shown in any way through their cards, the ones further than the search goes): a change to the search or the cards may
            // raise the passages, not lower them. Every target opens now (a file no card shows: the game as it starts, with the notice).
            Assert.True(passage >= 1416, $"{passage} card texts at their passage");
            Assert.Empty(failed);
            Assert.True(screens >= 14, $"{screens} screen texts' screens");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
