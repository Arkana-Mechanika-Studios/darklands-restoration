using System.Text.Json;
using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Core.Events;
using Darklands.Core.Randomness;
using Darklands.Core.Rules;

namespace Darklands.Oracle.Tests;

/// <summary>
/// The rules that read outside their data against the original program (Corpus/memory.json,
/// tools/oracle/build_memory_corpus.py): the event scans that end at slot 299 return the AX the
/// original leaves, set_member_bit(who) writes 2E38:082C + who × 0x14 + bit / 8 unresolved, and
/// has_item's formula test reads the byte 2E38:075F + who × 0x16 + code wherever it lands.
/// </summary>
public sealed class MemoryOracleTests
{
    private static readonly Lazy<Corpus> Data = new(() => Corpus.Load("memory.json"));

    /// <summary>Linear address of segment 2E38 (the far data the member tables live in).</summary>
    private const int FarData = 0x2E38 * 16;

    [Fact]
    public void EventRemoveReturnsWhatTheOriginalReturns()
    {
        foreach (var c in Data.Value.Cases("event_remove"))
        {
            var (s, a) = Log(c);
            var ax = s.Events.Remove(a[0], a[1], a[2], a[3], a[4]);
            Check(c, s, ax);
        }
    }

    [Fact]
    public void LocFactsClearReturnsWhatTheOriginalReturns()
    {
        foreach (var c in Data.Value.Cases("loc_facts_clear"))
        {
            var (s, a) = Log(c);
            Check(c, s, s.Events.ClearFacts(a[0], a[1]));
        }
    }

    [Fact]
    public void TheBoostMaximumReturnsWhatTheOriginalReturns()
    {
        foreach (var c in Data.Value.Cases("event_48_max"))
        {
            var (s, a) = Log(c);
            Assert.Equal((short)c.Ax, (short)s.Events.AttributeBoost(a[0], a[1]));
        }
    }

    [Fact]
    public void SetMemberBitWritesTheByteTheOriginalWrites()
    {
        foreach (var c in Data.Value.Cases("set_member_bit"))
        {
            var s = Party();
            Saints.Learn(s, Int(c.Inputs, "who"), Int(c.Inputs, "bit"));
            var written = c.Element.GetProperty("changed").EnumerateObject().Single();
            var offset = Corpus.Hex(written.Name) - FarData;
            Assert.Equal(Corpus.Hex(written.Value.GetString()!), s.Party.MemoryByte(offset));
        }
    }

    [Fact]
    public void HasItemReadsTheByteTheOriginalReads()
    {
        foreach (var c in Data.Value.Cases("has_item"))
        {
            var s = Party();
            s.Party.SetMemoryByte(Int(c.Inputs, "byte"), 1);
            Assert.Equal((short)c.Ax, (short)Inventory.Has(s, Int(c.Inputs, "who"), Int(c.Inputs, "code")));
        }
    }

    /// <summary>Slots 0..298 hold a filler of kind 0x50, slot 299 the case's event.</summary>
    private static (GameState State, int[] Args) Log(OracleCase c)
    {
        var s = new GameState(new MscRandom());
        for (var k = 0; k < EventLog.Capacity - 1; k++)
        {
            s.Events.Put(k, new TimedEvent { Kind = 0x50, Owner = 0 });
        }
        var e = c.Inputs.GetProperty("event");
        s.Events.Put(EventLog.Capacity - 1, new TimedEvent
        {
            Kind = Int(e, "kind"), Type = Int(e, "type"), Owner = Int(e, "owner"), Location = Int(e, "location"),
            Value = Int(e, "value"), Tag = Int(e, "tag"),
        });
        var args = c.Inputs.GetProperty("args").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        return (s, args);
    }

    private static void Check(OracleCase c, GameState s, int ax)
    {
        Assert.Equal((short)c.Ax, (short)ax);
        var freed = c.Element.GetProperty("freed").GetInt32() == 1;
        Assert.Equal(freed, s.Events[EventLog.Capacity - 1] is null);
    }

    /// <summary>Five empty slots: every byte of the member tables is 0, as in the program image.</summary>
    private static GameState Party()
    {
        var s = new GameState(new MscRandom());
        for (var k = 0; k < 5; k++)
        {
            s.Party.Members.Add(new Character { Status = CharacterStatus.Empty });
        }
        return s;
    }

    private static int Int(JsonElement e, string name) => e.GetProperty(name).GetInt32();
}
