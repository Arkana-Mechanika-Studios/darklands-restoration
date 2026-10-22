using System.Text.Json;
using Darklands.Core;
using Darklands.Core.Events;
using Darklands.Core.Randomness;
using Darklands.Persistence;

namespace Darklands.Oracle.Tests;

/// <summary>
/// The event table's work file around a battle against the original program (Corpus/eventsfile.json,
/// tools/oracle/build_eventsfile_corpus.py): cards_shutdown writes the events in use to EVENTS.TMP in slot
/// order and empties every slot (EventLog.Shutdown); world_load reads the file's n records back into slots
/// 0..n−1 and leaves slots n and up as they are (EventLog.Reload).
/// </summary>
public sealed class EventsFileOracleTests
{
    private static readonly Lazy<Corpus> Data = new(() => Corpus.Load("eventsfile.json"));

    [Fact]
    public void ShutdownWritesAndEmptiesWhatTheOriginalDoes()
    {
        var cases = Data.Value.Cases("cards_shutdown");
        Assert.NotEmpty(cases);
        foreach (var c in cases)
        {
            var s = State(Table(c.Inputs));
            s.Events.Shutdown();
            Assert.Empty(s.Events.All());
            // What the file holds comes back in slots 0..n-1 of an empty table.
            s.Events.Reload();
            var file = c.Element.GetProperty("file").EnumerateArray().Select(x => x.GetString()!).ToList();
            Assert.Equal(c.Element.GetProperty("count").GetInt32(), file.Count);
            Assert.Equal(file, s.Events.All().Select(x => Convert.ToHexStringLower(EventRecords.Write(x.Event))));
            Assert.Equal(Enumerable.Range(0, file.Count), s.Events.All().Select(x => x.Slot));
        }
    }

    [Fact]
    public void ReloadReadsBackWhatTheOriginalReadsBack()
    {
        var cases = Data.Value.Cases("world_load");
        Assert.NotEmpty(cases);
        foreach (var c in cases)
        {
            var file = c.Inputs.GetProperty("file").EnumerateArray().Select(x => x.GetString()).ToArray();
            // The work file: the file's records written by a shutdown of a table that held them (with gaps).
            var s = State(2 * file.Length <= EventLog.Capacity ? file.SelectMany(x => new[] { null, x }).ToArray() : file);
            s.Events.Shutdown();
            var battle = Table(c.Inputs);
            for (var i = 0; i < EventLog.Capacity; i++)
            {
                s.Events.Put(i, battle[i] is { } hex ? EventRecords.Read(Convert.FromHexString(hex)) : null);
            }
            var before = Enumerable.Range(0, EventLog.Capacity).Select(i => s.Events[i]).ToArray();
            s.Events.Reload();
            var after = c.Element.GetProperty("after").EnumerateArray().ToArray();
            for (var i = 0; i < EventLog.Capacity; i++)
            {
                var expected = after[i];
                if (expected.ValueKind == JsonValueKind.Null)
                {
                    Assert.Null(s.Events[i]);
                }
                else if (expected.GetString() == "kept")
                {
                    Assert.Same(before[i], s.Events[i]);
                }
                else
                {
                    Assert.NotSame(before[i], s.Events[i]);
                    Assert.Equal(expected.GetString(), Convert.ToHexStringLower(EventRecords.Write(s.Events[i]!)));
                }
            }
        }
    }

    private static string?[] Table(JsonElement inputs) =>
        inputs.GetProperty("table").EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Null ? null : x.GetString()).ToArray();

    private static GameState State(string?[] slots)
    {
        var s = new GameState(new MscRandom());
        for (var i = 0; i < slots.Length && i < EventLog.Capacity; i++)
        {
            s.Events.Put(i, slots[i] is { } hex ? EventRecords.Read(Convert.FromHexString(hex)) : null);
        }
        return s;
    }
}
