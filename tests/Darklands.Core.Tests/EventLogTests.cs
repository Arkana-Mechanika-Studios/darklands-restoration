using Darklands.Core.Events;
using Darklands.Core.Rules;
using Darklands.Core.Time;

namespace Darklands.Core.Tests;

public sealed class EventLogTests
{
    [Fact]
    public void ANewEventRunsFromNowForItsHours()
    {
        var s = TestGame.Create();
        var i = s.Events.Create(-2, 5, 0, 0x0A, 3, 7, 0, 12, 0, 0);

        var e = s.Events[i]!;
        Assert.Equal(s.Date, e.Start);
        Assert.Equal(s.Date.AddHours(12), e.End);
        Assert.Equal(i, s.Events.Last);
        Assert.True(s.Events.At(0x0A, 5));
        Assert.Equal(7, s.Events.ValueAt(0x0A, 5));
    }

    [Fact]
    public void ForeverMeansTheEndOf1499()
    {
        var s = TestGame.Create();
        var i = s.Events.CreateHere(0x0B, EventLog.Forever);

        Assert.Equal(new GameDate(23, 31, 12, 1499), s.Events[i]!.End);
        Assert.Equal(100, s.Events[i]!.Value);   // event_here's value
        Assert.Equal(s.CurrentLocation, s.Events[i]!.Location);
    }

    [Fact]
    public void QueriesOnlySeeStartedEventsButCountSeesAll()
    {
        var s = TestGame.Create();
        var i = s.Events.Create(-2, 5, 0, 0x30, 1, 9, 0, 48, 0, 0);
        s.Events[i]!.Start = s.Date.AddHours(5);

        Assert.False(s.Events.At(0x30, 5));
        Assert.False(s.Events.Started(0x30));
        Assert.Equal(0, s.Events.ValueAt(0x30, 5));
        Assert.Equal(9, s.Events.Value(0x30));   // event_value does not ask
        Assert.Equal(1, s.Events.Count(0x30));
        Assert.True(s.Events.Exists(0x30));
    }

    [Fact]
    public void TheSuppressionFlagHidesPresenceAndValueQueriesOnly()
    {
        var s = TestGame.Create();
        s.Events.Create(-2, 5, 0, 0x30, 1, 9, 0, 48, 0, 4);
        s.Events.QueriesSuppressed = true;

        Assert.False(s.Events.Exists(0x30));
        Assert.False(s.Events.At(0x30, 5));
        Assert.Equal(0, s.Events.ValueOfType(0x30, 1));
        Assert.Equal(9, s.Events.Value(0x30));
        Assert.Equal(1, s.Events.Count(0x30));
    }

    [Fact]
    public void UpdateCreatesMissingEventsAndAddOnANewOneDoubles()
    {
        var s = TestGame.Create();
        var i = s.Events.Update(-2, 5, 0x1C, 3, 4, 2, EventUpdate.Add);

        Assert.Equal(8, s.Events[i]!.Value);   // created with 4, then 4 added
        Assert.Equal(s.Date.AddHours(999), s.Events[i]!.End);

        s.Events.Update(-1, 5, 0x1C, 3, 4, 2, EventUpdate.Add);
        Assert.Equal(12, s.Events[i]!.Value);
        s.Events.Update(-2, -1, 0x1C, 3, 6, -1, EventUpdate.And);
        Assert.Equal(4, s.Events[i]!.Value);
        s.Events.Update(-2, 5, 0x1C, 3, 3, 2, EventUpdate.Or);
        Assert.Equal(7, s.Events[i]!.Value);
        s.Events.Update(-2, 5, 0x1C, 3, -1, 2, EventUpdate.Set);
        Assert.Equal(-1, s.Events[i]!.Value);
    }

    [Fact]
    public void TheDoublingCanBeFixed()
    {
        var s = TestGame.Create();
        s.Behaviour.FixNewEventAddDoubles = true;
        var i = s.Events.Update(-2, 5, 0x1C, 3, 4, 2, EventUpdate.Add);
        Assert.Equal(4, s.Events[i]!.Value);
    }

    [Fact]
    public void RenewExtendsTheMatchingEventOrCreatesItWithLiteralHours()
    {
        var s = TestGame.Create();
        var i = s.Events.Renew(-2, 5, 0, 0x20, 1, 3, 0, 10, 0, 0);
        Assert.Equal(s.Date.AddHours(10), s.Events[i]!.End);

        Assert.Equal(i, s.Events.Renew(-2, -1, 0, 0x20, 1, 3, 0, 5, 0, 0));
        Assert.Equal(s.Date.AddHours(10).AddHours(5), s.Events[i]!.End);

        var j = s.Events.Renew(-2, 6, 0, 0x20, 1, 3, 0, EventLog.Forever, 0, 0);
        Assert.Equal(s.Date.AddHours(9999), s.Events[j]!.End);
    }

    [Fact]
    public void ExtendHereLeavesTheScanPositionInLastEvent()
    {
        var s = TestGame.Create();
        s.Events.ExtendHere(0x0A, 12);   // nothing yet: creates one, Last ends as -1
        Assert.Equal(-1, s.Events.Last);
        Assert.Equal(1, s.Events.Count(0x0A));

        s.Events.ExtendHere(0x0A, 12);
        Assert.Equal(1, s.Events.Count(0x0A));
        Assert.Equal(s.Date.AddHours(12).AddHours(12), s.Events[0]!.End);
        Assert.Equal(0, s.Events.Last);
    }

    [Fact]
    public void LocationFactScansAlsoSeeQuestsAndClearingRemovesThem()
    {
        var s = TestGame.Create();
        s.Events.Create(-2, 5, 0, EventKinds.Quest, 3, 4, 0, 100, 0, 9);
        s.Events.Create(-2, 5, 0, EventKinds.LocationFact, 3, 6, 0, 100, 0, 9);
        s.Events.Create(-2, 6, 0, EventKinds.LocationFact, 3, 7, 0, 100, 0, 9);

        Assert.True(s.Events.Fact(9, 3, 5));
        Assert.Equal(4, s.Events.FactValue(9, 3, 5));   // the quest comes first
        Assert.Equal(2, s.Events.ClearFacts(9, 5));
        Assert.Equal(0, s.Events.Count(EventKinds.Quest));
        Assert.Equal(7, s.Events.FactValueAnywhere(9, 3));
    }

    [Fact]
    public void FindAndRemoveTakeMinusOneAsAny()
    {
        var s = TestGame.Create();
        s.Events.Create(1, 5, 0, 0x40, 2, 0, 0, 100, 0, 3);
        var j = s.Events.Create(2, 6, 0, 0x40, 2, 0, 0, 100, 0, 3);
        s.Events[j]!.Data1 = 77;

        Assert.Equal(j, s.Events.Find(0x40, -1, -1, 2, -1, -1));
        Assert.Equal(j, s.Events.Find(0x40, 2, -1, -1, 3, 77));
        Assert.Equal(6, s.Events.FindLocation(0x40, -1, -1, -1, 77));
        Assert.Equal(-1, s.Events.Find(0x40, 9, -1, -1, -1, -1));
        Assert.Equal(2, s.Events.Remove(0x40, 2, -1, -1, -1));
        Assert.Equal(0, s.Events.Count(0x40));
    }

    [Fact]
    public void AFullLogRefusesNewEvents()
    {
        var s = TestGame.Create();
        var messages = new GameMessageLog();
        s.Messages = messages;
        for (var k = 0; k < EventLog.Capacity; k++)
        {
            Assert.Equal(k, s.Events.CreateHere(0x50, 10));
        }
        Assert.Equal(-1, s.Events.CreateHere(0x50, 10));
        Assert.Contains("EVENT Failure", messages.Lines);
    }

    [Fact]
    public void ExpiredEventsAreRemovedAfterTimePasses()
    {
        var s = TestGame.Create();
        s.Events.CreateHere(0x0A, 2);
        s.Events.CreateHere(0x0B, 5);

        TimePassage.Advance(s, 2);
        Assert.Equal(0, s.Events.Count(0x0A));   // ends at its end hour
        Assert.Equal(1, s.Events.Count(0x0B));
        TimePassage.Advance(s, 3);
        Assert.Equal(0, s.Events.Count(0x0B));
    }

    [Fact]
    public void AnExpiringQuestClearsItsFacts()
    {
        var s = TestGame.Create();
        s.Events.Create(-2, 5, 0, EventKinds.Quest, 0, 0, 0, 1, 0, 9);
        s.Events.Create(-2, 5, 0, EventKinds.LocationFact, 1, 0, 0, 100, 0, 9);
        s.Events.Create(-2, 6, 0, EventKinds.LocationFact, 1, 0, 0, 100, 0, 9);

        TimePassage.Advance(s, 1);

        Assert.Equal(0, s.Events.CountQuests(9));
        Assert.False(s.Events.Fact(9, 1, 5));
        Assert.True(s.Events.Fact(9, 1, 6));
    }

    /// <summary>
    /// cards_shutdown / world_load around a battle: the battle runs on an empty table, what it adds below the file's
    /// count is lost (a boost called in the battle is never undone), the rest stays, and the table comes back compacted.
    /// </summary>
    [Fact]
    public void ABattleRunsOnAnEmptyTableAndTheEventsComeBackCompactedOverItsOwn()
    {
        var s = TestGame.Create();
        s.Events.Create(-2, 5, 0, 0x0A, 0, 1, 0, 100, 0, 0);
        s.Events.Create(-2, 5, 0, 0x0B, 0, 2, 0, 100, 0, 0);
        s.Events.Create(-2, 5, 0, 0x0C, 0, 3, 0, 100, 0, 0);
        s.Events.Free(0);
        var last = s.Events.Last;

        s.Events.Shutdown();
        Assert.Empty(s.Events.All());
        Assert.False(s.Events.Exists(0x0B));
        Assert.Equal(last, s.Events.Last);
        // The battle: a boost (slot 0, below the file's two) and two more (slots 1 and 2).
        s.Events.Create(0, -1, 0, EventKinds.AttributeBoost, 1, 5, 0, 24, 0, 0);
        s.Events.Create(-2, 5, 0, 0x0D, 0, 4, 0, 100, 0, 0);
        s.Events.Create(-2, 5, 0, 0x0E, 0, 5, 0, 100, 0, 0);
        s.Events.Reload();

        Assert.Equal([(0, 0x0B), (1, 0x0C), (2, 0x0E)], s.Events.All().Select(x => (x.Slot, x.Event.Kind)));
        Assert.Equal(0, s.Events.Count(EventKinds.AttributeBoost));
    }

    [Fact]
    public void TheWorkFileHoldsWordsAndEachReloadMakesNewRecords()
    {
        var s = TestGame.Create();
        var i = s.Events.Create(-2, 5, 0, 0x0A, 0, 0, 0, 100, 0, 0);
        s.Events[i]!.Value = 0x12345;
        s.Events.Shutdown();

        s.Events.Reload();
        var first = s.Events[0]!;
        first.Value = 7;
        s.Events.ReleaseAfterSave();
        Assert.Empty(s.Events.All());
        s.Events.Reload();

        Assert.NotSame(first, s.Events[0]);
        Assert.Equal(0x2345, s.Events[0]!.Value);
    }

    [Fact]
    public void AReloadBeforeAnyShutdownChangesNothing()
    {
        var s = TestGame.Create();
        s.Events.Create(-2, 5, 0, 0x0A, 0, 0, 0, 100, 0, 0);

        s.Events.Reload();

        Assert.Single(s.Events.All());
    }
}
