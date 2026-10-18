using System.Text.Json;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Tactics;
using Darklands.Core.Characters;
using Darklands.Core.Randomness;

namespace Darklands.Oracle.Tests;

/// <summary>
/// The path search against the original path_find (trackers, waypoint pruning, the AI's path
/// hook, the shortcut pass) on generated floors (tools/oracle/build_path_corpus.py → Corpus/path.json).
/// </summary>
public sealed class PathOracleTests
{
    [Fact]
    public void PathsAreTheOriginals()
    {
        var path = Environment.GetEnvironmentVariable("PATH_CORPUS") ?? Path.Combine(AppContext.BaseDirectory, "Corpus", "path.json");
        if (!File.Exists(path))
        {
            return;
        }
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var n = 0;
        var failures = new List<string>();
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var i = c.GetProperty("inputs");
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
                var cb = battle[k];
                cb.Position = new MapPoint(BitConverter.ToInt16(rec, 4), BitConverter.ToInt16(rec, 6));
                cb.Destination = new MapPoint(BitConverter.ToInt16(rec, 8), BitConverter.ToInt16(rec, 0x0A));
                cb.Status = (CharacterStatus)rec[0x14];
                cb.Facing = rec[0x19];
                cb.Target = (sbyte)rec[0x4A];
                cb.Floor = (sbyte)floors[k];
                cb.HalfWidth = (sbyte)halfW[k];
                cb.HalfHeight = (sbyte)halfH[k];
                cb.Moving = moving[k] != 0;
                k++;
            }
            var shapes = new FurnitureShape?[16];
            var f = 0;
            foreach (var s in i.GetProperty("shapes").EnumerateArray())
            {
                shapes[f++] = new FurnitureShape(s[0].GetInt32(), s[1].GetInt32(), [.. s[2].EnumerateArray().Select(v => v.GetInt32())],
                    [.. s[3].EnumerateArray().Select(v => v.GetInt32())]);
            }
            var map = new BattleMap(battle, level, shapes, 16);
            var finder = new PathFinder(map);
            finder.UseHalfSizes(w => battle[w].HalfWidth, h => battle[h].HalfHeight);
            var stepX = i.GetProperty("stepX").EnumerateArray().Select(v => (sbyte)v.GetInt32()).ToArray();
            var stepY = i.GetProperty("stepY").EnumerateArray().Select(v => (sbyte)v.GetInt32()).ToArray();
            var who = i.GetProperty("who").GetInt32();
            if (c.TryGetProperty("trackers", out var tr) && tr.ValueKind == JsonValueKind.Object)
            {
                var me = battle[who];
                var trackLength = finder.TrackLength(who, me.Target, me.Position, me.Destination, me.Facing, stepX, stepY);
                var trackFound = (Convert.ToInt32(tr.GetProperty("ax").GetString(), 16) & 0x7FFF) == 1;
                if (trackFound != trackLength >= 0)
                {
                    failures.Add($"case {n} trackers: found {trackFound} vs {trackLength >= 0}");
                }
                else if (trackFound)
                {
                    var steps = Convert.FromHexString(tr.GetProperty("steps").GetString()!);
                    var mine = finder.LastTrack[..Math.Min(trackLength + 1, 250)].ToArray();
                    if (tr.GetProperty("length").GetInt32() != trackLength || !steps.AsSpan().SequenceEqual(mine))
                    {
                        failures.Add($"case {n} trackers: trackLength {tr.GetProperty("length").GetInt32()} vs {trackLength}\n  {Convert.ToHexString(steps)}\n  {Convert.ToHexString(mine)}");
                    }
                }
                finder = new PathFinder(map);
                finder.UseHalfSizes(w => battle[w].HalfWidth, h => battle[h].HalfHeight);
            }
            var found = finder.Find(battle, who, stepX, stepY);
            var label = $"case {n++} (who {who})";
            var expectedFound = Convert.ToInt32(c.GetProperty("ax").GetString(), 16) == 1;
            if (expectedFound != found)
            {
                failures.Add($"{label}: found {expectedFound} vs {found}");
                continue;
            }
            if (!found)
            {
                continue;
            }
            var length = c.GetProperty("length").GetInt32();
            var expected = Convert.FromHexString(c.GetProperty("path").GetString()!);
            var first = Enumerable.Range(0, Math.Min(length, 249) + 1).FirstOrDefault(s => expected[s] != finder.Path[s], -1);
            if (length != finder.PathLength || first >= 0)
            {
                failures.Add($"{label}: length {length} vs {finder.PathLength}, first difference at step {first}\n  {Convert.ToHexString(expected, 0, Math.Min(length + 1, 250))}\n  {Convert.ToHexString(finder.Path, 0, Math.Min(finder.PathLength + 1, 250))}");
            }
        }
        Assert.True(n > 0);
        Assert.True(failures.Count == 0, $"{failures.Count} of {n} differ:\n" + string.Join("\n", failures));
    }
}
