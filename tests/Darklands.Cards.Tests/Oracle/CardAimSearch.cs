using System.Text.Json;
using System.Text.Json.Nodes;

namespace Darklands.Cards.Tests.Oracle;

/// <summary>
/// Opt-in tools of the aimed start states (tools/oracle, local/cards-aim): exporting listed run keys for the oracle
/// (DARKLANDS_CARD_ORACLE_KEYS) and searching the C# for the variants that turn option lines on (DARKLANDS_CARD_AIM_SPEC).
/// </summary>
public sealed class CardAimSearch
{
    /// <summary>Writes the start states of the run keys listed (one per line) in DARKLANDS_CARD_ORACLE_KEYS into DARKLANDS_CARD_ORACLE_EXPORT.</summary>
    [Fact]
    public void ExportKeys()
    {
        var folder = Environment.GetEnvironmentVariable("DARKLANDS_CARD_ORACLE_EXPORT");
        if (folder is null || !CardHarness.Available || Environment.GetEnvironmentVariable("DARKLANDS_CARD_ORACLE_KEYS") is not { Length: > 0 } list)
        {
            return;
        }
        Directory.CreateDirectory(folder);
        var done = new HashSet<string>();
        foreach (var line in File.ReadAllLines(list).Select(l => l.Trim()).Where(l => l.Length > 0))
        {
            // the start state of a key does not depend on the forced line (F<block>.<line> parts)
            var key = line.Replace("\"", "");
            var state = CardOracleStates.FromKey(key);
            var stateKey = StateKey(key);
            if (!done.Add(stateKey))
            {
                continue;
            }
            var (sav, extra) = CardOracleStates.Export(state);
            File.WriteAllBytes(Path.Combine(folder, $"state-{stateKey}.sav"), sav);
            File.WriteAllText(Path.Combine(folder, $"state-{stateKey}.json"), JsonSerializer.Serialize(extra));
        }
    }

    /// <summary>The key without its forced-line parts (the oracle looks the state up by it).</summary>
    public static string StateKey(string key)
    {
        var v = key.IndexOf("-V", StringComparison.Ordinal);
        if (v < 0)
        {
            return key;
        }
        var option = key[key.LastIndexOf("-O", StringComparison.Ordinal)..];
        var parts = key[(v + 2)..key.LastIndexOf("-O", StringComparison.Ordinal)].Split('+')
            .Where(p => !(p.Length > 3 && p[0] == 'F' && p[1..].Split('.') is [var b, var l] && int.TryParse(b, out _) && int.TryParse(l, out _))
                && !(p.Length > 1 && p[0] == 'P' && p[1..].Split('_').All(x => int.TryParse(x, out _))));
        return key[..v] + (parts.Any() ? "-V" + string.Join('+', parts) : "") + option;
    }

    /// <summary>
    /// Spec (DARKLANDS_CARD_AIM_SPEC): {"cards": {"104": ["ev87", "sk3_60+ev12", ...]}, "options": [0], "location": 3}. For each card and
    /// variant (and the base state) the C# card runs under the oracle's policy; the output (DARKLANDS_CARD_AIM_OUT) holds per card and
    /// variant the option lines ("block.line") seen enabled in any menu and a hash of the whole trace.
    /// </summary>
    [Fact]
    public void Search()
    {
        if (Environment.GetEnvironmentVariable("DARKLANDS_CARD_AIM_SPEC") is not { Length: > 0 } specFile
            || Environment.GetEnvironmentVariable("DARKLANDS_CARD_AIM_OUT") is not { Length: > 0 } outFile || !CardHarness.Available)
        {
            return;
        }
        var spec = JsonNode.Parse(File.ReadAllText(specFile))!;
        var options = spec["options"]?.AsArray().Select(x => x!.GetValue<int>()).ToArray() ?? [0];
        var location = spec["location"]?.GetValue<int>() ?? 3;
        var clock = Convert.ToUInt32(Environment.GetEnvironmentVariable("DARKLANDS_CARD_AIM_CLOCK") ?? "1DC0", 16);
        var cards = typeof(Card).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Card)) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (Card)Activator.CreateInstance(t)!)
            .ToDictionary(c => c.Number);
        var result = new System.Collections.Concurrent.ConcurrentDictionary<string, JsonObject>();
        var workers = int.Parse(Environment.GetEnvironmentVariable("DARKLANDS_CARD_AIM_WORKERS") ?? "4");
        Parallel.ForEach(spec["cards"]!.AsObject().ToList(), new ParallelOptions { MaxDegreeOfParallelism = workers }, entry =>
        {
            var number = Convert.ToInt32(entry.Key, 16);
            if (!cards.TryGetValue(number, out var template))
            {
                return;
            }
            var perVariant = new JsonObject();
            foreach (var variant in new[] { "" }.Concat(entry.Value!.AsArray().Select(x => x!.GetValue<string>())))
            {
                var on = new SortedSet<string>(StringComparer.Ordinal);
                var hash = new System.Text.StringBuilder();
                foreach (var option in options)
                {
                    var key = variant.Length == 0 ? $"L{location}-O{option}" : $"L{location}-V{variant}-O{option}";
                    try
                    {
                        var (trace, end, _, _) = CardOracleTests.Run((Card)Activator.CreateInstance(template.GetType())!, key, option, false, clock);
                        foreach (var line in trace.Where(l => l.StartsWith("menu ", StringComparison.Ordinal)))
                        {
                            var p = line.Split(' ');
                            var states = p[2].Trim('[', ']');
                            for (var i = 0; i < states.Length; i++)
                            {
                                if (states[i] == '1')
                                {
                                    on.Add($"{p[1]}.{i}");
                                }
                            }
                        }
                        hash.Append(string.Join('|', trace.Select(l => l.Length > 60 ? l[..60] : l))).Append(end);
                    }
                    catch (Exception e)
                    {
                        hash.Append("X" + e.GetType().Name);
                    }
                }
                perVariant[variant] = new JsonObject
                {
                    ["on"] = new JsonArray(on.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray()),
                    ["h"] = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(hash.ToString())))[..12],
                };
            }
            result[entry.Key] = perVariant;
        });
        File.WriteAllText(outFile, new JsonObject(result.OrderBy(p => p.Key).Select(p => KeyValuePair.Create(p.Key, (JsonNode?)p.Value))).ToJsonString());
    }

    /// <summary>
    /// Spec (DARKLANDS_CARD_PATH_SPEC): {"cards": ["104", ...], "location": 3, "options": [0, ...], "depth": 4, "cap": 400, "variant": "rn1000"}.
    /// For each card and option (the seed) the choices of the first menus are enumerated depth first (the variant part <c>P2_0_1</c>, see
    /// <see cref="CardOracleStates.PathOf"/>) and the output (DARKLANDS_CARD_PATH_OUT) lists, per card and option line ("block.line") seen
    /// enabled, the shortest path that showed it.
    /// </summary>
    [Fact]
    public void PathSearch()
    {
        if (Environment.GetEnvironmentVariable("DARKLANDS_CARD_PATH_SPEC") is not { Length: > 0 } specFile
            || Environment.GetEnvironmentVariable("DARKLANDS_CARD_PATH_OUT") is not { Length: > 0 } outFile || !CardHarness.Available)
        {
            return;
        }
        var spec = JsonNode.Parse(File.ReadAllText(specFile))!;
        var options = spec["options"]?.AsArray().Select(x => x!.GetValue<int>()).ToArray() ?? [0];
        var location = spec["location"]?.GetValue<int>() ?? 3;
        var depth = spec["depth"]?.GetValue<int>() ?? 4;
        var cap = spec["cap"]?.GetValue<int>() ?? 400;
        var variant = spec["variant"]?.GetValue<string>() ?? "";
        // "cardVariants": {"104": ["ev8_0_2_0_1", ...]} runs the card once for each of its variants (and "cards" is then ignored)
        var perCard = spec["cardVariants"]?.AsObject().SelectMany(p => p.Value!.AsArray().Select(v => (p.Key, v!.GetValue<string>()))).ToList();
        var clock = Convert.ToUInt32(Environment.GetEnvironmentVariable("DARKLANDS_CARD_AIM_CLOCK") ?? "1DC0", 16);
        var cards = typeof(Card).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Card)) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (Card)Activator.CreateInstance(t)!)
            .ToDictionary(c => c.Number);
        var result = new System.Collections.Concurrent.ConcurrentDictionary<string, JsonObject>();
        var workers = int.Parse(Environment.GetEnvironmentVariable("DARKLANDS_CARD_AIM_WORKERS") ?? "4");
        var work = perCard ?? spec["cards"]!.AsArray().Select(x => (x!.GetValue<string>(), variant)).ToList();
        Parallel.ForEach(work, new ParallelOptions { MaxDegreeOfParallelism = workers }, item =>
        {
            var (name, variant) = item;
            var number = Convert.ToInt32(name, 16);
            if (!cards.TryGetValue(number, out var template))
            {
                return;
            }
            var found = new JsonObject();
            var runs = 0;
            foreach (var option in options)
            {
                var budget = cap;
                void Dfs(int[] path)
                {
                    if (budget-- <= 0)
                    {
                        return;
                    }
                    runs++;
                    var parts = new List<string>();
                    if (variant.Length > 0)
                    {
                        parts.Add(variant);
                    }
                    if (path.Length > 0)
                    {
                        parts.Add("P" + string.Join('_', path));
                    }
                    var key = parts.Count == 0 ? $"L{location}-O{option}" : $"L{location}-V{string.Join('+', parts)}-O{option}";
                    List<string> trace;
                    try
                    {
                        (trace, _, _, _) = CardOracleTests.Run((Card)Activator.CreateInstance(template.GetType())!, key, option, false, clock);
                    }
                    catch (Exception)
                    {
                        return;
                    }
                    var menus = trace.Where(l => l.StartsWith("menu ", StringComparison.Ordinal)).Select(l => l.Split(' ')).ToList();
                    for (var m = 0; m < menus.Count; m++)
                    {
                        var states = menus[m][2].Trim('[', ']');
                        for (var i = 0; i < states.Length; i++)
                        {
                            var line = $"{menus[m][1]}.{i}";
                            if (states[i] == '1' && (found[line] is not JsonArray list || list.Count < 3
                                && !list.Any(e => e![0]!.GetValue<int>() == option)))
                            {
                                (found[line] ??= new JsonArray()).AsArray().Add(new JsonArray(JsonValue.Create(option), JsonValue.Create(string.Join('_', path))));
                            }
                        }
                    }
                    if (path.Length < depth && menus.Count > path.Length)
                    {
                        var enabled = menus[path.Length][2].Trim('[', ']').Count(ch => ch == '1');
                        for (var v = 0; v < enabled; v++)
                        {
                            Dfs([.. path, v]);
                        }
                    }
                }
                Dfs([]);
            }
            found["runs"] = runs;
            result[variant.Length == 0 ? name : name + " " + variant] = found;
        });
        File.WriteAllText(outFile, new JsonObject(result.OrderBy(p => p.Key).Select(p => KeyValuePair.Create(p.Key, (JsonNode?)p.Value))).ToJsonString());
    }
}
