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

        public required BattleScreen Screen { get; init; }

        public required CallHost Host { get; init; }

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

    /// <summary>Calls a private method of the battle screen by name (the exception the method throws, not the reflection's).</summary>
    internal static object? CallScreen(BattleScreen screen, string name, params object?[] args)
    {
        try
        {
            return typeof(BattleScreen).GetMethod(name, Any)!.Invoke(screen, args);
        }
        catch (System.Reflection.TargetInvocationException e) when (e.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            throw;
        }
    }

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

    /// <summary>
    /// leave_level (R023+0x17D2) adds an encounter record for the group left on the level: the record it adds holds the three kinds of the slots 4..6 whatever the
    /// slot holds: for a slot with no enemy the original writes a local it never set (the stack's leftovers; the rewrite has -1 for the first, 0 for the others),
    /// unseen as the count is 0: the original's byte is taken. Used by the direct call and by the stairs action that makes it (R014+0x2170, case 2).
    /// </summary>
    private static void AdoptUnsetBytesOfTheNewRecord(Context c, CallJob job, int floor, int before)
    {
        // the encounter record it adds holds the three kinds of the slots 4..6 whatever the slot holds: for a slot with no enemy the original writes a local
        // it never set (the stack's leftovers; the rewrite has -1 for the first, 0 for the others), unseen as the count is 0: the original's byte is taken
        var list = c.Tactics.Activations[floor];
        if (list.Count == before + 1)
        {
            var group = list[^1];
            // and the reinforcement part (+0x14..+0x1D) of a record that has none (+0x07 = 0): the original leaves the bytes the slot held, the rewrite's
            // new group has its defaults; nothing reads them (the reinforcements arrive only for a group that has the record)
            var recordAt = TacticsOverlay.ActivationsAt + TacticsOverlay.ActivationSize * before;
            int Byte(int o) => job.Orig.FarChanged.TryGetValue((recordAt + o).ToString(), out var v) ? v : c.Input.Far[recordAt + o];
            if (group.HasReinforcements == 0)
            {
                group.ReinforcementKind = (sbyte)Byte(0x14);
                group.Byte15 = (sbyte)Byte(0x15);
                group.ReinforcementX = (short)(Byte(0x16) | Byte(0x17) << 8);
                group.ReinforcementY = (short)(Byte(0x18) | Byte(0x19) << 8);
                group.ReinforcementBatch = (sbyte)Byte(0x1A);
                group.ReinforcementPool = (sbyte)Byte(0x1B);
                group.ReinforcementFacing = (sbyte)Byte(0x1C);
                group.Byte1D = (sbyte)Byte(0x1D);
            }
            for (var k = 0; k < 3; k++)
            {
                var kindAt = TacticsOverlay.ActivationsAt + TacticsOverlay.ActivationSize * before + 8 + k;
                if (group.Counts[k] == 0)
                {
                    group.Kinds[k] = (sbyte)(job.Orig.FarChanged.TryGetValue(kindAt.ToString(), out var kind) ? kind : c.Input.Far[kindAt]);
                }
            }
        }
    }

    /// <summary>
    /// Every writer of the leader DS:907B stores a party slot: the new-leader searches loop over the five slots (R014+0x08FE 0x0AAA, 0x0D0C; R016+0x09BC;
    /// R029+0x255A; R030+0x0C44; R031+0x10AE, 0x264C) and the battle starts with 0 (R001+0x00A9). A fuzz value past the thirteen records (0xFF in the old rounds)
    /// makes the original read the record before the first, which the rewrite's array does not have: the jobs that end in that exception are not judged
    /// (those that agree with such a leader stay judged).
    /// </summary>
    private static T UnlessLeaderIsNoCombatant<T>(Context c, Func<T> call)
    {
        try
        {
            return call();
        }
        catch (IndexOutOfRangeException) when (c.Input.Ds[0x907B] >= Combatant.Count)
        {
            throw new SkipJob($"leader {c.Input.Ds[0x907B]} is not a combatant: only party slots are ever stored in DS:907B");
        }
    }

    /// <summary>The calls: original function to what the rewrite does for it, returning the value the original leaves in AX (null: none compared).</summary>
    private static readonly Dictionary<string, Func<Context, CallJob, int?>> Adapters = new()
    {
        ["R014+0x08FE"] = (c, job) =>
        {
            var switches = c.Tactics.LevelSwitches;
            var over = UnlessLeaderIsNoCombatant(c, () => BattleEnd.IsOver(c.Battle, c.Tactics));
            if (!over && c.Tactics.LevelSwitches != switches && Environment.GetEnvironmentVariable("B2L58_DRAW") == "1")
            {
                var exp = c.Input.Expected(job.Orig);
                var tv = (IBattleViewport)typeof(TacticalBattle).GetField("_view", Any)!.GetValue(c.Tactics)!;
                var view = (BattlefieldView)typeof(BattleScreen).GetField("_view", Any)!.GetValue(c.Screen)!;
                view.CentreColumn = (short)(exp.Ds[ScreenOverlay.CameraX] | exp.Ds[ScreenOverlay.CameraX + 1] << 8) / 32;
                view.CentreRow = (short)(exp.Ds[ScreenOverlay.CameraY] | exp.Ds[ScreenOverlay.CameraY + 1] << 8) / 16;
                Call(c.Tactics, "TurnTowardTargets");
                c.Tactics.Effects.Drawn(tv);
                Call(c.Tactics, "RemoveArrivedMissiles");
            }
            return over ? 1 : 0;
        },
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
            if (job.Args[2] is > 0 and < 6 || job.Args[0] is < 0 or >= 13 || (job.Args[1] is < -1 or >= 13 && job.Args[1] != 0xFFFF))
            {
                throw new SkipJob("a missile of a combatant at a combatant or a spot; the type is 0 (the weapon) or 6 and more (a spell)");
            }
            if (job.Args[2] != 0 && c.Battle[job.Args[0]].AnimationSlot >= TacticsOverlay.Slots)
            {
                throw new SkipJob("a spell's sprite family is the shooter's animation slot's code: the slot is one of the seven loaded");
            }
            if (job.Args[2] == 0 && c.Battle[job.Args[0]].ReadiedWeapon is < 0x17 or > 0x21)
            {
                throw new SkipJob("a weapon that is no missile weapon: the original's sprite and speed are uninitialised (a documented divergence)");
            }
            c.Tactics.LaunchMissile(job.Args[0], (short)job.Args[1], job.Args[2], job.Args[3], job.Args[4]);
            return null;
        },
        ["R014+0x10672"] = (c, job) =>
        {
            // cloud_blocks_line (shooter, target): a missed cloud box restores the line from four locals the call never set, the words its frame (below the
            // state's stack pointer: the far return, two arguments, the saved bp) lay over the state's stack
            if (job.Args[0] is < 0 or >= Combatant.Count || job.Args[1] is < 0 or >= Combatant.Count)
            {
                throw new SkipJob("a combatant number past the thirteen");
            }
            if (c.Input.Sp0 == 0)
            {
                throw new SkipJob("a jobs file without the state's stack pointer");
            }
            var bp = (c.Input.Sp0 - 0x40 - 4 - 4 - 2) & 0xFFFF;
            short Local(int back) => (short)(c.Input.Ds[bp - back] | c.Input.Ds[bp - back + 1] << 8);
            return c.Tactics.Effects.LineCrossesCloud(job.Args[0], job.Args[1], (Local(6), Local(8), Local(0x0A), Local(0x0C))) ? 1 : 0;
        },
        ["R024+0x08F2"] = (c, job) =>
        {
            // place_around (who, the 14-byte record whose +8/+10 words are the spot and +12 the direction): 1 when a place was found
            var at = job.Args[1];
            var ds = c.Input.Ds;
            if (ds[at + 12] is not (0 or 2 or 4 or 6))
            {
                throw new SkipJob("a direction that is no even number below 8: the original reads the offsets of its stack garbage (the stairs records hold 0, 2, 4 or 6)");
            }
            var spot = new MapPoint((short)(ds[at + 8] | ds[at + 9] << 8), (short)(ds[at + 10] | ds[at + 11] << 8));
            return (bool)Call(c.Tactics, "PlaceAround", job.Args[0], spot, (int)ds[at + 12])! ? 1 : 0;
        },
        ["R023+0x17D2"] = (c, job) =>
        {
            // leave_level (who, the stairs record the caller copied to its frame): the call is made with the record at the DS offset given
            if (job.Args[0] is < 0 or >= Combatant.PartySlots)
            {
                throw new SkipJob("a member of the party leaves");
            }
            var at = job.Args[1];
            var before = c.Tactics.Activations[c.Battle.FloorShown].Count;
            Call(c.Tactics, "LeaveLevel", job.Args[0], c.Input.Ds.AsSpan(at, 14).ToArray());
            AdoptUnsetBytesOfTheNewRecord(c, job, c.Battle.FloorShown, before);
            return null;
        },
        ["R022+0x03E4"] = (c, job) =>
        {
            // do_surrender: the question "Do you really want to surrender?" is answered by the job's first argument (the original's stub); only in real combat
            if (job.Args[0] == 1 && c.Battle.RealCombat)
            {
                c.Tactics.Surrender();
            }
            return null;
        },
        ["R014+0x33A2"] = (c, job) =>
        {
            // cursor_normal: the pointer's shape from its place (the zones of the view's edges); the original keeps the handle of the shape in use (DS:A68F),
            // takes the arrow's from DS:D72B + 2 * arrow (up, up-right, right, down-right, down, down-left, left, up-left) when it is another one, and at no
            // edge makes the normal pointer (R014+0x4780), whose handle is not modelled: AX is the new arrow's handle when the shape changed, not compared otherwise
            var ds = c.Input.Ds;
            int x = (short)(ds[ScreenOverlay.MouseX] | ds[ScreenOverlay.MouseX + 1] << 8), y = (short)(ds[ScreenOverlay.MouseY] | ds[ScreenOverlay.MouseY + 1] << 8);
            var arrow = x >= 60 ? (int)typeof(BattleScreen).GetMethod("EdgeArrow", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, [x, y])! : -1;
            var current = ds[0xA68F] | ds[0xA690] << 8;
            if (arrow < 0)
            {
                var became = job.Orig.Changed.TryGetValue("42639", out var lo) ? lo | (job.Orig.Changed.TryGetValue("42640", out var hi) ? hi : ds[0xA690]) << 8 : current;
                for (var a = 0; a < 8; a++)
                {
                    if (became != current && became == (ds[0xD72B + 2 * a] | ds[0xD72C + 2 * a] << 8))
                    {
                        return -1;
                    }
                }
                return null;
            }
            var handle = ds[0xD72B + 2 * arrow] | ds[0xD72C + 2 * arrow] << 8;
            return handle == current ? null : handle;
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
            // the renderer's view (DS:C26C / DS:C293) lays out the clouds and areas; DS:E0AB set (a full redraw since the last frame) lays the
            // areas out again (R014+0x682F), which the rewrite does on a changed view
            ScreenOverlay.Camera(c.Screen, c.Input);
            TacticsOverlay.Set(c.Tactics.Effects, "_lastCamera", c.Input.Ds[0xE0AB] != 0 ? (int.MinValue, 0) : (c.Screen.CameraX, c.Screen.CameraY));
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
            var set = CallersSet(c, job);
            var shooter = c.Battle[job.Args[0]];
            if (shooter.State == ActionState.Swinging && shooter.NextFrameAt <= c.Battle.Clock && shooter.Frame + 1 == shooter.SequenceLength - 1
                && shooter.ReadiedWeapon is < 0x17 or > 0x21)
            {
                throw new SkipJob("a shot without a missile weapon readied: the missile order needs one (the original's sprite and speed are then a stack word's)");
            }
            Call(c.Tactics, "MissileAction", job.Args[0], set);
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
            ThrowLineOutputs(c, c.Tactics.Effects.ThrowList(job.Args[0]), job.Args[0], true);
            return null;
        },
        ["R016+0x0000"] = (c, job) =>
        {
            // combat_keys with the key the call was given (args[0], scan << 8 | character): the screen's key handler. The keys that open another screen
            // (the sheets F1..F5, Enemy Info, Surrender, Throw's menu, the Alt keys of the menu bar) run a loop of their own that the original's call
            // does not get its keys for.
            var key = job.Args[0] & 0xFFFF;
            if (c.Input.Ds[0x907B] >= Combatant.PartySlots)
            {
                // every writer of the leader DS:907B stores a party slot (the new-leader search loops over the five party slots; the battle starts with one):
                // a fuzz value of 5 or more names an enemy record, whose floor and stairs the original's U key then uses (tables of five entries read past)
                throw new SkipJob($"leader {c.Input.Ds[0x907B]} is not a party slot: only party slots are ever stored in DS:907B");
            }
            if (c.Input.Ds[0x07D3] != (c.Input.Ds[0xA695] != 0 ? 1 : 0))
            {
                // the clock's hold DS:07D3 is a count: the pause sets DS:A695 and adds 1 together (R016+0x0755/0x075A, R014+0xBC8E/0xBC9E, both only
                // when A695 is 0), takes both back together (R016+0x1443/0x144D, called only from R016+0x076D when A695 is not 0), a battle starts with
                // both 0 (R002+0x1912/0x191E); every other holder adds and takes back 1 inside itself (or sets 1 only from 0: the bar root 0000:0000,
                // the pause box root 0000:1080), and none of them calls this. So at a key 07D3 is 1 exactly when A695 is not 0 (every captured state)
                throw new SkipJob($"DS:07D3 = {c.Input.Ds[0x07D3]} with DS:A695 = {c.Input.Ds[0xA695]:X2}: the clock is held exactly while the battle is paused");
            }
            if (key == 0)
            {
                // no key waits: the direction keys held (DS:0880 / DS:0881) give an arrow
                var directions = typeof(Darklands.Presentation.GameUi).GetProperty("Directions")!.GetValue(typeof(BattleScreen).GetField("_ui", Any)!.GetValue(c.Screen))!;
                typeof(Darklands.Presentation.KeyboardDirections).GetProperty("X")!.SetValue(directions, (int)(sbyte)c.Input.Ds[0x880]);
                typeof(Darklands.Presentation.KeyboardDirections).GetProperty("Y")!.SetValue(directions, (int)(sbyte)c.Input.Ds[0x881]);
                CallScreen(c.Screen, "CombatKeys");
                return null;
            }
            if (key is >= 0x3B00 and <= 0x3F00 || char.ToUpperInvariant((char)(key & 0xFF)) is 'E' or 'S' or 'T' || (key & 0xFF) == 0 && key is not (>= 0x4700 and <= 0x5100 or >= 0x5E00 and <= 0x6200))
            {
                throw new SkipJob($"key {key:X4} opens another screen or belongs to the menu bar");
            }
            if (Environment.GetEnvironmentVariable("DARKLANDS_BATTLE_CALLS_ONLY") is not null)
            {
                Console.WriteLine($"key {key:X4}: shown {c.Battle.FloorShown} levels {c.Tactics.LevelCount} party floor {c.Battle.PartyFloor} floors {string.Join(",", Enumerable.Range(0, 5).Select(k => c.Battle[k].Floor))} selected {c.Tactics.Selected} paused {c.Tactics.Paused}");
            }
            if (char.ToUpperInvariant((char)(key & 0xFF)) == 'U' && c.Input.Ds[0xA695] == 0)
            {
                // U outside the pause takes the leader's stairs record (DS:C8B2 when the party is split, DS:C77E when Use Stairs is on): the
                // original reads it without a test of the level's count (R023+0x1618); R014+0xDC14 sets these from a stairs record of the level
                var ds = c.Input.Ds;
                var leader = ds[0x907B];
                var split = ds[0xC623] != ds[0x11CA];
                int n = split ? ds[0xC8B2 + leader] : ds[0x96BD] == 1 ? ds[0xC77E + leader] : 0xFF;
                if (n != 0xFF && n >= c.Input.Far[0x2FB6])
                {
                    throw new SkipJob($"the leader's stairs record {n} past the level's {c.Input.Far[0x2FB6]}: R014+0xDC14 sets it from a stairs record of the level");
                }
            }
            if (key == 0x011B && c.Input.Ds[0xA695] == 1 && (sbyte)c.Input.Ds[OrderOverlay.Selected] == -1 && c.Input.Ds[OrderOverlay.FleeBefore] != 0)
            {
                throw new SkipJob("Esc while paused with nobody selected and DS:D6EA set: the original's pause byte becomes 0x11 (R016+0x13F9), which the rewrite's flag does not hold");
            }
            CallScreen(c.Screen, "Key", key);
            if (key == 0x1C0D && c.Input.Ds[OrderOverlay.MoveMode] == 0)
            {
                OrderOverlay.TakeActiveMemberMinusOne(c, job);
            }
            if (Environment.GetEnvironmentVariable("DARKLANDS_BATTLE_CALLS_ONLY") is not null)
            {
                var levels = TacticsOverlay.Get<IReadOnlyList<BattlefieldLevel>>(c.Tactics, "_levels");
                Console.WriteLine("levels: " + string.Join(" ", levels.Select(l => $"{l.Image[0x300C]}/{l.Image.GetHashCode()}")) + $" current {c.Tactics.Level.Image.GetHashCode()}");
                Console.WriteLine($"after: shown {c.Battle.FloorShown} selected {c.Tactics.Selected}");
            }
            return null;
        },
        ["R014+0x2170"] = (c, job) =>
        {
            if (job.Args[0] is < 0 or >= Combatant.PartySlots)
            {
                throw new SkipJob("a place action is given to a party member");
            }
            if (c.Input.Ds[0xA6C4 + job.Args[0]] == (byte)PlaceAction.Stairs && c.Input.Ds[0xC77E + job.Args[0]] >= c.Input.Far[0x2FB6])
            {
                // the Use Stairs entry (and so this action) is on only on a stairs record of the level, whose index R014+0xDC14 keeps (DS:C77E)
                throw new SkipJob($"the stairs action with stairs record {c.Input.Ds[0xC77E + job.Args[0]]} past the level's {c.Input.Far[0x2FB6]}: R014+0xDC14 sets it from a stairs record of the level");
            }
            if (c.Input.Ds[0xA6C4 + job.Args[0]] == (byte)PlaceAction.TakeLoot && (sbyte)c.Input.Ds[0xC81C + job.Args[0]] >= c.Input.Ds[0xC80E])
            {
                // the Loot Bodies entry is on only next to a pile outside real combat: R014+0xD7A2 (order mode's entry, peaceful only) stores the member's pile
                // DS:C81C+m as a pile of the list or 0xFF, R014+0xEA10 as 0, a pile it makes or minus the recovered count; the list's count DS:C80E is reset only
                // at a battle's start (R002+0x1A7A) and grows (R014+0x2324), so a pile index past it is the last battle's, which the entry overwrites
                throw new SkipJob($"the loot action with member pile {(sbyte)c.Input.Ds[0xC81C + job.Args[0]]} past the list's {c.Input.Ds[0xC80E]} piles: the member's pile is one of the list or negative");
            }
            if (c.Input.Ds[0xA6C4 + job.Args[0]] == (byte)PlaceAction.DissolveLock && c.Input.Ds[ImageOverlay.Combatants + ImageOverlay.Record * job.Args[0] + 0x14] == 0)
            {
                // R014+0x2170 has one caller, combat_actions (R014+0x12B7), whose pass skips a combatant with status 0 (R014+0x11B0..0x11B6). The other actions of an
                // empty member agree and stay judged; the dissolve one uses up a slot of its pack (the original's count 0 wraps to 0xFF), which the rewrite does not
                throw new SkipJob("the dissolve action for an empty member: combat_actions passes over status 0 (R014+0x11B0), the only caller");
            }
            var floor = c.Battle.FloorShown;
            var records = c.Tactics.Activations[floor].Count;
            Call(c.Tactics, "DoPlaceAction", job.Args[0]);
            // the stairs make leave_level's record (R014+0x2170 case 2 -> R023+0x17D2): its unset bytes are the original's
            AdoptUnsetBytesOfTheNewRecord(c, job, floor, records);
            return null;
        },
        ["R014+0xBC0C"] = (c, _) =>
        {
            var member = (sbyte)c.Input.Ds[OrderOverlay.Selected];
            if (member is < 0 or >= Combatant.PartySlots)
            {
                throw new SkipJob("order mode is entered for a party member (DS:A6B2)");
            }
            ScreenOverlay.HotSpots(c.Screen, c.Input);
            c.Tactics.EnterOrderMode(member);
            return null;
        },
        ["R014+0xBCA8"] = (c, job) =>
        {
            c.Tactics.LeaveOrderMode();
            OrderOverlay.TakeActiveMemberMinusOne(c, job);
            PanelRedrawTime(c, job);
            return null;
        },
        ["R014+0x0620"] = (c, _) =>
        {
            Call(c.Tactics, "RepeatActivation", (int)c.Input.Ds[0x9080]);
            return null;
        },
        ["R014+0x3C86"] = (c, job) =>
        {
            if (job.Args[0] is < 0 or >= Combatant.Count)
            {
                throw new SkipJob("not a combatant");
            }
            c.Battle[job.Args[0]].QueueOrder((CombatAction)(ushort)job.Args[1]);
            return null;
        },
        ["R014+0xB576"] = (c, job) =>
        {
            // combat_mouse: args = releases the driver counted, Ctrl held
            if (c.Input.Ds[OrderOverlay.OrderMode] != 0 && (sbyte)c.Input.Ds[OrderOverlay.Selected] < 0)
            {
                throw new SkipJob("order mode with no member selected: the original reads the record before combatant 0");
            }
            ScreenOverlay.Apply(c.Screen, c.Host, c.Input, job.Args[0], job.Args[1] != 0);
            CallScreen(c.Screen, "CombatMouse");
            if (Environment.GetEnvironmentVariable("DARKLANDS_BATTLE_CALLS_ONLY") is not null)
            {
                Console.WriteLine($"DBG after mouse: selected {c.Tactics.Selected} order mode {c.Tactics.InOrderMode} paused {c.Tactics.Paused} flags {string.Join(",", Enumerable.Range(0, 5).Select(k => c.Battle[k].Selected))} cursor {c.Tactics.CursorTarget}");
            }
            if (c.Input.Ds[OrderOverlay.MoveMode] != 0)
            {
                // travelling in a formation the cursor is the group cursor (DS:C987, DS:CA19 / DS:CA2A sprites), which the rewrite keeps no sprite for: not compared
                for (var b = 0; b < 2; b++)
                {
                    c.Actual[OrderOverlay.Cursor + b] = job.Orig.Changed.TryGetValue((OrderOverlay.Cursor + b).ToString(), out var v) ? (byte)v : c.Input.Ds[OrderOverlay.Cursor + b];
                }
            }
            ScreenOverlay.Outputs(c.Screen, c);
            return null;
        },
        ["R014+0x2324"] = (c, job) =>
        {
            if (job.Args[0] is < 0 or >= Combatant.Count)
            {
                throw new SkipJob("not a combatant");
            }
            // its one caller (R014+0x1584) passes the animation set it worked out for the combatant at the head of its pass; the call reads only
            // the set's corpse kind and loot flag (DS:C83F, DS:C803), so another set with the same two is the same call
            var set = c.Tactics.AnimationSet(job.Args[0]);
            if (job.Args[1] is < 0 or >= 11 || c.Tactics.Slots[job.Args[1]].CorpseKind != c.Tactics.Slots[set].CorpseKind
                || c.Tactics.Slots[job.Args[1]].DropsLoot != c.Tactics.Slots[set].DropsLoot)
            {
                throw new SkipJob($"set {job.Args[1]} is not like the combatant's animation set {set}: the caller passes that");
            }
            ScreenOverlay.Camera(c.Screen, c.Input);
            Call(c.Tactics, "Dying", job.Args[0], job.Args[1]);
            return null;
        },
        ["R014+0xEA10"] = (c, _) =>
        {
            LootOverlay.SkipBlocksPastTheFile(c.Input);
            UnlessLeaderIsNoCombatant(c, () => Call(c.Tactics, "GatherLoot"));
            return null;
        },
        ["R025+0x0000"] = (c, job) =>
        {
            // enemies_place(mode, far pointer to the encounter's record, who): the record is one of the level's list (20A5:47EE, 30 bytes each)
            var mode = job.Args[0];
            if (mode is not (0 or 1) || job.Args[3] is < 0 or >= Combatant.PartySlots)
            {
                throw new SkipJob("only the encounter placement (mode 0) and the reinforcement wave (mode 1) of a party member");
            }
            EnemyGroup? group = null;
            if (mode == 0)
            {
                var list = c.Tactics.Activations[c.Battle.FloorShown];
                var k = (job.Args[1] - TacticsOverlay.ActivationsAt) / TacticsOverlay.ActivationSize;
                if ((job.Args[1] - TacticsOverlay.ActivationsAt) % TacticsOverlay.ActivationSize != 0 || k < 0 || k >= list.Count)
                {
                    throw new SkipJob("not a record of the level's encounter list");
                }
                group = list[k];
                if (group.X < 0 || group.Y < 0)
                {
                    // R014+0x0620 places an encounter when a member enters its box: off the map no member does; there the original's collision
                    // test reads the far segment past the level image (other battle data), the rewrite's zeros
                    throw new SkipJob($"encounter {k} lies off the map ({group.X}, {group.Y}): no member reaches it");
                }
            }
            return (bool)Call(c.Tactics, "PlaceEnemies", mode, group, job.Args[3])! ? 1 : 0;
        },
        ["R014+0xDF28"] = (c, _) =>
        {
            var a = c.Input.Ds[0xA6B2];
            if (a >= Combatant.PartySlots)
            {
                throw new SkipJob("the selected member (DS:A6B2) is past the party: the order menu is built for a member");
            }
            if (Environment.GetEnvironmentVariable("DARKLANDS_BATTLE_CALLS_ONLY") is not null)
            {
                var d = c.Battle[a];
                Console.WriteLine($"menu for {a}: real {c.Battle.RealCombat} held {d.Held} party floor {c.Battle.PartyFloor} shown {c.Battle.FloorShown} flags {d.Flags} attack {string.Join(",", c.Tactics.Menus.Attack)}");
            }
            Call(c.Tactics, "BuildOrderMenu", (int)a);
            var menus = c.Tactics.Menus;
            // the label suffixes (DS:06AB + 8, 06BB + 10, 06CD + 10, 06DF + 9, 06F1 + 13, 0727 + 16): the first byte says "" or a suffix
            foreach (var (at, text) in new[] { (0x6B3, menus.DoorSuffix), (0x6C5, menus.StairsSuffix), (0x6D7, menus.ChestSuffix), (0x6E8, menus.PickSuffix), (0x6FE, menus.DissolveSuffix), (0x737, menus.ExitSuffix) })
            {
                c.Outputs[at] = "menu suffix";
                c.Actual[at] = text.Length == 0 ? (byte)0 : (byte)text[0];
            }
            if (c.Battle.RealCombat)
            {
                ThrowLineOutputs(c, menus.ThrowLines, a, false);
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
        ["R014+0x116E"] = (c, _) =>
        {
            // a combatant that dies here leaves a pile, moved into one screen tile by the tiles of the last full redraw (R014+0x2802 reads
            // R136+0x5890): the view's origin as for R014+0x2324
            ScreenOverlay.Camera(c.Screen, c.Input);
            return Plain(c, "CombatActions");
        },
        ["R014+0x1F00"] = (c, job) =>
        {
            Call(c.Tactics, "SpellAction", job.Args[0], CallersSet(c, job));
            return null;
        },
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

    /// <summary>The throw list as R019+0x0C80 leaves it: the entry state (DS:97DD) and, unless the member is mid-throw, the lines (DS:9A47, 0x16 bytes each).</summary>
    private static void ThrowLineOutputs(Context c, IReadOnlyList<(int Potion, int Quality, int Count)> lines, int member, bool entryState)
    {
        if (entryState)
        {
            c.Outputs[0x97DD] = "throw entry state";
            c.Actual[0x97DD] = (byte)(lines.Count != 0 ? 1 : 2);
        }
        if (((ushort)c.Battle[member].Action & 2) == 2)
        {
            return;
        }
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

    /// <summary>
    /// The second argument of the missile and spell actions: the animation set combat_actions works out for the combatant at the head of its
    /// pass (R014+0x11C4) and pushes (R014+0x1288 / 0x12A0). Jobs made with the combatant alone left the original reading a stack word there.
    /// </summary>
    private static int CallersSet(Context c, CallJob job)
    {
        if (job.Args[0] is < 0 or >= 13)
        {
            throw new SkipJob("not a combatant");
        }
        if (job.Args.Length < 2)
        {
            throw new SkipJob("the caller's animation set (the second argument) was not passed: the original read a stack word for it");
        }
        if (job.Args[1] != c.Tactics.AnimationSet(job.Args[0]))
        {
            throw new SkipJob($"set {job.Args[1]} is not the combatant's animation set {c.Tactics.AnimationSet(job.Args[0])}: the caller passes that");
        }
        return job.Args[1];
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

    /// <summary>
    /// The emulated time of the party panel's redraw after show_message: the message's delay_ticks loop (root 0410:0302) runs the machine past
    /// the quiet budget the tester gives a call before its time-based timer (orig_side.BATTLE_QUIET), so the timer callbacks of the emulator's
    /// speed come in the code after it: order_mode_leave's root 0000:1716(0x100) took 6 of them in each of the four "walk split" jobs
    /// (local/batch/stairs/measure.log: DS:07D1 +150 at the delay's return, the clock +150 when running). The rewrite counts the wait's 150
    /// ticks (BattleScreen.ShowMessage); a few more callbacks of the original after a wait are machine speed, taken over (up to 8).
    /// </summary>
    private static void PanelRedrawTime(Context c, CallJob job)
    {
        var input = c.Input.Ds[TacticsOverlay.TimerCallbacksAt];
        if (((c.Tactics.TimerCallbacks - input) & 0xFF) != 0x96 || !job.Orig.Changed.TryGetValue(TacticsOverlay.TimerCallbacksAt.ToString(), out var orig))
        {
            return;
        }
        var extra = (orig - c.Tactics.TimerCallbacks) & 0xFF;
        if (extra is 0 or > 8)
        {
            return;
        }
        c.Tactics.TimerCallbacks = orig;
        if (c.Tactics.PauseByte == 0)
        {
            c.Battle.Clock = (ushort)(c.Battle.Clock + extra);
        }
        c.Notes.Add($"{extra} timer callbacks after the message's wait taken over (the emulated time of root 0000:1716)");
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
        // the place-changed bit (0x08 of a member's flags) is set and cleared within one call: R014+0xDC14 clears it at the end of every member's
        // pass (R014+0xDD17), R017+0x0000 sets it for the five members and clears it again after the rows are drawn (R017+0x001E, 0x004C).
        // Set at the entry it is inert unless one of those ran (the original's flags then have it clear)
        for (var k = 0; k < Combatant.PartySlots; k++)
        {
            var at = TacticsOverlay.FlagsAt + 2 * k;
            if ((ds[at] & 0x08) != 0 && job.Orig.Changed.TryGetValue(at.ToString(), out var after) && (after & 0x08) == 0)
            {
                throw new SkipJob($"flags[{k}] with the place-changed bit (0x08): R014+0xDC14 and R017+0x0000 clear it in the call that sets it");
            }
        }
        // flag bytes the original writes as 0 or 1 only (and its saved games carry as written): DS:D610 (R014+0x0F9C, 0x0FEA), DS:D6F0 (R002+0x18E1,
        // R023+0x18FA, R025+0x0CFE), DS:CA29 (R002+0x18F1, R014+0xD943/0xD9C5/0xDA77), moving DS:A6D3[i] (R014+0x0438, 0x0C96, 0x1228, 0x145B, 0x1720 ...),
        // engaged DS:DA36[i] (R014+0x9A5D, 0xFBF3, 0xFD49). Some readers test == 1, others != 0, so another value splits them; the rewrite keeps bools
        foreach (var (name, at, count) in new[] { ("DS:D610", 0xD610, 1), ("DS:D6F0", 0xD6F0, 1), ("DS:CA29", 0xCA29, 1), ("moving DS:A6D3", 0xA6D3, Combatant.Count), ("engaged DS:DA36", 0xDA36, Combatant.Count) })
        {
            for (var k = 0; k < count; k++)
            {
                if (ds[at + k] > 1)
                {
                    throw new SkipJob($"{name}[{k}] = 0x{ds[at + k]:X2}: the original writes this flag as 0 or 1 only");
                }
            }
        }
        for (var k = 0; k < Combatant.Count; k++)
        {
            var record = ImageOverlay.Combatants + ImageOverlay.Record * k;
            // a member stands on one of the battlefield's levels or has left (0x9D)
            if (k < Combatant.PartySlots && ds[record + 0x14] != 0 && ds[TacticsOverlay.FloorsAt + k] != 0x9D && ds[TacticsOverlay.FloorsAt + k] >= Math.Max((int)ds[0xD661], 1))
            {
                throw new SkipJob($"member {k} on floor {ds[TacticsOverlay.FloorsAt + k]}: the battlefield has {ds[0xD661]} levels");
            }
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
    private readonly Dictionary<string, (TacticalBattle Tactics, BattleScreen Screen, CallHost Host)> _natural = [];

    private static (TacticalBattle Tactics, BattleScreen Screen, CallHost Host) StartFromSave(GameFiles files, BattleRequest request)
    {
        var folder = Path.Combine(Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR")!, "SAVES");
        var sav = File.ReadAllBytes(Path.Combine(folder, "DKSAVE0.SAV"));
        var bsv = File.Exists(Path.Combine(folder, "DKSAVE0.BSV")) ? File.ReadAllBytes(Path.Combine(folder, "DKSAVE0.BSV")) : [];
        var world = WorldData.Load(files);
        var state = Darklands.Persistence.OriginalSaves.Read(sav, bsv, "dksave0.sav", world).State;
        var host = new CallHost();
        var ui = new GameUi(host, new GameAssets(files));
        var screens = new BattleScreens(ui, () => state);
        var screen = new BattleScreen(ui, new BattleAssets(files), state, screens);
        screen.Start(request);
        return (screen.Tactics!, screen, host);
    }

    private (TacticalBattle Tactics, BattleScreen Screen, CallHost Host) Natural(CallJobs jobs, string fight)
    {
        // a new battle for every job (about 15 ms): nothing a job left behind in the rewrite's own state, outside what the overlay sets, reaches the next.
        // DARKLANDS_BATTLE_CALLS_REUSE=1 keeps one battle per fight (faster, but order-dependent: the path finder's scratch, the doors opened for a search ...)
        if (Environment.GetEnvironmentVariable("DARKLANDS_BATTLE_CALLS_REUSE") is null)
        {
            _natural.Remove(fight);
        }
        if (!_natural.TryGetValue(fight, out var started))
        {
            var w = jobs.Fights[fight].EnumerateArray().Select(e => e.GetInt32()).ToArray();
            var request = new BattleRequest(w[1], w[2], [(w[3], w[4], w[5]), (w[6], w[7], w[8]), (w[9], w[10], w[11])], w[0]);
            started = StartFromSave(GameFolder.Files!, request);
            _natural[fight] = started;
        }
        return started;
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
            var tactics = Natural(jobs, state.Fight).Tactics;
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
            var (tactics, screen, host) = Natural(jobs, state.Fight);
            var random = (MscRandom)tactics.Battle.Random;
            var draws = new List<uint>();
            MscRandom.BeforeDraw = () =>
            {
                if (Environment.CurrentManagedThreadId == thread)
                {
                    if (Environment.GetEnvironmentVariable("DARKLANDS_BATTLE_CALLS_DRAWSTACK") is not null)
                    {
                        var frames = new System.Diagnostics.StackTrace(true).GetFrames().Skip(4).Take(3).Select(f => f.GetMethod()?.Name + ":" + f.GetFileLineNumber());
                        Console.WriteLine($"DRAW {draws.Count} {random.State:X8} {string.Join(" < ", frames)}");
                    }
                    draws.Add(random.State);
                }
            };
            random.Seed(BitConverter.ToUInt32(input.Ds, 0x7B20));
            ImageOverlay.Apply(tactics.Battle, input);
            var context = new Context { Tactics = tactics, Screen = screen, Host = host, Input = input };
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
                var switches = tactics.LevelSwitches;
                // a state captured without the loot file (older rounds): the C# store is its own start's, so a call that appends to the file
                // (R014+0xE774: the new block's offset is the file's length) or cuts blocks out of it (R014+0xE7C8) works on another file
                var lootStore = tactics.Field.Store.Loot;
                var fileBefore = lootStore.FileExists;
                if (!input.LootKnown)
                {
                    lootStore.FileExists = false;
                }
                var value = adapter(context, job);
                if (!input.LootKnown)
                {
                    // a file without blocks (no pile holds one: the piles' blocks lie in it, R014+0xE774 / 0xE7C8) is the empty file the rewrite has
                    var blocks = Enumerable.Range(0, Math.Min((int)input.Ds[LootOverlay.PileCount], LootOverlay.MaxPiles))
                        .Any(k => (short)(input.Far[LootOverlay.PilesAt + LootOverlay.PileSize * k] | input.Far[LootOverlay.PilesAt + LootOverlay.PileSize * k + 1] << 8) >= 0
                            && input.Far[LootOverlay.PilesAt + LootOverlay.PileSize * k + 2] != 0);
                    if (lootStore.FileExists && blocks)
                    {
                        throw new SkipJob("the call appends to or cuts the loot file, which this state did not capture (its piles hold blocks)");
                    }
                    lootStore.FileExists = fileBefore;
                }
                if (tactics.LevelSwitches != switches)
                {
                    // level_switch (R024+0x059A) saves the level it leaves (20A5:0000, its encounter list 20A5:47EE through DS:A895) only when
                    // DS:D4E4 is set, and loads the level it shows from its files: what the job changed in a level without that flag, or in a
                    // level the original then reloads, is the files' and not the memory's
                    if (input.Ds[0xD4E4] == 0)
                    {
                        throw new SkipJob("a level switch with DS:D4E4 clear: the original reloads the level it left from its file (the rewrite keeps the levels in memory)");
                    }
                    if (job.Ops.Any(op => op.Where == "floor" && op.Offset < TacticsOverlay.ActivationsAt + 0x300))
                    {
                        throw new SkipJob("a level switch after the job changed the level or its encounter list: the original's copies of the other levels are its files'");
                    }
                }
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
                if (tactics.Battle.FloorShown != (sbyte)input.Ds[0x11CA])
                {
                    // another level is shown now: the original loaded its image from its own copies, the rewrite from its battlefield's (the levels not shown are not carried)
                    Array.Copy(expected.Far, 0, actual.Far, 0, TacticsOverlay.FloorSize);
                    // and its encounter list (20A5:47EE, DS:D6CC records), which level_switch loads from the level's file through DS:A895
                    var records = Math.Max(expected.Ds[TacticsOverlay.ActivationCount], actual.Ds[TacticsOverlay.ActivationCount]);
                    Array.Copy(expected.Far, TacticsOverlay.ActivationsAt, actual.Far, TacticsOverlay.ActivationsAt,
                        Math.Min(TacticsOverlay.ActivationSize * records, actual.Far.Length - TacticsOverlay.ActivationsAt));
                    actual.Ds[TacticsOverlay.ActivationCount] = expected.Ds[TacticsOverlay.ActivationCount];
                }
                {
                    // the missile records past the count are leftovers: missile_remove (R018+0x029C..0x02B3) moves all sixteen records after the
                    // one removed down, and every reader stops at the count (DS:C180)
                    var from = TacticsOverlay.MissilesAt + TacticsOverlay.MissileSize * Math.Min((int)expected.Ds[TacticsOverlay.MissileCount], TacticsOverlay.MaxMissiles);
                    var to = TacticsOverlay.MissilesAt + TacticsOverlay.MissileSize * TacticsOverlay.MaxMissiles;
                    if (expected.Ds[TacticsOverlay.MissileCount] == actual.Ds[TacticsOverlay.MissileCount] && from < to)
                    {
                        Array.Copy(expected.Ds, from, actual.Ds, from, to - from);
                    }
                }
                {
                    // the encounter records past the count are not the level's: every reader stops at DS:D6CC, a level switch leaves the staging
                    // buffer's (DS:A896) leftovers of the last level loaded there, and the draw's scratch lies past the longest lists (20A5:4C62..)
                    var records = Math.Max(expected.Ds[TacticsOverlay.ActivationCount], tactics.LevelSwitches != switches ? 0 : input.Ds[TacticsOverlay.ActivationCount]);
                    var from = TacticsOverlay.ActivationsAt + TacticsOverlay.ActivationSize * records;
                    var to = Math.Min(TacticsOverlay.ActivationsAt + TacticsOverlay.ActivationSize * 40, actual.Far.Length);
                    if (from < to)
                    {
                        Array.Copy(expected.Far, from, actual.Far, from, to - from);
                    }
                }
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
