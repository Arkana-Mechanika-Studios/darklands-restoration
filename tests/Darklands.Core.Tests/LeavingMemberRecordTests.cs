using Darklands.Core.Characters;
using Darklands.Core.Rules;

namespace Darklands.Core.Tests;

/// <summary>
/// party-leave (R037+0x3B58) writes back only the 0x80-byte member record, and party-join (R037+0x3CD6) leaves the slot's figure
/// (card oracle, local/batch/cards1o: cards 0x0D, 0x8D and 0x120).
/// </summary>
public sealed class LeavingMemberRecordTests
{
    private static GameState WithRoster()
    {
        var s = TestGame.Create(location: 7);
        s.Roster.Reset(s.Party);
        return s;
    }

    /// <summary>Member 2 learns a saint and a formula and gets an item after the file last had the record; then leaves.</summary>
    private static (GameState State, Character Member, int Items) Changed(OriginalBehaviour behaviour)
    {
        var s = WithRoster();
        s.Behaviour = behaviour;
        var member = s.Party.Members[2];
        var items = member.Pack.Stacks;
        member.Saints.Add(17);
        member.Formulas.Add(4);
        member.Pack.Add(new ItemStack(0x2C, 0, 10, 1, 1));
        member.Status = CharacterStatus.Away;
        Assert.Equal(1, PartyRules.ReportAway(s));
        return (s, member, items);
    }

    [Fact]
    public void TheFileKeepsItsSaintsFormulasAndPackButTakesTheItemCount()
    {
        var (s, member, items) = Changed(OriginalBehaviour.Faithful);

        var entry = s.Roster.Characters[2];
        Assert.Same(member, entry);
        Assert.DoesNotContain(17, entry.Saints);
        Assert.DoesNotContain(4, entry.Formulas);
        Assert.Equal(items + 1, entry.Pack.Stacks);                 // the count is in the member record
        Assert.NotEqual(0x2C, entry.Pack[items].Code);             // the entry is the file's (here empty)
        // the slot's memory keeps what the member had
        Assert.Contains(17, s.Party.Slot(2).Saints);
        Assert.Equal(0x2C, s.Party.Slot(2).Pack[items].Code);
    }

    [Fact]
    public void WithTheDataSafetyFixTheWholeRecordIsWritten()
    {
        var (s, _, items) = Changed(OriginalBehaviour.DataSafe);

        var entry = s.Roster.Characters[2];
        Assert.Contains(17, entry.Saints);
        Assert.Contains(4, entry.Formulas);
        Assert.Equal(0x2C, entry.Pack[items].Code);
    }

    [Fact]
    public void AMemberJoiningASlotNobodyHeldTakesNoFigure()
    {
        var s = WithRoster();
        s.Party.Members[0].Image = "F60";
        s.Party.Members[0].ImagePalette = Enumerable.Range(1, 24).Select(i => (byte)i).ToArray();
        var count = s.Party.Members.Count;
        Assert.True(count < Party.MaxMembers);

        // the character of slot 0 is read into the free slot too: its own copy, the same roster entry, the figure the slot had (none)
        Assert.Equal(count, PartyRules.AddFromRoster(s, 0));
        var copy = s.Party.Members[count];
        Assert.NotSame(s.Party.Members[0], copy);
        Assert.Equal("", copy.Image);
        Assert.All(copy.ImagePalette, b => Assert.Equal(0, b));
        Assert.Equal("F60", s.Party.Members[0].Image);
        s.Roster.Reconcile(s.Party);
        Assert.Equal(0, s.Roster.PartySlots[0]);
        Assert.Equal(0, s.Roster.PartySlots[count]);
    }
}
