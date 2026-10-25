using Darklands.Content;
using Darklands.Content.Formats.Text;

namespace Darklands.Cards.Tests;

/// <summary>A card block's position as the scanner R043+0x0C9E stores it: y clamped to 0..200, x to 0..320 (as signed words).</summary>
public sealed class MessageBlockClampTests
{
    [Theory]
    // y byte, x word, width word -> the block's x, y, width
    [InlineData(20, 30, 100, 30, 20, 100)]          // inside: as read
    [InlineData(250, 30, 100, 30, 200, 100)]        // y above 200: 200
    [InlineData(200, 320, 100, 320, 200, 100)]      // the limits themselves stay
    [InlineData(10, 400, 90, 320, 10, 90)]          // x above 0x140: 0x140
    [InlineData(10, 0xFFF0, 90, 0, 10, 90)]         // x negative as a signed word: 0
    public void TheBlockIsClamped(int y, int x, int width, int blockX, int blockY, int blockWidth)
    {
        byte[] payload = [1, (byte)y, (byte)x, (byte)(x >> 8), (byte)width, (byte)(width >> 8), (byte)'A', 0];
        var card = Assert.Single(MsgCardDocumentParser.ParseEntryPayload(payload).Cards);
        var block = MessageBlock.From(card);
        Assert.Equal((blockX, blockY, blockWidth), (block.X, block.Y, block.Width));
    }
}
