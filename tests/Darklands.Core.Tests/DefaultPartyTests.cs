using Darklands.Core.Characters;

namespace Darklands.Core.Tests;

/// <summary>The Quickstart party (R001 0E76:0000): its figure colours come with the program, not from SAVES\DEFAULT.</summary>
public sealed class DefaultPartyTests
{
    [Fact]
    public void EachMemberCarriesItsSlotsColours()
    {
        var party = DefaultParty.Create(1);
        Assert.Equal(5, DefaultParty.Colours.Count);
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(DefaultParty.Colours[i], party.Members[i].ImagePalette);
            Assert.NotSame(DefaultParty.Colours[i], party.Members[i].ImagePalette);
            Assert.Contains(party.Members[i].ImagePalette, b => b != 0);
        }
        Assert.All(DefaultParty.Colours[4], b => Assert.Equal(0, b));
    }

    [Fact]
    public void TheColoursAreTheOriginalsStores()
    {
        // R001+0x0797: Gretchen's first triple 3F 28 19, Ebhard's last 13 0F 07, Hans's from 0x31 on: 00 20 34.
        Assert.Equal(new byte[] { 0x3F, 0x28, 0x19 }, DefaultParty.Colours[0][..3]);
        Assert.Equal(new byte[] { 0x13, 0x0F, 0x07 }, DefaultParty.Colours[3][21..]);
        Assert.Equal(new byte[] { 0x00, 0x20, 0x34 }, DefaultParty.Colours[2][..3]);
        Assert.All(DefaultParty.Colours, c => Assert.Equal(24, c.Length));
    }

    [Fact]
    public void AChangeToAMembersColoursLeavesTheNextPartyAlone()
    {
        DefaultParty.Create(1).Members[0].ImagePalette[0] = 0;
        Assert.Equal(0x3F, DefaultParty.Create(1).Members[0].ImagePalette[0]);
    }
}
