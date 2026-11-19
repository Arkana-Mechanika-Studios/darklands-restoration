namespace Darklands.Localization.Tests;

public class PseudoLocaleTests
{
    [Fact]
    public void Every_ascii_letter_is_mapped()
    {
        var all = string.Concat(Enumerable.Range('A', 26).Select(c => (char)c))
                  + string.Concat(Enumerable.Range('a', 26).Select(c => (char)c));
        var result = PseudoLocale.Transform(all, brackets: false);
        Assert.Equal(all.Length, result.TrimEnd('~').Length);
        Assert.DoesNotContain(result, char.IsAsciiLetter);
    }

    [Fact]
    public void Keeps_placeables_and_stretches_text()
    {
        const string source = "Hello { $name }, you carry { $count } items on the long road.";
        var result = PseudoLocale.Transform(source);
        Assert.Contains("{ $name }", result);
        Assert.Contains("{ $count }", result);
        Assert.StartsWith("[", result);
        Assert.EndsWith("]", result);
        var letters = source.Count(char.IsAsciiLetter) - "name".Length - "count".Length;
        Assert.True(result.Count(c => c == '~') >= letters * 0.3, result);
        Assert.True(result.Length > source.Length + 8, result);
        Assert.DoesNotContain("Hello", result);
    }

    [Fact]
    public void Keeps_selects_and_quoted_braces_inside_placeables()
    {
        const string placeable = "{ $n ->\n    [one] a { \"}\" } thing\n   *[other] things\n}";
        var result = PseudoLocale.Transform("Got " + placeable + " today");
        Assert.Contains(placeable, result);
    }

    [Fact]
    public void Is_deterministic()
    {
        Assert.Equal(PseudoLocale.Transform("Some text here"), PseudoLocale.Transform("Some text here"));
    }

    [Fact]
    public void Leaves_game_codes_and_hotkeys_alone()
    {
        // 0x1F is a with diaeresis, a brace is o with diaeresis, a bar is u with diaeresis or a hotkey marker
        var source = "J|ust rest $Name\u001f b{se";
        var result = PseudoLocale.Transform(source, placeables: false);
        Assert.Contains("J|u", result);
        Assert.Contains("$Name", result);
        Assert.Contains("\u001f", result);
        Assert.Contains("{", result);
    }

    [Fact]
    public void A_single_character_text_is_unchanged()
    {
        Assert.Equal("j", PseudoLocale.Transform("j"));
    }
}
