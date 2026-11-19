using Darklands.Core;

namespace Darklands.Simulation;

/// <summary>
/// The player's commands that change the game outside the list screens (seam (a)1): a screen turns a key or click into one of these
/// and never writes the state itself. Each screen keeps the original's own test before the command (the five leader changes are five
/// different tests: docs in DESIGN-a1-a2), so a command does no test of its own.
/// </summary>
public static class GameCommands
{
    /// <summary>The member in <paramref name="slot"/> leads the party.</summary>
    // orig: the writes of party-leader (DS:8DBA): R036's key loop, the global keys (R043), R032+0x0F60, R031's keys and R031+0x264C
    public static void Lead(GameState state, int slot)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.Party.LeaderIndex = slot;
    }

    /// <summary>The difficulty from now on (a battle reads the game's: Battle.DifficultyOf).</summary>
    // orig: difficulty-easy-set root 0000:1408, difficulty-normal-set 0000:1418, difficulty-hard-set 0000:1428: difficulty-setting = 0 / 1 / 2
    public static void SetDifficulty(GameState state, Difficulty difficulty)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.Difficulty = difficulty;
    }

    /// <summary>The saintly changes' reports on or off (DS:26BC); the new value.</summary>
    // orig: saint-notices-switch root 0000:1154: the byte ^= 1
    public static bool ToggleSaintlyChanges(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.ShowSaintlyChanges = !state.ShowSaintlyChanges;
        return state.ShowSaintlyChanges;
    }
}
