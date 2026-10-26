using System.Text;
using Darklands.Cards.Scripts;

namespace Darklands.Cards.Tests;

/// <summary>Card differences the card oracle found from saved games and controlled results (local/batch/cards1o), as the original.</summary>
public sealed class CardLeadsTests
{
    private static CardContext? Context()
    {
        if (!CardHarness.Available)
        {
            return null;
        }
        return new CardContext(CardHarness.NewGame(1, 3), CardHarness.World!, new ScriptedScreen(-1));
    }

    private static string Expand(CardContext c, string text) =>
        Encoding.Latin1.GetString(TextVariables.Expand(Encoding.Latin1.GetBytes(text), c));

    [Fact]
    public void AChosenIndexPastThePartyReadsTheLoadedMessageFile()
    {
        // party[k] is DS:9C69 + 0x80k (name +0x2A, sex +3) and msg_file DS:A895: slot 29's name starts at file offset 638 (card 0x6C
        // puts the least agile member's Agility, 29, there: "With a crash storeroom. You slide ..." in the original)
        if (Context() is not { } c)
        {
            return;
        }
        var file = new byte[700];
        Encoding.Latin1.GetBytes("storeroom.").CopyTo(file, 638);
        c.MessageFileBytes = file;
        c.Variables.Chosen[0] = 29;
        Assert.Equal("[storeroom.] he", Expand(c, "[$ChosenOneName] $he"));
        file[599] = 1;
        Assert.Equal("she", Expand(c, "$he"));
        // outside the file (another slot, or no file): no name
        c.Variables.Chosen[0] = 12;
        Assert.Equal("[]", Expand(c, "[$ChosenOneName]"));
        c.Variables.Chosen[0] = 29;
        c.MessageFileBytes = null;
        Assert.Equal("[]", Expand(c, "[$ChosenOneName]"));
    }

    [Fact]
    public void TheKnightsRenownWrapsAtSixteenBits()
    {
        // R092+0x16F2: fame * 3 in a 16-bit int, clamped to 1..99 (signed): fame 12392 gives 37176 = -28360, so 1
        if (Context() is not { } c)
        {
            return;
        }
        c.State.Party.Fame = 12392;
        Assert.Equal(1, KnightOnTheRoad.Renown(c));
        c.State.Party.Fame = 23584;   // 70752 = 5216
        Assert.Equal(99, KnightOnTheRoad.Renown(c));
        c.State.Party.Fame = 20;
        Assert.Equal(60, KnightOnTheRoad.Renown(c));
    }
}
