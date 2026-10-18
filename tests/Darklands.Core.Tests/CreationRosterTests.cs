using Darklands.Core.Characters;
using Darklands.Core.Characters.Creation;

namespace Darklands.Core.Tests;

/// <summary>The "Create a new party" menu's roster and party slots (R037 1462 0444).</summary>
public sealed class CreationRosterTests
{
    private static CreationRoster New() =>
        new(DefaultParty.Create(1).Members, [.. Enumerable.Range(0, 5).Select(i => Enumerable.Repeat((byte)i, 24).ToArray())]);

    [Fact]
    public void OnlyFourSlotsAreFilledAndACharacterJoinsOnce()
    {
        var r = New();
        r.Add(new Character { Name = "Fifth" });
        Assert.Equal(0, r.AddToParty(2, "F01"));
        Assert.Equal(-1, r.AddToParty(2, "F01"));
        Assert.Equal(1, r.AddToParty(0, "F01"));
        Assert.Equal(2, r.AddToParty(1, "F01"));
        Assert.Equal(3, r.AddToParty(3, "F01"));
        Assert.Equal(-1, r.AddToParty(4, "F01"));
        Assert.Equal("Hans Muller", r.Member(0)!.Name);
        Assert.Same(r.SlotPalettes[0], r.Member(0)!.ImagePalette);
    }

    [Fact]
    public void TheSlotHoldsACopyAndHeraldryWrapsFromOToA()
    {
        var r = New();
        r.AddToParty(0, "F60");
        for (var k = 0; k < 14; k++)
        {
            r.NextShield(0);
        }
        Assert.Equal('O', r.Member(0)!.ShieldLetter);
        r.NextShield(0);
        Assert.Equal('A', r.Member(0)!.ShieldLetter);
        r.NextShield(0);
        Assert.Equal('A', r.Characters[0].ShieldLetter);
        r.DropFromParty(0);
        r.AddToParty(0, "F60");
        Assert.Equal('A', r.Member(0)!.ShieldLetter);
    }

    [Fact]
    public void KillingMovesLaterCharactersDown()
    {
        var r = New();
        r.AddToParty(3, "F01");
        r.AddToParty(1, "F01");
        Assert.True(r.Kill(1));
        Assert.Equal(3, r.Characters.Count);
        Assert.Equal([2, -1, -1, -1, -1], r.Slots);
    }

    [Fact]
    public void ThePartyRunsToTheLastFilledSlotWithEmptyMembersBetween()
    {
        var r = New();
        Assert.Null(r.ToParty(5));
        r.AddToParty(0, "F01");
        r.AddToParty(1, "F01");
        r.DropFromParty(0);
        var party = r.ToParty(5)!;
        Assert.Equal(2, party.Members.Count);
        Assert.False(party.Members[0].IsPresent);
        Assert.Equal(0, party.LeaderIndex);              // the leader variable stays 0, on the empty slot
        Assert.Equal(1, r.ToParty(5, leader: 1)!.LeaderIndex);
        Assert.Equal(5, party.Seed);
    }
}
