using System.Collections;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Darklands.Cards;
using Darklands.Core;
using Darklands.Core.Events;
using Darklands.Core.Randomness;

namespace Darklands.Cards.Tests;

/// <summary>
/// Golden traces of every card: each card runs with options 0..9, in a city and in the
/// wilderness, once with the plain scripted screen (only the chosen line's hint is asked) and once
/// probing (every offered line's hint is asked and the choice comes from the item/saint pop-up
/// box). The trace is the screen log (pictures, texts, menus with their option states, hints,
/// messages, time), the next card, the random generator's state and a digest of the game state
/// and the card variables afterwards. Refactoring the card scripts must leave every trace as it
/// is. Set DARKLANDS_UPDATE_GOLDEN=1 to write the fixture again (only when a change of behaviour is
/// intended).
/// </summary>
public sealed class GoldenTraceTests
{
    private const string FixtureName = "card-traces.txt.gz";

    [Fact]
    public void EveryCardRunsAsRecorded()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var actual = AllTraces();
        var path = FixturePath();
        if (Environment.GetEnvironmentVariable("DARKLANDS_UPDATE_GOLDEN") == "1")
        {
            Write(path, actual);
            return;
        }
        Assert.True(File.Exists(path), $"no golden traces at {path} (run with DARKLANDS_UPDATE_GOLDEN=1)");
        var expected = Read(path);
        var differences = new List<string>();
        foreach (var (card, trace) in actual)
        {
            if (!expected.TryGetValue(card, out var want))
            {
                differences.Add($"{card}: not recorded");
                continue;
            }
            if (want != trace)
            {
                differences.Add($"{card}: {FirstDifference(want, trace)}");
            }
        }
        differences.AddRange(expected.Keys.Except(actual.Keys).Select(k => $"{k}: recorded but gone"));
        if (differences.Count > 0)
        {
            var dump = Path.Combine(Path.GetTempPath(), "darklands-card-traces.actual.txt");
            File.WriteAllText(dump, string.Concat(actual.Select(p => p.Value)));
            Assert.Fail($"{differences.Count} card(s) changed (actual traces in {dump}):\n" + string.Join("\n", differences.Take(30)));
        }
    }

    /// <summary>The traces of all cards, by card number.</summary>
    public static SortedDictionary<string, string> AllTraces()
    {
        // Every run builds its own game, so the cards can be traced side by side.
        var traces = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
        Parallel.ForEach(CardTypes(), type =>
        {
            var number = New(type).Number;
            var text = new StringBuilder();
            text.Append($"== card {number:X3} {type.Name}\n");
            foreach (var probe in new[] { false, true })
            {
                foreach (var location in new[] { 3, 120 })
                {
                    for (var option = 0; option < 10; option++)
                    {
                        text.Append($"-- {(probe ? "probe" : "plain")} location {location} option {option}\n");
                        text.Append(Trace(New(type), location, option, probe));
                    }
                }
            }
            Assert.True(traces.TryAdd($"{number:X3}", text.ToString()), $"two cards have the number {number:X3}");
        });
        return new SortedDictionary<string, string>(traces, StringComparer.Ordinal);
    }

    private static string Trace(Card card, int location, int option, bool probe)
    {
        var state = CardHarness.NewGame(seed: (uint)(option + 7), location: location);
        // DARKLANDS_GOLDEN_FLAGS=n starts every run with the ten global option flags at n (as a previous card may leave them),
        // to find the lines a card shows from the flags without assigning them (compare with a normal run).
        if (int.TryParse(Environment.GetEnvironmentVariable("DARKLANDS_GOLDEN_FLAGS"), out var flags))
        {
            Array.Fill(state.OptionFlags, flags);
        }
        var screen = new TracingScreen(option, probe);
        var c = new CardContext(state, CardHarness.World!, screen) { PersonalNames = CardHarness.PersonalNames };
        screen.Context = c;
        state.CurrentCard = card.Number;
        string end;
        try
        {
            end = $"next {card.Run(c)}";
        }
        catch (Exception e)
        {
            end = $"throws {e.GetType().Name}: {e.Message}";
        }
        var random = state.Random is MscRandom msc ? msc.State.ToString("X8") : "?";
        var log = new StringBuilder();
        foreach (var line in screen.Log)
        {
            log.Append(line).Append('\n');
        }
        log.Append($"{end} random {random} state {Digest(state, c, $"{card.Number:X3}-{(probe ? "probe" : "plain")}-{location}-{option}")}\n");
        return log.ToString();
    }

    private static IEnumerable<Type> CardTypes() =>
        typeof(Card).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Card)) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) is not null)
            .OrderBy(t => New(t).Number).ThenBy(t => t.FullName, StringComparer.Ordinal);

    private static Card New(Type type) => (Card)Activator.CreateInstance(type)!;

    // ---- the state digest ---------------------------------------------------------------

    /// <summary>The card-context values a card can leave behind (looked up by name so the context's layout does not matter).</summary>
    private static readonly string[] ContextValues =
    [
        "Next", "CardItems", "CardSaints", "MenuMember", "MenuSlot", "MenuPopup", "Variables", "BattleSetup",
        "LastBattleResult", "MapLeaveMode", "StandingSeen", "Memory", "Support", "BattleTargetSlain", "EncounterApproach",
        "OptionsBlock", "BattleMode", "BattleEnemy", "BattleSeed", "CombatFlags", "BattleResult", "CardBattle",
        "LastCastleFlags",
    ];

    private static string Digest(GameState state, CardContext c, string run)
    {
        var text = new StringBuilder();
        var dumper = new Dumper(text);
        dumper.Dump("state", state, 0);
        foreach (var name in ContextValues)
        {
            var property = typeof(CardContext).GetProperty(name);
            dumper.Dump(name, property is null ? "missing" : Safe(() => property.GetValue(c)), 0);
        }
        if (Environment.GetEnvironmentVariable("DARKLANDS_GOLDEN_DIGESTS") is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
            var name = $"{run}.txt";
            File.WriteAllText(Path.Combine(folder, name), text.ToString());
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..16];
    }

    private static object? Safe(Func<object?> get)
    {
        try
        {
            return get();
        }
        catch (Exception e)
        {
            return $"!{e.GetType().Name}";
        }
    }

    /// <summary>Writes the public values of the domain objects, recursively (each object once).</summary>
    private sealed class Dumper(StringBuilder text)
    {
        private readonly HashSet<object> _seen = new(ReferenceEqualityComparer.Instance);

        private static readonly Dictionary<Type, PropertyInfo[]> Properties = [];

        public void Dump(string name, object? value, int depth)
        {
            text.Append(' ', depth).Append(name).Append('=');
            switch (value)
            {
                case null:
                    text.Append("null\n");
                    return;
                case string s:
                    text.Append('"').Append(s).Append("\"\n");
                    return;
                case Delegate:
                    text.Append("fn\n");
                    return;
                case Enum or bool or char or byte or sbyte or short or ushort or int or uint or long or ulong:
                    text.Append(value).Append('\n');
                    return;
                case EventLog events:
                    text.Append($"events last {events.Last} suppressed {events.QueriesSuppressed}\n");
                    foreach (var (slot, e) in events.All())
                    {
                        Dump($"[{slot}]", e, depth + 1);
                    }
                    return;
            }
            var type = value.GetType();
            if (type.Namespace is not { } ns || !ns.StartsWith("Darklands.", StringComparison.Ordinal) && value is not IEnumerable)
            {
                text.Append(type.Name).Append('\n');
                return;
            }
            if (ns.StartsWith("Darklands.Content", StringComparison.Ordinal) || value is ICardScreen or IRandomSource)
            {
                text.Append(type.Name).Append('\n');
                return;
            }
            if (!type.IsValueType && !_seen.Add(value))
            {
                text.Append("seen\n");
                return;
            }
            if (value is IEnumerable items)
            {
                text.Append(type.Name).Append('\n');
                var k = 0;
                foreach (var item in items)
                {
                    Dump($"[{k++}]", item, depth + 1);
                }
                return;
            }
            text.Append(type.Name).Append('\n');
            if (depth > 8)
            {
                return;
            }
            foreach (var property in PropertiesOf(type))
            {
                Dump(property.Name, Safe(() => property.GetValue(value)), depth + 1);
            }
        }

        private static PropertyInfo[] PropertiesOf(Type type)
        {
            lock (Properties)
            {
                if (!Properties.TryGetValue(type, out var list))
                {
                    list = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                        .OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
                    Properties[type] = list;
                }
                return list;
            }
        }
    }

    // ---- the fixture -----------------------------------------------------------------------

    private static string FixturePath([CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "Golden", FixtureName);

    private static void Write(string path, SortedDictionary<string, string> traces)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = File.Create(path);
        using var zip = new GZipStream(file, CompressionLevel.SmallestSize);
        using var writer = new StreamWriter(zip, new UTF8Encoding(false));
        writer.NewLine = "\n";
        foreach (var trace in traces.Values)
        {
            writer.Write(trace);
        }
    }

    private static Dictionary<string, string> Read(string path)
    {
        using var file = File.OpenRead(path);
        using var zip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(zip, Encoding.UTF8);
        var all = reader.ReadToEnd();
        var traces = new Dictionary<string, string>();
        foreach (var section in all.Split("== card ", StringSplitOptions.RemoveEmptyEntries))
        {
            traces[section[..3]] = "== card " + section;
        }
        return traces;
    }

    private static string FirstDifference(string want, string got)
    {
        var a = want.Split('\n');
        var b = got.Split('\n');
        var run = "";
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var x = i < a.Length ? a[i] : "<end>";
            var y = i < b.Length ? b[i] : "<end>";
            if (x.StartsWith("-- ", StringComparison.Ordinal))
            {
                run = x;
            }
            if (x != y)
            {
                return $"{run}: line {i}: expected '{x}', got '{y}'";
            }
        }
        return "?";
    }
}

/// <summary>
/// The scripted screen of the golden traces: the same choices as <see cref="ScriptedScreen"/>, and
/// a log that also holds each menu's option states and the hints asked. Probing, it asks the hint
/// of every offered line and, when the chosen line is an item or saint line, makes the choice in its
/// pop-up box the way the real box can: the last member with a choosable slot, that member's first
/// such slot (a line whose box offers nothing is chosen plainly, as Enter would).
/// </summary>
public sealed class TracingScreen(int option, bool probe) : ICardScreen
{
    private int _menus;
    private string _card = "";

    public CardContext? Context { get; set; }

    public List<string> Log { get; } = [];

    public void Show(string cardName, string picture)
    {
        _card = cardName;
        Log.Add($"show {cardName} {picture}");
    }

    public void ShowPicture(string picture) => Log.Add($"picture {picture}");

    public void Text(int block, TextFrame frame = TextFrame.OnPicture) => Log.Add($"text {block} {frame} {Variables()}");

    /// <summary>The text variables a text would be shown with.</summary>
    private string Variables()
    {
        if (Context?.Variables is not { } v)
        {
            return "";
        }
        return $"chosen {string.Join(",", v.Chosen)} money {string.Join(",", v.Money)} numbers {string.Join(",", v.Numbers)} " +
            $"named {string.Join("|", v.Named)} texts {string.Join("|", v.Texts)} loc {v.Location},{v.Location2} item {v.Item} " +
            $"if {string.Concat(v.Conditions.Select(x => x ? 1 : 0))} support {string.Join(",", v.Support)} city {v.CityLocation}";
    }

    // The trace has no text layout: a menu on the last layout is logged as such and chosen like block 0's.
    public int ChooseOnLastLayout(IReadOnlyList<OptionState> options, Func<int, OptionHint>? hint, Func<int, OptionHint>? popupHint)
    {
        Log.Add("menu on the last layout");
        return Choose(0, options, hint);
    }

    public int Choose(int block, IReadOnlyList<OptionState> options, Func<int, OptionHint>? hint = null)
    {
        if (++_menus > 50)
        {
            throw new InvalidOperationException("The card keeps asking: it never moves on.");
        }
        Log.Add($"menu {block} [{string.Concat(options.Select(o => (int)o))}]");
        var enabled = Enumerable.Range(0, options.Count).Where(i => options[i] == OptionState.Enabled).ToList();
        if (enabled.Count == 0)
        {
            throw new InvalidOperationException($"Menu {block} offers no option.");
        }
        var first = option >= 0 && enabled.Contains(option) ? enabled.IndexOf(option) : 0;
        var choice = enabled[(first + _menus - 1) % enabled.Count];
        if (probe && hint is not null)
        {
            for (var i = 0; i < options.Count; i++)
            {
                if (options[i] != OptionState.Hidden)
                {
                    Log.Add($"hint {i}: {Say(hint(i))}");
                }
            }
        }
        if (hint is not null)
        {
            Log.Add($"hint {choice}: {Say(hint(choice))}");
        }
        if (probe && Context is { } c)
        {
            c.MenuPopup = false;
            if (PopupKind(block, choice) is { } saints && PopupChoice(c, saints) is var (member, slot))
            {
                c.MenuPopup = true;
                c.MenuMember = member;
                c.MenuSlot = slot;
            }
        }
        Log.Add($"choose {choice}");
        return choice;
    }

    /// <summary>Whether option line <paramref name="line"/> of the block opens a pop-up box: true saints, false items, null none.</summary>
    private bool? PopupKind(int block, int line)
    {
        if (CardHarness.Assets is not { } assets || _card.Length == 0)
        {
            return null;
        }
        IReadOnlyList<Content.MessageBlock> blocks;
        try
        {
            blocks = assets.Messages(_card).Blocks;
        }
        catch (KeyNotFoundException)
        {
            return null;   // a test card name that has no .MSG file
        }
        if (block < 0 || block >= blocks.Count)
        {
            return null;
        }
        var markers = blocks[block].Text.Where(b => b is 0x15 or 0x10 or 0x16 or 0x06).ToList();
        return line < markers.Count ? markers[line] switch { 0x10 => false, 0x16 => true, _ => null } : null;
    }

    /// <summary>A member and slot the box lets the player choose (R043+0x2CE2's state 1), the last member first.</summary>
    private static (int Member, int Slot)? PopupChoice(CardContext c, bool saints)
    {
        var list = saints ? c.CardSaints : c.CardItems;
        for (var i = c.State.Party.Members.Count - 1; i >= 0; i--)
        {
            if (c.State.Party.Members[i].Status is Core.Characters.CharacterStatus.Empty or Core.Characters.CharacterStatus.Away)
            {
                continue;
            }
            for (var j = 0; j < 4; j++)
            {
                if (list[j] != -1 && (c.PopupListsAll || (saints
                        ? Core.Rules.Saints.Knows(c.State, i, list[j])
                        : Core.Rules.Inventory.Has(c.State, i, list[j]) == 1)))
                {
                    return (i, j);
                }
            }
        }
        return null;
    }

    private static string Say(OptionHint h) => $"{h.Chance} '{h.Message}' {h.Member}";

    public void Tell(string message, int member = OptionHint.WholeParty) => Log.Add($"tell {member} {message}");

    public void PassTime(int hours) => Log.Add($"time {hours}");
}
