using Darklands.Core.Characters;
using Darklands.Core.Rules;

namespace Darklands.Core.Tests;

/// <summary>
/// A leader index past the party (party-leave R037+0x3B58 of the leader in the last slot while no other member is able keeps the index,
/// party-headcount shrinks): hours-elapse R029+0x255A reads party[leader] of the five-slot table, finds status 0 and takes the first able slot.
/// </summary>
public sealed class LeaderPastThePartyTests
{
    private static GameState LeaderLeftFromTheLastSlot()
    {
        var s = TestGame.Create(location: 7);
        s.Roster.Reset(s.Party);
        var last = s.Party.Members.Count - 1;
        foreach (var m in s.Party.Members)
        {
            m.Status = CharacterStatus.Unconscious;
        }
        s.Party.Members[last].Status = CharacterStatus.Able;
        s.Party.LeaderIndex = last;
        PartyRules.RemoveFromParty(s, last);
        Assert.Equal(last, s.Party.LeaderIndex);                // nobody else able: the leader byte stays
        Assert.Equal(last, s.Party.Members.Count);              // ... past the party
        return s;
    }

    [Fact]
    public void TheLeaderSlotPastThePartyIsTheEmptiedRecord()
    {
        var s = LeaderLeftFromTheLastSlot();
        Assert.Equal(CharacterStatus.Empty, s.Party.Leader.Status);
    }

    [Fact]
    public void TimePassingMakesTheFirstAbleMemberTheLeader()
    {
        var s = LeaderLeftFromTheLastSlot();
        s.Party.Members[1].Status = CharacterStatus.Able;
        TimePassage.Recover(s, 1);
        Assert.Equal(1, s.Party.LeaderIndex);
    }

    [Fact]
    public void WithNobodyAbleTheLeaderStaysPastTheParty()
    {
        var s = LeaderLeftFromTheLastSlot();
        var past = s.Party.LeaderIndex;
        TimePassage.Recover(s, 1);
        Assert.Equal(past, s.Party.LeaderIndex);
    }
}
