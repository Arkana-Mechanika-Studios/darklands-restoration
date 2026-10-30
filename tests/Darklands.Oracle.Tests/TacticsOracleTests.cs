using System.Text.Json;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Tactics;
using Darklands.Core.Characters;
using Darklands.Core.Randomness;

namespace Darklands.Oracle.Tests;

/// <summary>
/// The tactical geometry against the original program: fighter-collision-pass, sight-line-blocked and
/// three-way-distance run from DARKLAND.EXE on generated floors and combatants
/// (tools/oracle/build_tactics_corpus.py → Corpus/tactics.json).
/// </summary>
public sealed class TacticsOracleTests
{
    private static readonly Lazy<JsonElement[]> Cases = new(() =>
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Corpus", "tactics.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return [.. doc.RootElement.GetProperty("cases").EnumerateArray().Select(e => e.Clone())];
    });

    [Fact]
    public void CollisionsAreTheOriginals()
    {
        var n = 0;
        foreach (var c in Cases.Value.Where(c => c.GetProperty("function").GetString() == "R014+0x5ECA"))
        {
            var i = c.GetProperty("inputs");
            var (battle, map) = Scene(i);
            map.StrictWalls = i.GetProperty("strict").GetInt32() == 1;
            map.Searching = i.GetProperty("searching").GetInt32() == 1;
            map.SearchTarget = i.GetProperty("target").GetInt32();
            map.IgnoreScenery = i.GetProperty("noScenery").GetInt32() == 1;
            map.IgnorePeople = i.GetProperty("noPeople").GetInt32() == 1;
            var who = i.GetProperty("who").GetInt32();
            var result = map.Collides(who, i.GetProperty("x").GetInt32(), i.GetProperty("y").GetInt32());
            var label = $"case {n++} (who {who} at {i.GetProperty("x").GetInt32()},{i.GetProperty("y").GetInt32()})";
            Assert.True(Convert.ToInt32(c.GetProperty("ax").GetString(), 16) == (result.Mask & 0xFFFF), $"{label}: result {c.GetProperty("ax").GetString()} vs {result.Mask & 0xFFFF:X4}");
            if (!result.IsVoid)
            {
                Assert.True(c.GetProperty("last").GetInt32() == (map.LastMask & 0xFFFF), $"{label}: DS:D708");
                var parts = c.GetProperty("parts").EnumerateArray().Select(p => p.GetInt32()).ToArray();
                for (var k = 0; k < 15; k++)
                {
                    Assert.True(parts[k] == map.LastParts[k], $"{label}: DS:E11C[{k}] {parts[k]:X4} vs {map.LastParts[k]:X4}");
                }
            }
        }
        Assert.True(n > 0);
    }

    [Fact]
    public void LinesOfSightAreTheOriginals()
    {
        var n = 0;
        foreach (var c in Cases.Value.Where(c => c.GetProperty("function").GetString() == "R014+0xEF3C"))
        {
            var i = c.GetProperty("inputs");
            if (i.GetProperty("a").GetInt32() == -2 && i.GetProperty("mode").GetInt32() == 0x100)
            {
                continue;   // the original reads the record before combatant 0 (a divergence of BattleMap.LineBlocked)
            }
            var (_, map) = Scene(i);
            var from = i.GetProperty("from");
            var to = i.GetProperty("to");
            var result = map.LineBlocked(new MapPoint(from[0].GetInt32(), from[1].GetInt32()), new MapPoint(to[0].GetInt32(), to[1].GetInt32()),
                i.GetProperty("a").GetInt32(), i.GetProperty("b").GetInt32(), i.GetProperty("mode").GetInt32());
            Assert.True(Convert.ToInt32(c.GetProperty("ax").GetString(), 16) == (result & 0xFFFF), $"case {n}: {c.GetProperty("ax").GetString()} vs {result}");
            n++;
        }
        Assert.True(n > 0);
    }

    [Fact]
    public void DistancesAreTheOriginals()
    {
        foreach (var c in Cases.Value.Where(c => c.GetProperty("function").GetString() == "root 047C:1120"))
        {
            var i = c.GetProperty("inputs");
            int a = i.GetProperty("a").GetInt32(), b = i.GetProperty("b").GetInt32();
            Assert.True(Convert.ToInt32(c.GetProperty("ax").GetString(), 16) == TacticalBattle.Distance(a, b), $"three-way-distance({a}, {b})");
        }
    }

    private static (Battle Battle, BattleMap Map) Scene(JsonElement i)
    {
        var battle = new Battle(new CombatTables(new byte[CombatTables.Length], new byte[16], new byte[7]), new MscRandom(1));
        var level = new BattlefieldLevel();
        foreach (var cell in i.GetProperty("cells").EnumerateArray())
        {
            level[cell[0].GetInt32(), cell[1].GetInt32()] = new BattlefieldCell(cell[2].GetUInt32());
        }
        var floors = Convert.FromHexString(i.GetProperty("floors").GetString()!);
        var halfW = Convert.FromHexString(i.GetProperty("halfW").GetString()!);
        var halfH = Convert.FromHexString(i.GetProperty("halfH").GetString()!);
        var moving = Convert.FromHexString(i.GetProperty("moving").GetString()!);
        var k = 0;
        foreach (var hex in i.GetProperty("combatants").EnumerateArray())
        {
            var rec = Convert.FromHexString(hex.GetString()!);
            var c = battle[k];
            c.Position = new MapPoint(BitConverter.ToInt16(rec, 4), BitConverter.ToInt16(rec, 6));
            c.Status = (CharacterStatus)rec[0x14];
            c.ReadiedSkill = (sbyte)rec[0x59];
            c.Skills[4] = (sbyte)rec[0x6F];
            c.Floor = (sbyte)floors[k];
            c.HalfWidth = (sbyte)halfW[k];
            c.HalfHeight = (sbyte)halfH[k];
            c.Moving = moving[k] != 0;
            k++;
        }
        var shapes = new FurnitureShape?[16];
        var f = 0;
        foreach (var s in i.GetProperty("shapes").EnumerateArray())
        {
            shapes[f++] = new FurnitureShape(s[0].GetInt32(), s[1].GetInt32(), [.. s[2].EnumerateArray().Select(v => v.GetInt32())],
                [.. s[3].EnumerateArray().Select(v => v.GetInt32())]);
        }
        return (battle, new BattleMap(battle, level, shapes, i.GetProperty("sight").GetInt32()));
    }
}
