using Darklands.Content;
using Darklands.Localization;
using Darklands.Presentation;

namespace Darklands.Rendering.Tests;

/// <summary>The pseudo-locale pack: longer texts in the game's letters, every structure the check requires kept.</summary>
public sealed class PseudoPackTests
{
    [Fact]
    public void OnlyTheWordsChange()
    {
        Assert.Equal("{ $arg0 } höllds{\" \"}", PseudoPack.Transform("{ $arg0 } holds{\" \"}"));
        Assert.Equal("<c12> M</c>öree", PseudoPack.Transform("<c12> M</c>ore"));
        Assert.Equal("Äskk $ChosenOneName<br><opt>...yess", PseudoPack.Transform("Ask $ChosenOneName<br><opt>...yes"));
        Assert.Equal("Thee", PseudoPack.Transform("The"));
        // a select's variant keys stay (the money texts' plurals by $count); a text's own bracket is words
        var plural = PseudoPack.Transform("\n    { $count ->\n        [one] { $amount } pfennig\n       *[other] { $amount } pfennigs\n    }");
        Assert.Contains("\n        [one] { $amount } ", plural, StringComparison.Ordinal);
        Assert.Contains("\n       *[other] { $amount } ", plural, StringComparison.Ordinal);
        Assert.DoesNotContain("pfennig\n", plural, StringComparison.Ordinal);
        Assert.DoesNotContain("[one]", PseudoPack.Transform("a [one] b"), StringComparison.Ordinal);
    }

    [Fact]
    public void ThePseudoPackOfTheGamePassesTheCheck()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var template = TranslatorTemplate.Build(files, []);
        var folder = Path.Combine(Path.GetTempPath(), "dl-qps-" + Guid.NewGuid().ToString("N"));
        try
        {
            TranslatorTemplate.Write(folder, PseudoPack.From(template), PseudoPack.Language, "pseudo");
            var pack = LanguagePack.Load(folder, out var why);
            Assert.True(pack is not null, why);
            Assert.Equal("qps", pack!.Language);
            var findings = LanguagePackCheck.Run(pack, new GameAssets(files), template, []);
            Assert.DoesNotContain(findings, f => f.Severity == LanguagePackCheck.Severity.Error);
            // used as written: the localizer does not transform a pack's qps texts again
            var more = template.Single(m => m.Key == "exe:290E:0112");
            Assert.Equal(PseudoPack.Transform(more.Value).Replace("{\" \"}", " ", StringComparison.Ordinal), pack.Localizer.Get(more.Key).Replace("{\" \"}", " ", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
