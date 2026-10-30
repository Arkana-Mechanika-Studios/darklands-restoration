using System.Globalization;
using System.Text.Json;
using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Core.Events;
using Darklands.Core.Randomness;
using Darklands.Core.Rules;
using Darklands.Core.World;

namespace Darklands.Oracle.Tests;

/// <summary>
/// The saints against the original program (Corpus/saints.json, tools/oracle/build_saints_corpus.py):
/// the saint table, every saint function's table values and chance, and the help of 48 saints
/// executed from DARKLAND.EXE with generated party, purse, locations and random state.
/// </summary>
public sealed class SaintsOracleTests
{
    private const int DataSegment = 0x321A;
    private const int PartyRecords = DataSegment * 16 + 0x9C69;
    private const int Purse = DataSegment * 16 + 0x906B;
    private const int Bankers = DataSegment * 16 + 0x4BB6;
    private const int SaintsKnown = 0x2E38 * 16 + 0x082C;
    private const int LocationSegment = 0x6000;
    private const int LocationSpacing = 0x40;

    private static readonly Lazy<Corpus> Data = new(() => Corpus.Load("saints.json"));

    [Fact]
    public void TheSaintTableReachesTheFunctionsSaintBookNames()
    {
        var table = Data.Value.Root.GetProperty("saintTable").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(Enumerable.Range(0, SaintTable.Count).Select(i => SaintBook.Of(i).Original), table);
    }

    [Fact]
    public void EveryFunctionsTableValuesMatchSaintTable()
    {
        var cases = Data.Value.Cases("values");
        Assert.Equal((SaintTable.Count + 2) * 7, cases.Count);
        foreach (var c in cases)
        {
            var offset = Int(c.Inputs, "offset");
            var what = Int(c.Inputs, "what");
            var saint = Enumerable.Range(0, SaintTable.Count).FirstOrDefault(i => SaintBook.Of(i).Offset == offset, -1);
            var info = saint >= 0 ? SaintTable.Of(saint) : Unlisted(offset);
            int[] values = [info.FeastDay, info.Background, info.MinFavor, info.MaxFavor, info.MinVirtue, info.BaseChance, (int)info.Use];
            Assert.True(values[what] == (short)c.Ax, $"165C:{offset:X4} what {what}: {(short)c.Ax} vs {values[what]}");
        }
    }

    /// <summary>The table values of the two functions saint-handler-tables does not list (SaintBook.Unlisted).</summary>
    private static SaintInfo Unlisted(int offset) => offset switch
    {
        0x75D8 => new SaintInfo(206, 7, 27, 52, 19, 35, 1),
        0x7812 => new SaintInfo(244, 5, 25, 60, 31, 23, 1),
        _ => throw new InvalidDataException($"165C:{offset:X4}"),
    };

    [Fact]
    public void EverySaintsChanceMatches()
    {
        var cases = Data.Value.Cases("chance");
        Assert.Equal(SaintTable.Count * 4, cases.Count);
        foreach (var c in cases)
        {
            var (s, _) = WithParty(c.Inputs, 0);
            var saint = Int(c.Inputs, "saint");
            var chance = Saints.Chance(s, Int(c.Inputs, "who"), saint, Int(c.Inputs, "x"));
            Assert.True((short)c.Ax == chance, $"saint {saint}: {(short)c.Ax} vs {chance}");
        }
    }

    [Fact]
    public void TheHelpOfTheSaintsMatches()
    {
        var cases = Data.Value.Cases("help");
        Assert.NotEmpty(cases);
        foreach (var c in cases)
        {
            CheckEvents(c);
            CheckState(c);
        }
    }

    /// <summary>
    /// Cases aimed at branches the generated ones leave open (Corpus/saints-flip.json, tools/oracle/flip_units.py): the party in
    /// the open country (closest-city stubbed to the city next to it), the whole party (who -2), and the places the saints'
    /// functions compare the current location with.
    /// </summary>
    [Fact]
    public void TheFlippedCasesMatch()
    {
        var failures = new List<string>();
        foreach (var c in Data.Value.FlipCases("help"))
        {
            try
            {
                CheckEvents(c);
                CheckState(c);
            }
            catch (Exception e)
            {
                failures.Add($"flip case {c.Element.GetProperty("flip").GetProperty("n").GetInt32()}: {e.Message}");
            }
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// The events the help starts, in order, against the original's recorded event-create / event-here-test
    /// calls (an empty log, so each boost's event is made as in the original).
    /// </summary>
    private static void CheckEvents(OracleCase c)
    {
        var (s, _) = Prepare(c.Inputs);
        SaintBook.Help(s, Int(c.Inputs, "who"), Int(c.Inputs, "saint"));
        var expected = c.Element.GetProperty("events").EnumerateArray().Select(e => e.EnumerateArray().ToArray()).ToList();
        var actual = s.Events.All().Select(e => e.Event).ToList();
        var label = $"saint {Int(c.Inputs, "saint")}";
        Assert.True(expected.Count == actual.Count, $"{label}: {expected.Count} events vs {actual.Count}");
        for (var k = 0; k < expected.Count; k++)
        {
            var a = actual[k];
            var e = expected[k];
            var words = e.Skip(1).Select(x => (int)(short)x.GetInt32()).ToArray();
            if (e[0].GetString() == "R029+0x2930")
            {
                Assert.Equal((words[0], words[1]), (a.Kind, Hours(a)));
                Assert.Equal((-2, s.CurrentLocation, 100), (a.Owner, a.Location, a.Value));
                continue;
            }
            Assert.True(words.SequenceEqual([a.Owner, a.Location, a.Extra, a.Kind, a.Type, a.Value, (int)a.ExpiryHandler, Hours(a), (int)a.Flags, a.Tag]),
                $"{label} event {k}: [{string.Join(' ', words)}] vs {a}");
        }
    }

    /// <summary>
    /// The state after the help, against every byte the original changed. The C# log is filled with
    /// unrelated events so that, as in the stubbed original, no boost event exists to raise a cap.
    /// </summary>
    private static void CheckState(OracleCase c)
    {
        var (s, records) = Prepare(c.Inputs);
        for (var k = 0; k < EventLog.Capacity; k++)
        {
            s.Events.Create(9, 0, 0, 0x7F, 0, 0, 0, 1000, 0, 0);
        }
        var saint = Int(c.Inputs, "saint");
        SaintBook.Help(s, Int(c.Inputs, "who"), saint);
        var label = $"saint {saint}";
        for (var m = 0; m < records.Length; m++)
        {
            var member = s.Party.Members[m];
            var at = PartyRecords + 0x80 * m;
            byte Final(int offset) => c.FinalByte(at + offset, records[m][offset]);
            for (var k = 0; k < Character.AttributeCount; k++)
            {
                Assert.True(Final(0x49 + k) == member.Attributes[k], $"{label} member {m} attribute {k}");
                Assert.True(Final(0x50 + k) == member.AttributeMaximums[k], $"{label} member {m} maximum {k}");
            }
            for (var k = 0; k < Character.SkillCount; k++)
            {
                Assert.True(Final(0x57 + k) == member.Skills[k], $"{label} member {m} skill {k}");
            }
            Assert.Equal(Final(0x3B), (byte)member.VitalArmor.Quality);
            Assert.Equal(Final(0x3C), (byte)member.LimbArmor.Quality);
            Assert.Equal(Final(0x41), member.BattleCharms);
            Assert.Equal(Final(0x10), member.CharmStrength);
            var known = Known(c.Inputs, m);
            for (var b = 0; b < 0x14; b++)
            {
                Assert.True(c.FinalByte(SaintsKnown + 0x14 * m + b, known[b]) == KnownByte(member, b), $"{label} member {m} saints byte {b}");
            }
        }
        var purse = c.Inputs.GetProperty("purse").EnumerateArray().Select(x => (short)x.GetInt32()).ToArray();
        Assert.Equal(new Money(c.FinalInt16(Purse, purse[0]), c.FinalInt16(Purse + 2, purse[1]), c.FinalInt16(Purse + 4, purse[2])), s.Party.Purse);
        var bankers = c.Inputs.GetProperty("bankers").EnumerateArray().Select(x => (short)x.GetInt32()).ToArray();
        Assert.Equal(c.FinalInt16(Bankers, bankers[0]), (short)s.FuggerStanding);
        Assert.Equal(c.FinalInt16(Bankers + 2, bankers[1]), (short)s.MediciStanding);
        foreach (var p in c.Inputs.GetProperty("locations").EnumerateObject())
        {
            var index = int.Parse(p.Name, CultureInfo.InvariantCulture);
            var before = BitConverter.ToInt16(Convert.FromHexString(p.Value.GetString()!), 0x12);
            Assert.True(c.FinalInt16(LocationSegment * 16 + index * LocationSpacing + 0x12, before) == s.Locations[index].Reputation,
                $"{label} standing at {index}");
        }
        Assert.True(c.FinalUInt32(Data.Value.HoldRand, State(c.Inputs)) == ((MscRandom)s.Random).State, $"{label} random state");
    }

    /// <summary>A case's game: members, leader, current location and its records, purse, bankers, saints known, random state.</summary>
    private static (GameState State, byte[][] Records) Prepare(JsonElement i)
    {
        var (s, records) = WithParty(i, State(i));
        s.Party.LeaderIndex = Int(i, "leader");
        var locRecords = i.GetProperty("locations").EnumerateObject()
            .ToDictionary(p => int.Parse(p.Name, CultureInfo.InvariantCulture), p => Convert.FromHexString(p.Value.GetString()!));
        for (var k = 0; k < 414; k++)
        {
            // the record's icon (+0), map position (+4, +6): closest-city reads them when the party is in the open country
            s.Locations.Add(locRecords.TryGetValue(k, out var rec)
                ? new Location { Index = k, Icon = BitConverter.ToInt16(rec, 0), X = BitConverter.ToInt16(rec, 4), Y = BitConverter.ToInt16(rec, 6) }
                : new Location { Index = k });
        }
        foreach (var (index, r) in locRecords)
        {
            s.Locations[index].Reputation = BitConverter.ToInt16(r, 0x12);
        }
        if (i.TryGetProperty("x", out var mapX))
        {
            s.MapPosition = new MapPosition(mapX.GetInt32(), Int(i, "y"));
        }
        s.CurrentLocation = Int(i, "curLoc");
        var purse = i.GetProperty("purse").EnumerateArray().Select(x => (short)x.GetInt32()).ToArray();
        s.Party.Purse = new Money(purse[0], purse[1], purse[2]);
        var bankers = i.GetProperty("bankers").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        (s.FuggerStanding, s.MediciStanding) = (bankers[0], bankers[1]);
        s.ShowSaintlyChanges = false;
        for (var m = 0; m < records.Length; m++)
        {
            var known = Known(i, m);
            for (var bit = 0; bit < Character.SaintCount; bit++)
            {
                if ((known[bit >> 3] & 0x80 >> (bit & 7)) != 0)
                {
                    s.Party.Members[m].Saints.Add(bit);
                }
            }
        }
        return (s, records);
    }

    private static byte[] Known(JsonElement i, int member) =>
        Convert.FromHexString(i.GetProperty("known").GetString()!).AsSpan(0x14 * member, 0x14).ToArray();

    private static byte KnownByte(Character m, int b)
    {
        var value = 0;
        for (var k = 0; k < 8; k++)
        {
            if (m.Saints.Contains(b * 8 + k))
            {
                value |= 0x80 >> k;
            }
        }
        return (byte)value;
    }

    /// <summary>The case's five member records (status, sex, attributes, skills, armour qualities, charms).</summary>
    private static (GameState State, byte[][] Records) WithParty(JsonElement i, uint seed)
    {
        var s = new GameState(new MscRandom(seed));
        var records = i.GetProperty("members").EnumerateArray().Select(m => Convert.FromHexString(m.GetString()!)).ToArray();
        foreach (var r in records)
        {
            var m = new Character
            {
                Status = (CharacterStatus)r[0], IsFemale = r[3] == 1,
                VitalArmor = new EquippedItem(-1, (sbyte)r[0x3B]), LimbArmor = new EquippedItem(-1, (sbyte)r[0x3C]),
                BattleCharms = r[0x41], CharmStrength = r[0x10],
            };
            for (var k = 0; k < Character.AttributeCount; k++)
            {
                m.Attributes[k] = r[0x49 + k];
                m.AttributeMaximums[k] = r[0x50 + k];
            }
            for (var k = 0; k < Character.SkillCount; k++)
            {
                m.Skills[k] = r[0x57 + k];
            }
            s.Party.Members.Add(m);
        }
        return (s, records);
    }

    /// <summary>An event's duration in hours (from its start to its end).</summary>
    private static int Hours(TimedEvent e)
    {
        for (var h = 0; h < 10000; h++)
        {
            if (e.Start.AddHours(h) == e.End)
            {
                return h;
            }
        }
        return -1;
    }

    private static uint State(JsonElement e) => e.GetProperty("state").GetUInt32();

    private static int Int(JsonElement e, string name) => e.GetProperty(name).GetInt32();
}
