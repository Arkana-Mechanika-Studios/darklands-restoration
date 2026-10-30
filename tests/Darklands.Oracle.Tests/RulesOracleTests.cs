using System.Text.Json;
using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Core.Randomness;
using Darklands.Core.Rules;
using Darklands.Core.World;

namespace Darklands.Oracle.Tests;

/// <summary>
/// The rules card scripts use, against the original program: every case in Corpus/rules.json
/// is the result of executing the original function from DARKLAND.EXE with generated party,
/// location and city data (tools/oracle/build_rules_corpus.py).
/// </summary>
public sealed class RulesOracleTests
{
    private const int DataSegment = 0x321A;
    private const int PartyRecords = 0x9C69;
    private const int LocationSegment = 0x6000;
    private const int LocationSpacing = 0x40;

    private static readonly Lazy<Corpus> Data = new(() => Corpus.Load("rules.json"));

    public static TheoryData<string> Functions() =>
    [
        "R029+0x1A8E", "R029+0x1DFE", "R029+0x19D0", "R029+0x18A8", "R029+0x0A72", "thunk 09C0:1F63", "thunk 09C0:1C1B",
        "R029+0x01C0", "R029+0x021E", "R029+0x026A", "R029+0x1396", "R029+0x13DE",
        "R029+0x1446", "R029+0x14A4", "party_max_skill_women", "R029+0x153A", "R029+0x15A2",
        "R029+0x1600", "R029+0x164A", "party_min_attr_sum", "R029+0x16FE", "party_max_attr_women",
        "R029+0x179C", "R029+0x1800", "party_max_attr_skill",
    ];

    private const int BestValue = DataSegment * 16 + 0x99E6;
    private const int BestMember = DataSegment * 16 + 0x991D;
    private const int BestSkill = DataSegment * 16 + 0x99EB;
    private const int BestAttribute = DataSegment * 16 + 0x9C1D;
    private const int AttributeMember = DataSegment * 16 + 0x9A44;
    private const int CitySegment = 0x7000;

    [Theory]
    [MemberData(nameof(Functions))]
    public void MatchesTheOriginalFunction(string function)
    {
        var cases = Data.Value.Cases(function);
        Assert.NotEmpty(cases);
        foreach (var c in cases)
        {
            Check(function, c);
        }
    }

    private static void Check(string function, OracleCase c)
    {
        var i = c.Inputs;
        switch (function)
        {
            case "R029+0x1A8E":
                {
                    var s = WithLocations(i);
                    var cur = Int(i, "curLoc");
                    var city = new byte[CityRecord.Size];
                    Convert.FromHexString(i.GetProperty("city").GetString()!).CopyTo(city, 0x40);
                    s.CurrentLocation = cur;
                    s.City = new CityRecord(cur is >= 0 and < Location.CityCount ? cur : 0, city);
                    s.Date = s.Date with { Month = (short)Int(i, "month") };
                    s.Party.Seed = Int(i, "seed");
                    s.MapPosition = new MapPosition(Int(i, "x"), Int(i, "y"));
                    var what = Int(i, "what");
                    Assert.True((short)c.Ax == (short)LocationRules.Query(s, Int(i, "loc"), what), $"place-query {what} at {cur}");
                    break;
                }
            case "R029+0x19D0":
                {
                    var s = WithLocations(i);
                    s.Locations[0].Reputation = Int(i, "rep");
                    var random = (MscRandom)s.Random;
                    random.Seed(State(i));
                    var r = LocationRules.AdjustReputation(s, 0, Int(i, "a"), Int(i, "b"));
                    Assert.Equal((short)c.Ax, (short)r);
                    Assert.Equal(c.FinalInt16(LocationSegment * 16 + 0x12, (short)Int(i, "rep")), (short)s.Locations[0].Reputation);
                    Assert.Equal(c.FinalUInt32(Data.Value.HoldRand, State(i)), random.State);
                    break;
                }
            case "R029+0x18A8":
                {
                    var s = new GameState(new MscRandom(State(i)));
                    var v = Practice.ImproveRoll(s, Int(i, "v"), Int(i, "lo"), Int(i, "hi"));
                    Assert.Equal((short)c.Ax, (short)v);
                    Assert.Equal(c.FinalUInt32(Data.Value.HoldRand, State(i)), ((MscRandom)s.Random).State);
                    break;
                }
            case "R029+0x0A72":
                {
                    var (s, records) = WithParty(i, 0);
                    MemberRules.AddAttribute(s, Int(i, "who"), Int(i, "k"), Int(i, "d"));
                    AssertMembers(c, s, records, 0x49, m => m.Attributes);
                    break;
                }
            case "thunk 09C0:1F63":
                {
                    var (s, records) = WithParty(i, State(i));
                    s.ReportSkillChanges = false;
                    Practice.PracticeSkill(s, Int(i, "who"), Int(i, "k"), Int(i, "mode"), Int(i, "pct"), Int(i, "lvl"));
                    AssertMembers(c, s, records, 0x57, m => m.Skills);
                    Assert.Equal(c.FinalUInt32(Data.Value.HoldRand, State(i)), ((MscRandom)s.Random).State);
                    break;
                }
            case "R029+0x1DFE":
                {
                    var s = WithLocations(i);
                    var cur = Int(i, "curLoc");
                    var city = new byte[CityRecord.Size];
                    var seeded = Convert.FromHexString(i.GetProperty("city").GetString()!);
                    seeded.CopyTo(city, 0x40);
                    s.CurrentLocation = cur;
                    s.City = new CityRecord(cur is >= 0 and < Location.CityCount ? cur : 0, city);
                    var before = Convert.FromHexString(i.GetProperty("locations").GetProperty("1").GetString()!);
                    LocationRules.Set(s, 1, Int(i, "what"), Int(i, "value"));
                    var loc = s.Locations[1];
                    var at = LocationSegment * 16 + LocationSpacing;
                    Assert.Equal(c.FinalInt16(at + 0x12, BitConverter.ToInt16(before, 0x12)), (short)loc.Reputation);
                    Assert.Equal(c.FinalByte(at + 0x14, before[0x14]), (byte)loc.Flags);
                    for (var k = 0; k < 3; k++)
                    {
                        Assert.Equal(c.FinalByte(at + 0x15 + k, before[0x15 + k]), (byte)loc.LocalValues[k]);
                        Assert.Equal(c.FinalInt16(at + 0x1C + 2 * k, BitConverter.ToInt16(before, 0x1C + 2 * k)), (short)loc.Recorded[k]);
                    }
                    var record = s.City.ToArray();
                    for (var k = 0x40; k < 0x70; k++)
                    {
                        Assert.Equal(c.FinalByte(CitySegment * 16 + k, seeded[k - 0x40]), record[k]);
                    }
                    break;
                }
            case "thunk 09C0:1C1B":
                {
                    var (s, _) = WithParty(i, 0);
                    Assert.Equal((short)c.Ax, (short)PartyQueries.LoreLevel(s));
                    break;
                }
            default:
                CheckQuery(function, c);
                break;
        }
    }

    /// <summary>A party query: its result and the "last query" values it changed.</summary>
    private static void CheckQuery(string function, OracleCase c)
    {
        var (s, _) = WithParty(c.Inputs, 0);
        var a = c.Inputs.GetProperty("args").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        int Arg(int k) => a[k];
        var result = function switch
        {
            "R029+0x01C0" => MemberRules.BestWeaponSkill(s, Arg(0)),
            "R029+0x021E" => PartyQueries.BestHandWeaponSkill(s),
            "R029+0x026A" => PartyQueries.BestMissileSkill(s),
            "R029+0x1396" => PartyQueries.MinSkill(s, Arg(0)),
            "R029+0x13DE" => PartyQueries.MinSkill(s, Arg(0), Arg(1)),
            "R029+0x1446" => PartyQueries.MinSkillSum(s, Arg(0), Arg(1)),
            "R029+0x14A4" => PartyQueries.MaxSkill(s, Arg(0)),
            "party_max_skill_women" => PartyQueries.MaxSkillOfWomen(s, Arg(0)),
            "R029+0x153A" => PartyQueries.MaxSkill(s, Arg(0), Arg(1)),
            "R029+0x15A2" => PartyQueries.MaxSkillSum(s, Arg(0), Arg(1)),
            "R029+0x1600" => PartyQueries.AverageSkill(s, Arg(0)),
            "R029+0x164A" => PartyQueries.MinAttribute(s, Arg(0)),
            "party_min_attr_sum" => PartyQueries.MinAttributeSum(s, Arg(0), Arg(1)),
            "R029+0x16FE" => PartyQueries.MaxAttribute(s, Arg(0)),
            "party_max_attr_women" => PartyQueries.MaxAttributeOfWomen(s, Arg(0)),
            "R029+0x179C" => PartyQueries.MaxAttributeSum(s, Arg(0), Arg(1)),
            "R029+0x1800" => PartyQueries.AverageAttribute(s, Arg(0)),
            "party_max_attr_skill" => PartyQueries.MaxAttributePlusSkill(s, Arg(0), Arg(1)),
            _ => throw new InvalidOperationException(function),
        };
        Assert.True((byte)c.Ax == (byte)result, $"{function}: {(byte)c.Ax} vs {result}");
        var last = s.Party.Last;
        AssertIfChanged(c, BestValue, last.Value, function);
        AssertIfChanged(c, BestMember, last.Member, function);
        AssertIfChanged(c, BestSkill, last.Skill, function);
        AssertIfChanged(c, BestAttribute, last.AttributeValue, function);
        AssertIfChanged(c, AttributeMember, last.AttributeMember, function);
    }

    /// <summary>Compares a byte the original wrote with a new value (an unchanged byte cannot be told apart).</summary>
    private static void AssertIfChanged(OracleCase c, int linear, int actual, string function)
    {
        if (c.FinalByte(linear, 0) == c.FinalByte(linear, 1))
        {
            Assert.True(c.FinalByte(linear, 0) == (byte)actual, $"{function} at {linear:X5}");
        }
    }

    /// <summary>A game with the case's location records (others empty).</summary>
    private static GameState WithLocations(JsonElement i)
    {
        var s = new GameState(new MscRandom());
        for (var k = 0; k < 414; k++)
        {
            s.Locations.Add(new Location { Index = k });
        }
        if (!i.TryGetProperty("locations", out var records))
        {
            return s;
        }
        foreach (var p in records.EnumerateObject())
        {
            var r = Convert.FromHexString(p.Value.GetString()!);
            var loc = s.Locations[int.Parse(p.Name, System.Globalization.CultureInfo.InvariantCulture)];
            loc.Reputation = BitConverter.ToInt16(r, 0x12);
            loc.Flags = r[0x14];
            for (var k = 0; k < 3; k++)
            {
                loc.LocalValues[k] = (sbyte)r[0x15 + k];
                loc.Recorded[k] = BitConverter.ToInt16(r, 0x1C + 2 * k);
            }
            loc.CacheHandle = BitConverter.ToInt16(r, 0x18);
            loc.Mark = BitConverter.ToInt16(r, 0x1A);
        }
        return s;
    }

    /// <summary>A game with the case's five member records.</summary>
    private static (GameState State, byte[][] Records) WithParty(JsonElement i, uint seed)
    {
        var s = new GameState(new MscRandom(seed));
        var records = i.GetProperty("members").EnumerateArray().Select(m => Convert.FromHexString(m.GetString()!)).ToArray();
        foreach (var r in records)
        {
            var m = new Character { Status = (CharacterStatus)r[0], IsFemale = r[3] == 1 };
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
        s.Party.LeaderIndex = Int(i, "leader");
        return (s, records);
    }

    private static void AssertMembers(OracleCase c, GameState s, byte[][] records, int offset, Func<Character, int[]> values)
    {
        for (var m = 0; m < records.Length; m++)
        {
            var actual = values(s.Party.Members[m]);
            for (var k = 0; k < actual.Length; k++)
            {
                var linear = DataSegment * 16 + PartyRecords + 0x80 * m + offset + k;
                Assert.Equal(c.FinalByte(linear, records[m][offset + k]), (byte)actual[k]);
            }
        }
    }

    private static uint State(JsonElement e) => e.GetProperty("state").GetUInt32();

    private static int Int(JsonElement e, string name) => e.GetProperty(name).GetInt32();
}
