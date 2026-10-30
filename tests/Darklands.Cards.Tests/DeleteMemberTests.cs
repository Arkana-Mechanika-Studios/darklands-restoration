using Darklands.Cards;
using Darklands.Core.Characters;

namespace Darklands.Cards.Tests;

/// <summary>
/// The leader and the party size after a member is deleted (R037+0x3F30): the expected values come from the original's bytes run on
/// these parties (status bytes DS:9C69 + 0x80 i, leader byte DS:907B, party-headcount DS:A67E afterwards; its calls stubbed).
/// </summary>
public sealed class DeleteMemberTests
{
    [Theory]
    [InlineData(new[] { 1, 1, 1, 0, 0 }, 0, 0, 1, 3)]
    [InlineData(new[] { 1, 3, 1, 0, 0 }, 0, 0, 2, 3)]
    [InlineData(new[] { 1, 2, 2, 0, 0 }, 0, 0, 0, 3)]   // nobody able: the leader byte keeps the deleted slot
    [InlineData(new[] { 1, 1, 1, 0, 0 }, 2, 0, 2, 3)]
    [InlineData(new[] { 2, 1, 1, 1, 1 }, 1, 1, 2, 5)]
    [InlineData(new[] { 1, 1, 0, 0, 0 }, 1, 1, 0, 1)]
    [InlineData(new[] { 1, 0, 0, 0, 1 }, 0, 0, 4, 5)]
    [InlineData(new[] { 5, 4, 1, 1, 0 }, 0, 0, 2, 4)]
    [InlineData(new[] { 1, 1, 1, 1, 1 }, 4, 4, 0, 4)]
    [InlineData(new[] { 1, 2, 3, 4, 5 }, 0, 0, 0, 5)]
    [InlineData(new[] { 0, 1, 1, 0, 0 }, 0, 0, 1, 3)]
    [InlineData(new[] { 1, 1, 1, 0, 0 }, 3, 3, 0, 3)]   // a slot past the party that is the leader's: the leadership still passes on
    public void TheLeaderAndThePartySizeAreTheOriginals(int[] statuses, int leader, int who, int leaderAfter, int sizeAfter)
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var state = CardHarness.NewGame(seed: 5, location: 3);
        var party = state.Party;
        while (party.Members.Count < Party.MaxMembers)
        {
            party.Members.Add(new Character());
        }
        for (var i = 0; i < Party.MaxMembers; i++)
        {
            party.Members[i].Status = (CharacterStatus)statuses[i];
        }
        party.TrimToSize();
        party.LeaderIndex = leader;
        var c = new CardContext(state, CardHarness.World!, new ScriptedScreen(0));
        c.DeleteMember(who);
        Assert.Equal(leaderAfter, party.LeaderIndex);
        Assert.Equal(sizeAfter, party.Members.Count);
        Assert.Equal(CharacterStatus.Empty, party.Slot(who).Status);
    }
}
