using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Darklands.Core.Randomness;

namespace Darklands.Cards.Tests.Oracle;

/// <summary>
/// Every card against the ORIGINAL program: Oracle/cards-oracle.json.gz holds, per card and run
/// (options 0..9, location 3 and 120, plain and probing), what the original card function did when
/// tools/oracle/card_oracle.py ran it from DARKLAND.EXE on the same start state (the C#'s own state,
/// loaded by the original's saved-game loader): the trace (pictures, expanded texts, menus with their
/// option states, hints, messages, time), the next card, the random generator, and the bytes of the
/// saved game the original's writer makes afterwards that differ from the start. The C# card runs on
/// that state with <see cref="OracleScreen"/> and must agree.
/// </summary>
/// <remarks>
/// Known differences that are not bugs are listed in Oracle/card-divergences.json (card, run or "*",
/// what, why, optionally how the original's run ended); everything else fails. DARKLANDS_CARD_ORACLE_REPORT=file writes the per-card agreement
/// (docs/faithfulness/CARDS.md is made from it).
/// </remarks>
public sealed class CardOracleTests
{
    /// <summary>Saved-game bytes the C# does not model and the original's writer changes on the way.</summary>
    // orig: +0x84 DS:E898, the card-text state the text drawer sets (R043+0x0F54: mode, then 2 after an option list); SaveFile keeps it raw
    private static readonly string[] UnmodelledHeadOffsets = [(0x84 - 0x64).ToString("X"), (0x85 - 0x64).ToString("X")];

    private static string Folder([CallerFilePath] string here = "") => Path.GetDirectoryName(here)!;

    public static JsonDocument? Corpus(string corpusFile = "cards-oracle.json.gz")
    {
        // DARKLANDS_CARD_ORACLE_CORPUS: another corpus (the pilot's start-state variations)
        var path = Environment.GetEnvironmentVariable("DARKLANDS_CARD_ORACLE_CORPUS") is { Length: > 0 } other
            ? other : Path.Combine(Folder(), corpusFile);
        if (!File.Exists(path))
        {
            return null;
        }
        using var file = File.OpenRead(path);
        using var zip = new GZipStream(file, CompressionMode.Decompress);
        return JsonDocument.Parse(zip);
    }

    [Fact]
    public void EveryCardAgreesWithTheOriginal()
    {
        if (!CardHarness.Available || Corpus() is not { } corpus)
        {
            return;
        }
        AgreeWith(corpus);
    }

    /// <summary>
    /// The runs with controlled service results (local/batch/cards1): the battle ends with the result of the key's part B&lt;n&gt; and the
    /// prayer screen answers as Y&lt;c|f|a&gt; (the oracle's stubs, tools/oracle/card_oracle.py results_of), reached by the first menu's line P&lt;k&gt;.
    /// </summary>
    [Fact]
    public void ControlledResultsAgreeWithTheOriginal()
    {
        if (!CardHarness.Available || Environment.GetEnvironmentVariable("DARKLANDS_CARD_ORACLE_CORPUS") is { Length: > 0 }
            || Corpus("cards-controlled.json.gz") is not { } corpus)
        {
            return;
        }
        AgreeWith(corpus);
    }

    /// <summary>
    /// The lead runs of local/batch/cards1 that start from built states (judged in local/batch/cards1o): the death film when nobody is
    /// left, the member who leaves with the file's record tail, the text variable read through msg_file, a never-held slot's figure;
    /// and those of local/batch/cards2 (judged in local/batch/cards2o): the heir who leaves nobody (the death film after the card), the
    /// fellow prisoner who joins with the file's record, a choice in a pop-up box opened on a line whose handler has none of its own.
    /// </summary>
    [Fact]
    public void LeadRunsAgreeWithTheOriginal()
    {
        if (!CardHarness.Available || Environment.GetEnvironmentVariable("DARKLANDS_CARD_ORACLE_CORPUS") is { Length: > 0 }
            || Corpus("cards-leads.json.gz") is not { } corpus)
        {
            return;
        }
        AgreeWith(corpus);
    }

    private static void AgreeWith(JsonDocument corpus)
    {
        var divergences = Divergences();
        var cards = typeof(Card).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Card)) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (Card)Activator.CreateInstance(t)!)
            .ToDictionary(c => c.Number);
        var allDifferences = new System.Collections.Concurrent.ConcurrentBag<string>();
        var report = new System.Collections.Concurrent.ConcurrentBag<string>();
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        var entries = corpus.RootElement.GetProperty("cards").EnumerateObject().ToList();
        var clock = Convert.ToUInt32(corpus.RootElement.GetProperty("clock").GetString()!, 16);
        var dump = Environment.GetEnvironmentVariable("DARKLANDS_CARD_ORACLE_DUMP");
        // "sound": true in the corpus: its traces hold the sound_play / music_play lines (card_oracle.py since the audio round); older corpora do not
        var soundTrace = corpus.RootElement.TryGetProperty("sound", out var soundFlag) && soundFlag.GetBoolean();
        Parallel.ForEach(entries, entry =>
        {
            var number = Convert.ToInt32(entry.Name, 16);
            var runs = entry.Value.GetProperty("runs").EnumerateObject().ToList();
            var texts = entry.Value.TryGetProperty("texts", out var t) ? t.EnumerateArray().Select(x => x.GetString()!).ToArray() : [];
            int agree = 0, justified = 0, differ = 0, stray = 0;
            var first = "";
            var differing = new List<string>();
            foreach (var run in runs)
            {
                if (!cards.TryGetValue(number, out var card))
                {
                    if (Justified(divergences, entry.Name, run.Name, "no C# card"))
                    {
                        justified++;
                    }
                    else
                    {
                        differ++;
                        first = "no C# card";
                    }
                    continue;
                }
                var drop = divergences.Where(d => (d.Card == "*" || d.Card == entry.Name) && d.DropCSharp is { Length: > 0 })
                    .Select(d => d.DropCSharp!).ToList();
                var blind = divergences.Any(d => (d.Card == "*" || d.Card == entry.Name) && d.IgnoreTexts);
                var difference = Compare((Card)Activator.CreateInstance(card.GetType())!, run.Name, run.Value, clock, dump, texts, drop, blind, soundTrace);
                if (difference is null && blind)
                {
                    justified++;
                    continue;
                }
                if (difference is null && drop.Count > 0)
                {
                    justified++;
                    continue;
                }
                if (difference is null)
                {
                    agree++;
                }
                else if (run.Value.TryGetProperty("stray", out _))
                {
                    // the original read through index -1 of the location or event table, or its own code through a null far pointer (an
                    // empty event slot), as the card ran without its quest:
                    // memory that holds something else; the C# reads an empty entry (TextVariables, CardContext.Events: DIVERGENCE)
                    justified++;
                    stray++;
                }
                else if (Justified(divergences, entry.Name, run.Name, difference, run.Value.TryGetProperty("end", out var origEnd) ? origEnd.GetString() : null))
                {
                    justified++;
                }
                else
                {
                    differ++;
                    differing.Add(run.Name);
                    allDifferences.Add($"{entry.Name}\t{run.Name}\t{difference.Replace('\t', ' ').Replace('\n', ' ')}");
                    if (first.Length == 0)
                    {
                        first = $"{run.Name}: {difference}";
                    }
                }
            }
            report.Add($"{entry.Name}\t{entry.Value.GetProperty("function").GetString()}\t{agree}\t{justified}\t{differ}\t{first.Replace('\t', ' ').Replace('\n', ' ')}\t{stray}\t{string.Join(",", differing)}");
            if (differ > 0)
            {
                failures.Add($"{entry.Name}: {differ} run(s) differ, first {first}");
            }
        });
        if (Environment.GetEnvironmentVariable("DARKLANDS_CARD_ORACLE_REPORT") is { Length: > 0 } reportFile)
        {
            File.WriteAllLines(reportFile, report.OrderBy(l => l, StringComparer.Ordinal));
        }
        if (Environment.GetEnvironmentVariable("DARKLANDS_CARD_ORACLE_DIFFS") is { Length: > 0 } diffsFile)
        {
            File.WriteAllLines(diffsFile, allDifferences.OrderBy(l => l, StringComparer.Ordinal));
        }
        Assert.True(failures.IsEmpty, $"{failures.Count} card(s) differ from the original:\n" +
            string.Join("\n", failures.OrderBy(f => f, StringComparer.Ordinal).Take(40)));
    }

    /// <summary>The first difference of one run, or null when the C# agrees.</summary>
    public static string? Compare(Card card, string runName, JsonElement expected, uint clock, string? dump = null, string[]? texts = null,
        IReadOnlyList<string>? dropCSharp = null, bool ignoreTexts = false, bool soundTrace = false)
    {
        var parts = runName.Split(' ');
        var probe = parts[0] is "probe" or "hand" or "direct";
        var key = parts[1];
        // "hand <key> M1L7[H][S2]": a handler call (OracleScreen.Call)
        var hand = parts[0] == "hand" ? HandCall.Parse(parts[2]) : null;
        // "direct <key> D": the handlers of the card file's dormant option table are called directly (Direct)
        var direct = parts[0] == "direct" ? parts[2] : null;
        var option = int.Parse(key[(key.LastIndexOf("-O") + 2)..]);
        var (actual, end, random, sav) = Run(card, key, option, probe, clock, hand, direct);
        if (!soundTrace)
        {
            // a corpus recorded before the sound lines existed: they are compared by CardSoundOracleTests against a sidecar of the original's
            actual.RemoveAll(IsSound);
        }
        foreach (var prefix in dropCSharp ?? [])
        {
            // a C# line the original has no counterpart for, listed in card-divergences.json
            var at = actual.FindIndex(l => l.StartsWith(prefix, StringComparison.Ordinal));
            if (at >= 0)
            {
                var dropped = actual[at];
                actual.RemoveAt(at);
                // without that show the card's next picture is where the original first loads the card
                var next = actual.FindIndex(at, l => l.StartsWith("show ", StringComparison.Ordinal) || l.StartsWith("picture ", StringComparison.Ordinal));
                if (dropped.StartsWith("show ", StringComparison.Ordinal) && next >= 0 && actual[next].StartsWith("picture ", StringComparison.Ordinal))
                {
                    actual[next] = $"show {dropped.Split(' ')[1]} {actual[next]["picture ".Length..]}";
                }
            }
        }
        if (CardOracleStates.ForceOf(key) is { } forced)
        {
            // a hint asked of a line the card never offers (the forced line, see CardOracleStates.ForceOf): when its handler is an
            // empty function the original's register leftovers come back ("0 '' 0"), which the C# words as its default hint; not compared
            var want = expected.GetProperty("trace").EnumerateArray().Select(e => e.GetString()!).ToList();
            var hint = $"hint {forced.Line}: ";
            var wanted = want.Where(l => l.StartsWith(hint, StringComparison.Ordinal)).ToList();
            var mine = Enumerable.Range(0, actual.Count).Where(i => actual[i].StartsWith(hint, StringComparison.Ordinal)).ToList();
            for (var k = 0; k < Math.Min(wanted.Count, mine.Count); k++)
            {
                if (wanted[k] == hint + "0 '' 0" && actual[mine[k]].StartsWith(hint + "-1 '", StringComparison.Ordinal))
                {
                    actual[mine[k]] = wanted[k];
                }
            }
        }
        if (hand is not null || direct is not null)
        {
            // a hint asked of a line whose handler is an empty function: the original's register leftovers come back ("0 '' 0"),
            // which the C# words as its default hint; not compared
            var want = expected.GetProperty("trace").EnumerateArray().Select(e => e.GetString()!).Where(l => l.StartsWith("hint ", StringComparison.Ordinal)).ToList();
            var mine = Enumerable.Range(0, actual.Count).Where(i => actual[i].StartsWith("hint ", StringComparison.Ordinal)).ToList();
            for (var k = 0; k < Math.Min(want.Count, mine.Count); k++)
            {
                var number = want[k][..(want[k].IndexOf(':') + 2)];
                if (want[k] == number + "0 '' 0" && actual[mine[k]].StartsWith(number + "-1 '", StringComparison.Ordinal))
                {
                    actual[mine[k]] = want[k];
                }
                // direct calls: a hint that leaves its result uninitialised (the original returns stale stack words, different on
                // every card and machine; the C# reads them as "Success is assured."): not compared
                else if (direct is not null && want[k].Contains(" '' ", StringComparison.Ordinal) && !want[k].StartsWith(number + "-1 ", StringComparison.Ordinal)
                    && !want[k].StartsWith(number + "0 ", StringComparison.Ordinal) && actual[mine[k]].StartsWith(number + "-1 'Success is assured.'", StringComparison.Ordinal))
                {
                    actual[mine[k]] = want[k];
                }
            }
        }
        return CompareResult(expected, actual, end, random, sav, dump is { Length: > 0 } ? Path.Combine(dump, $"{card.Number:X3} {runName}.txt") : null, texts, ignoreTexts);
    }

    /// <summary>A trace line of the sound device: <c>sound n</c> (sound_play, root 047C:078A) or <c>music x</c> (music_play, R043+0x09AE).</summary>
    public static bool IsSound(string line) => line.StartsWith("sound ", StringComparison.Ordinal) || line.StartsWith("music ", StringComparison.Ordinal);

    /// <summary>A run's trace, end, random state and saved game against the original's (null: they agree).</summary>
    public static string? CompareResult(JsonElement expected, List<string> actual, string end, string random,
        (string Place, JsonObject Sections)? sav, string? dumpFile = null, string[]? texts = null, bool ignoreTexts = false)
    {
        // the rules redraw the card after a full screen, a prayer or a battle (card_load through thunk 09C0:1EA5, "redraw" in the
        // original's trace); the C# card shows its picture again itself: a "picture" right after one of those is that redraw
        actual = Pictures(actual);
        var want = expected.GetProperty("trace").EnumerateArray().Select(e => Resolve(e.GetString()!, texts))
            .ToList();
        want = Pictures(want);
        if (ignoreTexts)
        {
            // listed: the card shows texts the original reads from outside its .MSG (or an option list it never drew)
            static string Bare(string l) => l.Split(' ')[0] is "text" or "menu" ? l[..Math.Max(l.LastIndexOf(' '), 0)] : l;
            want = want.Select(Bare).ToList();
            actual = actual.Select(Bare).ToList();
        }
        if (dumpFile is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dumpFile)!);
            var text = new StringBuilder("== original" + "\n");
            want.ForEach(l => text.Append(l).Append("\n"));
            text.Append($"{expected.GetProperty("end").GetString()} random {expected.GetProperty("random").GetString()}" + "\n");
            text.Append(expected.TryGetProperty("sav", out var sv) ? sv.ToString() : "").Append("\n").Append("== C#" + "\n");
            actual.ForEach(l => text.Append(l).Append("\n"));
            text.Append($"{end} random {random}" + "\n");
            text.Append(sav is { } cs ? SaveSections.Canonical(cs.Sections) : "").Append("\n");
            File.WriteAllText(dumpFile, text.ToString());
        }
        for (var i = 0; i < Math.Max(want.Count, actual.Count); i++)
        {
            var w = i < want.Count ? want[i] : "<end>";
            var a = i < actual.Count ? actual[i] : "<end>";
            if (w != a)
            {
                return $"line {i}: original '{Short(w)}', C# '{Short(a)}'";
            }
        }
        var wantEnd = expected.GetProperty("end").GetString()!;
        // the menu policy gives up the same way on both sides (no choosable option, or more than 50 menus)
        const string PolicyStop = "throws InvalidOperationException: ";
        if (end.StartsWith(PolicyStop, StringComparison.Ordinal) && end[PolicyStop.Length..] is var reason
            && (reason.StartsWith("Menu ", StringComparison.Ordinal) || reason.StartsWith("The card keeps asking", StringComparison.Ordinal) || reason == "battle loop"))
        {
            end = "stops " + reason;
        }
        // the game is over after the death film: the original's run stops at the kbhit() that follows it (INT 21h AH=0Bh)
        if (end == GameOver && want.LastOrDefault() == "film death" && wantEnd.StartsWith("stops INT 21h AH=0Bh ", StringComparison.Ordinal))
        {
            end = wantEnd;
        }
        if (wantEnd != end)
        {
            return $"end: original '{Short(wantEnd)}', C# '{end}'";
        }
        var wantRandom = expected.GetProperty("random").GetString()!;
        if (wantRandom != random)
        {
            return $"random: original {wantRandom}, C# {random}";
        }
        if (expected.TryGetProperty("sav", out var s) && sav is not null)
        {
            if (s.TryGetProperty("error", out var err))
            {
                return $"saved game: the original's writer stopped ({err.GetString()})";
            }
            var place = s.GetProperty("place").GetString();
            if (place != sav.Value.Place)
            {
                return $"saved game place: original '{place}', C# '{sav.Value.Place}'";
            }
            var want2 = Without((JsonObject)JsonNode.Parse(s.GetProperty("sections").GetRawText())!);
            var got = Without((JsonObject)JsonNode.Parse(sav.Value.Sections.ToJsonString())!);
            if (want.Any(l => l.StartsWith("battle ", StringComparison.Ordinal)))
            {
                // the oracle stubs cards_shutdown / cards_startup (R043+0x054E / R043+0x078E) with the battle: the original's table
                // keeps the slots an expiry freed before the fight, the C#'s comes back compacted (EventLog.Shutdown / Reload), so a
                // later event takes another slot and the saved list's order differs: the events are compared as a set
                SortedEvents(want2);
                SortedEvents(got);
            }
            if (SaveSections.Canonical(want2) != SaveSections.Canonical(got))
            {
                return $"saved game: {SaveSections.FirstDifference(want2, got)}";
            }
        }
        return null;
    }

    /// <summary>
    /// The pictures as the player sees them, the same for both traces: after a full screen the card's main picture is back
    /// (the original's rules redraw the card, "redraw" in its trace; the C# screen does it itself, the picture its card last
    /// gave Show, "main" in its trace); after a battle or a prayer the next picture is kept whatever it is; and a "picture"
    /// of the picture already shown is left out.
    /// </summary>
    private static List<string> Pictures(List<string> trace)
    {
        var result = new List<string>();
        string shown = "", main = "";
        var afterFight = false;
        foreach (var line in trace)
        {
            var p = line.Split(' ');
            switch (p[0])
            {
                case "redraw" when p.Length > 2:
                    main = p[2];
                    // after a battle or a prayer the rules redraw pic_buf, which the C# does not know (a card picture
                    // may sit in pic_buf or pic_buf2): the next picture is compared whatever it is
                    shown = afterFight ? "" : p[2];
                    continue;
                case "main" when p.Length > 1:
                    main = p[1];
                    continue;
                case "show" when p.Length > 2:
                    shown = main = p[2];
                    break;
                case "picture" when p.Length > 1:
                    if (p[1] == shown)
                    {
                        continue;
                    }
                    shown = p[1];
                    break;
                case "screen":
                    shown = main;
                    break;
                case "battle" or "prayer":
                    shown = "";
                    afterFight = true;
                    result.Add(line);
                    continue;
            }
            afterFight = false;
            result.Add(line);
        }
        return result;
    }

    /// <summary>A "#k" at the end of a text or menu line is the card's texts[k].</summary>
    private static string Resolve(string line, string[]? texts)
    {
        var at = line.LastIndexOf(" #", StringComparison.Ordinal);
        if (texts is null || at < 0 || !int.TryParse(line[(at + 2)..], out var k) || k >= texts.Length)
        {
            return line;
        }
        return line[..(at + 1)] + texts[k];
    }

    /// <summary>The sections without the bytes the C# does not model.</summary>
    private static JsonObject Without(JsonObject sections)
    {
        if (sections["head"] is JsonObject head && head["0"] is JsonObject record)
        {
            foreach (var o in UnmodelledHeadOffsets)
            {
                record.Remove(o);
            }
            if (record.Count == 0)
            {
                head.Remove("0");
            }
            if (head.Count == 0)
            {
                sections.Remove("head");
            }
        }
        if (sections["chars"] is JsonObject chars)
        {
            // a character whose item count changed: the item records past the new count are not items any more (the
            // original's item_delete leaves what it shifted there, the C# keeps what the record held)
            foreach (var (id, node) in chars.ToList())
            {
                if (node is JsonObject bytes && bytes["7E"] is { } countByte)
                {
                    var count = (sbyte)Convert.ToByte(countByte.GetValue<string>(), 16);
                    var end = 0xAA + 6 * Math.Max((int)count, 0);
                    foreach (var key in bytes.Select(p => p.Key).Where(k => Convert.ToInt32(k, 16) >= end).ToList())
                    {
                        bytes.Remove(key);
                    }
                    if (bytes.Count == 0)
                    {
                        chars.Remove(id);
                    }
                }
            }
            if (chars.Count == 0)
            {
                sections.Remove("chars");
            }
        }
        else if (sections["chars"] is JsonArray records)
        {
            // the whole records (their number changed): the same, the bytes past the item count as zeros
            for (var k = 0; k < records.Count; k++)
            {
                var r = Convert.FromHexString(records[k]!.GetValue<string>());
                if (r.Length > 0x7E)
                {
                    var end = 0xAA + 6 * Math.Max((int)(sbyte)r[0x7E], 0);
                    if (end < r.Length)
                    {
                        r.AsSpan(end).Clear();
                    }
                    records[k] = Convert.ToHexString(r);
                }
            }
        }
        return sections;
    }

    private static void SortedEvents(JsonObject sections)
    {
        if (sections["events"] is JsonArray list)
        {
            sections["events"] = new JsonArray(list.Select(e => e!.GetValue<string>()).Order(StringComparer.Ordinal)
                .Select(e => (JsonNode)JsonValue.Create(e)!).ToArray());
        }
    }

    /// <summary>The C# run's end when nobody was left (the death film played; the game is over).</summary>
    private const string GameOver = "game over";

    /// <summary>Stops a run when nobody is left (<see cref="Run"/>).</summary>
    private sealed class PartyPerishedStop : Exception;

    private static string Short(string s) => s.Length > 160 ? s[..160] + "…" : s;

    /// <summary>Runs the C# card on a start state as the oracle did.</summary>
    public static (List<string> Trace, string End, string Random, (string Place, JsonObject Sections)? Sav) Run(
        Card card, string key, int option, bool probe, uint clock, HandCall? hand = null, string? direct = null)
    {
        var state = CardOracleStates.FromKey(key);
        // C: srand((unsigned)time(0)) of the oracle's machine: its DOS clock is fixed, time(0) gives the corpus's "clock"
        state.ClockSeed = () => clock;
        var (start, _) = Persistence.OriginalSaves.Write(state);
        // A game saved in a battle comes up with DS:9081 bit 0 (GameState.ResumeBattle); the main loop starts the battle instead of the card and the card never
        // runs with it set. The oracle runs the card directly, and the original writes the in-battle bit of the difficulty (R037+0x2B57) only while a battle
        // runs (DS:907A = 1, R037+0x2AEF), so the saved game after the card has it clear: the run goes on as after the battle, with the bit consumed.
        state.ResumeBattle = false;
        // a roster entry outside the party is the saved game's record (OriginalSaves.Write writes its StoredRecord), as a load reads it:
        // an aimed change made to a member that the start then drops (part sz<n>) is not in the file. In play a member who leaves the
        // party has the record written back (party_remove R037+0x3B58), so the entry and the file's record are one.
        for (var entry = 0; entry < state.Roster.Characters.Count; entry++)
        {
            if (Array.IndexOf(state.Roster.PartySlots, entry) < 0 && state.Roster.StoredRecord(entry) is var record
                && !ReferenceEquals(record, state.Roster.Characters[entry]))
            {
                state.Roster.Characters[entry] = record.Copy();
            }
        }
        // the original runs on that saved game as loaded: its character records keep the bytes the model does not
        // (the item records past the count, which keep_items_pct reads), as a loaded game's do
        foreach (var character in state.Party.Members.Concat(state.Roster.Characters))
        {
            character.OriginalRecord ??= Persistence.CharacterRecords.Write(character);
        }
        // the original's CHARACTR.TMP is that saved game's: its records of the party are the members as the start has them (aimed
        // changes included), as a load leaves them (OriginalSaves.Read: CharacterRoster.KeepStored)
        state.Roster.Reconcile(state.Party);
        foreach (var entry in state.Roster.PartySlots.Where(e => e >= 0 && e < state.Roster.Characters.Count))
        {
            var member = state.Roster.Characters[entry];
            state.Roster.KeepStored(member, member.Copy());
        }
        var screen = new OracleScreen(option, probe, hand) { Force = CardOracleStates.ForceOf(key), Path = CardOracleStates.PathOf(key) };
        // the texts the card before left loaded: the card's own, as card_oracle.py loads them first, or the key's T part
        if ((CardOracleStates.TextsOf(key) ?? CardTable.Find(card.Number)?.Name) is { Length: > 0 } texts)
        {
            screen.PreloadTexts(texts);
        }
        var c = new CardContext(state, CardHarness.World!, screen) { PersonalNames = CardHarness.PersonalNames };
        if (OptionsBlockStart is { } block)
        {
            c.OptionsBlock = block;
        }
        c.Screens = new OracleScreens(screen.Log);
        // sound: music_play(x) is a "music x" line and then the driver's command (sound x, or sound 1 with the music off), sound_play(n)
        // a "sound n" line; the oracle's saved games hold music on (DS:01BF = 1). The presentation's wiring (DarklandsGame.Play)
        // is the same: Music = GameUi.PlayMusic, SoundPlayer = Audio.PlayMusic, MusicPlaying = GameUi.MusicPlaying.
        var audio = new OracleAudio(screen.Log);
        c.Music = tune =>
        {
            screen.Log.Add($"music {tune}");
            Darklands.Audio.GameSounds.PlayMusic(audio, tune, musicOn: true);
        };
        c.SoundPlayer = number => audio.PlayMusic(number);
        c.MusicPlaying = () =>
        {
            screen.Log.Add("music 5");
            return Darklands.Audio.GameSounds.RequestCurrentPiece(audio, musicOn: true);
        };
        // the oracle stubs the battle (result 0, or the run key's part B<n>) and the prayer screen (0, the prayer fails, or the part Y<c|f|a>)
        var battleResults = CardOracleStates.BattleResultsOf(key);
        var prayerAnswers = CardOracleStates.PrayerAnswersOf(key);
        var battles = 0;
        var prayers = 0;
        c.Battle = (side, seed, parameters) =>
        {
            screen.Log.Add($"battle {side} {(short)seed} {string.Join(" ", parameters)}");
            if (battleResults is not null && battles > 200)
            {
                // a constant controlled result that makes the card fight again and again: both sides give up here (card_oracle.py raises the same stop)
                throw new InvalidOperationException("battle loop");
            }
            return battleResults is null ? 0 : battleResults[Math.Min(battles++, battleResults.Length - 1)];
        };
        // the films (root 030B:0004 plays each animation; the oracle records them)
        // orig: R133+0x186C: cards_shutdown (sound_play(1)), then each scene's tune (music_play) and its PAN film; Cinematics.Film does the same
        c.PlayFilm = steps =>
        {
            audio.StopMusic();
            foreach (var step in steps)
            {
                screen.Log.Add($"music {step.Tune}");
                Darklands.Audio.GameSounds.PlayMusic(audio, step.Tune, musicOn: true);
                if (step.Animation is not null)
                {
                    screen.Log.Add($"film {step.Animation.ToLowerInvariant()}");
                }
            }
            return true;
        };
        c.SaintPrayer = (who, saint) =>
        {
            screen.Log.Add($"prayer {who} {saint}");
            return prayerAnswers is null ? 0 : prayerAnswers[Math.Min(prayers++, prayerAnswers.Length - 1)];
        };
        // nobody left: hours_pass R029+0x255A and run_battle R029+0x2278 play the death film (the oracle records it) and end the program
        // (the original's run stops at the kbhit after the film, INT 21h AH=0Bh); the game's host does the same (DarklandsGame: Perished,
        // AllDead), the harness records the film and stops the run
        // orig: run_battle R029+0x2278: music_play(0x12), then the film (Cinematics.Death)
        c.AllDead = () =>
        {
            screen.Log.Add("music 18");
            Darklands.Audio.GameSounds.PlayMusic(audio, 0x12, musicOn: true);
            screen.Log.Add("film death");
            throw new PartyPerishedStop();
        };
        void Perished(Core.GameState perished)
        {
            if (ReferenceEquals(perished, state))
            {
                // orig: hours_pass R029+0x255A: music_play(0x12), cards_shutdown_vec (sound_play(1)), then the film (Cinematics.DeathAfterShutdown)
                screen.Log.Add("music 18");
                Darklands.Audio.GameSounds.PlayMusic(audio, 0x12, musicOn: true);
                audio.StopMusic();
                screen.Log.Add("film death");
                throw new PartyPerishedStop();
            }
        }
        Core.Rules.TimePassage.PartyPerished += Perished;
        screen.Context = c;
        state.CurrentCard = card.Number;
        var end = "";
        var ok = false;
        try
        {
            if (direct is not null)
            {
                Direct(card, c, screen, direct);
                end = "ok";
                ok = true;
            }
            var next = direct is not null ? -2 : card.Run(c);
            if (direct is null)
            {
                end = $"next {next}";
                ok = true;
            }
            if (direct is null && next is > -2 or < -6)
            {
                // the main loop's step after a card that does not start a battle (as CardDispatcher.RunCard; the oracle runs the original's)
                if (state.CurrentCard != Simulation.CardDispatcher.OverlandMap)
                {
                    state.MapLeaveMode = 0;
                }
                state.PreviousCard = state.CurrentCard;
                state.CurrentCard = next;
                Core.Rules.TimePassage.Advance(state, 0);
            }
        }
        catch (PartyPerishedStop)
        {
            // the game is over (also in the main loop's advance_hours(0) after the card): the original's run stops at the kbhit
            // after the film, so there is no saved game to compare
            end = GameOver;
            ok = false;
        }
        catch (Exception e)
        {
            end = $"throws {e.GetType().Name}: {e.Message}";
        }
        finally
        {
            Core.Rules.TimePassage.PartyPerished -= Perished;
        }
        if (OptionsBlockStart is not null)
        {
            screen.Log.Add($"options_block {c.OptionsBlock}");
        }
        var random = state.Random is MscRandom msc ? msc.State.ToString("X8") : "?";
        (string, JsonObject)? sav = null;
        if (ok)
        {
            try
            {
                sav = SaveDiff(start, state);
            }
            catch (ArgumentOutOfRangeException)
            {
                // the C# state cannot be written as a saved game (nobody left in the party): a difference to report, not a crash of the replay
                end = "saved game unwritable";
            }
        }
        return (screen.Log, end, random, sav);
    }

    /// <summary>
    /// The handlers of a card file's dormant option table (the card function never installs or offers them), called directly as
    /// card_oracle.py run_direct does; run group D: the hint handlers of lines 0..9 (menu_member, menu_slot = line / 4, line % 4), the table's
    /// default ("screwed up."), the actions of lines 0..9 (each followed by the next card it set), group P: the item pop-up handlers of slots 0..3.
    /// </summary>
    /// <summary>
    /// DARKLANDS_CARD_ORACLE_OPTBLOCK=n (as card_oracle.py): every run starts with options_block (DS:EE53, <see cref="CardContext.OptionsBlock"/>) = n
    /// and its trace ends with the value the card left ("options_block n").
    /// </summary>
    private static readonly int? OptionsBlockStart =
        int.TryParse(Environment.GetEnvironmentVariable("DARKLANDS_CARD_ORACLE_OPTBLOCK"), out var block) ? block : null;

    private static void Direct(Card card, CardContext c, OracleScreen screen, string group)
    {
        var method = card.GetType().GetMethod("DormantOptions", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Public) ?? throw new InvalidOperationException($"{card.GetType().Name} has no DormantOptions");
        var lines = (MenuLine[])method.Invoke(null, [c])!;
        screen.UseTexts(card.Name);
        for (var k = 0; k < 10 && group == "D"; k++)
        {
            c.MenuMember = k / 4;
            c.MenuSlot = k % 4;
            screen.Log.Add($"hint {k}: {OracleScreen.Describe(lines[k].AskHint())}");
        }
        c.MenuMember = 0;
        c.MenuSlot = 0;
        if (group == "D")
        {
            screen.Log.Add($"default 0: {OracleScreen.Describe(MenuLine.ScrewedUp())}");
        }
        for (var k = 0; k < 10 && group == "D"; k++)
        {
            c.Next = 0;
            lines[k].Choose(-1);
            screen.Log.Add($"action {k}: next {c.Next}");
        }
        for (var slot = 0; slot < 4 && group == "P"; slot++)
        {
            c.MenuMember = 0;
            c.MenuSlot = slot;
            screen.Log.Add($"popup {slot}: {OracleScreen.Describe((lines[0].Popup ?? lines[0].AskHint)())}");
        }
    }

    /// <summary>The saved game of the state against the start (from +0x64: the texts are compared as texts).</summary>
    public static (string Place, JsonObject Sections) SaveDiff(byte[] start, Core.GameState state)
    {
        var (bytes, _) = Persistence.OriginalSaves.Write(state);
        return (Encoding.Latin1.GetString(bytes, 0, 0x15).Split((char)0)[0], SaveSections.Diff(start, bytes));
    }

    /// <summary>Which part of the saved game an offset is (for the messages).</summary>
    public static string SaveField(int offset)
    {
        if (offset < 0x189)
        {
            return offset switch
            {
                < 0x68 => "seed",
                < 0x70 => "date",
                < 0x76 => "purse",
                < 0x7C => "fame",
                < 0x7E => "cur_loc",
                < 0x82 => "map position",
                < 0x8C => "cards",
                < 0x96 => "bank notes/stone/hirelings",
                < 0xA6 => "difficulty/battle/leader/options",
                < 0xF3 => "party size/count",
                _ => "slots/images/palettes",
            };
        }
        return "records (character 0x22A each, then events 0x30, places 0x3A, cache)";
    }

    /// <summary>
    /// A listed difference: the runs whose first difference contains <see cref="Contains"/> (with <see cref="End"/>, whose original run
    /// ended with that prefix; with <see cref="OriginalEnd"/>, whose original end contains that text), or a C# line to leave out
    /// (<see cref="DropCSharp"/>). Conditions on the run: <c>GuardNotAMember</c>, a start state whose saved_card (DS:E7D8) is not −1..4;
    /// <c>LeaderAbsent</c>, a start state whose leader's slot holds nobody.
    /// </summary>
    private sealed record Divergence(string Card, string Run, string Contains, string Why, string? DropCSharp = null, bool IgnoreTexts = false,
        string? End = null, bool GuardNotAMember = false, bool LeaderAbsent = false, string? OriginalEnd = null);

    private static List<Divergence> Divergences()
    {
        var path = Path.Combine(Folder(), "card-divergences.json");
        return File.Exists(path) ? JsonSerializer.Deserialize<List<Divergence>>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [] : [];
    }

    private static bool Justified(List<Divergence> list, string card, string run, string difference, string? originalEnd = null) =>
        list.Any(d => d.DropCSharp is null && (d.Card == "*" || d.Card == card) && (d.Run == "*" || d.Run == run)
            && difference.Contains(d.Contains, StringComparison.Ordinal)
            && (d.End is null || (originalEnd ?? "").StartsWith(d.End, StringComparison.Ordinal))
            && (d.OriginalEnd is null || (originalEnd ?? "").Contains(d.OriginalEnd, StringComparison.Ordinal))
            && (!d.GuardNotAMember || CardOracleStates.FromKey(run.Split(' ')[1]).SavedCard is < -1 or > 4)
            && (!d.LeaderAbsent || CardOracleStates.FromKey(run.Split(' ')[1]).Party is var party && !party.Leader.IsPresent));
}
