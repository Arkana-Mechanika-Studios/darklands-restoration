using Darklands.Core;
using Darklands.Core.Battles;
using Darklands.Core.Characters;
using Darklands.Core.Events;
using Darklands.Core.Randomness;
using Darklands.Core.Time;
using Darklands.Core.World;

namespace Darklands.Persistence.Tests;

/// <summary>
/// A save made in a battle (R037+0x2ACE with DS:907A = 1): the events of EVENTS.TMP are read back into slots
/// 0..n−1 over the battle's, the table is saved as it then stands, and every event is freed after the file is written.
/// </summary>
public sealed class BattleEventTableSaveTests
{
    [Fact]
    public void ASaveInABattleWritesTheWorkFileOverTheBattlesEventsAndEmptiesTheTable()
    {
        var state = Game();
        state.Events.Put(3, Event(0x30, 1));
        state.Events.Put(10, Event(0x31, 2));
        state.Events.Put(20, Event(0x32, 3));
        state.Events.Shutdown();
        // The battle's events: slots 0..2 are below the file's three, slot 3 is not.
        for (var k = 0; k < 4; k++)
        {
            state.Events.Put(k, Event(0x48, 10 + k));
        }
        var battle = new Battle(new CombatTables(new byte[CombatTables.Length], new byte[16]), new MscRandom(1));

        var (sav, _) = OriginalSaves.Write(state, "in battle", inBattle: battle);

        var saved = SaveFile.Parse(sav).Events.Select(r => EventRecords.Read(r)).Select(e => (e.Kind, e.Value)).ToList();
        Assert.Equal([(0x30, 1), (0x31, 2), (0x32, 3), (0x48, 13)], saved);
        Assert.Empty(state.Events.All());
        // The work file is as the shutdown wrote it: after the battle the three come back, compacted.
        state.Events.Reload();
        Assert.Equal([(0, 0x30), (1, 0x31), (2, 0x32)], state.Events.All().Select(x => (x.Slot, x.Event.Kind)));
    }

    [Fact]
    public void ASaveOutsideABattleLeavesTheTableAsItIs()
    {
        var state = Game();
        state.Events.Put(7, Event(0x30, 1));

        var (sav, _) = OriginalSaves.Write(state, "outside");

        Assert.Single(SaveFile.Parse(sav).Events);
        Assert.Equal([7], state.Events.All().Select(x => x.Slot));
    }

    private static TimedEvent Event(int kind, int value) => new() { Kind = kind, Value = value, End = EventLog.ForeverEnd };

    private static GameState Game()
    {
        var state = new GameState(new MscRandom(1234)) { Party = DefaultParty.Create(seed: 77), Date = new GameDate(9, 3, 4, 1401) };
        state.Roster.Reset(state.Party);
        state.Locations.Add(new Location { Index = 0, Name = "Koln", X = 10, Y = 20 });
        state.CurrentLocation = 0;
        return state;
    }
}
