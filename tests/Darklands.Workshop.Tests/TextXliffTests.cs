using System.Xml.Linq;
using Darklands.Workshop.Text;

namespace Darklands.Workshop.Tests;

/// <summary>XLIFF 2.0: the export's units with their notes and inline codes; a CAT tool's targets read back, the codes turned back.</summary>
public sealed class TextXliffTests
{
    [Fact]
    public void AnXliffRoundTripKeepsTheNamesAndHotKeys()
    {
        if (GameFolder.Files is null || Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") is not { } game)
        {
            return;
        }
        var folder = Path.Combine(Path.GetTempPath(), "dl-xliff-" + Guid.NewGuid().ToString("N"));
        try
        {
            var project = TextProject.Create(folder, "de", "Test", null, game);
            var card = project.Entries.First(e => e.Key == "msg:$CITYC00#0");
            var menu = project.Entries.First(e => e.Key == "exe:290E:0112");
            var xliff = XDocument.Parse(TextXliff.Write(project, TextExport.Items(project, [card, menu], Glossary.Load(folder))));
            XNamespace ns = "urn:oasis:names:tc:xliff:document:2.0";
            Assert.Equal("de", xliff.Root!.Attribute("trgLang")!.Value);
            var units = xliff.Descendants(ns + "unit").ToList();
            Assert.Equal(9, units.Count);
            var gift = units.First(u => u.Attribute("id")!.Value.EndsWith("/option4", StringComparison.Ordinal));
            Assert.Contains(gift.Descendants(ns + "ph"), ph => ph.Attribute("disp")!.Value == "$Money1");
            Assert.Contains(gift.Descendants(ns + "note"), n => n.Attribute("category")!.Value == "keep" && n.Value.Contains("$Money1", StringComparison.Ordinal));

            // a CAT tool: each target the source's codes, the words German
            foreach (var unit in units)
            {
                var segment = unit.Element(ns + "segment")!;
                var target = new XElement(ns + "target", segment.Element(ns + "source")!.Nodes().Select(n => n is XText t ? new XText(Deutsch(t.Value)) : (XNode)new XElement((XElement)n)));
                segment.Add(target);
            }
            var read = TextXliff.Read(xliff.ToString());
            Assert.Equal(9, read.Count);
            var marks = ReviewMarks.Load(folder);
            var result = TextImport.Apply(project, marks, TextImport.Read(xliff.ToString(), ".xlf"));
            Assert.Equal(2, result.Imported.Count);
            Assert.Contains("$Money1", card.Translation, StringComparison.Ordinal);
            Assert.Contains("<opt>...<indent>", card.Translation, StringComparison.Ordinal);
            Assert.StartsWith("<c12> M</c>", menu.Translation, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // a stand-in translation: the words marked, so a test sees they were replaced
    // (a hot key's letter between its codes, " M", stays: its "keep" note says so)
    private static string Deutsch(string words) => words.Trim().Length <= 1 ? words : "DE:" + words;
}
