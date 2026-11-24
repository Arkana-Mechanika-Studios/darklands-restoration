using System.Text.RegularExpressions;
using Darklands.Presentation;

namespace Darklands.Battles.Tests;

/// <summary>
/// The view registry's catalogue (ViewCatalogue) stays true to the code: every screen name the presentation announces (OnScreen, and
/// the shops' pages by name) has an entry, every announced entry is named by a screen, every screen the recorded sessions saw (the
/// shipped boxes) has an entry, and each entry's slots are those recordings' keys.
/// </summary>
public sealed class ViewCatalogueTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Darklands.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    // the names the presentation's code announces: OnScreen("Name"), and a shop page's Screen = "Name" (BuySellPage, OnScreen(page.Screen))
    private static HashSet<string> Announced()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Root(), "src", "Darklands.Presentation"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }
            var text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, @"OnScreen\(""([A-Za-z]+)""\)|\.Screen = ""([A-Za-z]+)"""))
            {
                names.Add(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
            }
        }
        return names;
    }

    [Fact]
    public void EveryAnnouncedScreenHasAnEntryAndEveryEntryIsAnnounced()
    {
        var announced = Announced();
        Assert.True(announced.Count >= 19, $"the scan found only {announced.Count} screen names");
        var missing = announced.Where(n => ViewCatalogue.Find(n) is null).Order().ToList();
        Assert.True(missing.Count == 0, "screens announced but not in ViewCatalogue: " + string.Join(", ", missing));
        var unnamed = ViewCatalogue.Entries.Where(e => e.Announced && e.Kind == ViewKind.ClassicLoop && !announced.Contains(e.Id)).Select(e => e.Id).ToList();
        Assert.True(unnamed.Count == 0, "entries no screen announces: " + string.Join(", ", unnamed));
        // an entry that does not announce itself is one no code names
        Assert.All(ViewCatalogue.Entries.Where(e => !e.Announced), e => Assert.DoesNotContain(e.Id, announced));
    }

    /// <summary>Every screen class of the presentation (a class named ...Screen) and every overlay the recordings name (ScreenNames) has an entry.</summary>
    [Fact]
    public void EveryScreenClassAndOverlayHasAnEntry()
    {
        var classes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Root(), "src", "Darklands.Presentation"), "*.cs", SearchOption.AllDirectories))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"\bclass ([A-Za-z]+Screen)\b"))
            {
                classes.Add(m.Groups[1].Value);
            }
        }
        Assert.True(classes.Count >= 17, $"the scan found only {classes.Count} screen classes");
        classes.UnionWith(["MenuBar", "MessageBox", "SaveGameDialog", "TextInputBox"]);
        var missing = classes.Where(c => ViewCatalogue.Find(c) is null).Order().ToList();
        Assert.True(missing.Count == 0, "screen classes or overlays not in ViewCatalogue: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryRecordedScreenHasAnEntry()
    {
        var recorded = ShippedBoxes.Default.SelectMany(b => b.Screens).Distinct().Where(s => s != "?").Order().ToList();
        Assert.NotEmpty(recorded);
        var missing = recorded.Where(s => ViewCatalogue.Find(s) is null).ToList();
        Assert.True(missing.Count == 0, "screens of the shipped boxes not in ViewCatalogue: " + string.Join(", ", missing));
    }

    [Fact]
    public void EntriesAreUniqueAndTheirSlotsAreTheRecordedKeys()
    {
        Assert.Equal(ViewCatalogue.Entries.Count, ViewCatalogue.Entries.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(ViewCatalogue.Entries, e => Assert.NotEmpty(e.Commands));
        // the character sheet: its slots are the keys the boxes saw on it, sorted, none twice
        var sheet = ViewCatalogue.SlotsOf("CharacterSheetScreen");
        Assert.True(sheet.Count > 100, $"{sheet.Count} slots on the character sheet");
        Assert.Equal(sheet.Order(StringComparer.Ordinal), sheet);
        Assert.Equal(sheet.Count, sheet.Distinct(StringComparer.Ordinal).Count());
        Assert.All(sheet, key => Assert.Contains(ShippedBoxes.Default, b => b.Key == key && b.Screens.Contains("CharacterSheetScreen")));
        Assert.Empty(ViewCatalogue.SlotsOf("NoSuchScreen"));
        Assert.Null(ViewCatalogue.Find("NoSuchScreen"));
    }
}
