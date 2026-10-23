using System.Text;
using System.Text.Json;
using Darklands.Cards;
using Darklands.Content;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Tactics;
using Darklands.Core.Randomness;
using Darklands.Presentation;
using Darklands.Presentation.Battles;

namespace Darklands.Battles.Tests.BattleCalls;

/// <summary>
/// Single battle functions of the original against the rewrite, from the same machine state: tools/oracle/battle_calls.py captures states
/// inside whole fights, changes their memory and calls one original function; this test builds the rewrite's battle from the same bytes
/// (<see cref="ImageOverlay"/>, <see cref="TacticsOverlay"/>), makes the corresponding call and compares the function's result, the memory the
/// rewrite models and the random generator's draws. Set DARKLANDS_BATTLE_CALLS to the jobs file (battle_calls.py export);
/// DARKLANDS_BATTLE_CALLS_OUT is the folder for results.tsv (every job) and excluded.txt (the differing jobs, "function|state#n" as the
/// coverage lines name them). Without the jobs file (or the game) the test passes vacuously. Differences are reported, not asserted: they
/// are leads.
/// </summary>
public sealed class BattleCallsTests(Xunit.Abstractions.ITestOutputHelper output)
{
    /// <summary>A call the original makes from a state the game never has (an index past the table, a field the original reads uninitialised): not judged.</summary>
    internal sealed class SkipJob(string why) : Exception(why);

    /// <summary>What a job's call gets: the rewrite's battle built from the job's input memory.</summary>
    internal sealed class Context
    {
        public required TacticalBattle Tactics { get; init; }

        public Battle Battle => Tactics.Battle;

        public required MemoryPair Input { get; init; }

        public List<string> Notes { get; } = [];

        /// <summary>Memory a call writes that has no place in the overlay (an output buffer): DS offset to name; the adapter puts the rewrite's bytes into <see cref="Actual"/>.</summary>
        public Dictionary<int, string> Outputs { get; } = [];

        public Dictionary<int, byte> Actual { get; } = [];

        /// <summary>The same for the far segment (20A5): offset to name, and the rewrite's bytes.</summary>
        public Dictionary<int, string> FarOutputs { get; } = [];

        public Dictionary<int, byte> ActualFar { get; } = [];
    }

    private static readonly System.Reflection.BindingFlags Any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;

    /// <summary>Calls a method of the tactical battle by name (most of the functions under test are private).</summary>
    internal static object? Call(TacticalBattle t, string name, params object?[] args)
    {
        try
        {
            return typeof(TacticalBattle).GetMethod(name, Any)!.Invoke(t, args);
        }
        catch (System.Reflection.TargetInvocationException e) when (e.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            throw;
        }
    }

    /// <summary>The calls: original function to what the rewrite does for it, returning the value the original leaves in AX (null: none compared).</summary>
    private static readonly Dictionary<string, Func<Context, CallJob, int?>> Adapters = new()
    {
        ["R014+0x08FE"] = (c, _) => BattleEnd.IsOver(c.Battle, c.Tactics) ? 1 : 0,
        ["R014+0x481A"] = (c, job) =>
        {
            var ok = (bool)Call(c.Tactics, "FindPath", job.Args[0])!;
            // the original returns 1 for a path and 0 or 2 for none (its callers test for 1): the rewrite's bool
            var answer = ok ? 1 : job.Orig.Ax is 0 or 2 ? job.Orig.Ax : 0;
            // the found path: its steps at 20A5:71BA, the length in DS:E118 (path_give copies length + 1 bytes)
            var paths = c.Tactics.Paths;
            if (!ok)
            {
                // no path: the original leaves a sentinel length and the last search's bytes, the callers do not read them
                return answer;
            }
            c.Outputs[0xE118] = "path length";
            c.Outputs[0xE119] = "path length high";
            c.Actual[0xE118] = (byte)paths.PathLength;
            c.Actual[0xE119] = (byte)(paths.PathLength >> 8);
            for (var k = 0; k < paths.PathLength && k < PathFinder.MaxSteps; k++)
            {
                c.FarOutputs[0x71BA + k] = $"found path +{k}";
                c.ActualFar[0x71BA + k] = paths.Path[k];
            }
            return answer;
        },
        ["R018+0x0000"] = (c, job) =>
        {
            if (job.Args[2] != 0 || job.Args[0] is < 0 or >= 13 || (job.Args[1] is < -1 or >= 13 && job.Args[1] != 0xFFFF))
            {
                throw new SkipJob("only the weapon missiles of a combatant at a combatant or a spot: a spell's sprite family is the animation slot's, which this overlay does not carry");
            }
            if (c.Battle[job.Args[0]].ReadiedWeapon is < 0x17 or > 0x21)
            {
                throw new SkipJob("a weapon that is no missile weapon: the original's sprite and speed are uninitialised (a documented divergence)");
            }
            c.Tactics.LaunchMissile(job.Args[0], (short)job.Args[1], job.Args[2], job.Args[3], job.Args[4]);
            return null;
        },
        ["R014+0x0DEE"] = (c, _) =>
        {
            BattleEnd.Recover(c.Battle, c.Tactics);
            return null;
        },
        ["R014+0x0D86"] = (c, _) =>
        {
            BattleEnd.LeaveCombatMode(c.Battle, c.Tactics);
            return null;
        },
        ["R014+0x1624"] = (c, job) =>
        {
            Call(c.Tactics, "Step", job.Args[0], job.Args[1]);
            return null;
        },
        ["R014+0x2CCA"] = (c, job) =>
        {
            if (Environment.GetEnvironmentVariable("DARKLANDS_BATTLE_CALLS_ONLY") is not null)
            {
                var d = c.Battle[job.Args[0]];
                Console.WriteLine($"approach {job.Args[0]}: clock {c.Battle.Clock} repath at {d.RepathAt} path index {d.PathIndex} path byte {d.Path[(byte)d.PathIndex % 250]} queued {d.QueuedStep} held {d.Held} target {d.Target} pos {d.Position} dest {d.Destination}");
            }
            Call(c.Tactics, "Approach", job.Args[0]);
            return null;
        },
        ["R014+0xFADC"] = (c, _) =>
        {
            Call(c.Tactics, "EngageIdle");
            return null;
        },
        ["R014+0x10232"] = (c, job) =>
        {
            Call(c.Tactics, "Rethink", job.Args[0], job.Args[1]);
            return null;
        },
        ["R014+0x2A4A"] = (c, _) =>
        {
            Call(c.Tactics, "MeleeExchanges");
            return null;
        },
        ["R014+0x45AE"] = (c, _) =>
        {
            Call(c.Tactics, "RecoverEndurance");
            return null;
        },
        ["R014+0x4442"] = (c, _) => (bool)Call(c.Tactics, "CheckActivations")! ? 1 : 0,
        ["R014+0x0F58"] = (c, _) =>
        {
            if (c.Tactics.Paused)
            {
                throw new SkipJob("paused: the tick draws the renderer's random numbers, which depend on the view the rewrite's screen does not carry here");
            }
            c.Tactics.Tick();
            return null;
        },
        ["R018+0x0706"] = (c, _) =>
        {
            Call(c.Tactics, "EnemyTurn");
            return null;
        },
        ["R014+0x2FA2"] = (c, job) =>
        {
            Call(c.Tactics, "Arrive", job.Args[0]);
            return null;
        },
        ["R014+0x9C4C"] = (c, job) => c.Tactics.AnimationSet(job.Args[0]),
        ["R014+0x306A"] = (c, job) =>
        {
            var who = c.Battle[job.Args[0]];
            if (who.Target < 0 && who.Action != CombatAction.UseItem)
            {
                // the original indexes the records with the target -1 (the record before combatant 0): the rewrite answers the sprite's facing
                throw new SkipJob("no target and no item order: the original reads the record before combatant 0");
            }
            return c.Tactics.FacingToTarget(job.Args[0]);
        },
        ["R018+0x04B6"] = (c, job) =>
        {
            var seen = new bool[5];
            var n = (int)Call(c.Tactics, "EnemySees", seen)!;
            for (var p = 0; p < 5; p++)
            {
                c.Outputs[job.Args[0] + 2 * p] = $"seen[{p}]";
                c.Outputs[job.Args[0] + 2 * p + 1] = $"seen[{p}] high";
                c.Actual[job.Args[0] + 2 * p] = (byte)(seen[p] ? 1 : 0);
                c.Actual[job.Args[0] + 2 * p + 1] = 0;
            }
            return n;
        },
        ["R023+0x00B8"] = (c, job) =>
        {
            CheckDoor(c, job.Args[0]);
            Call(c.Tactics, "ToggleDoor", job.Args[0], job.Args[1]);
            return null;
        },
        ["R023+0x04AC"] = (c, job) =>
        {
            CheckDoor(c, job.Args[0]);
            var record = c.Tactics.Level.Image.AsSpan(0x1F54 + 14 * job.Args[0], 14);
            if (SideFlags(record) != 1)
            {
                throw new SkipJob("a door record without exactly one side flag");
            }
            Call(c.Tactics, "SetDoor", job.Args[0], job.Args[1] != 0, job.Args[2] != 0);
            return null;
        },
        ["R023+0x03CA"] = (c, job) =>
        {
            // (who, x, y, side): the member's door-opens byte (DS:C678) says set or clear
            if (job.Args[0] is < 0 or >= Combatant.PartySlots || job.Args[1] is < 0 or >= 40 || job.Args[2] is < 0 or >= 40)
            {
                throw new SkipJob("not a member, or a cell outside the 40 x 40 map");
            }
            if (job.Args[3] is >= 0 and <= 3)
            {
                Call(c.Tactics, "DoorBit", job.Args[1], job.Args[2], job.Args[3], c.Battle[job.Args[0]].DoorOpens);
            }
            return null;
        },
        ["R023+0x0734"] = (c, job) =>
        {
            // (x, y, side, flag): sets the bit when the flag is 1, clears it otherwise
            if (job.Args[0] is < 0 or >= 40 || job.Args[1] is < 0 or >= 40)
            {
                throw new SkipJob("a cell outside the 40 x 40 map");
            }
            if (job.Args[2] is >= 0 and <= 3)
            {
                Call(c.Tactics, "DoorBit", job.Args[0], job.Args[1], job.Args[2], job.Args[3] == 1);
            }
            return null;
        },
        ["R018+0x03AE"] = (c, job) =>
        {
            Call(c.Tactics, "DropRecoverable", job.Args[0], job.Args[1]);
            return null;
        },
        ["R014+0x1BA0"] = (c, job) =>
        {
            Call(c.Tactics, "MissileAction", job.Args[0]);
            return null;
        },
        ["R014+0xD04E"] = (c, job) => Member(c, job, "DoorAt"),
        ["R014+0xD402"] = (c, job) => Member(c, job, "RegionAt"),
        ["R014+0xD328"] = (c, job) => Member(c, job, "OnStairsAt"),
        ["R014+0xD60A"] = (c, job) => Member(c, job, "ZoneAt"),
        ["R014+0xD6DA"] = (c, job) =>
        {
            Call(c.Tactics, "Triggers", job.Args[0]);
            return null;
        },
        ["R014+0xDC14"] = (c, _) =>
        {
            Call(c.Tactics, "PartyLocations");
            return null;
        },
        ["R019+0x0000"] = (c, job) =>
        {
            Effect(c, "Lands", job.Args[0], job.Args[1], Party(job.Args[2]), job.Args[3], job.Args[4], job.Args[5], job.Args[6]);
            return null;
        },
        ["R019+0x0300"] = (c, job) => Slot(c, job, "CloudActs", 8, job.Args[0], job.Args[1], job.Args[2], job.Args[3]),
        ["R019+0x058A"] = (c, job) => Slot(c, job, "MarkerActs", 8, job.Args[0], job.Args[1], job.Args[2], job.Args[3], job.Args[4], job.Args[5]),
        ["R019+0x01E2"] = (c, job) => Slot(c, job, "RemoveCloud", 8, job.Args[0]),
        ["R019+0x0260"] = (c, job) => Slot(c, job, "RemoveArea", 8, job.Args[0]),
        ["R019+0x02B4"] = (c, job) => Slot(c, job, "RemoveMarker", 8, job.Args[0]),
        ["R019+0x0968"] = (c, job) => Slot(c, job, "WearItem", 13, job.Args[0], job.Args[1], job.Args[2], job.Args[3]),
        ["R019+0x0914"] = (c, job) => Slot(c, job, "DropWornOut", 13, job.Args[0]),
        ["R019+0x0A7C"] = (c, job) => Slot(c, job, "EndAttributeModifier", 13, job.Args[0]),
        ["R019+0x0AAC"] = (c, job) => Slot(c, job, "LowerAttributes", 13, job.Args[0], job.Args[1], job.Args[2]),
        ["R019+0x0B00"] = (c, job) => Slot(c, job, "EndSkillModifier", 13, job.Args[0]),
        ["R019+0x0B44"] = (c, job) => Slot(c, job, "LowerSkills", 13, job.Args[0], job.Args[1], job.Args[2]),
        ["R019+0x0B9E"] = (c, job) => Slot(c, job, "ReReady", 13, job.Args[0]),
        ["R019+0x0BF8"] = (c, job) => Slot(c, job, "EndHold", 13, job.Args[0]),
        ["R019+0x0C28"] = (c, job) => Slot(c, job, "Hold", 13, job.Args[0], job.Args[1]),
        ["R019+0x0DFE"] = (c, job) => Slot(c, job, "UseUpPotion", 13, job.Args[0], job.Args[1], job.Args[2]),
        ["R019+0x0C80"] = (c, job) =>
        {
            if (job.Args[0] is < 0 or >= Combatant.PartySlots)
            {
                // its one caller is the orders menu of the active member; the pack it reads is 2E38:0x3E + 0x180 i (R019+0x0CC8), the members' layout
                throw new SkipJob("not a party member: the throw list is the active member's orders menu, read from the members' pack layout");
            }
            var lines = c.Tactics.Effects.ThrowList(job.Args[0]);
            c.Outputs[0x97DD] = "throw entry state";
            c.Actual[0x97DD] = (byte)(lines.Count != 0 ? 1 : 2);
            if (((ushort)c.Battle[job.Args[0]].Action & 2) != 2)
            {
                for (var k = 0; k < lines.Count; k++)
                {
                    var at = 0x9A47 + 0x16 * k;
                    var nameAt = c.Input.Ds[0x114E + 2 * lines[k].Potion] | c.Input.Ds[0x114E + 2 * lines[k].Potion + 1] << 8;
                    foreach (var (offset, value, name) in new[] { (0, nameAt & 0xFF, "name"), (1, nameAt >> 8, "name high"), (9, lines[k].Potion, "potion"), (8, lines[k].Quality, "quality"), (7, lines[k].Count, "count") })
                    {
                        c.Outputs[at + offset] = $"throw line {k} {name}";
                        c.Actual[at + offset] = (byte)value;
                    }
                }
            }
            return null;
        },
        ["R014+0x469E"] = (c, job) =>
        {
            if (job.Args[1] is < 0 or >= 13 || job.Args[0] < 0x8000 || job.Args[0] > 0xFFF0)
            {
                throw new SkipJob("not a spot in the memory above the stack, or not a combatant");
            }
            var spot = c.Input.Ds.AsSpan(job.Args[0], 5);
            return c.Tactics.Effects.Inside((short)(spot[0] | spot[1] << 8), (short)(spot[2] | spot[3] << 8), spot[4], job.Args[1]) ? 1 : 0;
        },
        ["R014+0xB0D8"] = (c, job) => Member13(c, job, () =>
        {
            var left = Attacks.UseAmmunition(c.Battle, job.Args[0], k => Call(c.Tactics, "RanOutOfAmmunition", k));
            if (left == -1)
            {
                // the caller's part: the action state 0x0D (DS:CA64) and the AI reset are set by the missile action after the call, by the original inside it
                c.Battle[job.Args[0]].State = (ActionState)0x0D;
            }
            return left;
        }),
        ["R014+0xB18E"] = (c, job) => Member13(c, job, () => Attacks.Carries(c.Battle, job.Args[0], (byte)job.Args[1], (byte)job.Args[2]) ? 1 : 0),
        ["R014+0xB244"] = (c, job) => Member13(c, job, () => Attacks.ConsumeItem(c.Battle, job.Args[0], (byte)job.Args[1], (byte)job.Args[2])),
        ["R014+0x116E"] = (c, _) => Plain(c, "CombatActions"),
        ["R014+0x1F00"] = (c, job) => Member13(c, job, () => 0) is null ? null : Plain1(c, "SpellAction", job.Args[0]),
        ["R014+0xF67A"] = (c, _) => Plain(c, "FollowMovedTargets"),
        ["R014+0xFC50"] = (c, _) => Plain(c, "FightBack"),
        ["R014+0x10146"] = (c, _) => Plain(c, "Schedule"),
        ["R014+0xED10"] = (c, _) => Plain(c, "AdvanceMissiles"),
        ["R014+0x4188"] = (c, job) =>
        {
            if (job.Args[2] is < 0 or >= 8)
            {
                // every caller (R014+0x00F2, 0xD932, 0xD9D3, 0xDA85, R024+0x0575) pushes a zero-extended byte of the level data: never 0xFFFF; 8..255
                // leave the formation offsets uninitialised (the documented DIVERGENCE of PlaceParty)
                throw new SkipJob("a direction outside 0..7: the callers push a byte, and past 7 the original's formation offsets are uninitialised");
            }
            c.Tactics.PlaceParty((short)job.Args[0], (short)job.Args[1], (short)job.Args[2]);
            return null;
        },
        ["R014+0xF88E"] = (c, job) => Member13(c, job, () => (int)Call(c.Tactics, "MoveToFreeSlot", job.Args[0])!),
        ["R014+0xF9D8"] = (c, job) =>
        {
            Member13(c, job, () => 0);
            Call(c.Tactics, "BackToMelee", job.Args[0]);
            return null;
        },
        ["R014+0x47AE"] = (c, job) =>
        {
            if (job.Args[0] is < 0 or >= 13 || job.Args[1] is < 0 or >= 13)
            {
                throw new SkipJob("not combatants");
            }
            return (bool)Call(c.Tactics, "HostilePair", job.Args[0], job.Args[1])! ? 1 : 0;
        },
        ["R014+0xAED0"] = (c, job) =>
        {
            if (job.Args[0] is < 0 or >= 13)
            {
                throw new SkipJob("not a combatant");
            }
            Attacks.ReadyWeapon(c.Battle, job.Args[0], (short)job.Args[1], job.Args[2], code => c.Tactics.SlotWeapons[c.Battle[job.Args[0]].AnimationSlot] = code);
            return null;
        },
        ["R014+0x0694"] = (c, _) =>
        {
            Call(c.Tactics, "AfterBattle");
            return null;
        },
    };

    private static int? Plain1(Context c, string method, int arg)
    {
        Call(c.Tactics, method, arg);
        return null;
    }

    private static int? Plain(Context c, string method)
    {
        Call(c.Tactics, method);
        return null;
    }

    private static int Party(int who) => who is < 0 or >= 13 ? throw new SkipJob("not a combatant") : who;

    private static int? Member13(Context c, CallJob job, Func<int> call)
    {
        if (job.Args[0] is < 0 or >= 13)
        {
            throw new SkipJob("not a combatant");
        }
        return call();
    }

    /// <summary>Calls a method of the effect field (public or private) with word arguments converted to its parameter types (a word as the original pushes it).</summary>
    private static void Effect(Context c, string name, params int[] args)
    {
        var field = c.Tactics.Effects;
        var method = typeof(EffectField).GetMethod(name, Any)!;
        var converted = method.GetParameters().Select((p, k) => ConvertWord(args[k], p.ParameterType)).ToArray();
        try
        {
            method.Invoke(field, converted);
        }
        catch (System.Reflection.TargetInvocationException e) when (e.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
        }
    }

    private static object ConvertWord(int word, Type type) =>
        type == typeof(int) ? (object)(int)(short)word : type == typeof(byte) ? (byte)word : type == typeof(sbyte) ? (sbyte)word : type == typeof(short) ? (short)word : type == typeof(ushort) ? (ushort)word : type == typeof(bool) ? word != 0 : throw new NotSupportedException(type.Name);

    /// <summary>A call of an effect function on a slot or combatant: the index must be inside the original's tables.</summary>
    private static int? Slot(Context c, CallJob job, string name, int limit, params int[] args)
    {
        if (args[0] < 0 || args[0] >= limit)
        {
            throw new SkipJob($"index {args[0]} past the {limit} entries of the original's table");
        }
        var count = name switch
        {
            "RemoveCloud" or "CloudActs" => c.Tactics.Effects.CloudCount,
            "RemoveArea" => c.Tactics.Effects.AreaCount,
            "RemoveMarker" or "MarkerActs" => c.Tactics.Effects.MarkerCount,
            _ => int.MaxValue,
        };
        if (args[0] >= count)
        {
            throw new SkipJob($"slot {args[0]} past the list's count {count}");
        }
        Effect(c, name, args);
        return null;
    }

    /// <summary>The per-member place tests: the original's tables of the party members (door, stairs, chest, exit zone) hold five entries, a call for an enemy writes past them.</summary>
    private static int? Member(Context c, CallJob job, string method)
    {
        if (job.Args[0] is < 0 or >= Combatant.PartySlots)
        {
            throw new SkipJob("an enemy: the party members' place tables have five entries");
        }
        Call(c.Tactics, method, job.Args[0]);
        return null;
    }

    /// <summary>A door record without a side flag leaves the direction of door_set uninitialised in the original (R023+0x04AC): a path search opens every door, so any such record in the level makes the call undefined.</summary>
    /// <summary>Mutated states the game never has (each found by a difference that went away without the one change; see local/batch/battleleads).</summary>
    private static void CheckReachable(CallJob job, MemoryPair input, byte[] captured)
    {
        var ds = input.Ds;
        // the whole tick from a clock moved far from the captured one (battle_fuzz's "times" scenario sets the clock and the battle's scalar
        // timers only): the per-combatant clocks, the effects' and the next tick's stay behind, which a battle never has (its clock rises one
        // or a few ticks a pass). The 108 tick jobs that differed so all agree with the same changes and the clock one tick on.
        var moved = (ushort)((ds[0x07D4] | ds[0x07D5] << 8) - (captured[0x07D4] | captured[0x07D5] << 8));
        if (job.Function == "R014+0x0F58" && moved is > 64 and < 0xFFC0)
        {
            throw new SkipJob($"the clock moved {(short)moved} ticks from the captured state with the per-combatant and effect timers left behind");
        }
        // a weapon missile (sprite < 6) at a spot (target −1) with its done byte 0: missile_launch leaves done 0 for a weapon missile only when the
        // shooter's exchange (DS:CA57) is a hit (R018+0x0212..0x024D), which the attack roll R014+0xA592 sets against a target; the callers that take
        // a missile user's target away also end its exchange or action. On arrival the original then marks DS:D4F2[−1] (= DS:D4F1) and applies damage to
        // record −1 (R014+0xEDB4).
        if (job.Function is "R014+0x116E" or "R014+0xED10" or "R014+0x0F58")
        {
            for (var m = 0; m < Math.Min((int)ds[TacticsOverlay.MissileCount], TacticsOverlay.MaxMissiles); m++)
            {
                var at = TacticsOverlay.MissilesAt + TacticsOverlay.MissileSize * m;
                if (ds[at + 0x10] == 0xFF && (ds[at + 0x14] & 0x7F) < 6 && ds[at + 0x11] == 0)
                {
                    throw new SkipJob($"missile {m}: a weapon missile at a spot that counts as a hit (done 0), which missile_launch makes only for a hit rolled against a target");
                }
            }
        }
        // DS:E13C is set only around one collision test (R014+0xF5D5..0xF5E3, the far call 1462:000A), never at a pass head or a function's entry
        if (ds[0xE13C] != 0)
        {
            throw new SkipJob("DS:E13C (walls ignored) set at the entry: the original sets it only around one collision test (R014+0xF5D5..0xF5E3)");
        }
        for (var k = 0; k < Combatant.Count; k++)
        {
            var record = ImageOverlay.Combatants + ImageOverlay.Record * k;
            // the battle code writes the status bytes 0, 1, 2, 3 and 5 only (R014+0x09EA, 0x0AF1, 0xAE6E, 0xAE91, R025+0x0461 ...)
            if (ds[record + 0x14] is not (0 or 1 or 2 or 3 or 5))
            {
                throw new SkipJob($"status {ds[record + 0x14]} of combatant {k}: the battle code writes 0, 1, 2, 3 and 5 only");
            }
            // an enemy's target is a member (the enemies' choice, R018+0x0706): the original indexes its five seen flags with it
            if (k >= Combatant.PartySlots && ds[record + 0x14] == 1 && ds[record + 0x4A] is >= Combatant.PartySlots and < Combatant.Count)
            {
                throw new SkipJob($"enemy {k} targets enemy {ds[record + 0x4A]}: the enemies choose members only");
            }
            // the watched facings' count (DS:D5C6) is written by enemies_place only, always 7 (R025+0x0540)
            if (k >= Combatant.PartySlots && ds[TacticsOverlay.WatchedCounts + k] is not (0 or 7))
            {
                throw new SkipJob($"watched facings count {ds[TacticsOverlay.WatchedCounts + k]} of enemy {k}: R025+0x0540 writes 7 only");
            }
        }
        // a whole record copied over another slot that is (nearly) another combatant's: one character twice, with the parallel tables of the slot it replaced
        foreach (var op in job.Ops)
        {
            if (op.Where != "ds" || op.Hex.Length != 2 * ImageOverlay.Record || (op.Offset - ImageOverlay.Combatants) % ImageOverlay.Record != 0)
            {
                continue;
            }
            var b = (op.Offset - ImageOverlay.Combatants) / ImageOverlay.Record;
            if (b is < 0 or >= Combatant.Count || ds[op.Offset + 0x14] == 0)
            {
                continue;
            }
            for (var k = 0; k < Combatant.Count; k++)
            {
                var other = ImageOverlay.Combatants + ImageOverlay.Record * k;
                if (k != b && ds[other + 0x14] != 0 && Enumerable.Range(0, ImageOverlay.Record).Count(j => ds[op.Offset + j] != ds[other + j]) <= 8)
                {
                    throw new SkipJob($"record {b} is a copy of record {k}: one combatant twice, with the tables of the slot it replaced");
                }
            }
        }
    }

    private static void CheckDoors(TacticalBattle t)
    {
        var image = t.Level.Image;
        for (var k = 0; k < Math.Min((int)image[0x1F52], 40); k++)
        {
            var at = 0x1F54 + 14 * k;
            if ((image[at] | image[at + 1] << 8) > 0 && SideFlags(image.AsSpan(at, 14)) != 1)
            {
                throw new SkipJob("a door record without exactly one side flag: the original's direction is uninitialised (none) or its own choice (several)");
            }
        }
    }

    /// <summary>How many of the four side flags (0x80 and 0x40 of the record's bytes +8 and +9) a door record has: a real door has one.</summary>
    private static int SideFlags(ReadOnlySpan<byte> r) => ((r[8] & 0x80) != 0 ? 1 : 0) + ((r[8] & 0x40) != 0 ? 1 : 0) + ((r[9] & 0x80) != 0 ? 1 : 0) + ((r[9] & 0x40) != 0 ? 1 : 0);

    private static void CheckDoor(Context c, int n)
    {
        if (n >= c.Tactics.Level.Image[0x1F52])
        {
            throw new SkipJob("a door past the level's door count");
        }
    }

    /// <summary>A battle of the fight's request, started the usual way from the tester's saved game (its party; the generator and the battlefield are the original's for that request); reused for all jobs of the fight.</summary>
    private readonly Dictionary<string, TacticalBattle> _natural = [];

    private static TacticalBattle StartFromSave(GameFiles files, BattleRequest request)
    {
        var folder = Path.Combine(Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR")!, "SAVES");
        var sav = File.ReadAllBytes(Path.Combine(folder, "DKSAVE0.SAV"));
        var bsv = File.Exists(Path.Combine(folder, "DKSAVE0.BSV")) ? File.ReadAllBytes(Path.Combine(folder, "DKSAVE0.BSV")) : [];
        var world = WorldData.Load(files);
        var state = Darklands.Persistence.OriginalSaves.Read(sav, bsv, "dksave0.sav", world).State;
        var ui = new GameUi(new BattleScreenTests.SilentHost(), new GameAssets(files));
        var screens = new BattleScreens(ui, () => state);
        var screen = new BattleScreen(ui, new BattleAssets(files), state, screens);
        screen.Start(request);
        return screen.Tactics!;
    }

    private TacticalBattle Natural(CallJobs jobs, string fight)
    {
        // a new battle for every job (about 15 ms): nothing a job left behind in the rewrite's own state, outside what the overlay sets, reaches the next.
        // DARKLANDS_BATTLE_CALLS_REUSE=1 keeps one battle per fight (faster, but order-dependent: the path finder's scratch, the doors opened for a search ...)
        if (Environment.GetEnvironmentVariable("DARKLANDS_BATTLE_CALLS_REUSE") is null)
        {
            _natural.Remove(fight);
        }
        if (!_natural.TryGetValue(fight, out var tactics))
        {
            var w = jobs.Fights[fight].EnumerateArray().Select(e => e.GetInt32()).ToArray();
            var request = new BattleRequest(w[1], w[2], [(w[3], w[4], w[5]), (w[6], w[7], w[8]), (w[9], w[10], w[11])], w[0]);
            tactics = StartFromSave(GameFolder.Files!, request);
            _natural[fight] = tactics;
        }
        return tactics;
    }

    /// <summary>How far the rewrite's own start of a fight is from the original's state at an early pass (DARKLANDS_BATTLE_CALLS_PROBE=1): the differing modelled bytes by name.</summary>
    [Fact]
    public void TheStartOfTheFightStandsAsTheOriginalHadIt()
    {
        if (GameFolder.Files is null || CallJobs.Load() is not { } jobs || Environment.GetEnvironmentVariable("DARKLANDS_BATTLE_CALLS_PROBE") is null)
        {
            return;
        }
        foreach (var (name, state) in jobs.States)
        {
            if (jobs.Fights[state.Fight].ValueKind != JsonValueKind.Array)
            {
                continue;
            }
            var tactics = Natural(jobs, state.Fight);
            var image = new MemoryPair((byte[])state.Ds.Clone(), (byte[])state.Far.Clone());
            var mine = image.Clone();
            ImageOverlay.Extract(tactics.Battle, mine);
            TacticsOverlay.Extract(tactics, mine);
            var all = ImageOverlay.Differences(image, mine, null, 100000);
            var groups = all.GroupBy(l => l.Split('(')[0].Split('[')[0].Trim()).Select(g => $"{g.Key} x{g.Count()}");
            output.WriteLine($"{name}: {all.Count} differing modelled bytes: {string.Join(", ", groups)}");
            for (var slot = 0; slot < tactics.Slots.Length; slot++)
            {
                var sx = Convert.ToHexString(tactics.Slots[slot].StepX.Select(v => (byte)v).ToArray());
                var sy = Convert.ToHexString(tactics.Slots[slot].StepY.Select(v => (byte)v).ToArray());
                output.WriteLine($"  slot {slot}: StepX {sx} / DS {Convert.ToHexString(state.Ds, 0xC681 + 10 * slot, 10)}; StepY {sy} / DS {Convert.ToHexString(state.Ds, 0xC6F4 + 10 * slot, 10)}; "
                    + $"half {tactics.Slots[slot].HalfWidth},{tactics.Slots[slot].HalfHeight} / DS {state.Ds[0x1182 + slot]},{state.Ds[0x118E + slot]}; push {tactics.Slots[slot].CanBePushed} / DS {state.Ds[0xC799 + slot]}");
            }
        }
    }

    [Fact]
    public void TheRewriteMakesTheCallsAsTheOriginalDid()
    {
        if (GameFolder.Files is null || CallJobs.Load() is not { } jobs)
        {
            return;
        }
        var thread = Environment.CurrentManagedThreadId;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var only = Environment.GetEnvironmentVariable("DARKLANDS_BATTLE_CALLS_ONLY");
        var results = new StringBuilder();
        var excluded = new StringBuilder();
        var per = new SortedDictionary<string, (int Jobs, int Agree, int Skipped)>();
        var firsts = new Dictionary<string, string>();
        foreach (var job in jobs.Jobs)
        {
            if (!Adapters.TryGetValue(job.Function, out var adapter) || (only is not null && !job.Id.StartsWith(only, StringComparison.Ordinal)))
            {
                continue;
            }
            var state = jobs.States[job.State];
            if (jobs.Fights[state.Fight].ValueKind != JsonValueKind.Array)
            {
                continue;
            }
            var input = MemoryPair.Input(state, job);
            var tactics = Natural(jobs, state.Fight);
            var random = (MscRandom)tactics.Battle.Random;
            var draws = new List<uint>();
            MscRandom.BeforeDraw = () =>
            {
                if (Environment.CurrentManagedThreadId == thread)
                {
                    draws.Add(random.State);
                }
            };
            random.Seed(BitConverter.ToUInt32(input.Ds, 0x7B20));
            ImageOverlay.Apply(tactics.Battle, input);
            var context = new Context { Tactics = tactics, Input = input };
            string verdict;
            try
            {
                TacticsOverlay.Apply(tactics, input);
                if (only is not null)
                {
                    output.WriteLine($"{job.Id}: far doors {input.Far[0x1F52]}, level doors {tactics.Level.Image[0x1F52]}, same array {ReferenceEquals(tactics.Level, tactics.Map.Level)}, ops {job.Ops.Count}");
                }
                CheckDoors(tactics);
                CheckReachable(job, input, state.Ds);
                var value = adapter(context, job);
                MscRandom.BeforeDraw = null;
                var actual = input.Clone();
                ImageOverlay.Extract(tactics.Battle, actual);
                TacticsOverlay.Extract(tactics, actual);
                foreach (var (at, b) in context.Actual)
                {
                    actual.Ds[at] = b;
                }
                foreach (var (at, b) in context.ActualFar)
                {
                    actual.Far[at] = b;
                }
                var expected = input.Expected(job.Orig);
                var diffs = ImageOverlay.Differences(expected, actual, input, int.TryParse(Environment.GetEnvironmentVariable("DARKLANDS_BATTLE_CALLS_DIFFS"), out var most) ? most : 8, context.Outputs, context.FarOutputs);
                if (value is { } v && (ushort)v != (ushort)job.Orig.Ax)
                {
                    diffs.Insert(0, $"returns: original {(ushort)job.Orig.Ax:X4}, rewrite {(ushort)v:X4}");
                }
                if (random.State != job.Orig.Random)
                {
                    diffs.Add($"random state: original {job.Orig.Random:X8}, rewrite {random.State:X8} after {draws.Count} draws (original {job.Orig.Draws.Count})");
                }
                else if (!draws.SequenceEqual(job.Orig.Draws))
                {
                    diffs.Add("random draws differ");
                }
                verdict = diffs.Count == 0 ? "agree" : "DIFFER " + string.Join(" | ", diffs);
            }
            catch (SkipJob skip)
            {
                MscRandom.BeforeDraw = null;
                verdict = "SKIP " + skip.Message;
            }
            catch (Exception e)
            {
                MscRandom.BeforeDraw = null;
                verdict = "EXCEPTION " + e.GetType().Name + ": " + e.Message.Split('\n')[0] + " at " + string.Join(" < ", (e.StackTrace ?? "").Split('\n').Take(2).Select(l => l.Trim().Split(" in ")[0]));
                if (Environment.GetEnvironmentVariable("DARKLANDS_BATTLE_CALLS_ONLY") is not null)
                {
                    verdict += " FULL " + string.Join(" // ", e.ToString().Split('\n').Select(l => l.Trim()));
                }
            }
            if (job.Gaps.Count != 0 && verdict != "agree")
            {
                verdict = "GAP " + string.Join(", ", job.Gaps) + ": " + verdict;
            }
            results.AppendLine($"{job.Id}\t{verdict}");
            per.TryGetValue(job.Function, out var counts);
            var agree = verdict == "agree";
            var gap = verdict.StartsWith("GAP ", StringComparison.Ordinal) || verdict.StartsWith("SKIP ", StringComparison.Ordinal);
            per[job.Function] = (counts.Jobs + 1, counts.Agree + (agree ? 1 : 0), counts.Skipped + (gap ? 1 : 0));
            if (!agree)
            {
                excluded.AppendLine(job.Id);
                firsts.TryAdd(job.Function, $"{job.Id}: {verdict}");
            }
        }
        var folder = Environment.GetEnvironmentVariable("DARKLANDS_BATTLE_CALLS_OUT") ?? Path.GetDirectoryName(Environment.GetEnvironmentVariable("DARKLANDS_BATTLE_CALLS"))!;
        File.WriteAllText(Path.Combine(folder, "results.tsv"), results.ToString());
        File.WriteAllText(Path.Combine(folder, "excluded.txt"), excluded.ToString());
        output.WriteLine($"{jobs.Jobs.Count} jobs in the file, {clock.Elapsed.TotalSeconds:F1} s");
        foreach (var (function, c) in per)
        {
            output.WriteLine($"{function}: {c.Jobs} jobs, {c.Agree} agree, {c.Jobs - c.Agree - c.Skipped} differ, {c.Skipped} not judged (a state the rewrite does not carry, or the game never has)"
                + (firsts.TryGetValue(function, out var first) ? "; first: " + first : ""));
        }
    }
}
