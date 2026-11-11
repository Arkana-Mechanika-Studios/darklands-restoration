using Darklands.Core.Battles.Generation;
using Darklands.Core.Battles.Tactics;
using Darklands.Core.Randomness;

/// <summary>
/// The oracle's traces and feeds into the rules (tools/oracle/lockstep.py sets them in the environment). The rules read no environment
/// and write no console (tests/Darklands.Architecture.Tests); the host wires their hooks here, before the game starts.
/// </summary>
internal static class OracleFeeds
{
    public static void FromEnvironment()
    {
        // each seed and draw with its callers, and each combatant's step, to standard output
        if (Environment.GetEnvironmentVariable("DARKLANDS_RANDOM_TRACE") is not null)
        {
            MscRandom.Trace = Console.WriteLine;
        }
        if (Environment.GetEnvironmentVariable("DARKLANDS_STEP_TRACE") is not null)
        {
            TacticalBattle.StepTrace = Console.WriteLine;
        }
        // the original's values where it reads stale memory
        TacticalBattle.StaleDestinations = TacticalBattle.ParseStaleDestinations(Environment.GetEnvironmentVariable("DARKLANDS_BATTLE_STALE_DEST"));
        MineBattlefield.FeedPileWords(Environment.GetEnvironmentVariable("DARKLANDS_MINE_PILE_WORDS"));
    }
}
