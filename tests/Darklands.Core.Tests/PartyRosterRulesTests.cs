using Darklands.Core.Characters;
using Darklands.Core.Events;
using Darklands.Core.Rules;

namespace Darklands.Core.Tests;

/// <summary>
/// party_report_away, party_remove, party_add, char_in_slot and slot_of_char (R029 23e2,
/// R037 3b58/3cd6/3a8a/3ae2) against the roster (CHARACTR.TMP's ids[] and records).
/// </summary>
public sealed class PartyRosterRulesTests
{
    private static GameState WithRoster()
    {
        var s = TestGame.Create(location: 7);
        s.Roster.Reset(s.Party);
        return s;
    }

    [Fact]
    public void AMemberWhoWentAwayIsNotedByRosterEntryAndLeavesForTheRoster()
    {
        var s = WithRoster();
        // The roster's entries in another order than the slots: slot 1 holds entry 3.
        (s.Roster.PartySlots[1], s.Roster.PartySlots[3]) = (3, 1);
        (s.Roster.Characters[1], s.Roster.Characters[3]) = (s.Roster.Characters[3], s.Roster.Characters[1]);
        var away = s.Party.Members[1];
        away.Status = CharacterStatus.Away;
        s.Party.LeaderIndex = 1;

        Assert.Equal(1, PartyRules.ReportAway(s));

        var note = s.Events.Find(EventKinds.MemberAway, -1, 7, -1, -1, -1);
        Assert.Equal(3, s.Events[note]!.Type);                 // char_in_slot(1) = ids[1] = 3
        Assert.Equal(-1, s.Roster.PartySlots[1]);
        Assert.False(s.Party.Slot(1).IsPresent);
        Assert.Same(away, s.Roster.Characters[3]);              // the record stays on the roster ...
        Assert.Equal(CharacterStatus.Away, away.Status);        // ... with status 5 (party_remove keeps 5)
        Assert.Equal(7, away.RosterLocation);                   // and the record's first word = cur_loc
        Assert.Equal(0, s.Party.LeaderIndex);                   // the first other member of status 1 leads
    }

    [Fact]
    public void ACharacterOfTheRosterJoinsInTheFirstFreeSlot()
    {
        var s = WithRoster();
        var left = s.Party.Members[2];
        PartyRules.RemoveFromParty(s, 2);
        Assert.Equal(CharacterStatus.Empty, left.Status);
        Assert.Equal(-1, PartyRules.SlotOfCharacter(s, 2));

        Assert.Equal(2, PartyRules.AddFromRoster(s, 2));
        Assert.Same(left, s.Party.Members[2]);
        Assert.Equal(CharacterStatus.Able, left.Status);
        Assert.Equal(2, PartyRules.CharacterInSlot(s, 2));
        Assert.Equal(2, PartyRules.SlotOfCharacter(s, 2));
    }

    [Fact]
    public void NobodyJoinsAFullPartyOrFromPastTheRoster()
    {
        var s = WithRoster();
        Assert.Equal(-1, PartyRules.AddFromRoster(s, s.Roster.Characters.Count));
        while (s.Party.Members.Count < Party.MaxMembers)
        {
            s.Party.Members.Add(new Character { Status = CharacterStatus.Able });
        }
        Assert.Equal(-1, PartyRules.AddFromRoster(s, 0));
    }
}
