using System.Buffers.Binary;
using System.Text.Json;
using Darklands.Content;
using Darklands.Content.Formats.Exe;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Tactics;
using Darklands.Core.Characters;
using Darklands.Core.Randomness;
using Darklands.Persistence;

namespace Darklands.Oracle.Tests;

/// <summary>
/// The battle's effects (R019 effect-cast and everything it calls, the area test 0E76:469E,
/// cloud-on-line-test 1EC9:0142 and the Throw list 1EF8:0C80) against the original program
/// (tools/oracle/build_effects_corpus.py → Corpus/effects.json). The weapon tables come from the
/// player's EXE (DARKLANDS_DIR, or the "game" key of local/paths.json); without it the test passes vacuously.
/// </summary>
public sealed class EffectsOracleTests
{
    private const int Ds = 0x321A * 16;
    private const int Combatants = Ds + 0x9C55;
    private const int Clouds = Ds + 0xA2EB, CloudCount = Ds + 0xA33D, Areas = Ds + 0xA342, AreaCount = Ds + 0xA395;
    private const int Markers = Ds + 0xA396, MarkerCount = Ds + 0xA531;
    private const int Mods = Ds + 0xA532, FlashUntil = Ds + 0xDA89, ActionStates = Ds + 0xCA64, Delayed = Ds + 0xD842;
    private const int AttackRates = Ds + 0xD493, Queued = Ds + 0xD70C, Shown = Ds + 0xD4C7, Scratch = Ds + 0xFD00;
    private const int Menu = Ds + 0x9A47, MenuState = Ds + 0x97DD;

    private static readonly Lazy<Corpus> Data = new(() => Corpus.Load("effects.json"));

    private static readonly Lazy<CombatTables?> Tables = new(() =>
    {
        var folder = Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") ?? "";
        return GameFiles.IsGameFolder(folder) ? CombatTablesReader.Read(DarklandsExecutable.Load(new GameFiles(folder))) : null;
    });

    [Fact]
    public void EffectSpellIsTheOriginals()
    {
        if (Tables.Value is null)
        {
            return;
        }
        var n = 0;
        foreach (var c in Data.Value.Cases("R019+0x0000"))
        {
            var label = $"effect-cast case {n++}";
            var (battle, field, codes, sounds) = Scene(c.Inputs);
            var a = Args(c.Inputs);
            field.Lands(a[0], a[1], a[2], a[3], a[4], a[5], a[6]);
            var i = c.Inputs;
            var state = i.GetProperty("state").GetUInt32();
            Assert.True(c.FinalUInt32(Data.Value.HoldRand, state) == ((MscRandom)battle.Random).State, label + " random");
            Assert.True(c.FinalByte(Delayed, 1) == 0, label + " DS:D842");
            var calls = c.Element.GetProperty("calls").EnumerateArray().Count(e => e.GetString() == "sound");
            Assert.True(calls == sounds.Count, $"{label}: {calls} sounds vs {sounds.Count}");
            CheckCombatants(c, battle, codes, label);
            CheckLists(c, field, label);
            var flash = Convert.FromHexString(c.Element.GetProperty("flash").GetString()!);
            for (var k = 0; k < Combatant.Count; k++)
            {
                for (var w = 0; w < 3; w++)
                {
                    var original = BinaryPrimitives.ReadInt16LittleEndian(flash.AsSpan(6 * k + 2 * w));
                    var mine = battle[k].Flash[w] switch
                    {
                        EffectField.FlashSprite1 => 0x1234,
                        EffectField.FlashSprite2 => 0x5678,
                        var v => v,
                    };
                    Assert.True(original == mine, $"{label}: flash {k}.{w} {original:X4} vs {mine:X4}");
                }
                var pack = Convert.FromHexString(c.Element.GetProperty("packs")[k].GetString()!);
                var items = battle[k].Items;
                // The original's pack is seeded with as many records as the combatant had; consumed ones leave stale bytes behind.
                for (var s = 0; s < items.Count; s++)
                {
                    var r = pack.AsSpan(6 * s, 6);
                    var it = items[s];
                    Assert.True(BinaryPrimitives.ReadUInt16LittleEndian(r) == it.Code && r[2] == (byte)it.Type && r[3] == (byte)it.Quality
                        && r[4] == (byte)it.Count, $"{label}: item {k}.{s}");
                }
            }
        }
        Assert.True(n > 0);
    }

    [Fact]
    public void AreaTestIsTheOriginals()
    {
        if (Tables.Value is null)
        {
            return;
        }
        var n = 0;
        foreach (var c in Data.Value.Cases("in_area"))
        {
            var (_, field, _, _) = Scene(c.Inputs);
            var spot = Convert.FromHexString(c.Inputs.GetProperty("spot").GetString()!);
            var who = Args(c.Inputs)[0];
            var mine = field.Inside(BinaryPrimitives.ReadInt16LittleEndian(spot), BinaryPrimitives.ReadInt16LittleEndian(spot.AsSpan(2)), spot[4], who);
            Assert.True((c.Ax == 1) == mine, $"in_area case {n}: {c.Ax} vs {mine}");
            n++;
        }
        Assert.True(n > 0);
    }

    [Fact]
    public void CloudLineTestIsTheOriginals()
    {
        if (Tables.Value is null)
        {
            return;
        }
        var n = 0;
        foreach (var c in Data.Value.Cases("R014+0x10672"))
        {
            var (_, field, _, _) = Scene(c.Inputs);
            var a = Args(c.Inputs);
            var w = c.Inputs.GetProperty("residue").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            var mine = field.LineCrossesCloud(a[0], a[1], (w[0], w[1], w[2], w[3]));
            Assert.True((c.Ax == 1) == mine, $"cloud-on-line-test case {n}: {c.Ax} vs {mine}");
            n++;
        }
        Assert.True(n > 0);
    }

    /// <summary>hit-roll's missile chance with clouds about: the cloud test starts from what its roll-one-to-n(100) left on the stack.</summary>
    [Fact]
    public void ToHitThroughCloudsIsTheOriginals()
    {
        if (Tables.Value is null)
        {
            return;
        }
        var n = 0;
        foreach (var c in Data.Value.Cases("to_hit_clouds"))
        {
            var (battle, field, _, _) = Scene(c.Inputs);
            battle.Clouds = field;
            var rates = Convert.FromHexString(c.Inputs.GetProperty("rates").GetString()!);
            var reach = Convert.FromHexString(c.Inputs.GetProperty("reach").GetString()!);
            for (var k = 0; k < Combatant.Count; k++)
            {
                battle[k].AttackRate = rates[k];
                for (var j = 0; j < Combatant.Count; j++)
                {
                    battle.Reaches[k, j] = reach[k * 13 + j] != 0;
                }
            }
            var a = Args(c.Inputs);
            var result = Attacks.ToHit(battle, a[0], a[1], out _);
            Assert.True((short)c.Ax == (short)result, $"to_hit_clouds case {n}: {(short)c.Ax} vs {(short)result}");
            Assert.True(c.FinalUInt32(Data.Value.HoldRand, c.Inputs.GetProperty("state").GetUInt32()) == ((MscRandom)battle.Random).State, $"to_hit_clouds case {n} random");
            n++;
        }
        Assert.True(n > 0);
    }

    [Fact]
    public void ThrowListIsTheOriginals()
    {
        if (Tables.Value is null)
        {
            return;
        }
        var n = 0;
        foreach (var c in Data.Value.Cases("throw_list"))
        {
            var (battle, field, _, _) = Scene(c.Inputs);
            var who = Args(c.Inputs)[0];
            var lines = field.ThrowList(who);
            var label = $"throw_list case {n++}";
            Assert.True(c.FinalByte(MenuState, 0) == (lines.Count != 0 ? 1 : 2), $"{label}: DS:97DD");
            if (((ushort)battle[who].Action & 2) == 2)
            {
                continue;
            }
            for (var k = 0; k < lines.Count; k++)
            {
                var at = Menu + 0x16 * k;
                Assert.True(c.FinalInt16(at, 0) == 0x7000 + 2 * lines[k].Potion, $"{label}: line {k} name");
                Assert.True(c.FinalByte(at + 9, 0) == lines[k].Potion, $"{label}: line {k} potion");
                Assert.True(c.FinalByte(at + 8, 0) == lines[k].Quality, $"{label}: line {k} quality");
                Assert.True(c.FinalByte(at + 7, 0) == (byte)lines[k].Count, $"{label}: line {k} count");
            }
        }
        Assert.True(n > 0);
    }

    private static void CheckCombatants(OracleCase c, Battle battle, string[] codes, string label)
    {
        var i = c.Inputs;
        var mods = i.GetProperty("mods").EnumerateArray().Select(m => Convert.FromHexString(m.GetString()!)).ToArray();
        for (var k = 0; k < Combatant.Count; k++)
        {
            var seed = Convert.FromHexString(i.GetProperty("combatants")[k].GetString()!);
            var mine = (byte[])seed.Clone();
            CombatantRecords.Write(battle[k], mine);
            mine[0x7E] = (byte)battle[k].Items.Count;
            for (var b = 0; b < CombatantRecords.Size; b++)
            {
                Assert.True(c.FinalByte(Combatants + 0x80 * k + b, seed[b]) == mine[b], $"{label}: combatant {k} +0x{b:X2}");
            }
            var m = battle[k].Modifiers;
            var bytes = new byte[0x12];
            bytes[0] = (byte)m.Attributes[0];
            bytes[1] = (byte)m.Attributes[1];
            for (var s = 0; s < 7; s++)
            {
                bytes[2 + s] = (byte)m.Skills[s];
            }
            bytes[9] = (byte)(m.Held ? 1 : 0);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x0A), (ushort)m.SavedAction);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x0C), m.AttributesUntil);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x0E), m.SkillsUntil);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x10), m.HeldUntil);
            for (var b = 0; b < 0x12; b++)
            {
                Assert.True(c.FinalByte(Mods + 0x12 * k + b, mods[k][b]) == bytes[b], $"{label}: modifiers {k} +0x{b:X2}");
            }
            Assert.True(c.FinalByte(ActionStates + k, 7) == (byte)battle[k].State, $"{label}: fighter-action {k}");
            Assert.True((ushort)c.FinalInt16(FlashUntil + 2 * k, 0) == battle[k].FlashUntil, $"{label}: DS:DA89 {k}");
            Assert.True(c.FinalByte(AttackRates + k, 0x40) == battle[k].AttackRate, $"{label}: rate {k}");
            Assert.True(c.FinalByte(Queued + 2 * k, 0) == (byte)battle[k].QueuedEndurance, $"{label}: queued {k}");
            Assert.True(c.FinalByte(Queued + 2 * k + 1, 0) == (byte)battle[k].QueuedStrength, $"{label}: queued strength {k}");
            Assert.True(c.FinalByte(Shown + k, 0) == (byte)battle[k].DamageShown, $"{label}: shown {k}");
        }
        var seeds = i.GetProperty("codes").EnumerateArray().Select(e => e.GetString()!).ToArray();
        for (var s = 0; s < 7; s++)
        {
            var seed = seeds[s];
            var mine = codes[s];
            for (var b = 0; b < 2; b++)
            {
                Assert.True(c.FinalByte(Scratch + 4 * s + b, (byte)seed[b]) == (byte)mine[b], $"{label}: animation-file-name[{s}]");
            }
        }
    }

    private static void CheckLists(OracleCase c, EffectField field, string label)
    {
        var lists = c.Inputs.GetProperty("lists");
        var clouds = Convert.FromHexString(lists.GetProperty("clouds").GetProperty("hex").GetString()!);
        var areas = Convert.FromHexString(lists.GetProperty("areas").GetProperty("hex").GetString()!);
        var markers = Convert.FromHexString(lists.GetProperty("markers").GetProperty("hex").GetString()!);
        Assert.True(c.FinalByte(CloudCount, (byte)lists.GetProperty("clouds").GetProperty("count").GetInt32()) == field.CloudCount, label + " clouds");
        Assert.True(c.FinalByte(AreaCount, (byte)lists.GetProperty("areas").GetProperty("count").GetInt32()) == field.AreaCount, label + " areas");
        Assert.True(c.FinalByte(MarkerCount, (byte)lists.GetProperty("markers").GetProperty("count").GetInt32()) == field.MarkerCount, label + " markers");
        for (var k = 0; k < 8; k++)
        {
            var cl = field.CloudSlots[k];
            var r = new byte[10];
            BinaryPrimitives.WriteInt16LittleEndian(r, (short)cl.X);
            BinaryPrimitives.WriteInt16LittleEndian(r.AsSpan(2), (short)cl.Y);
            r[4] = cl.Size;
            r[5] = cl.Kind;
            r[6] = (byte)cl.Byte6;
            r[7] = (byte)cl.Level;
            BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(8), cl.Until);
            for (var b = 0; b < 10; b++)
            {
                Assert.True(c.FinalByte(Clouds + 10 * k + b, clouds[10 * k + b]) == r[b], $"{label}: cloud {k} +{b}");
            }
            var ar = field.AreaSlots[k];
            BinaryPrimitives.WriteInt16LittleEndian(r, (short)ar.X);
            BinaryPrimitives.WriteInt16LittleEndian(r.AsSpan(2), (short)ar.Y);
            r[4] = ar.Size;
            r[5] = ar.Strength;
            r[6] = (byte)ar.Level;
            r[7] = (byte)ar.Byte7;
            BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(8), ar.Until);
            for (var b = 0; b < 10; b++)
            {
                Assert.True(c.FinalByte(Areas + 10 * k + b, areas[10 * k + b]) == r[b], $"{label}: area {k} +{b}");
            }
            var mk = field.MarkerSlots[k];
            BinaryPrimitives.WriteInt16LittleEndian(r, (short)mk.X);
            BinaryPrimitives.WriteInt16LittleEndian(r.AsSpan(2), (short)mk.Y);
            r[4] = mk.Frame;
            r[5] = mk.Kind;
            r[6] = (byte)mk.Level;
            r[7] = (byte)mk.Byte7;
            for (var b = 0; b < 8; b++)
            {
                Assert.True(c.FinalByte(Markers + 8 * k + b, markers[8 * k + b]) == r[b], $"{label}: marker {k} +{b}");
            }
        }
    }

    private static int[] Args(JsonElement i) => [.. i.GetProperty("args").EnumerateArray().Select(a => a.GetInt32())];

    private static (Battle Battle, EffectField Field, string[] Codes, List<int> Sounds) Scene(JsonElement i)
    {
        var battle = new Battle(Tables.Value!, new MscRandom(i.GetProperty("state").GetUInt32()))
        {
            RealCombat = true, Clock = (ushort)i.GetProperty("clock").GetInt32(), FloorShown = 0,
        };
        var level = new BattlefieldLevel();
        foreach (var cell in i.GetProperty("cells").EnumerateArray())
        {
            level[cell[0].GetInt32(), cell[1].GetInt32()] = new BattlefieldCell(cell[2].GetUInt32());
        }
        var floors = Convert.FromHexString(i.GetProperty("floors").GetString()!);
        var immune = i.GetProperty("immune").EnumerateArray().Select(v => v.GetInt32()).ToArray();
        var mods = i.GetProperty("mods").EnumerateArray().Select(m => Convert.FromHexString(m.GetString()!)).ToArray();
        var packs = i.GetProperty("packs").EnumerateArray().ToArray();
        for (var k = 0; k < Combatant.Count; k++)
        {
            var c = battle[k];
            CombatantRecords.Read(Convert.FromHexString(i.GetProperty("combatants")[k].GetString()!), c);
            c.Floor = (sbyte)floors[k];
            c.HalfWidth = 5;
            c.HalfHeight = 2;
            c.EffectImmunity = (short)immune[k];
            c.State = ActionState.Ready;
            c.AttackRate = 0x40;
            var m = c.Modifiers;
            var r = mods[k];
            m.Attributes[0] = (sbyte)r[0];
            m.Attributes[1] = (sbyte)r[1];
            for (var s = 0; s < 7; s++)
            {
                m.Skills[s] = (sbyte)r[2 + s];
            }
            m.Held = r[9] != 0;
            m.SavedAction = (CombatAction)BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(0x0A));
            m.AttributesUntil = BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(0x0C));
            m.SkillsUntil = BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(0x0E));
            m.HeldUntil = BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(0x10));
            c.Items.Clear();
            foreach (var it in packs[k].EnumerateArray())
            {
                c.Items.Add(new ItemStack(it[0].GetInt32(), it[1].GetInt32(), it[2].GetInt32(), it[3].GetInt32(), it[4].GetInt32()));
            }
        }
        var shapes = new FurnitureShape?[16];
        var f = 0;
        foreach (var s in i.GetProperty("shapes").EnumerateArray())
        {
            shapes[f++] = new FurnitureShape(s[0].GetInt32(), s[1].GetInt32(), [.. s[2].EnumerateArray().Select(v => v.GetInt32())],
                [.. s[3].EnumerateArray().Select(v => v.GetInt32())]);
        }
        var map = new BattleMap(battle, level, shapes, i.GetProperty("sight").GetInt32());
        var codes = i.GetProperty("codes").EnumerateArray().Select(e => e.GetString()!).ToArray();
        var sounds = new List<int>();
        var field = new EffectField(battle, map, sounds.Add,
            (k, w, flag) => Attacks.ReadyWeapon(battle, k, w, flag, code => codes[battle[k].AnimationSlot] = code),
            slot => codes[slot], () => false);
        var lists = i.GetProperty("lists");
        var clouds = Convert.FromHexString(lists.GetProperty("clouds").GetProperty("hex").GetString()!);
        var areas = Convert.FromHexString(lists.GetProperty("areas").GetProperty("hex").GetString()!);
        var markers = Convert.FromHexString(lists.GetProperty("markers").GetProperty("hex").GetString()!);
        for (var k = 0; k < 8; k++)
        {
            var r = clouds.AsSpan(10 * k);
            field.CloudSlots[k] = new EffectCloud
            {
                X = BinaryPrimitives.ReadInt16LittleEndian(r), Y = BinaryPrimitives.ReadInt16LittleEndian(r[2..]), Size = r[4], Kind = r[5],
                Byte6 = (sbyte)r[6], Level = (sbyte)r[7], Until = BinaryPrimitives.ReadUInt16LittleEndian(r[8..]),
            };
            r = areas.AsSpan(10 * k);
            field.AreaSlots[k] = new EffectArea
            {
                X = BinaryPrimitives.ReadInt16LittleEndian(r), Y = BinaryPrimitives.ReadInt16LittleEndian(r[2..]), Size = r[4], Strength = r[5],
                Level = (sbyte)r[6], Byte7 = (sbyte)r[7], Until = BinaryPrimitives.ReadUInt16LittleEndian(r[8..]),
            };
            r = markers.AsSpan(8 * k);
            field.MarkerSlots[k] = new EffectMarker
            {
                X = BinaryPrimitives.ReadInt16LittleEndian(r), Y = BinaryPrimitives.ReadInt16LittleEndian(r[2..]), Frame = r[4], Kind = r[5],
                Level = (sbyte)r[6], Byte7 = (sbyte)r[7],
            };
        }
        field.SetCounts(lists.GetProperty("clouds").GetProperty("count").GetInt32(), lists.GetProperty("areas").GetProperty("count").GetInt32(),
            lists.GetProperty("markers").GetProperty("count").GetInt32());
        return (battle, field, codes, sounds);
    }
}
