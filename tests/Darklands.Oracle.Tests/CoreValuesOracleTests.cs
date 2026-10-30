using System.Text.Json;
using Darklands.Core.Economy;
using Darklands.Core.Randomness;
using Darklands.Core.Time;

namespace Darklands.Oracle.Tests;

/// <summary>
/// The Core value rules against the original program: every case in Corpus/core.json is the
/// result of executing the original function from DARKLAND.EXE (tools/oracle/build_core_corpus.py).
/// </summary>
public sealed class CoreValuesOracleTests
{
    private static readonly Lazy<Corpus> Data = new(() => Corpus.Load());

    public static TheoryData<string> Functions() =>
    [
        "R029+0x4FB4", "R029+0x5040", "R029+0x5090", "R029+0x50FC", "R029+0x514E", "R029+0x51A0",
        "R029+0x51E4", "R029+0x57BE", "R029+0x58FA", "R029+0x5AB4", "R029+0x5654", "root 0000:0C70", "root 0410:0008", "root 0000:0C8C",
    ];

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
            case "R029+0x4FB4":
                AssertMoneyAtA(c, MoneyOf(i, "m"), Money.From(Int(i, "fl"), Int(i, "gr"), Int(i, "pf")));
                break;
            case "R029+0x5040":
                AssertMoneyAtA(c, MoneyOf(i, "m"), MoneyOf(i, "m").Add(Int(i, "fl"), Int(i, "gr"), Int(i, "pf")));
                break;
            case "R029+0x5090":
                AssertMoneyAtA(c, MoneyOf(i, "m"), MoneyOf(i, "m").Subtract(Int(i, "fl"), Int(i, "gr"), Int(i, "pf")));
                break;
            case "R029+0x50FC":
                AssertMoneyAtA(c, MoneyOf(i, "a"), MoneyOf(i, "a").Add(MoneyOf(i, "b")));
                break;
            case "R029+0x514E":
                AssertMoneyAtA(c, MoneyOf(i, "a"), MoneyOf(i, "a").Subtract(MoneyOf(i, "b")));
                break;
            case "R029+0x51A0":
                AssertMoneyAtA(c, new Money(7, 7, 7), MoneyOf(i, "unit").Times(Int(i, "n")));
                break;
            case "R029+0x51E4":
                Assert.Equal((short)c.Ax, (short)MoneyOf(i, "a").CompareTo(MoneyOf(i, "b")));
                break;
            case "R029+0x57BE":
                AssertDateAtA(c, default, GameDate.Create(Int(i, "year"), Int(i, "month"), Int(i, "day"), Int(i, "hour")));
                break;
            case "R029+0x58FA":
                AssertDateAtA(c, DateOf(i, "date"), DateOf(i, "date").AddHours(Int(i, "hours")));
                break;
            case "R029+0x5AB4":
                Assert.Equal((short)c.Ax, (short)DateOf(i, "a").DaysSince(DateOf(i, "b")));
                break;
            case "R029+0x5654":
                Assert.Equal((short)c.Ax, (short)TimeOfDay.Band(Int(i, "hour")));
                break;
            case "root 0000:0C70":
            case "root 0410:0008":
            case "root 0000:0C8C":
                {
                    var random = new MscRandom(i.GetProperty("state").GetUInt32());
                    var n = Int(i, "n");
                    // roll-one-to-n (root 0000:0C8C, 1..n with n < 2 counting as 2) is Battle.RollOneTo: Below(n) + 1
                    var value = function == "root 0000:0C70" ? random.Below(n) : function == "root 0410:0008" ? random.Scaled(n) : random.Below(n) + 1;
                    Assert.Equal((short)c.Ax, (short)value);
                    Assert.Equal(c.FinalUInt32(Data.Value.HoldRand, i.GetProperty("state").GetUInt32()), random.State);
                    break;
                }
            default:
                throw new InvalidOperationException(function);
        }
    }

    private static void AssertMoneyAtA(OracleCase c, Money initial, Money expected)
    {
        var a = Data.Value.ScratchA;
        var actual = new Money(c.FinalInt16(a, initial.Florins), c.FinalInt16(a + 2, initial.Groschen),
            c.FinalInt16(a + 4, initial.Pfennigs));
        Assert.Equal(actual, expected);
    }

    private static void AssertDateAtA(OracleCase c, GameDate initial, GameDate expected)
    {
        var a = Data.Value.ScratchA;
        var actual = new GameDate(c.FinalInt16(a, initial.Hour), c.FinalInt16(a + 2, initial.Day),
            c.FinalInt16(a + 4, initial.Month), c.FinalInt16(a + 6, initial.Year));
        Assert.Equal(actual, expected);
    }

    private static int Int(JsonElement e, string name) => e.GetProperty(name).GetInt32();

    private static Money MoneyOf(JsonElement e, string name)
    {
        var m = e.GetProperty(name);
        return new Money((short)m[0].GetInt32(), (short)m[1].GetInt32(), (short)m[2].GetInt32());
    }

    private static GameDate DateOf(JsonElement e, string name)
    {
        var d = e.GetProperty(name);
        return new GameDate((short)d[0].GetInt32(), (short)d[1].GetInt32(), (short)d[2].GetInt32(),
            (short)d[3].GetInt32());
    }
}
