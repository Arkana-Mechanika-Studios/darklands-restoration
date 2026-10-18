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
    }
}
