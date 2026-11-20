using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Darklands.Workshop.Text;

namespace Darklands.Workshop.Tests;

/// <summary>
/// The Workshop's window in its states, drawn without a screen at 1280x800 (or DARKLANDS_WORKSHOP_SHOTS_SIZE): the start with no pack, a card text whose letters the game's
/// fonts lack (the hint to build the font pack), the same with the font pack built, a screen text with its preview and width bar, a
/// check's results, and a Greek and a Russian card text (the font pack's other alphabets; Φ has no decorated capital, В takes B's).
/// With DARKLANDS_WORKSHOP_SHOTS set, each is saved there as a picture (the before / after pictures of a change to the window);
/// without it, the states are only opened.
/// </summary>
[Collection(WorkshopWindowTests.Collection)]
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
        // the window's size: DARKLANDS_WORKSHOP_SHOTS_SIZE (as 1920x1080), else 1280x800
        var size = (Environment.GetEnvironmentVariable("DARKLANDS_WORKSHOP_SHOTS_SIZE") ?? "1280x800").Split('x');
        var (width, height) = (int.Parse(size[0], System.Globalization.CultureInfo.InvariantCulture), int.Parse(size[1], System.Globalization.CultureInfo.InvariantCulture));
        var root = Path.Combine(Path.GetTempPath(), "dl-shots-" + Guid.NewGuid().ToString("N"));
        // the packs in a neutral folder (the status line names it): DARKLANDS_WORKSHOP_SHOTS_PACKS (a video's D:\Packs; the folders fr, el and ru must not exist), else a temporary one
        var packs = Environment.GetEnvironmentVariable("DARKLANDS_WORKSHOP_SHOTS_PACKS") is { Length: > 0 } chosen ? chosen : Path.Combine(root, "Packs");
        string Pack(string language) => Path.Combine(packs, language);
        string[] languages = ["fr", "el", "ru"];
        Assert.DoesNotContain(languages, l => Directory.Exists(Pack(l)));
        const string Card = "msg:$CITYC00#0";
        try
        {
            var fr = TextProject.Create(Pack("fr"), "fr", "Exemple", "a translator", game);
            var card = fr.Entries.First(e => e.Key == Card);
            fr.Set(card, card.English.Replace("You come to the", "Vous arrivez à", StringComparison.Ordinal));
            var leave = fr.Entries.First(e => e.Key == "exe:DS:2FF2");
            fr.Set(leave, "Quitter la boutique et revenir plus tard");
            // its layout in the pack: two lines of 130 px (the Layout row under the preview)
            fr.SetLayout(leave.Key, new Darklands.Localization.SlotLayout(Width: 130, Lines: 2));
            var menu = fr.Entries.First(e => e.Key == "exe:290E:0112");
            fr.Set(menu, "<c12> W</c>eitere Chargen");
            fr.Save();
            foreach (var (language, words) in new[]
            {
                ("el", new[] { ("You come to the", "Φτάνετε στην"), ("the church of", "την εκκλησία του"), ("A beggar sits outside, asking for", "Ένας ζητιάνος κάθεται έξω και ζητά") }),
                ("ru", new[] { ("You come to the", "Вы приходите в"), ("the church of", "церковь города"), ("A beggar sits outside, asking for", "Нищий сидит у входа и просит") }),
            })
            {
                var project = TextProject.Create(Pack(language), language, language, "a translator", game);
                var entry = project.Entries.First(e => e.Key == Card);
                project.Set(entry, words.Aggregate(entry.English, (text, w) => text.Replace(w.Item1, w.Item2, StringComparison.Ordinal)));
                project.Save();
                FontNeeds.Build(game, Pack(language), language);
            }
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
                void Choose(Window window, string key) => ((WorkshopWindow)window).Editor!.Select(key);
                WorkshopWindow Open(string? pack)
                {
                    // the packs opened lately: the start page lists them (the last one first)
                    var settings = new WorkshopSettings { GameFolder = game, RecentPacks = [Pack("fr"), Pack("ru"), Pack("el")] };
                    var window = new WorkshopWindow(settings, pack is null ? [] : [pack]) { Width = width, Height = height };
                    window.Show();
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    return window;
                }

                var empty = Open(null);
                Shot(empty, "1-start");
                empty.Close();

                // the new pack dialog, a language picked from the list
                var dialog = new NewPackDialog(game, Path.Combine(root, "program", "packs"), "Marta") { Width = 560 };
                dialog.Show();
                dialog.Pick("pol", "pl");
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal("pl", dialog.LanguageTag);
                Assert.Equal("Polski", dialog.PackName);
                Assert.True(dialog.BuildFont);
                Shot(dialog, "0-new-pack");
                dialog.Close();

                var before = Open(Pack("fr"));
                Choose(before, Card);
                Shot(before, "2a-card-text-no-font-pack");
                before.Close();
                FontNeeds.Build(game, Pack("fr"), "fr");

                var window = Open(Pack("fr"));
                Choose(window, Card);
                Shot(window, "2-card-text");
                // the Rows view: the first option's field writes the option's words in place, its marker and indent kept
                var fields = window.GetVisualDescendants().OfType<TranslationBox>().Where(b => b.IsEffectivelyVisible).ToList();
                Assert.True(fields.Count > 3, $"{fields.Count} fields");
                fields[1].Text = "assister à la messe.";
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var chosen = window.Editor!.Current!;
                Assert.Contains("<opt>...<indent>assister à la messe.<br><p><opt>", chosen.Translation, StringComparison.Ordinal);
                Choose(window, leave.Key);
                Shot(window, "3-screen-text");
                window.RunCheckForTests();
                Choose(window, menu.Key);
                Shot(window, "4-check");
                window.Close();

                foreach (var language in new[] { "el", "ru" })
                {
                    var other = Open(Pack(language));
                    Choose(other, Card);
                    Shot(other, "5-card-text-" + language);
                    other.Close();
                }
                // the closed windows' last layout passes run while the application is there
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                return 0;
            }, CancellationToken.None);
        }
        finally
        {
            // only what the test made
            foreach (var language in languages.Where(l => Directory.Exists(Pack(l))))
            {
                Directory.Delete(Pack(language), recursive: true);
            }
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
