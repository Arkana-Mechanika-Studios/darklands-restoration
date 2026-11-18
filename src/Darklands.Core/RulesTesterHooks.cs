using Darklands.Core.Battles.Generation;
using Darklands.Core.Battles.Tactics;
using Darklands.Core.Randomness;

namespace Darklands.Core;

/// <summary>
/// The rules' tester traces and oracle feeds (tools/oracle sets them in the environment). The rules read no environment and write no
/// console (tests/Darklands.Architecture.Tests): the host gives both, through Presentation's TesterHooks, before the game starts.
/// </summary>
public static class RulesTesterHooks
{
    public static void Apply(Func<string, string?> environment, Action<string> console)
    {
        // each seed and draw with its callers, each combatant's step
        if (environment("DARKLANDS_RANDOM_TRACE") is not null)
        {
            MscRandom.Trace = console;
        }
        if (environment("DARKLANDS_STEP_TRACE") is not null)
        {
            TacticalBattle.StepTrace = console;
        }
        // the original's values where it reads stale memory
        TacticalBattle.StaleDestinations = TacticalBattle.ParseStaleDestinations(environment("DARKLANDS_BATTLE_STALE_DEST"));
        MineBuilder.PileWords = MineBuilder.ParsePileWords(environment("DARKLANDS_MINE_PILE_WORDS"));
    }
}
