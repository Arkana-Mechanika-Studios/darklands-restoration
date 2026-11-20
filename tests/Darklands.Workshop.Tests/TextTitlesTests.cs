using Darklands.Workshop.Text;

namespace Darklands.Workshop.Tests;

/// <summary>The list's titles: a text's first words, a menu's hot key marked, the game's codes left out.</summary>
public sealed class TextTitlesTests
{
    [Theory]
    [InlineData("<c12> M</c>ore batches", "More batches", "\uE000M\uE001ore batches")]
    [InlineData("{\"\\u0002\"}Expert", "Expert", "Expert")]
    [InlineData("{ $arg0 } barters for { $arg1 }", "$arg0 barters for $arg1", "$arg0 barters for $arg1")]
    public void ATitleIsTheFirstWords(string english, string plain, string marked)
    {
        Assert.Equal(plain, TextTitles.FirstWords(english));
        Assert.Equal(marked, TextTitles.Marked(english));
    }

    [Fact]
    public void ACutTitleClosesItsHotKey()
    {
        var marked = TextTitles.Marked("<c12> Averyveryverylongwordthatgoesonandonandon</c> more", 20);
        Assert.Equal(marked.Count(c => c == TextTitles.HotStart), marked.Count(c => c == TextTitles.HotEnd));
    }

    [Fact]
    public void AScreensNameIsInWords() => Assert.Equal("Character sheet", TextTitles.Screen("CharacterSheetScreen"));
}
