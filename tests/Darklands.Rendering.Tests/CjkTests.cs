using Darklands.Presentation.Graphics;
using Darklands.Rendering;

namespace Darklands.Rendering.Tests;

/// <summary>
/// Chinese, Japanese and Korean (1.1): where their lines break, and a font collection's Korean font drawing Hangul. The CJK font is
/// not in the repository: its test runs where DARKLANDS_CJK_FONT names a .ttc of Noto Sans CJK (index 1: Korean), else it passes
/// without running.
/// </summary>
public sealed class CjkTests
{
    [Fact]
    public void ChineseAndJapaneseBreakBetweenCharactersButNotAtTheirMarks()
    {
        var breaks = LineBreaks.For("ja")!;
        Assert.True(breaks('日', '本'));
        // no line starts with a closing mark, none ends with an opening one
        Assert.False(breaks('本', '。'));
        Assert.False(breaks('「', '日'));
        Assert.True(breaks('。', '日'));
        // Latin letters and digits keep together
        Assert.False(breaks('a', 'b'));
        Assert.False(breaks('1', '2'));
        Assert.True(breaks('a', '日'));
        Assert.NotNull(LineBreaks.For("zh-Hans"));
        // Korean breaks at its spaces, as the game does; every other language too
        Assert.Null(LineBreaks.For("ko"));
        Assert.Null(LineBreaks.For("fr"));
        Assert.Null(LineBreaks.For(null));
    }

    [Fact]
    public void ACollectionsKoreanFontDrawsHangul()
    {
        if (Environment.GetEnvironmentVariable("DARKLANDS_CJK_FONT") is not { Length: > 0 } ttc || !File.Exists(ttc))
        {
            return;
        }
        var folder = Path.Combine(Path.GetTempPath(), "dl-cjk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "slot1.json"),
                "{ \"ttf\": \"" + ttc.Replace("\\", "\\\\", StringComparison.Ordinal) + "\", \"index\": 1, \"size\": 10, \"lineSpacing\": 2 }");
            var font = TtfFont.Load(Path.Combine(folder, "slot1.json"), out var problem);
            Assert.True(font is not null, problem);
            Assert.True(font.Has('한') && font.Has('글') && font.Has('A'));
            Assert.True(font.Advance('한') > 0);
            Assert.Contains(Enumerable.Range(0, font.Advance('한') * font.Height), i => font.Pixel('한', i % font.Advance('한'), i / font.Advance('한')));
            Assert.Equal(font.Height + 2, font.LineHeight);
            File.WriteAllText(Path.Combine(folder, "slot2.json"), "{ \"ttf\": \"" + ttc.Replace("\\", "\\\\", StringComparison.Ordinal) + "\", \"index\": 50, \"size\": 10 }");
            Assert.Null(TtfFont.Load(Path.Combine(folder, "slot2.json"), out problem));
            Assert.Contains("no font 50", problem, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
