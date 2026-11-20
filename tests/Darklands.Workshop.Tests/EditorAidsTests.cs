using Darklands.Localization;
using Darklands.Workshop.Text;

namespace Darklands.Workshop.Tests;

/// <summary>The editor's aids (W-0 + E-text set 4): the glossary, the pack's variables, the chips left out or unknown.</summary>
public sealed class EditorAidsTests
{
    [Fact]
    public void TheGlossaryFindsItsTermsAndATranslationThatMissesOne()
    {
        var folder = Path.Combine(Path.GetTempPath(), "dl-glossary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var glossary = Glossary.Load(folder);
            glossary.Set(new GlossaryTerm("florin", "Gulden", "the gold coin"));
            glossary.Set(new GlossaryTerm("St. Gall", "St. Gallus", ""));
            glossary.Save(folder);
            var again = Glossary.Load(folder);
            Assert.Equal(2, again.Terms.Count);
            Assert.Equal(["florin"], again.In("Pay 3 florins? No: one florin.").Select(t => t.Term).Where(t => t == "florin").Distinct());
            Assert.Empty(again.In("florinus"));
            Assert.Equal(["St. Gall"], again.Missed("Pray to St. Gall", "Betet zum Heiligen Gallus").Select(t => t.Term));
            Assert.Empty(again.Missed("Pray to St. Gall", "Betet zu St. Gallus"));
            again.Remove("florin");
            Assert.Single(again.Terms);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ThePacksVariablesAreSetSavedAndReadByTheGame()
    {
        if (GameFolder.Files is null || Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") is not { } game)
        {
            return;
        }
        var folder = Path.Combine(Path.GetTempPath(), "dl-vars-" + Guid.NewGuid().ToString("N"));
        try
        {
            var project = TextProject.Create(folder, "de", "Test", null, game);
            project.SetVariable("him1", "ihr", "ihm");
            project.SetVariable("Him1", "Ihr", "Ihm");
            Assert.Throws<ArgumentException>(() => project.SetVariable("him-1", "a", "b"));
            project.Save();
            var pack = LanguagePack.Load(folder, out _)!;
            Assert.Empty(pack.Problems);
            Assert.Equal("ihr", pack.Variable("him1", female: true));
            Assert.Equal("Ihm", pack.Variable("Him1", female: false));
            Assert.True(File.Exists(Path.Combine(project.TextFolder, TextProject.VariablesFile)));
            var again = TextProject.Open(folder, game);
            Assert.Equal([("Him1", "Ihr", "Ihm"), ("him1", "ihr", "ihm")], again.Variables.OrderBy(v => v.Name, StringComparer.Ordinal));
            again.RemoveVariable("Him1");
            again.Save();
            Assert.Null(LanguagePack.Load(folder, out _)!.Variable("Him1", female: false));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void TheChipsLeftOutOrUnknownAreNamed()
    {
        var (missing, unknown) = TextTokens.Compare("Give $ChosenOneName<br><opt>{ $arg0 }", "Gebt $him1<opt>{ $arg0 }", new HashSet<string> { "him1" });
        Assert.Equal(["$ChosenOneName", "<br>"], missing);
        Assert.Empty(unknown);
        var (_, strange) = TextTokens.Compare("Give $ChosenOneName", "Gebt $Nobody", null);
        Assert.Equal(["$Nobody"], strange);
    }
}
