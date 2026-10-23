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
    public void ASlotLeftWithoutAnEntryIsNoRosterMemberWhenACardGivesItAStatus()
    {
        // BearCave (R104) sets a random member unconscious: a slot party_remove emptied (ids[1] = -1) gets status 2 and nothing else,
        // so charactr.tmp and the saved game have no record for it (the oracle's card 0xF0 on a save with such a slot).
        var s = WithRoster();
        var count = s.Roster.Characters.Count;
        PartyRules.RemoveFromParty(s, 1);
        s.Party.Slot(1).Status = CharacterStatus.Unconscious;

        Assert.Equal(-1, PartyRules.CharacterInSlot(s, 1));
        Assert.Equal(count, s.Roster.Characters.Count);
    }

    [Fact]
    public void AHirelingTakesTheFigureArgumentAndThePaletteTheSlotHeld()
    {
        // add_member (R029+0x4650): member_shield[slot] = the [bp+0xC] argument (R029+0x48B1), member_palette stays the slot's.
        var s = WithRoster();
        var last = s.Party.Members.Count - 1;
        s.Party.Members[1].Image = "C00";
        s.Party.Members[1].ImagePalette = Enumerable.Range(1, 24).Select(i => (byte)i).ToArray();
        s.Party.Members[last].Image = "A00";
        s.Party.Members[last].ImagePalette = Enumerable.Range(30, 24).Select(i => (byte)i).ToArray();
        PartyRules.RemoveFromParty(s, last);
        PartyRules.RemoveFromParty(s, 1);

        Assert.Equal(1, PartyRules.AddMember(s, "Wolf Bauer", "Wolf", "F01", 100, 1));
        Assert.Equal("F01", s.Party.Members[1].Image);
        Assert.Equal(Enumerable.Range(1, 24).Select(i => (byte)i), s.Party.Members[1].ImagePalette);
        Assert.Equal(last, PartyRules.AddMember(s, "Hans Bauer", "Hans", "F01", 100, 1));
        Assert.Equal("F01", s.Party.Members[last].Image);
        Assert.Equal(Enumerable.Range(30, 24).Select(i => (byte)i), s.Party.Members[last].ImagePalette);
        Assert.True(PartyRules.CharacterInSlot(s, 1) >= 0);   // a hireling is a roster entry (idx[slot] = cnt++)
    }

    [Fact]
    public void AHirelingInASlotAMemberLeftPastThePartyKeepsThatMembersRecord()
    {
        // party_remove clears the status byte only: add_member there leaves +0x58/+0x5A and the unnamed record bytes as they were.
        var s = WithRoster();
        var last = s.Party.Members.Count - 1;
        var leaving = s.Party.Members[last];
        leaving.Weapon = new EquippedItem(3, 31);
        leaving.MissileWeapon = new EquippedItem(-1, 17);
        var bytes = new byte[Character.MemberRecordSize];
        bytes[0x60] = 0x5A;
        leaving.OriginalRecord = bytes;
        PartyRules.RemoveFromParty(s, last);
        Assert.Equal(last, s.Party.Members.Count);

        Assert.Equal(last, PartyRules.AddMember(s, "Wolf Bauer", "Wolf", "F01", 100, 1));
        var m = s.Party.Members[last];
        Assert.Equal(new EquippedItem(-1, 31), m.Weapon);
        Assert.Equal(new EquippedItem(-1, 17), m.MissileWeapon);
        Assert.Equal(0x5A, m.OriginalRecord![0x60]);
        Assert.Equal(last, m.OriginalRecord[0x1C]);
    }

    [Fact]
    public void ACharacterJoiningFromTheRosterTakesTheFigureTheSlotHeld()
    {
        // party_add (R037+0x3CD6) reads record, saints, formulas and pack into the slot, not member_shield / member_palette.
        var s = WithRoster();
        var a = s.Party.Members[1];
        var b = s.Party.Members[2];
        a.Image = "C00";
        a.ImagePalette = Enumerable.Range(1, 24).Select(i => (byte)i).ToArray();
        b.Image = "A00";
        b.ImagePalette = Enumerable.Range(30, 24).Select(i => (byte)i).ToArray();
        var entryA = PartyRules.CharacterInSlot(s, 1);
        var entryB = PartyRules.CharacterInSlot(s, 2);
        PartyRules.RemoveFromParty(s, 1);
        PartyRules.RemoveFromParty(s, 2);

        Assert.Equal(1, PartyRules.AddFromRoster(s, entryB));
        Assert.Same(b, s.Party.Members[1]);
        Assert.Equal("C00", b.Image);
        Assert.Equal(Enumerable.Range(1, 24).Select(i => (byte)i), b.ImagePalette);
        Assert.Equal(2, PartyRules.AddFromRoster(s, entryA));
        Assert.Equal("A00", a.Image);
        Assert.Equal(Enumerable.Range(30, 24).Select(i => (byte)i), a.ImagePalette);
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
