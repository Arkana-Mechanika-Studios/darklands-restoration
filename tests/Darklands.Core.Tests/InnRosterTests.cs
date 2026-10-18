using Darklands.Core.Characters;
using Darklands.Core.Characters.Creation;
using Darklands.Core.Events;
using Darklands.Core.Rules;

namespace Darklands.Core.Tests;

/// <summary>
/// The inn's party roster (R038+0x23EC, R038+0x02F8): the list with its row table, stale rows and '+' marks, Add to the Party
/// (joining, leaving again within the week, no room), heraldry, the colour keys' row index, and the file's own records.
/// </summary>
public sealed class InnRosterTests
{
    private static readonly string[] Images = ["F01", "F60", "A00", "C00"];

    private static GameState WithRoster()
    {
        var s = TestGame.Create(location: 7);
        s.Roster.Reset(s.Party);
        return s;
    }

    [Fact]
    public void EveryMemberIsListedAndTheRowsOfTheSlotsEntriesAreMarked()
    {
        var s = WithRoster();
        var menu = new PartyMenuState();
        var rows = InnRoster.List(s, menu, staleLocation: 0);
        Assert.Equal([0, 1, 2, 3], rows.Select(r => r.Entry));
        Assert.All(rows, r => Assert.True(r.Marked));
        Assert.Equal(4, menu.Count);
        Assert.Equal([0, 1, 2, 3], menu.Rows.Take(4));
    }

    [Fact]
    public void TheMarkIsByRowNumberSoItSlipsWhenARecordBeforeAMemberIsNotListed()
    {
        var s = WithRoster();
        PartyRules.RemoveFromParty(s, 1);                       // left here (location 7), status 0
        var menu = new PartyMenuState();
        var here = InnRoster.List(s, menu, 0);
        Assert.Equal([0, 1, 2, 3], here.Select(r => r.Entry));
        Assert.Equal([true, false, true, true], here.Select(r => r.Marked));

        s.CurrentLocation = 8;                                  // another inn: the record left at 7 is not listed
        var there = InnRoster.List(s, menu, 0);
        Assert.Equal([0, 2, 3], there.Select(r => r.Entry));
        // ids = [0, -1, 2, 3]: rows 0 and 2 are marked, i.e. entry 0 and entry 3; entry 2 (a member) on row 1 is not.
        Assert.Equal([true, false, true], there.Select(r => r.Marked));
        Assert.Equal(4, menu.Count);                            // every record, listed or not
        Assert.Equal([0, 2, 3, 3], menu.Rows.Take(4));          // row 3 keeps the entry of the earlier listing
    }

    [Fact]
    public void AHirelingIsHiddenOnlyWhenTheStaleLocationWordNamesWhereTheTermBegan()
    {
        var s = WithRoster();
        var slot = PartyRules.AddMember(s, "Hans Hired", "Hans", 100, 1);
        Assert.Equal(4, slot);
        var menu = new PartyMenuState();
        Assert.Contains(InnRoster.List(s, menu, staleLocation: 3), r => r.Record.Name == "Hans Hired");
        Assert.DoesNotContain(InnRoster.List(s, menu, staleLocation: 7), r => r.Record.Name == "Hans Hired");
    }

    [Fact]
    public void ACharacterJoinsUnequippedWithAnEmptyPackAndLeavesAgainWithTheRecordAsRead()
    {
        var s = WithRoster();
        var member = s.Party.Members[1];
        PartyRules.RemoveFromParty(s, 1);
        var record = s.Roster.Characters[1];
        var stacks = record.Pack.Stacks;
        Assert.True(stacks > 0);
        var menu = new PartyMenuState();
        InnRoster.List(s, menu, 0);
        menu.Selected = 1;

        Assert.Equal(new InnRosterResult(InnRosterChange.Joined, 1), InnRoster.AddOrRemove(s, menu, Images));
        Assert.Same(record, s.Party.Members[1]);
        Assert.Equal(1, s.Roster.PartySlots[1]);
        Assert.Equal(CharacterStatus.Able, record.Status);
        Assert.Equal(new EquippedItem(-1, 0), record.Weapon);
        Assert.Equal(new EquippedItem(-1, 0), record.Shield);
        Assert.Equal(0, record.Pack.Stacks);
        Assert.Equal("F01", record.Image);
        Assert.Equal(PortraitColours.InnStartPalette, record.ImagePalette);
        Assert.Equal(PortraitColours.InnStartPalette, menu.Colours.Palette);
        Assert.True(s.Events.TypeAt(EventKinds.JoinedAtInn, 1, 7));
        Assert.Equal(stacks, s.Roster.StoredRecord(1).Pack.Stacks);  // the file keeps the record read
        Assert.Equal(CharacterStatus.Empty, s.Roster.StoredRecord(1).Status);
        _ = member;

        record.Skills[0] = 99;                                  // a week in the party ...
        Assert.Equal(new InnRosterResult(InnRosterChange.Left, 1), InnRoster.AddOrRemove(s, menu, Images));
        Assert.Equal(-1, s.Roster.PartySlots[1]);
        Assert.Equal(CharacterStatus.Empty, s.Party.Members[1].Status);
        Assert.Equal(4, s.Party.Members.Count);                 // the party size is not lowered
        Assert.NotSame(record, s.Roster.Characters[1]);         // ... is not written back
        Assert.Equal(stacks, s.Roster.Characters[1].Pack.Stacks);
        Assert.NotEqual(99, s.Roster.Characters[1].Skills[0]);
        Assert.False(s.Events.TypeAt(EventKinds.JoinedAtInn, 1, 7));
    }

    [Fact]
    public void AMemberWhoDidNotJoinHereStaysAndAFullPartyTakesNobody()
    {
        var s = WithRoster();
        var menu = new PartyMenuState();
        s.Roster.Characters.Add(new Character { Name = "Waiting", Status = CharacterStatus.Empty, RosterLocation = 7 });
        var rows = InnRoster.List(s, menu, 0);
        Assert.Equal(5, rows.Count);

        menu.Selected = 0;
        Assert.Equal(new InnRosterResult(InnRosterChange.None, 0), InnRoster.AddOrRemove(s, menu, Images));
        menu.Selected = 4;
        Assert.Equal(new InnRosterResult(InnRosterChange.NoRoom, -1), InnRoster.AddOrRemove(s, menu, Images));
        Assert.Equal(CharacterStatus.Empty, s.Roster.Characters[4].Status);
    }

    [Fact]
    public void HeraldryWrapsFromOToAAndTheColourKeysCompareTheRowNumber()
    {
        var s = WithRoster();
        var menu = new PartyMenuState();
        s.Party.Members[2].ShieldLetter = 'O';
        InnRoster.List(s, menu, 0);
        menu.Selected = 2;
        Assert.Equal(2, InnRoster.NextShield(s, menu));
        Assert.Equal('A', s.Party.Members[2].ShieldLetter);

        PartyRules.RemoveFromParty(s, 1);
        s.CurrentLocation = 8;
        InnRoster.List(s, menu, 0);                             // rows: entries 0, 2, 3
        menu.Selected = 1;                                      // the row of entry 2 ...
        Assert.Equal(-1, InnRoster.CycleColour(s, menu, 1));    // ... but no slot holds entry 1
        Assert.Equal(2, InnRoster.NextShield(s, menu));         // heraldry takes the row's entry: slot 2
        menu.Selected = 2;                                      // the row of entry 3 ...
        Assert.Equal(2, InnRoster.CycleColour(s, menu, 1));     // ... colours the slot holding entry 2
    }

    [Fact]
    public void TheArrowsWrapOnTheNumberOfRecords()
    {
        var menu = new PartyMenuState { Count = 6, Selected = 0 };
        InnRoster.SelectPrevious(menu);
        Assert.Equal(5, menu.Selected);
        InnRoster.SelectNext(menu);
        Assert.Equal(0, menu.Selected);
        Assert.Equal(-1, InnRoster.RowEntry(menu, PartyMenuState.RowTableLength));
    }

    [Fact]
    public void ACreatedCharacterJoinsTheRosterLeftHere()
    {
        var s = WithRoster();
        InnRoster.AddNew(s, new Character { Name = "New", Status = CharacterStatus.Able });
        Assert.Equal(7, s.Roster.Characters[^1].RosterLocation);
        Assert.Contains(InnRoster.List(s, new PartyMenuState(), 0), r => r.Record.Name == "New");
    }

    [Fact]
    public void TheInnsSecondColourOnF60WritesTheMembersSeventhByteTwice()
    {
        var colours = new PortraitColours();
        var member = Enumerable.Repeat((byte)0xAA, 24).ToArray();
        colours.CycleAtInn(2, "F60", member);
        Assert.Equal(1, colours.SecondStep);
        Assert.Equal(0x14, member[6]);                          // the shared byte 7
        Assert.Equal(0xAA, member[7]);                          // never written
        Assert.Equal(0x10, member[4]);
        Assert.Equal(0x00, member[3]);
    }
}
