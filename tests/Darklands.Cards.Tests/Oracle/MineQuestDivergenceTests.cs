namespace Darklands.Cards.Tests.Oracle;

/// <summary>
/// The condition of card-divergences.json's SourceOfTheHorrors row (card 0xE9, texts not compared): it holds only for a start without the
/// mine quest. With the quest (an event of kind Quest and tag Mine, atom ev8_0_12: its Data3 0 is the horror's detail) the original draws
/// the horror's option block as the C# does, so those runs are compared in full and count.
/// </summary>
public sealed class MineQuestDivergenceTests
{
    [Fact]
    public void TheRowHoldsWithoutTheMineQuestOnly()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        Assert.True(CardOracleTests.WithoutMineQuest("hand L3-O0 M1L2S1"));
        Assert.False(CardOracleTests.WithoutMineQuest("hand L3-Vev8_0_12-O0 M1L2S1"));
        Assert.Equal(0, Core.Rules.Quests.MineDetail(CardOracleStates.FromKey("L3-Vev8_0_12-O0")));
    }
}
