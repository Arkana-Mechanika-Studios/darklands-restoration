using System.Text.Json;
using Darklands.Workshop.Text;

namespace Darklands.Workshop.Tests;

/// <summary>The whole-file export: a card text in its rows with their context, the instruction header and its style, JSON and CSV.</summary>
public sealed class TextExportTests
{
    [Fact]
    public void ACardTextIsExportedInItsRowsWithTheirContext()
    {
        if (GameFolder.Files is null || Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") is not { } game)
        {
            return;
        }
        var folder = Path.Combine(Path.GetTempPath(), "dl-export-" + Guid.NewGuid().ToString("N"));
        try
        {
            var project = TextProject.Create(folder, "fr", "Test", null, game);
            var card = project.Entries.First(e => e.Key == "msg:$CITYC00#0");
            var menu = project.Entries.First(e => e.Key == "exe:290E:0112");
            var glossary = Glossary.Load(folder);
            glossary.Set(new GlossaryTerm("priest", "prêtre", ""));
            var items = TextExport.Items(project, [card, menu], glossary);
            // the introduction, then the seven options, then the menu line
            Assert.Equal(["msg:$CITYC00#0/intro", .. Enumerable.Range(1, 7).Select(i => $"msg:$CITYC00#0/option{i}"), "exe:290E:0112"], items.Select(i => i.Key));
            var gift = items.First(i => i.Key.EndsWith("/option4", StringComparison.Ordinal));
            Assert.StartsWith("… virtuously give", gift.English, StringComparison.Ordinal);
            Assert.Contains("$Money1", gift.Keep);
            Assert.DoesNotContain("$Money1", items.First(i => i.Key.EndsWith("/option1", StringComparison.Ordinal)).Keep);
            Assert.Contains(items, i => i.Terms.Any(t => t.Translation == "prêtre"));
            Assert.Contains("hot key", string.Join(" ", items[^1].Keep), StringComparison.Ordinal);
            Assert.Contains("px", items[^1].Room, StringComparison.Ordinal);

            using var json = JsonDocument.Parse(TextExport.Json(project, items));
            var instructions = json.RootElement.GetProperty("instructions").EnumerateArray().Select(l => l.GetString()).ToList();
            Assert.Contains(TextExport.DefaultStyle, instructions);
            Assert.Equal("fr", json.RootElement.GetProperty("target").GetString());
            Assert.Equal(9, json.RootElement.GetProperty("texts").GetArrayLength());

            // the pack's own style replaces the default, and is kept in pack.json (its other fields as they were)
            project.Style = "Use the formal \"vous\".\nKeep German place names.";
            project.Save();
            var reopened = TextProject.Open(folder, game);
            Assert.Equal("Use the formal \"vous\".\nKeep German place names.", reopened.Style);
            Assert.Contains("Keep German place names.", TextExport.Instructions(reopened));
            Assert.DoesNotContain(TextExport.DefaultStyle, TextExport.Instructions(reopened));
            Assert.Contains("\"language\": \"fr\"", File.ReadAllText(Path.Combine(folder, "pack.json")), StringComparison.Ordinal);

            var csv = TextExport.Csv(reopened, items).Split("\r\n");
            Assert.StartsWith("# written by Darklands Workshop ", csv[0], StringComparison.Ordinal);
            Assert.StartsWith("# Darklands translation", csv[1], StringComparison.Ordinal);
            var header = Array.FindIndex(csv, l => !l.StartsWith('#'));
            Assert.Equal(string.Join(",", TextExport.CsvColumns), csv[header]);
            // a cell with a comma is quoted
            Assert.Contains(csv, l => l.StartsWith("msg:$CITYC00#0/intro,", StringComparison.Ordinal) && l.Contains(",\"", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
