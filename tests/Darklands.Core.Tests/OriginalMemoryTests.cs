using Darklands.Core.Characters;
using Darklands.Core.Events;
using Darklands.Core.Items;
using Darklands.Core.Randomness;
using Darklands.Core.Rules;

namespace Darklands.Core.Tests;

/// <summary>
/// Where the original reads or writes outside its defined data, or returns a register left over,
/// and the outcome is deterministic (docs/faithfulness/DIVERGENCE_REVIEW.md, part B).
/// </summary>
public sealed class OriginalMemoryTests
{
    /// <summary>A log whose slots 0..298 hold kind-0x50 fillers, so the next event lands in slot 299.</summary>
    private static GameState FullBut299()
    {
        var s = TestGame.Create();
        for (var k = 0; k < EventLog.Capacity - 1; k++)
        {
            s.Events.Create(-2, 0, 0, 0x50, 0, 0, 0, 10, 0, 0);
        }
        return s;
    }

    [Fact]
    public void EventRemoveReturnsTheLeftoverAxWhenSlot299HoldsTheKind()
    {
        var s = FullBut299();
        Assert.Equal(299, s.Events.Create(1, 5, 0, 0x40, 2, 0, 0, 100, 0, 3));
        Assert.Equal(299, s.Events.Remove(0x40, 9, -1, -1, -1));   // type differs: AX = event_next's 299
        Assert.Equal(6, s.Events.Remove(0x40, 2, 6, -1, -1));      // location loaded and differs
        Assert.Equal(7, s.Events.Remove(0x40, 2, 5, 7, -1));       // owner loaded and differs
        Assert.Equal(4, s.Events.Remove(0x40, -1, -1, 1, 4));      // tag loaded and differs
        Assert.Equal(0, s.Events.Remove(0x40, 2, 5, 1, 3));        // removed: event_free leaves 0
        Assert.Null(s.Events[299]);
        Assert.Equal(299, s.Events.Remove(0x50, -1, -1, -1, -1));  // slot 299 empty now: the count (299 fillers)
    }

    [Fact]
    public void LocFactsClearReturnsTheLeftoverAxWhenSlot299HoldsAFact()
    {
        var s = FullBut299();
        s.Events.Create(-2, 5, 0, EventKinds.Quest, 3, 4, 0, 100, 0, 9);
        Assert.Equal(299, s.Events.ClearFacts(8, -1));
        Assert.Equal(6, s.Events.ClearFacts(9, 6));
        Assert.Equal(0, s.Events.ClearFacts(9, 5));
        Assert.Null(s.Events[299]);
    }

    [Fact]
    public void TheBoostMaximumReturnsTheLeftoverAxWhenSlot299HoldsABoost()
    {
        var s = FullBut299();
        s.Events.Create(2, 0, 0, EventKinds.AttributeBoost, 3, 5, 0, 100, 0, 0);
        Assert.Equal(4, s.Events.AttributeBoost(1, 4));   // type differs: the attribute
        Assert.Equal(1, s.Events.AttributeBoost(1, 3));   // type matches (owner differs): the member
        Assert.Equal(2, s.Events.AttributeBoost(2, 3));   // even the matching boost's own member
    }

    private const int Cosmas = 27, Damian = 30;

    [Fact]
    public void LearningForMinusOneSetsABitOfMember4sFormulaBytes()
    {
        var s = TestGame.Create();
        Assert.Equal(4, s.Party.Members.Count);              // slot 4 is empty: its bytes still exist
        Saints.Learn(s, Who.Leader, Cosmas);                  // byte 3, mask 0x80 >> 3
        Assert.DoesNotContain(Cosmas, s.Party.Leader.Saints);
        Assert.Equal(0x10, s.Party.Slot(4).Formulas.Bytes[2 + 3]);
        // has_item and knows_item_type test the whole byte: member 4 "knows" potion group 5
        Assert.Equal(2, Inventory.Has(s, 4, Potions.FirstPotion + 5));
        Assert.True(Inventory.KnowsPotion(s, 4, Potions.FirstPotion + 5));

        s.Behaviour.FixSaintLearnedByLeader = true;
        Saints.Learn(s, Who.Leader, Cosmas);
        Assert.Contains(Cosmas, s.Party.Leader.Saints);
    }

    [Fact]
    public void APrayerForThePartyTeachesMember4sFormulaBytesAndChangesNoCharm()
    {
        var s = TestGame.Create();
        SaintBook.Help(s, Who.Leader, Cosmas);                // teaches St.Damian: saints_known[-1][3] |= 2
        Assert.DoesNotContain(Damian, s.Party.Leader.Saints);
        Assert.Equal(0x02, s.Party.Slot(4).Formulas.Bytes[5]);
    }

    [Fact]
    public void HasItemBelowTheFormulaTableReadsMember4sLastPackEntries()
    {
        var s = TestGame.Create();
        Assert.Equal(0, Inventory.Has(s, 0, 0x47));
        // 2E38:075F + 0x47 = 07A6 = member 4's pack entry 60, its code's low byte
        s.Party.Slot(4).Pack[60] = new ItemStack(9, 0, 0, 0, 0);
        Assert.Equal(2, Inventory.Has(s, 0, 0x47));
        // above the formula table: member 4, code 0x75 + k reads saint byte k of member 0
        s.Party.Members[0].Saints.Add(8);
        Assert.Equal(2, Inventory.Has(s, 4, Potions.FirstPotion + 22 + 1));
    }

    [Fact]
    public void KeepingItemsOfAnEmptyPackLeavesMinusOneAndTheNextItemIsLost()
    {
        var s = TestGame.Create();
        var gunther = s.Party.Members[1];
        gunther.Pack.Clear();
        Inventory.KeepEach(s, 1, -1);                         // every roll fails: i = 0 removes "stack 0"
        Assert.Equal(-1, gunther.Pack.Stacks);
        Assert.Empty(gunther.Pack);
        Assert.Equal(0, Inventory.Add(s, 1, 60));             // written to entry -1 = Gretchen's entry 63
        Assert.Empty(gunther.Pack);
        Assert.Equal(60, s.Party.Members[0].Pack[63].Code);
        // member 0's entry -1 is the six bytes before the packs
        var gretchen = s.Party.Members[0];
        gretchen.Pack.Clear();
        Inventory.KeepEach(s, 0, -1);
        Inventory.Add(s, 0, 61);
        Assert.Equal(61, s.Party.BeforeFirstPack.Code);
        Assert.Equal(0, gretchen.Pack.Stacks);
    }

    [Fact]
    public void RemovingTheStackPastTheEndUnequipsByTheStaleEntryAndKeepsTheLastStack()
    {
        var s = TestGame.Create();
        var m = s.Party.Members[0];
        m.Pack.Clear();
        m.Pack.Add(new ItemStack(5, 3, 25, 1, 0));
        m.Pack.Add(new ItemStack(3, 3, 30, 1, 4));            // a hand weapon (item 3), equipped
        m.Weapon = new EquippedItem(3, 30);
        m.Pack.Stacks = 1;                                     // entry 1 is now a stale entry past the last stack
        Assert.Equal(0, Inventory.RemoveStackAt(s, 0, 1));    // index == count: unequips by entry 1, count 0
        Assert.True(m.Weapon.IsEmpty);
        Assert.Equal(0, m.Pack.Stacks);
        Assert.Equal(5, m.Pack[0].Code);                       // the last stack drops out but stays in its entry
    }

    [Fact]
    public void ReputationInTheOpenCountryDrawsItsChanceAndWritesNothing()
    {
        var s = TestGame.Create(location: GameState.Wilderness);
        var twin = TestGame.Twin(s);
        var reps = s.Locations.Select(l => l.Reputation).ToList();
        LocationRules.AdjustReputation(s, GameState.Wilderness, 20, 60);
        twin.Below(100);
        Assert.Equal(twin.State, ((MscRandom)s.Random).State);
        Assert.Equal(reps, s.Locations.Select(l => l.Reputation));
    }

    /// <summary>A DARKLAND.CTY of two cities whose bytes are their offsets' low bytes.</summary>
    private sealed class TwoCities : Core.World.IGameData
    {
        public IReadOnlyList<ItemDefinition> ItemDefinitions { get; } = [];

        private static readonly byte[] File = [.. Enumerable.Range(0, 1 + 2 * Core.World.CityRecord.Size).Select(i => (byte)i)];

        public Core.World.CityRecord LoadCity(int city) => new(city, File.AsSpan(1 + city * Core.World.CityRecord.Size, Core.World.CityRecord.Size).ToArray());

        public byte[]? CityFileBytes(long offset, int length) =>
            offset >= File.Length ? [] : File.AsSpan((int)offset, (int)Math.Min(length, File.Length - offset)).ToArray();
    }

    [Fact]
    public void AnUnknownCityKeepsTheRecordOrReadsTheFilesStart()
    {
        var data = new TwoCities();
        var before = data.LoadCity(1);
        // past the last city: the read returns nothing and the working copy stays
        var kept = Core.World.CityFile.Read(data, 200, before)!;
        Assert.Equal(before.ToArray(), kept.ToArray());
        Assert.Equal(1, kept.City);
        // negative: the seek fails and the read starts at the file's first byte (the count byte, then city 0)
        var head = Core.World.CityFile.Read(data, -1, before)!.ToArray();
        Assert.Equal(0, head[0]);
        Assert.Equal(1, head[1]);
    }
    /// <summary>
    /// R031+0x4CC4 on an index past the stacks (the rival dragon slayer passes an item code): the stale entry there loses one, and
    /// at 0 it is deleted, which lowers the stack count; an index from 64 on reaches the next member's pack.
    /// </summary>
    [Fact]
    public void TakeOnePastTheStacksUsesTheStaleEntryAndTheMemoryBeyond()
    {
        var s = TestGame.Create();
        var pack = s.Party.Slot(0).Pack;
        pack.Clear();
        pack.AddRange([new ItemStack(5, 5, 20, 2, 1), new ItemStack(6, 6, 20, 1, 1), new ItemStack(7, 7, 20, 1, 1)]);
        pack.Stacks = 2;   // entry 2 stale, count 1
        Inventory.TakeAt(s, 0, 2);
        Assert.Equal(1, pack.Stacks);   // R031+0x4C56 lowered the count whatever the index
        var next = s.Party.Slot(1).Pack;
        next.Clear();
        next.AddRange([new ItemStack(9, 9, 20, 3, 1), new ItemStack(10, 10, 20, 4, 1)]);
        Inventory.TakeAt(s, 0, 65);     // entry 65 of member 0 is entry 1 of member 1
        Assert.Equal(3, next[1].Count);
    }
}
