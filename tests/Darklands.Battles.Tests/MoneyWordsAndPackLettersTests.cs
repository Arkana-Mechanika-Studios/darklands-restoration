using Darklands.Content;
using Darklands.Content.Formats.Text.Strings;
using Darklands.Core;
using Darklands.Core.Economy;
using Darklands.Localization;
using Darklands.Presentation;

namespace Darklands.Battles.Tests;

/// <summary>
/// A card's $Money in a translation (MoneyWords): with the original's words exactly Money.ToString; with a pack's, its coins, plurals
/// and joins. And a language pack's letters against the original fonts (PackLetters): the HD fonts' cue for the launcher and the game.
/// </summary>
public sealed class MoneyWordsAndPackLettersTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir.FullName;
    }

    private static readonly Money[] Amounts =
    [
        Money.From(0, 0, 0), Money.From(0, 0, 1), Money.From(0, 0, 7), Money.From(0, 1, 0), Money.From(0, 5, 0), Money.From(1, 0, 0),
        Money.From(2, 0, 0), Money.From(1, 1, 1), Money.From(3, 2, 0), Money.From(1, 0, 11), Money.From(0, 1, 1), Money.From(1234, 19, 11),
        Money.From(21, 2, 5), Money.From(22, 12, 2),
    ];

    [Fact]
    public void TheOriginalsWordsGiveExactlyMoneyToString()
    {
        foreach (var money in Amounts)
        {
            var words = MoneyWords.Format(money, (id, args) => OriginalMessageText.OwnWords(new GameText(id, args.ToDictionary(a => a.Key, a => a.Value!)))!);
            Assert.Equal(money.ToString(), words);
        }
    }

    private static Func<string, IReadOnlyDictionary<string, object?>, string> Words(string sample)
    {
        var pack = LanguagePack.Load(Path.Combine(Root(), "docs", "language-samples", sample), out var why);
        Assert.True(pack is not null, why);
        foreach (var id in GameTextIds.MoneyWords)
        {
            Assert.True(pack!.Localizer.IsTranslated(id), id);
        }
        return (id, args) => pack!.Localizer.Get(id, args);
    }

    [Fact]
    public void ASamplesMoneyWordsHaveItsPluralsAndJoins()
    {
        var fr = Words("sample-fr");
        Assert.Equal("1 gros et 1 denier", MoneyWords.Format(Money.From(0, 1, 1), fr));
        Assert.Equal("3 florins, 2 gros et 1 denier", MoneyWords.Format(Money.From(3, 2, 1), fr));
        Assert.Equal("1 florin et 7 deniers", MoneyWords.Format(Money.From(1, 0, 7), fr));
        var ru = Words("sample-ru");
        Assert.Equal("1 флорин", MoneyWords.Format(Money.From(1, 0, 0), ru));
        Assert.Equal("2 флорина и 5 грошей", MoneyWords.Format(Money.From(2, 5, 0), ru));
        Assert.Equal("21 флорин, 11 грошей и 3 пфеннига", MoneyWords.Format(Money.From(21, 11, 3), ru));
        var pl = Words("sample-pl");
        Assert.Equal("22 floreny i 12 groszy", MoneyWords.Format(Money.From(22, 12, 0), pl));
        foreach (var sample in new[] { "sample-es", "sample-el", "sample-ja", "sample-ko" })
        {
            var words = Words(sample);
            Assert.DoesNotContain("groschen", MoneyWords.Format(Money.From(3, 2, 1), words), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ALanguageNeedingLettersTheOriginalFontsLackIsFoundAndAPackWithItsOwnFontIsNot()
    {
        if (GameFolder.Files is null || Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") is not { } game)
        {
            return;
        }
        var root = Directory.CreateTempSubdirectory("dkpackletters").FullName;
        try
        {
            void Copy(string sample, bool withFonts)
            {
                var from = Path.Combine(Root(), "docs", "language-samples", sample);
                foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(from, file);
                    if (!withFonts && relative.StartsWith("fonts", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    var to = Path.Combine(root, sample, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                    File.Copy(file, to);
                }
            }
            Copy("sample-ru", withFonts: false);
            Assert.Equal("Russian", PackLetters.NeedsFonts(Path.Combine(root, "sample-ru"), game));
            Assert.Contains('ж', PackLetters.Lacking(new GameFiles(game), LanguagePack.Load(Path.Combine(root, "sample-ru"), out _)!));
            // the samples as shipped bring their pixel fonts: nothing needed
            Assert.Null(PackLetters.NeedsFonts(Path.Combine(Root(), "docs", "language-samples", "sample-ru"), game));
            Assert.Null(PackLetters.NeedsFonts(Path.Combine(Root(), "docs", "language-samples", "sample-fr"), game));
            // no game folder: nothing said
            Assert.Null(PackLetters.NeedsFonts(Path.Combine(root, "sample-ru"), null));
            Assert.Equal("Greek", PackLetters.LanguageName("el"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // the translator's template of a coin's text chooses its plural by $count, so a translation's plural forms use the template's
    //   variables (the check: a variable the template lacks is an error); the joins are as their words
    [Fact]
    public void TheTemplatesCoinTextsChooseTheirPluralByCount()
    {
        foreach (var id in new[] { GameTextIds.MoneyFlorins, GameTextIds.MoneyGroschen, GameTextIds.MoneyPfennigs })
        {
            var template = OriginalMessageText.OwnTemplate(id);
            Assert.NotNull(template);
            Assert.Equal(["amount", "count"], FtlSource.Variables(template!).Order(StringComparer.Ordinal));
        }
        Assert.Null(OriginalMessageText.OwnTemplate(GameTextIds.MoneyTwo));
        Assert.Null(OriginalMessageText.OwnTemplate(GameTextIds.MoneyThree));
    }
}
