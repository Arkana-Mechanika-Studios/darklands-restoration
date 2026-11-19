namespace Darklands.Core.Tests;

/// <summary>Game bytes ↔ Unicode: every byte comes back, the fonts' letters are letters, the codes stay codes.</summary>
public sealed class GameTextCodecTests
{
    [Fact]
    public void EveryByteComesBack()
    {
        var all = Enumerable.Range(0, 256).Select(b => (byte)b).ToArray();
        var text = GameTextCodec.Decode(all);
        Assert.Equal(256, text.Length);
        Assert.Equal(256, text.Distinct().Count());
        Assert.Equal(all, GameTextCodec.Encode(text));
    }

    [Fact]
    public void TheFontsLettersAreLetters()
    {
        Assert.Equal("äöüÄÖÜß", GameTextCodec.Decode([0x1F, 0x7B, 0x7C, 0x5B, 0x5C, 0x5D, 0x5F]));
        Assert.Equal([0x53, 0x63, 0x68, 0x7B, 0x66, 0x66], GameTextCodec.Encode("Schöff"));
        Assert.Equal("Bürgermeister", GameTextCodec.Decode("B|rgermeister"u8));
    }

    [Fact]
    public void ControlAndColourCodesStayAsTheyAre()
    {
        // the hotkey colour (0x8C .. 0xFF), a line break, the card text's layout codes
        Assert.Equal("\x8C J\n\x14\x1D\x01\x13", GameTextCodec.Decode([0x8C, 0x20, 0x4A, 0xFF, 0x0A, 0x14, 0x1D, 0x01, 0x13]));
        Assert.Equal([0x8C, 0x20, 0x4A, 0xFF], GameTextCodec.Encode("\x8C J"));
        Assert.Equal([0x8C, 0x20, 0x4A, 0xFF], GameTextCodec.Encode("\x8C J\xFF"));
        // a colour byte from 0xA0 keeps a character of its own, never its Latin-1 letter (a translation's é is a letter)
        Assert.NotEqual('ä', GameTextCodec.ToChar(0xE4));
        Assert.NotEqual('é', GameTextCodec.ToChar(0xE9));
        Assert.Equal(0xE4, GameTextCodec.ToByte(GameTextCodec.ToChar(0xE4)));
        Assert.Equal(0xE9, GameTextCodec.ToByte(GameTextCodec.ToChar(0xE9)));
    }

    [Fact]
    public void TextWrittenTheOldWayGivesTheSameBytes()
    {
        Assert.Equal(new byte[] { 0x5B, 0x5C, 0x5D, 0x5F, 0x7B, 0x7C, 0x1F }, GameTextCodec.Encode("[\\]_{|\x1F"));
        Assert.Equal((byte)'?', GameTextCodec.ToByte('€'));
        Assert.Equal("", GameTextCodec.Decode([]));
    }
}
