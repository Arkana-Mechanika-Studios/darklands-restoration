using Darklands.Content.Formats.Graphics;

namespace Darklands.Battles.Tests;

public sealed class CatMemberDecoderTests
{
    [Fact]
    public void AChunkEndGoesOnWithTheNextChunkAfterTheOutputSoFar()
    {
        // control bits (lowest first): literal, long match with length 0 + byte 1 (the chunk ends), literal, short match of 2 at distance 2,
        // long match with length 0 + byte 0 (the end); file-read-whole 05D9:019A calls 05D9:0070 again for the second chunk, bits kept
        byte[] stored = [0x0D, 0x02, (byte)'A', 0x00, 0x00, 0x01, (byte)'B', 0xFE, 0x00, 0x00, 0x00];
        Assert.Equal("ABAB"u8.ToArray(), CatMemberDecoder.Decode(stored, 16));
    }

    [Fact]
    public void TheWildHuntsRiderDecodesWholeOverItsChunkEnd()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        // gen-carry-26: card 0x41's fight placed the rider at its start (the original ran on); the C# stopped at this member's chunk end
        var catalogue = CatArchive.Decode(files.Read("M00C.CAT"));
        Assert.Equal(42244, CatMemberDecoder.Decode(catalogue.ReadStored("M40WKWH.IMC"), 0x20000).Length);
    }
}
