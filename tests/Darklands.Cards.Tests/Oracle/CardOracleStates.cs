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
        return state;
    }

    public static string Key(int location, int option) => $"L{location}-O{option}";

    /// <summary>The pilot's start-state variations (DARKLANDS_CARD_ORACLE_VARIANTS): named changes to the golden-trace state.</summary>
    public static readonly string[] Variants = ["night", "hurt", "rich", "poor", "skilled", "items", "facts", "day", "day+poor", "day+skilled"];

    public static string Key(int location, int option, string? variant) =>
        variant is null ? Key(location, option) : $"L{location}-V{variant}-O{option}";

    /// <summary>The state of a key: <c>L3-O5</c> or <c>L3-Vnight-O5</c>.</summary>
    public static Core.GameState FromKey(string key)
    {
        var location = int.Parse(key[1..key.IndexOf('-')]);
        var option = int.Parse(key[(key.LastIndexOf("-O") + 2)..]);
        var v = key.IndexOf("-V", StringComparison.Ordinal);
        return v < 0 ? New(location, option) : New(location, option, key[(v + 2)..key.LastIndexOf("-O")]);
    }

    /// <summary>The golden-trace state with one variation applied.</summary>
    public static Core.GameState New(int location, int option, string variant)
    {
        var state = New(location, option);
        foreach (var one in variant.Split('+'))
        {
            Apply(state, one);
        }
        return state;
    }

    private static void Apply(Core.GameState state, string variant)
    {
        var members = state.Party.Members;
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
                throw new ArgumentException(variant);
        }
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
}
