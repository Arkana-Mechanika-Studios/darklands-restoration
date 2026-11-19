using Darklands.Content.Formats.Text;
using Darklands.Content.Formats.Text.Strings;

namespace Darklands.Rendering.Tests;

/// <summary>The written form of card texts (L-7): tags for the control codes, line breaks free, the option lines kept.</summary>
public sealed class CardTextFormTests
{
    [Fact]
    public void TheTagsAreTheBlocksCodes()
    {
        var bytes = CardTextForm.ToBytes("Ihr erfahrt...<br><p>\n    <opt>...<indent>einen Söldner.<br>\n    <opt-saints>...<x19>Heilige<if-1>ja<endif>", out var problem);
        Assert.Null(problem);
        byte[] expected =
        [
            .. "Ihr erfahrt..."u8, 0x0A, 0x14, 0x15, .. "..."u8, 0x1D, .. Darklands.Core.GameTextCodec.Encode("einen Söldner."), 0x0A,
            0x16, .. "..."u8, 0x19, .. "Heilige"u8, 0x01, (byte)' ', .. "ja"u8, 0x13, (byte)' ',
        ];
        Assert.Equal(expected, bytes);
    }

    [Fact]
    public void ALineBreakIsASpaceBetweenWordsAndNothingNextToATag()
    {
        Assert.Equal("one two"u8.ToArray(), CardTextForm.ToBytes("one   \n    two", out _));
        Assert.Equal([0x0A, .. "two"u8], CardTextForm.ToBytes("<br>\n  two", out _));
    }

    [Fact]
    public void TheWrittenFormNumbersThePassagesAndEscapesBraces()
    {
        // the byte after each marker is the one text-paint-wrapped skips: it is not written
        byte[] raw = [.. "A"u8, 0x01, 0x14, .. "one"u8, 0x13, 0x0A, 0x01, (byte)'T', .. "two $MonthName"u8, 0x13, 0x0A, 0x0A];
        Assert.Equal("A<if-1>one<endif><if-2>two $MonthName<endif><br>", CardTextForm.ToWritten(raw));
        // 0x7B is the game's o-umlaut (GameTextCodec), so only a } can come from the bytes
        Assert.Equal("öx{\"}\"}", CardTextForm.ToWritten("{x}"u8));
    }

    [Fact]
    public void ATranslationMustKeepTheOptionsPassagesNamesAndCodes()
    {
        byte[] raw = [.. "Ask $ChosenOneName"u8, 0x0A, 0x14, 0x15, .. "...yes"u8, 0x0A, 0x16, .. "...a saint"u8, 0x0A, 0x01, 0x14, .. "more"u8, 0x13, 0x0A, 0x19];
        Assert.Null(CardTextForm.Check("Frage $ChosenOneName<br><p><opt>...ja<br><opt-saints>...einen Heiligen<br><if-1>mehr<endif><x19>", raw));
        Assert.Contains("option lines", CardTextForm.Check("Frage $ChosenOneName<br><opt-saints>...<opt>...<if-1>mehr<endif><x19>", raw));
        Assert.Contains("option lines", CardTextForm.Check("Frage $ChosenOneName<br><opt>...ja<if-1>mehr<endif><x19>", raw));
        Assert.Contains("passages", CardTextForm.Check("Frage $ChosenOneName<opt>.<opt-saints>.<if-2>mehr<endif><x19>", raw));
        Assert.Contains("passages", CardTextForm.Check("Frage $ChosenOneName<opt>.<opt-saints>.<x19>", raw));
        Assert.Contains("$Names", CardTextForm.Check("Frage $ChosenTwoName<opt>.<opt-saints>.<if-1>mehr<endif><x19>", raw));
        Assert.Contains("code bytes", CardTextForm.Check("Frage $ChosenOneName<opt>.<opt-saints>.<if-1>mehr<endif>", raw));
        Assert.Contains("unknown tag", CardTextForm.Check("<if>", raw));
    }

    [Fact]
    public void AnUnknownTagOrAnOpenBracketIsRefused()
    {
        Assert.Null(CardTextForm.ToBytes("a <bold> word", out var unknown));
        Assert.Contains("<bold>", unknown);
        Assert.Null(CardTextForm.ToBytes("a < b", out var open));
        Assert.NotNull(open);
        Assert.Null(CardTextForm.ToBytes("<x41>", out _));   // a letter is no control byte
    }

    [Fact]
    public void EveryEnglishBlockKeepsItsOptionsThroughItsWrittenForm()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var table = OriginalTextExtractor.Extract(files);
        var blocks = table.Entries.Where(e => e.Key.StartsWith("msg:", StringComparison.Ordinal)).ToList();
        Assert.True(blocks.Count > 3000, $"{blocks.Count} blocks");
        foreach (var entry in blocks)
        {
            var raw = entry.Raw.ToArray();
            var written = CardTextForm.ToWritten(raw);
            var back = CardTextForm.ToBytes(written, out var problem);
            Assert.True(back is not null, $"{entry.Key}: {problem}");
            Assert.Null(CardTextForm.Check(written, raw));
            Assert.Equal(CardTextForm.Options(raw), CardTextForm.Options(back));
            // the same bytes but for the byte text-paint-wrapped skips after a conditional marker (a block ending with one, $situa00 #4,
            //   gets its space: the original runs past the text's end there, D-0d3a38)
            Assert.Equal(raw.Length + (raw.Length > 0 && raw[^1] is 0x01 or 0x13 ? 1 : 0), back.Length);
            for (var i = 0; i < raw.Length; i++)
            {
                if (raw[i] is 0x01 or 0x13 && i + 1 < raw.Length)
                {
                    Assert.Equal(raw[i], back[i]);
                    i++;
                    continue;
                }
                Assert.True(raw[i] == back[i], $"{entry.Key}: byte {i} {raw[i]:X2} became {back[i]:X2}");
            }
        }
    }
}
