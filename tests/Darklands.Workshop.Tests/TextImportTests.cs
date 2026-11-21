using System.Text.Json.Nodes;
using Darklands.Workshop.Text;

namespace Darklands.Workshop.Tests;

/// <summary>
/// The import: a file the export wrote, translated, read back (JSON, CSV, an AI's answer with a fenced block); a card text's rows put
/// back with their markers; every text marked for review; a reviewed text kept until Use; Undo puts all back.
/// </summary>
public sealed class TextImportTests
{
    [Fact]
    public void AnExportTranslatedComesBackIntoTheProject()
    {
        if (GameFolder.Files is null || Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") is not { } game)
        {
            return;
        }
        var folder = Path.Combine(Path.GetTempPath(), "dl-import-" + Guid.NewGuid().ToString("N"));
        try
        {
            var project = TextProject.Create(folder, "fr", "Test", null, game);
            var card = project.Entries.First(e => e.Key == "msg:$CITYC00#0");
            var menu = project.Entries.First(e => e.Key == "exe:290E:0112");
            var reviewed = project.Entries.First(e => e.Key == "exe:DS:2FF2");
            project.Set(reviewed, "<c12> B</c>rocanter pour un autre");
            var marks = ReviewMarks.Load(folder);
            var glossary = Glossary.Load(folder);

            // the export, its "translation"s filled in as a person or an AI would
            var json = JsonNode.Parse(TextExport.Json(project, TextExport.Items(project, [card, menu, reviewed], glossary)))!;
            foreach (var text in json["texts"]!.AsArray())
            {
                var key = text!["key"]!.GetValue<string>();
                text["translation"] = key switch
                {
                    "msg:$CITYC00#0/option1" => "… assister à la messe.",
                    "msg:$CITYC00#0/option2" => "aller à confesse.",
                    "exe:290E:0112" => "<c12> M</c>ultiplier les lots",
                    "exe:DS:2FF2" => "<c12> B</c>arguigner pour autrui",
                    _ => "",
                };
            }
            json["texts"]!.AsArray().Add(new JsonObject { ["key"] = "exe:NOPE", ["translation"] = "x" });
            var answer = "Voici la traduction :\n```json\n" + json.ToJsonString() + "\n```\nBonne chance !";
            var read = TextImport.Read(answer);
            Assert.Equal(5, read.Count);

            var result = TextImport.Apply(project, marks, read);
            Assert.Equal(["msg:$CITYC00#0", "exe:290E:0112"], result.Imported.Order(StringComparer.Ordinal).Reverse());
            Assert.Contains(result.NotImported, n => n.Key == "exe:NOPE");
            // the reviewed text is kept, the file's words waiting
            Assert.Equal("<c12> B</c>rocanter pour un autre", reviewed.Translation);
            Assert.Equal("exe:DS:2FF2", Assert.Single(result.Kept).Key);
            // the card text written with its markers and indents; the rows the file did not give stay the English's
            Assert.Contains("<opt>...<indent>assister à la messe.<br><p><opt>...<indent>aller à confesse.", card.Translation, StringComparison.Ordinal);
            Assert.Contains("talk to a priest", card.Translation, StringComparison.Ordinal);
            Assert.True(marks.Has(card.Key) && marks.Has(menu.Key) && !marks.Has(reviewed.Key));

            TextImport.Use(project, marks, result, reviewed.Key);
            Assert.Equal("<c12> B</c>arguigner pour autrui", reviewed.Translation);
            Assert.True(marks.Has(reviewed.Key));

            TextImport.Undo(project, marks, result);
            Assert.Null(card.Translation);
            Assert.Null(menu.Translation);
            Assert.Equal("<c12> B</c>rocanter pour un autre", reviewed.Translation);
            Assert.False(marks.Has(card.Key) || marks.Has(menu.Key) || marks.Has(reviewed.Key));

            // CSV the same way (a cell with a comma, quoted)
            var csv = TextExport.Csv(project, TextExport.Items(project, [menu], glossary))
                .Replace(",,", ",\"<c12> M</c>ultiplier, encore, les lots\",", StringComparison.Ordinal);
            var fromCsv = TextImport.Read(csv, ".csv");
            Assert.Equal("<c12> M</c>ultiplier, encore, les lots", Assert.Single(fromCsv).Translation);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
