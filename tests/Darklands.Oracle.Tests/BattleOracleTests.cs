using System.Text.Json;
using Darklands.Content;
using Darklands.Content.Formats.Exe;
using Darklands.Core.Battles;
using Darklands.Core.Randomness;
using Darklands.Persistence;

namespace Darklands.Oracle.Tests;

/// <summary>
/// The battle's attack rules (R014 18BF) against the original program: every case in
/// Corpus/battle.json is the original function run from DARKLAND.EXE on generated combatants
/// (tools/oracle/build_battle_corpus.py). The weapon tables come from the player's EXE
/// (DARKLANDS_DIR, or the "game" key of local/paths.json); without it the test passes vacuously.
/// </summary>
public sealed class BattleOracleTests
{
    private const int DataSegment = 0x321A;
    private const int Combatants = DataSegment * 16 + 0x9C55;
    private const int AttackRates = DataSegment * 16 + 0xD493;
    private const int Queued = DataSegment * 16 + 0xD70C;
    private const int Shown = DataSegment * 16 + 0xD4C7;
    private const int Crit = DataSegment * 16 + 0xFE00;
    private const int AnimFiles = DataSegment * 16 + 0xFD00;

    private static readonly Lazy<Corpus> Data = new(() => Corpus.Load("battle.json"));

    private static readonly Lazy<CombatTables?> Tables = new(() =>
    {
        var folder = Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") ?? "";
        return GameFiles.IsGameFolder(folder) ? CombatTablesReader.Read(DarklandsExecutable.Load(new GameFiles(folder))) : null;
    });

    public static TheoryData<string> Functions() =>
        ["attack_rate", "to_hit_roll", "hit_damage", "damage_queue", "damage_apply", "weapon_ready", "skill_try_improve"];

    [Theory]
    [MemberData(nameof(Functions))]
    public void MatchesTheOriginalFunction(string function)
    {
        if (Tables.Value is null)
        {
            return;
        }
        var cases = Data.Value.Cases(function);
        Assert.NotEmpty(cases);
        var n = 0;
        foreach (var c in cases)
        {
            Check(function, c, n++);
        }
    }

    private static void Check(string function, OracleCase c, int n)
    {
        var i = c.Inputs;
        var state = i.GetProperty("state").GetUInt32();
        var random = new MscRandom(state);
        var battle = Setup(i, random);
        var args = i.GetProperty("args").EnumerateArray().Select(a => a.GetInt32()).ToArray();
        var label = $"{function} case {n}";
        switch (function)
        {
            case "attack_rate":
                {
                    var k = args[0];
                    Attacks.UpdateAttackRate(battle, k);
                    Assert.True(c.FinalByte(AttackRates + k, Hex(i, "rates")[k]) == battle[k].AttackRate, label);
                    Assert.True(c.FinalByte(Combatants + 0x80 * k + 0x1D, Record(i, k)[0x1D]) == (byte)battle[k].AimDelay, label);
                    break;
                }
            case "to_hit_roll":
                {
                    battle.Clouds = new FixedClouds(i.GetProperty("cloud").GetInt32() == 1);
                    var result = Attacks.ToHit(battle, args[0], args[1], out var location);
                    Assert.True((short)c.Ax == (short)result, $"{label}: {result} vs {(short)c.Ax}");
                    Assert.True(c.FinalInt16(Crit, 0) == location, label + " location");
                    Assert.True(c.FinalUInt32(Data.Value.HoldRand, state) == random.State, label + " random");
                    break;
                }
            case "hit_damage":
                {
                    var r = Attacks.HitDamage(battle, args[0], args[1], (HitResult)args[2], args[3]);
                    Assert.True((short)c.Ax == (short)r, $"{label}: {r} vs {(short)c.Ax}");
                    CheckQueue(c, i, battle, args[1], label);
                    Assert.True(c.FinalUInt32(Data.Value.HoldRand, state) == random.State, label + " random");
                    break;
                }
            case "damage_queue":
                {
                    var r = Attacks.QueueDamage(battle, args[0], args[1], args[2], args[3]);
                    Assert.True((short)c.Ax == (short)r, $"{label}: {r} vs {(short)c.Ax}");
                    CheckQueue(c, i, battle, args[1], label);
                    Assert.True(c.FinalUInt32(Data.Value.HoldRand, state) == random.State, label + " random");
                    break;
                }
            case "damage_apply":
                {
                    var k = args[0];
                    Attacks.ApplyDamage(battle, k);
                    var rec = Record(i, k);
                    Assert.True(c.FinalByte(Combatants + 0x80 * k + 0x14, rec[0x14]) == (byte)battle[k].Status, label + " status");
                    Assert.True(c.FinalByte(Combatants + 0x80 * k + 0x5D, rec[0x5D]) == (byte)battle[k].Endurance, label + " endurance");
                    Assert.True(c.FinalByte(Combatants + 0x80 * k + 0x5E, rec[0x5E]) == (byte)battle[k].Strength, label + " strength");
                    Assert.True(c.FinalByte(Shown + k, Hex(i, "shown")[k]) == (byte)battle[k].DamageShown, label + " shown");
                    CheckQueue(c, i, battle, k, label);
                    break;
                }
            case "skill_try_improve":
                {
                    battle.Difficulty = i.GetProperty("difficulty").GetInt32();
                    var (m, skill) = (args[0], args[1]);
                    BattleEnd.TryImprove(battle, m, skill, args[2], null);
                    var at = Combatants + 0x80 * m + 0x6B + skill;
                    Assert.True(c.FinalByte(at, Record(i, m)[0x6B + skill]) == (byte)battle[m].Skills[skill], label + " skill");
                    Assert.True(c.FinalUInt32(Data.Value.HoldRand, state) == random.State, label + " random");
                    break;
                }
            case "weapon_ready":
                {
                    var k = args[0];
                    var code = "";
                    Attacks.ReadyWeapon(battle, k, args[1], args[2], x => code = x);
                    if (i.TryGetProperty("dragonCode", out _))
                    {
                        // the animation code copied into anim_file[slot] (the generator points slot s at DS:FD00 + 4s)
                        var at = AnimFiles + 4 * Record(i, k)[0x16];
                        Assert.True(c.FinalByte(at, 0) == code[0] && c.FinalByte(at + 1, 0) == code[1], $"{label}: code {code}");
                    }
                    var rec = Record(i, k);
                    var actual = (byte[])rec.Clone();
                    CombatantRecords.Write(battle[k], actual);
                    for (var b = 0; b < CombatantRecords.Size; b++)
                    {
                        Assert.True(c.FinalByte(Combatants + 0x80 * k + b, rec[b]) == actual[b], $"{label}: byte +0x{b:X2}");
                    }
                    Assert.True(c.FinalByte(AttackRates + k, Hex(i, "rates")[k]) == battle[k].AttackRate, label + " rate");
                    break;
                }
        }
    }

    private static void CheckQueue(OracleCase c, JsonElement i, Battle battle, int t, string label)
    {
        var queued = Hex(i, "queued");
        Assert.True(c.FinalByte(Queued + 2 * t, queued[2 * t]) == (byte)battle[t].QueuedEndurance, label + " queued endurance");
        Assert.True(c.FinalByte(Queued + 2 * t + 1, queued[2 * t + 1]) == (byte)battle[t].QueuedStrength, label + " queued strength");
    }

    private static Battle Setup(JsonElement i, IRandomSource random)
    {
        var battle = new Battle(Tables.Value!, random) { RealCombat = i.GetProperty("real").GetInt32() == 1 };
        if (i.TryGetProperty("dragon", out var fight))
        {
            battle.DragonFight = (byte)fight.GetInt32();
        }
        if (i.TryGetProperty("dragonCode", out var dragon) && dragon.ValueKind == JsonValueKind.String)
        {
            battle.DragonWeaponCode = dragon.GetString();
        }
        var rates = Hex(i, "rates");
        var queued = Hex(i, "queued");
        var shown = Hex(i, "shown");
        var reach = Hex(i, "reach");
        for (var k = 0; k < Combatant.Count; k++)
        {
            var c = battle[k];
            CombatantRecords.Read(Record(i, k), c);
            c.AttackRate = rates[k];
            c.QueuedEndurance = (sbyte)queued[2 * k];
            c.QueuedStrength = (sbyte)queued[2 * k + 1];
            c.DamageShown = (sbyte)shown[k];
            for (var a = 0; a < Combatant.Count; a++)
            {
                battle.Reaches[k, a] = reach[k * 13 + a] != 0;
            }
        }
        return battle;
    }

    private static byte[] Record(JsonElement i, int k) => Convert.FromHexString(i.GetProperty("combatants")[k].GetString()!);

    private static byte[] Hex(JsonElement i, string name) => Convert.FromHexString(i.GetProperty(name).GetString()!);

    private sealed class FixedClouds(bool crossing) : IEffectClouds
    {
        public bool LineCrossesCloud(int attacker, int target) => crossing;

        public bool InsideACloud(int combatant) => false;
    }
}
