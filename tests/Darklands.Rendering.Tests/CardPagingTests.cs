using Darklands.Content;
using Darklands.Content.Formats.Text;
using Darklands.Localization;
using Darklands.Presentation;
using Darklands.Presentation.Graphics;

namespace Darklands.Rendering.Tests;

/// <summary>
/// A pack's card text too long for the card's window is laid out in pages (CardPaging): the pages joined give the text back, each
/// fits, and the pack check calls such a text a note, not an overflow. With the other edition's pack of a developer
/// (DARKLANDS_EDITION_PACK: the German edition's card texts, 140 of which run past the foot) every overflowing text pages without
/// loss; without it the tests pass vacuously (no game text is in the repository).
/// </summary>
public sealed class CardPagingTests
{
    [Fact]
    public void ATextIsCutBetweenLinesOrAtSpacesAndJoinsBack()
    {
        // a made-up text: "fits" takes pages of at most 12 codes (the last of at most 20)
        int[] Codes(string s) => [.. s.Select(c => (int)c)];
        var text = Codes("aaa bbb\nccc ddd eee fff ggg\nhhh\n" + (char)CardTextLayout.PlainOption + "opt one\n" + (char)CardTextLayout.PlainOption + "opt two");
        var paging = CardPaging.Of(text, (page, _, last) => page.Length <= (last ? 20 : 12));
        Assert.NotNull(paging);
        Assert.Equal(text, paging!.Joined());
        // the option lines together on the last page, no page but the last holds one
        Assert.All(paging.Pages.SkipLast(1), p => Assert.DoesNotContain(p, c => CardTextLayout.IsOptionMarker(c)));
        Assert.Equal(2, paging.Pages[^1].Count(CardTextLayout.IsOptionMarker));
        // no page ends inside a word
        for (var k = 0; k < paging.Pages.Count - 1; k++)
        {
            var end = paging.Pages[k][^1];
            Assert.True(paging.CutAtSpace[k] || end == '\n', $"page {k} ends with '{(char)end}'");
        }
        // a text that fits is not paged; options that cannot fit cannot be paged
        Assert.Null(CardPaging.Of(Codes("short"), (_, _, _) => true));
        Assert.Null(CardPaging.Of(text, (page, _, _) => !page.Any(CardTextLayout.IsOptionMarker) && page.Length <= 12));
    }

    [Fact]
    public void ThePassagesAreResolvedBeforePaging()
    {
        int[] text = ['a', CardTextLayout.ConditionStart, 'b', CardTextLayout.ConditionEnd, 'c', CardTextLayout.ConditionStart, 'd', CardTextLayout.ConditionEnd, 'e'];
        Assert.Equal(['a', 'b', 'c', 'e'], CardPaging.Resolve(text, [true, false]));
        Assert.Equal(['a', 'c', 'd', 'e'], CardPaging.Resolve(text, [false, true]));
    }

    [Fact]
    public void TheOtherEditionsLongCardTextsAllPage()
    {
        if (GameFolder.Files is not { } files || Environment.GetEnvironmentVariable("DARKLANDS_EDITION_PACK") is not { Length: > 0 } folder
            || LanguagePack.Load(folder, out _) is not { } pack)
        {
            return;
        }
        var assets = new GameAssets(files);
        var painter = new TextPainter(assets.Font(3));
        var conditions = Enumerable.Repeat(true, 32).ToList();
        var paged = 0;
        foreach (var key in TranslatorTemplate.Build(files, []).Select(m => m.Key)
            .Where(k => k.StartsWith("msg:", StringComparison.Ordinal) && pack.Localizer.IsTranslated(k)))
        {
            var hash = key.IndexOf('#', StringComparison.Ordinal);
            var block = assets.Messages(key[4..hash]).Blocks[int.Parse(key.AsSpan(hash + 1), System.Globalization.CultureInfo.InvariantCulture)];
            if (CardTextForm.ToCodes(pack.Localizer.Get(key), out _) is not { } codes)
            {
                continue;
            }
            var canvas = new Canvas(new Darklands.Platform.Screen());
            new CardTextLayout(painter).Draw(canvas, codes, CardScreen.WindowX + block.X + 5, block.Y + 5, block.Width - 10, new CardTextLayout.Settings
            {
                Conditions = conditions,
                Color = 15,
                OptionColor = 15,
                DropCap = letter => DropCaps.Get(assets, letter),
            });
            if (CardPaging.Foot(canvas) < CardPaging.WindowFoot)
            {
                continue;
            }
            var paging = CardPaging.ForBlock(painter, block, codes, conditions, [], letter => DropCaps.Get(assets, letter));
            Assert.True(paging is not null, $"{key} does not page");
            Assert.Equal(CardPaging.Resolve(codes, conditions), paging!.Joined());
            paged++;
        }
        Assert.True(paged >= 100, $"{paged} texts paged");
    }
}
