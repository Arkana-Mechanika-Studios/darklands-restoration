using Darklands.Core.Characters;
using Darklands.Presentation.Battles;

namespace Darklands.Battles.Tests;

/// <summary>The party's colours in the battle palette: the Quickstart party is never drawn black.</summary>
public sealed class QuickstartColoursTests
{
    [Fact]
    public void AMemberWithItsOwnColoursKeepsThem()
    {
        var party = DefaultParty.Create(1);
        for (var i = 0; i < 4; i++)
        {
            Assert.Same(party.Members[i].ImagePalette, BattleScreen.PartyColours(party.Members[i].ImagePalette, i));
        }
        var own = Enumerable.Repeat((byte)0x21, 24).ToArray();
        Assert.Same(own, BattleScreen.PartyColours(own, 2));
    }

    [Fact]
    public void AMemberWithoutColoursTakesTheQuickstartColoursOfItsSlot()
    {
        // A Quickstart party saved by release 0.9.0 (marked DIVERGENCE in BattleScreen.PartyColours).
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(DefaultParty.Colours[i], BattleScreen.PartyColours(new byte[24], i));
        }
    }
}
