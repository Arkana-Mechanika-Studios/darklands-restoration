using System.Reflection;
using Darklands.Core.Events;
using Darklands.Core.Rules;
using Darklands.Core.World;
using Darklands.Simulation.Overland;

namespace Darklands.WorldCalls.Tests;

/// <summary>The calls: original function (by its coordinate) to what the rewrite does for it; the value left in AX, or null where none is compared.</summary>
internal static class Adapters
{
    internal static readonly Dictionary<string, Func<WorldCallsTests.Context, int?>> Map = new()
    {
        // ---- items: who -2 everybody, -1 the leader, 0..4 a member, 5.. a stored list
        ["R029+0x0F54"] = c => Inventory.BestQualityWithFlags(c.State, c.S(0), c.L(1)),
        ["R029+0x0C76"] = c => Inventory.Count(c.State, c.S(0), c.S(1)),
        ["R029+0x0DD8"] = c => Inventory.CountWithFlags(c.State, c.S(0), c.L(1)),

        // ---- time passing
        ["R029+0x255A"] = c => { TimePassage.Recover(c.State, c.S(0)); return null; },
        // R029+0x281C reads cur_month DS:00E4 for the birthdays; its callers (R029+0x5537, R029+0x55CD) pass DS:00E4 just incremented
        ["R029+0x281C"] = c => c.S(0) != c.State.Date.Month
            ? throw new WorldCallsTests.SkipJob("a month other than cur_month (its callers pass DS:00E4 itself)")
            : Run(() => MonthlyUpdate.Run(c.State, c.S(0))),
        ["R029+0x54D8"] = c => { TimePassage.Advance(c.State, c.S(0)); return null; },
        ["R029+0x5590"] = c => { TimePassage.AdvanceDays(c.State, c.S(0)); return null; },

        // ---- timed events
        ["R029+0x2E74"] = c => c.State.Events.Update(c.S(0), c.S(1), c.S(2), c.S(3), c.S(4), c.S(5), (EventUpdate)c.S(6)),
        ["R029+0x37E2"] = c => c.State.Events.CountAt(c.S(0), c.S(1)),
        ["R029+0x3AEC"] = c => c.State.Events.ValueOfTypeAt(c.S(0), c.S(1), c.S(2)),
        ["R029+0x3C28"] = c => c.State.Events.FindLocation(c.S(0), c.S(1), c.S(2), c.S(3), c.S(4)),

        ["R029+0x38D2"] = c => c.State.Events.HasType(c.S(0), c.S(1)) ? 1 : 0,
        ["R029+0x392C"] = c => c.State.Events.TypeAt(c.S(0), c.S(1), c.S(2)) ? 1 : 0,
        ["R029+0x3A20"] = c => c.State.Events.ValueAt(c.S(0), c.S(1)),
        ["R029+0x3996"] = c => c.State.Events.OwnedBy(c.S(0), c.S(1)) ? 1 : 0,
        ["R029+0x39F0"] = c => c.State.Events.Value(c.S(0)),
        ["R029+0x3A86"] = c => c.State.Events.ValueOfType(c.S(0), c.S(1)),
        ["R029+0x3B62"] = c => c.State.Events.Find(c.S(0), c.S(1), c.S(2), c.S(3), c.S(4), c.S(5)),
        ["R029+0x3CDE"] = c => c.State.Events.Remove(c.S(0), c.S(1), c.S(2), c.S(3), c.S(4)),
        ["R029+0x353E"] = c => c.State.Events.QuestWithValue(c.S(0), c.S(1), c.S(2)) ? 1 : 0,
        ["R029+0x35AA"] = c => c.State.Events.FactAnywhere(c.S(0), c.S(1)) ? 1 : 0,
        ["R029+0x360C"] = c => c.State.Events.Fact(c.S(0), c.S(1), c.S(2)) ? 1 : 0,
        ["R029+0x3676"] = c => c.State.Events.QuestValue(c.S(0)),
        ["R029+0x37B8"] = c => c.State.Events.Count(c.S(0)),
        ["R029+0x08F2"] = c => { MemberRules.HealAttribute(c.State, c.S(0), c.S(1), c.S(2)); return null; },
        ["R029+0x3878"] = c => c.State.Events.At(c.S(0), c.S(1)) ? 1 : 0,
        ["R029+0x3350"] = c => c.State.Events.ResolvedQuestExists(c.S(0)) ? 1 : 0,
        ["R029+0x3404"] = c => c.State.Events.ResolvedQuestAt(c.S(0), c.S(1), c.S(2)) ? 1 : 0,
        ["R029+0x2B36"] = c => { c.State.Events.ExtendHere(c.S(0), c.S(1)); return null; },
        ["R029+0x2A32"] = c => c.State.Events.CreateHere(c.S(0), c.S(1), c.S(2)),
        ["R029+0x2D5C"] = c => c.State.Events.Renew(c.S(0), c.S(1), c.S(2), c.S(3), c.S(4), c.S(5), c.S(6), c.S(7), c.S(8), c.S(9)),

        // ---- party
        ["R029+0x0792"] = c => { MemberRules.BoostEquippedWeaponSkills(c.State, c.S(0), c.S(1)); return null; },
        ["R029+0x1260"] = c => Saints.Knows(c.State, c.S(0), c.S(1)) ? 1 : 0,
        ["R029+0x4C04"] = c => { MemberRules.RaiseAttributeTemporarily(c.State, c.S(0), c.S(1), c.S(2), c.S(3)); return null; },
        ["R029+0x4AA4"] = c => { PartyRules.EndTerms(c.State, c.S(0)); return null; },

        // ---- travel on the map: the view's registers and the travel counters are DS words of the job
        ["R030+0x1300"] = Step,
        ["R030+0x25BE"] = CampWatch,
        ["R030+0x2DD6"] = SeasonPaletteCall,
        ["R030+0x2AC8"] = c => Terrain.MinutesPerStep(c.S(0)),
        ["R030+0x2D44"] = c => c.S(1) >= c.State.Locations.Count ? throw new WorldCallsTests.SkipJob("place 414, past the table: unreachable (the only caller, the step R030+0x1300, passes `found`, -1 or a table index below 414)") : (int)typeof(OverlandTravel).GetMethod("EncounterWeight", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(new OverlandTravel(c.State, c.Map), [c.S(0), c.S(1)])!,

        // ---- city politics, quests
        ["R039+0x0BF6"] = c => CityPolitics.Strength(c.State, 1),
        ["R039+0x0D46"] = c => CityPolitics.Strength(c.State, -1),
        // R039+0x0EBE: its 36 call sites (thunk 09C0:217F) are the uprising cards R065, R068, R074, R076, R089, R090, R091, run in a city
        ["R039+0x0EBE"] = c => c.State.CurrentLocation < 0
            ? throw new WorldCallsTests.SkipJob("in the open country (cur_loc -1): every caller runs in a city")
            : Run(() => CityPolitics.ConflictOutcome(c.State, c.S(0), c.S(1), c.S(2))),
        ["R039+0x1C22"] = c => Quests.FeastDayNearThisMonth(c.State),
        ["R039+0x1712"] = c => Quests.StartQuest10(c.State, c.S(0), out _),
    };

    static Adapters()
    {
        // the saints' helps: the original's function per saint (R039), called (who, 0, 8) by the prayer; the same coordinate serves more than one saint
        for (var saint = 0; saint < 136; saint++)
        {
            var coordinate = SaintBook.Of(saint).Coordinate;
            var number = saint;
            Map.TryAdd(coordinate, c => { SaintBook.Help(c.State, c.S(0), number); return null; });
        }
    }

    private sealed class BytePalette(int[] bytes) : SeasonPalette.IPaletteBytes
    {
        public int this[int offset]
        {
            get => bytes[offset];
            set => bytes[offset] = value & 0xFF;
        }
    }

    /// <summary>season_palette R030+0x2DD6: cur_month DS:00E4, cur_day DS:00E2, the palette DS:E48C (768 bytes), the map's background DS:2860.</summary>
    private static int? SeasonPaletteCall(WorldCallsTests.Context c)
    {
        var before = new int[768];
        for (var k = 0; k < 384; k++)
        {
            var w = c.Job.Word(0xE48C + 2 * k, false);
            before[2 * k] = w & 0xFF;
            before[2 * k + 1] = w >> 8;
        }
        var palette = (int[])before.Clone();
        var result = SeasonPalette.Apply(new BytePalette(palette), c.Job.Word(0xE4), c.Job.Word(0xE2));
        int Original(int address, int was) => c.Job.Orig.Changed.TryGetValue(address.ToString(), out var v) ? v : was;
        for (var i = 0; i < 768; i++)
        {
            if (Original(0xE48C + i, before[i]) != palette[i])
            {
                c.Notes.Add($"palette byte {i}: original {Original(0xE48C + i, before[i]):X2} rewrite {palette[i]:X2}");
                break;
            }
        }
        var was = c.Job.Word(0x2860, false);
        var background = Original(0x2860, was & 0xFF) | Original(0x2861, was >> 8) << 8;
        if (background != result.Background)
        {
            c.Notes.Add($"background original {background:X} rewrite {result.Background:X}");
        }
        return null;
    }

    /// <summary>A call whose AX is not compared.</summary>
    private static int? Run(Action call)
    {
        call();
        return null;
    }

    private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary>
    /// camp watch R030+0x25BE: the cell buffer as 20d2 filled it for the views the job lists (the call's view last; the bytes after the buffer
    /// keep the icons earlier draws left there), read under the marker DS:2866/2868 with the view's DS:286E/286C. Jobs without the view's words
    /// (the first travel corpus) get a view that holds the saved game's position and the whole 10 x 10 block inside the window.
    /// </summary>
    private static int? CampWatch(WorldCallsTests.Context c)
    {
        var members = c.State.Party.Members.Count;
        var buffer = new Simulation.Overland.CellBuffer(c.Map);
        if (!c.Job.Ds.ContainsKey(0x2866.ToString()) || !c.Job.Ds.ContainsKey(0x286A.ToString()))
        {
            var (x, y) = (c.State.MapPosition.X, c.State.MapPosition.Y);
            var viewColumn = Math.Max(x - 2, 1);
            var viewTop = Math.Max(y - 4, 0);
            buffer.FillFresh(viewColumn, viewTop, 1);
            return buffer.CampWatch(members, y - viewTop, x - viewColumn + 1);
        }
        foreach (var view in c.Job.Views ?? [[c.Job.Word(0x286A), c.Job.Word(0x286E), c.Job.Word(0x286C)]])
        {
            buffer.FillFresh(view[0], view[1], view[2]);
        }
        var (row, column) = Simulation.Overland.CellBuffer.MarkerCell(c.Job.Word(0x2866), c.Job.Word(0x2868), c.Job.Word(0x286E), c.Job.Word(0x286C));
        return buffer.CampWatch(members, row, column);
    }

    /// <summary>overland step R030+0x1300(x, y): the marker in the view, the view's registers DS:286A/286C/286E, the counters DS:8672/E488/D843.</summary>
    private static int? Step(WorldCallsTests.Context c)
    {
        var travel = new OverlandTravel(c.State, c.Map);
        var type = typeof(OverlandTravel);
        type.GetField("_minutes", Fields)!.SetValue(travel, c.Job.Word(0x8672));
        type.GetField("_danger", Fields)!.SetValue(travel, c.Job.Word(0xE488));
        type.GetField("_terrain", Fields)!.SetValue(travel, c.Job.Word(0xD843));
        travel.HourPassed = () => TimePassage.Advance(c.State, 1);
        travel.View = () => (c.Job.Word(0x286A), c.Job.Word(0x286C));
        var x = c.U(0) + 16 * c.Job.Word(0x286A);
        var y = c.S(1) + 4 * (c.Job.Word(0x286E) + c.Job.Word(0x286C));
        var result = travel.Step(x, y);
        switch (result.Outcome)
        {
            case StepOutcome.Walked:
                // party_x/party_y are left to the next pass of the key handler (R030+0x0C44 sets them from the marker) in both
                return -1;
            case StepOutcome.Blocked:
                return 9999;
            case StepOutcome.Arrived:
                return c.State.CurrentLocation;
            default:
                var here = c.State.CurrentLocation;
                if (here >= 0 && c.State.Locations[here].EncounterCard != 0x62 && result.Card == c.State.Locations[here].EncounterCard)
                {
                    return 2000;
                }
                // the road encounter's card is chosen by the loop's branch after the function, which the rewrite's Step includes
                if (!WorldCallsTests.Context.Strict)
                {
                    c.Lenient = true;
                    c.Tags.Add("lenient-road");
                }
                return 1000;
        }
    }
}
