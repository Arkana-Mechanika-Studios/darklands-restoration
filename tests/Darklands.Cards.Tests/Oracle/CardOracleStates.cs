using System.Text.Json;
using Darklands.Core.Randomness;
using Darklands.Persistence;

namespace Darklands.Cards.Tests.Oracle;

/// <summary>
/// The game states the card oracle starts from (tools/oracle/card_oracle.py): the states of the
/// golden traces (Quickstart, seed option + 7, location 3 or 120), as the original's own saved
/// game (<see cref="OriginalSaves.Write"/>) plus what a saved game does not hold (the random
/// generator, the bankers' standings, the working city record). The original loads the saved game
/// with its own loader, so both programs start from the same bytes.
/// </summary>
public sealed class CardOracleStates
{
    public static readonly int[] Locations = [3, 120];

    public const int Options = 10;

    /// <summary>The state a card run starts from (the same as <see cref="GoldenTraceTests"/>).</summary>
    public static Core.GameState New(int location, int option)
    {
        var state = CardHarness.NewGame(seed: (uint)(option + 7), location: location);
        // the world's data before anything reads the city record (a CardContext connects it too; Export reads it first)
        state.Data = CardHarness.World!;
        if (!Locations.Contains(location))
        {
            PlaceParty(state, location);
        }
        return state;
    }

    /// <summary>
    /// The other places the card oracle can start from (DARKLANDS_CARD_ORACLE_PLACES: location numbers, exported as
    /// L&lt;location&gt;-O&lt;option&gt;; the default export is unchanged). A saved game holds the current location and the
    /// party's map position, so the party stands on the place; the working city record (which a saved game does not hold)
    /// is the place's own when it is a city, the nearest city's otherwise (the last city entered).
    /// </summary>
    private static void PlaceParty(Core.GameState state, int location)
    {
        state.CurrentLocation = location;
        if (state.Here is not { } here)
        {
            return;
        }
        state.MapPosition = here.Position;
        if (!state.InCity)
        {
            var near = Enumerable.Range(0, Core.World.Location.CityCount)
                .MinBy(c => Math.Pow(state.Locations[c].X - here.X, 2) + Math.Pow(state.Locations[c].Y - here.Y, 2));
            state.City = state.Data.LoadCity(near);
        }
    }

    public static string Key(int location, int option) => $"L{location}-O{option}";

    /// <summary>The pilot's start-state variations (DARKLANDS_CARD_ORACLE_VARIANTS): named changes to the golden-trace state.</summary>
    public static readonly string[] Variants = ["night", "hurt", "rich", "poor", "skilled", "items", "facts", "day", "day+poor", "day+skilled"];

    public static string Key(int location, int option, string? variant) =>
        variant is null ? Key(location, option) : $"L{location}-V{variant}-O{option}";

    /// <summary>The state of a key: <c>L3-O5</c> or <c>L3-Vnight-O5</c>.</summary>
    public static Core.GameState FromKey(string key)
    {
        if (key.StartsWith('S'))
        {
            // a community saved game: S<id>-O5 (DARKLANDS_CARD_ORACLE_SAVES/<id>/*.SAV)
            return FromSave(key[1..key.LastIndexOf("-O", StringComparison.Ordinal)], int.Parse(key[(key.LastIndexOf("-O") + 2)..]));
        }
        var location = int.Parse(key[1..key.IndexOf('-')]);
        var option = int.Parse(key[(key.LastIndexOf("-O") + 2)..]);
        var v = key.IndexOf("-V", StringComparison.Ordinal);
        if (v < 0)
        {
            return New(location, option);
        }
        // the parts "F<block>.<line>" (ForceOf) and "P<n>_<n>.." (PathOf) change no state
        var variant = string.Join('+', key[(v + 2)..key.LastIndexOf("-O")].Split('+').Where(p => !IsForce(p) && !IsPath(p)));
        return variant.Length == 0 ? New(location, option) : New(location, option, variant);
    }

    private static bool IsPath(string part) => part.Length > 1 && part[0] == 'P' && part[1..].Split('_').All(x => int.TryParse(x, out _));

    /// <summary>
    /// The choices a run makes in its first menus (the variant part <c>P2_0_1</c>: the first menu chooses the 2nd enabled line, the second
    /// the 0th, the third the 1st; see tools/oracle/card_oracle.py split_key), or null.
    /// </summary>
    public static int[]? PathOf(string key)
    {
        var v = key.IndexOf("-V", StringComparison.Ordinal);
        if (v < 0)
        {
            return null;
        }
        var part = key[(v + 2)..key.LastIndexOf("-O")].Split('+').FirstOrDefault(IsPath);
        return part?[1..].Split('_').Select(int.Parse).ToArray();
    }

    private static bool IsForce(string part) =>
        part.Length > 3 && part[0] == 'F' && part[1..].Split('.') is [var b, var l] && int.TryParse(b, out _) && int.TryParse(l, out _);

    /// <summary>
    /// The line a run is told to choose whatever its state (the variant part <c>F12.3</c>: in the first menu of text block 12 choose
    /// line 3, hidden or not; the aim: to run the line's hint and action, see tools/oracle/card_oracle.py split_key), or null.
    /// </summary>
    public static (int Block, int Line)? ForceOf(string key)
    {
        var v = key.IndexOf("-V", StringComparison.Ordinal);
        if (v < 0)
        {
            return null;
        }
        foreach (var part in key[(v + 2)..key.LastIndexOf("-O")].Split('+'))
        {
            if (IsForce(part))
            {
                var p = part[1..].Split('.');
                return (int.Parse(p[0]), int.Parse(p[1]));
            }
        }
        return null;
    }

    /// <summary>DARKLANDS_CARD_ORACLE_SAVES: a folder of one folder per saved game (opt-in; the id is the folder's name).</summary>
    public static string? SavesFolder => Environment.GetEnvironmentVariable("DARKLANDS_CARD_ORACLE_SAVES") is { Length: > 0 } f ? f : null;

    public static string KeyOfSave(string id, int option) => $"S{id}-O{option}";

    /// <summary>
    /// A saved game as the start state: the game is read from the file by the loader (at the save's own location, the card
    /// the file holds is replaced by the one the run starts), with the random generator of the golden-trace states (seed option + 7).
    /// The battle file is not read (cards do not use it).
    /// </summary>
    public static Core.GameState FromSave(string id, int option)
    {
        var dir = Path.Combine(SavesFolder ?? throw new InvalidOperationException("DARKLANDS_CARD_ORACLE_SAVES"), id);
        var file = Directory.EnumerateFiles(dir).First(f => f.EndsWith(".sav", StringComparison.OrdinalIgnoreCase));
        var data = CardHarness.World!;
        var seed = (uint)(option + 7);
        var running = Simulation.NewGame.StartUp(data, seed);
        var state = OriginalSaves.Read(File.ReadAllBytes(file), [], Path.GetFileName(file), data, running, new MscRandom(seed)).State;
        state.Data = data;
        return state;
    }

    /// <summary>The golden-trace state with one variation applied.</summary>
    public static Core.GameState New(int location, int option, string variant)
    {
        var state = New(location, option);
        foreach (var one in variant.Split('+'))
        {
            Apply(state, one, option);
        }
        return state;
    }

    /// <summary>The generator state of sweep seed <paramref name="n"/> (s1, s2 ...) of an option.</summary>
    public static uint SweepSeed(int option, int n) => unchecked((uint)(n * 2654435761u + (uint)(option + 7) * 40503u + 12345u));

    private static void Apply(Core.GameState state, string variant, int option)
    {
        var members = state.Party.Members;
        if (variant.Length > 1 && variant[0] == 's' && variant[1..].All(char.IsAsciiDigit))
        {
            // a sweep seed (DARKLANDS_CARD_ORACLE_SEEDS): the random generator starts from another state than option + 7
            ((MscRandom)state.Random).Seed(SweepSeed(option, int.Parse(variant[1..])));
            return;
        }
        switch (variant)
        {
            case "day":
                state.Date = state.Date with { Hour = 12 };
                break;
            case "night":
                state.Date = state.Date with { Hour = 23 };
                break;
            case "hurt":
                for (var i = 2; i < members.Count; i += 2)
                {
                    members[i].Status = Core.Characters.CharacterStatus.Unconscious;
                }
                break;
            case "rich":
                state.Party.Purse = Core.Economy.Money.From(900, 0, 0);
                state.Party.Fame = 900;
                if (state.Here is { } rich)
                {
                    rich.Reputation = 90;
                }
                break;
            case "poor":
                state.Party.Purse = Core.Economy.Money.Zero;
                state.Party.Fame = 0;
                if (state.Here is { } poor)
                {
                    poor.Reputation = 0;
                    poor.Flags |= Core.World.Location.UprisingFlag;
                }
                state.Events.Create(-2, state.CurrentLocation, 0, Core.Events.EventKinds.Hostile, 0, 100, 0, 9999, 0, 0);
                break;
            case "skilled":
                foreach (var m in members)
                {
                    for (var a = 0; a < Core.Characters.Character.AttributeCount; a++)
                    {
                        m.Attributes[a] = m.AttributeMaximums[a] = 80;
                    }
                    Array.Fill(m.Skills, 85);
                    for (var s = 0; s < Core.Characters.Character.SaintCount; s++)
                    {
                        m.Saints.Add(s);
                    }
                }
                break;
            case "items":
                var defs = state.Data.ItemDefinitions;
                for (var i = 0; i < defs.Count; i++)
                {
                    var d = defs[i];
                    var pack = members[i % members.Count].Pack;
                    if (pack.Count < Core.Characters.Pack.Size - 1)
                    {
                        pack.Add(new Core.Characters.ItemStack(d.Code, d.Type, 25, 1, d.Weight));
                    }
                }
                break;
            case "facts":
                foreach (var tag in new[] { 3, 10 })
                {
                    for (var type = 0; type < 0x30; type++)
                    {
                        state.Events.Create(-2, state.CurrentLocation, 0, Core.Events.EventKinds.LocationFact, type, 2, 0, 9999, 0, tag);
                    }
                    for (var owner = 0; owner < members.Count; owner++)
                    {
                        state.Events.Create(owner, state.CurrentLocation, 0, Core.Events.EventKinds.Quest, 0, 2, 0, 9999, 0, tag);
                    }
                }
                break;
            default:
                if (!CardOracleAtoms.Apply(state, variant) && !Mutate(state, variant))
                {
                    throw new ArgumentException(variant);
                }
                break;
        }
    }

    /// <summary>
    /// Condition-directed mutations (DARKLANDS_CARD_ORACLE_MUTATIONS: a JSON file of variant name -> either
    /// {"0xDS-address": value} for a saved-game word, or a list of started events {kind, type, tag, location}).
    /// Unset, no variant name resolves and nothing changes.
    /// </summary>
    private static bool Mutate(Core.GameState state, string variant)
    {
        if (Environment.GetEnvironmentVariable("DARKLANDS_CARD_ORACLE_MUTATIONS") is not { Length: > 0 } file)
        {
            return false;
        }
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        if (!doc.RootElement.TryGetProperty(variant, out var m))
        {
            return false;
        }
        if (m.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in m.EnumerateArray())
            {
                int Get(string name, int or) => e.TryGetProperty(name, out var v)
                    ? (v.ValueKind == JsonValueKind.String ? state.CurrentLocation : v.GetInt32()) : or;
                state.Events.Create(-2, Get("location", 0), 0, Get("kind", 0), Get("type", 0), 0, 0, 9999, 0, Get("tag", 0));
            }
            return true;
        }
        foreach (var p in m.EnumerateObject())
        {
            var value = p.Value.GetInt32();
            switch (Convert.ToInt32(p.Name, 16))
            {
                case 0xA88D: state.PreviousCard = value; break;     // prev_screen (+0x8A)
                case 0xA891: state.MapLeaveMode = value; break;     // map-leave mode (+0xA4)
                case 0xE3D0: state.MapPosition = state.MapPosition with { X = value }; break;  // party_x (+0x7E)
                default: throw new ArgumentException(p.Name);
            }
        }
        return true;
    }

    /// <summary>The saved game and the side values of one state.</summary>
    public static (byte[] Sav, Dictionary<string, object> Extra) Export(Core.GameState state)
    {
        var (sav, _) = OriginalSaves.Write(state);
        var extra = new Dictionary<string, object>
        {
            ["random"] = state.Random is MscRandom m ? m.State : 0u,
            ["fugger"] = state.FuggerStanding,
            ["medici"] = state.MediciStanding,
            ["hanse"] = state.HanseStanding,
            ["showSaintlyChanges"] = state.ShowSaintlyChanges,
            ["reportSkillChanges"] = state.ReportSkillChanges,
            ["partyTerrain"] = state.PartyTerrain,
            ["city"] = state.City.City,
            ["cityRecord"] = Convert.ToHexString(state.City.ToArray()),
        };
        return (sav, extra);
    }

    /// <summary>Writes every start state into DARKLANDS_CARD_ORACLE_EXPORT (state-L3-O0.sav and .json ...).</summary>
    [Fact]
    public void ExportForTheOracle()
    {
        var folder = Environment.GetEnvironmentVariable("DARKLANDS_CARD_ORACLE_EXPORT");
        if (folder is null || !CardHarness.Available)
        {
            return;
        }
        Directory.CreateDirectory(folder);
        foreach (var location in Locations)
        {
            for (var option = 0; option < Options; option++)
            {
                var (sav, extra) = Export(New(location, option));
                var key = Key(location, option);
                File.WriteAllBytes(Path.Combine(folder, $"state-{key}.sav"), sav);
                File.WriteAllText(Path.Combine(folder, $"state-{key}.json"), JsonSerializer.Serialize(extra));
            }
        }
        ExportSeedSweep(folder);
        if (Environment.GetEnvironmentVariable("DARKLANDS_CARD_ORACLE_PLACES") is { Length: > 0 } places)
        {
            foreach (var location in places.Split(',').Select(int.Parse).Where(l => !Locations.Contains(l)))
            {
                for (var option = 0; option < Options; option++)
                {
                    var (sav, extra) = Export(New(location, option));
                    var key = Key(location, option);
                    File.WriteAllBytes(Path.Combine(folder, $"state-{key}.sav"), sav);
                    File.WriteAllText(Path.Combine(folder, $"state-{key}.json"), JsonSerializer.Serialize(extra));
                }
            }
        }
        if (Environment.GetEnvironmentVariable("DARKLANDS_CARD_ORACLE_VARIANTS") is not { Length: > 0 } only)
        {
            return;
        }
        // the pilot's variations (only when asked): L3-Vnight-O0 ...
        foreach (var variant in Variants.Where(v => only == "all" || only.Split(',').Contains(v)))
        {
            foreach (var location in Locations)
            {
                for (var option = 0; option < Options; option++)
                {
                    var (sav, extra) = Export(New(location, option, variant));
                    var key = Key(location, option, variant);
                    File.WriteAllBytes(Path.Combine(folder, $"state-{key}.sav"), sav);
                    File.WriteAllText(Path.Combine(folder, $"state-{key}.json"), JsonSerializer.Serialize(extra));
                }
            }
        }
    }

    /// <summary>
    /// The seed sweep (DARKLANDS_CARD_ORACLE_SEEDS=N, from DARKLANDS_CARD_ORACLE_SEED_FROM, default 1): the states of the chosen
    /// variants (DARKLANDS_CARD_ORACLE_SEED_VARIANTS, default "-,skilled,items,facts"; "-" is the plain state) once more with
    /// the random generator started from sweep seeds s1 ... sN: L3-Vs1-O5, L3-Vskilled+s2-O5.
    /// </summary>
    private static void ExportSeedSweep(string folder)
    {
        if (!int.TryParse(Environment.GetEnvironmentVariable("DARKLANDS_CARD_ORACLE_SEEDS"), out var count) || count <= 0)
        {
            return;
        }
        var from = int.TryParse(Environment.GetEnvironmentVariable("DARKLANDS_CARD_ORACLE_SEED_FROM"), out var f) ? f : 1;
        var chosen = (Environment.GetEnvironmentVariable("DARKLANDS_CARD_ORACLE_SEED_VARIANTS") ?? "-,skilled,items,facts").Split(',');
        foreach (var variant in chosen)
        {
            for (var n = from; n < from + count; n++)
            {
                var name = variant == "-" ? $"s{n}" : $"{variant}+s{n}";
                foreach (var location in Locations)
                {
                    for (var option = 0; option < Options; option++)
                    {
                        var (sav, extra) = Export(New(location, option, name));
                        var key = Key(location, option, name);
                        File.WriteAllBytes(Path.Combine(folder, $"state-{key}.sav"), sav);
                        File.WriteAllText(Path.Combine(folder, $"state-{key}.json"), JsonSerializer.Serialize(extra));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Writes a start state per saved game and option into DARKLANDS_CARD_ORACLE_EXPORT (state-S&lt;id&gt;-O3.sav and .json):
    /// DARKLANDS_CARD_ORACLE_SAVES is the folder of saved-game folders, DARKLANDS_CARD_ORACLE_SAVE_OPTIONS a subset of the options (0..9 when unset).
    /// </summary>
    [Fact]
    public void ExportSavesForTheOracle()
    {
        var folder = Environment.GetEnvironmentVariable("DARKLANDS_CARD_ORACLE_EXPORT");
        if (folder is null || SavesFolder is not { } saves || !CardHarness.Available)
        {
            return;
        }
        Directory.CreateDirectory(folder);
        var options = Environment.GetEnvironmentVariable("DARKLANDS_CARD_ORACLE_SAVE_OPTIONS") is { Length: > 0 } subset
            ? subset.Split(',').Select(int.Parse).ToArray() : Enumerable.Range(0, Options).ToArray();
        foreach (var id in Directory.GetDirectories(saves).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal))
        {
            foreach (var option in options)
            {
                var (sav, extra) = Export(FromSave(id, option));
                var key = KeyOfSave(id, option);
                File.WriteAllBytes(Path.Combine(folder, $"state-{key}.sav"), sav);
                File.WriteAllText(Path.Combine(folder, $"state-{key}.json"), JsonSerializer.Serialize(extra));
            }
        }
    }
}
