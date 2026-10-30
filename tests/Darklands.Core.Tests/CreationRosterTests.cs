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
    public void AddingToThePartyRecomputesTheEncumbrance()
    {
        // orig: R037+0x0D67 gear-weight for the slot; lockstep session chargen-created: a made character (nothing equipped, the
        // generator's 0x0A) is saved with +0x49 = 00
        var r = New();
        r.Add(new Character { Name = "Made", Encumbrance = 0x0A });
        Assert.Equal(0, r.AddToParty(4, "F01"));
        Assert.Equal(0, r.Member(0)!.Encumbrance);
        Assert.Equal(0x0A, r.Characters[4].Encumbrance);

        var weighed = new CreationRoster(DefaultParty.Create(1).Members, [.. Enumerable.Range(0, 5).Select(i => new byte[24])],
            item: code => new Items.ItemDefinition(code, "", "", 0, 0, 7, 0, 0));
        weighed.Characters[0].Encumbrance = 0;
        weighed.AddToParty(0, "F01");
        var member = weighed.Member(0)!;
        var equipped = Enum.GetValues<Rules.EquipmentSlot>().Count(k => Rules.Inventory.Equipped(member, k, new PartyQueryResult()) != -1);
        Assert.True(equipped > 0);
        Assert.Equal(7 * equipped, member.Encumbrance);
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
